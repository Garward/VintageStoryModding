using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRPG.Persistence;

namespace VRPG.Inventory.Persistence;

/// <summary>Flushes both archive slots together, independently of the engine's buffered world save.</summary>
public sealed class WorldScopedPlayerItemArchiveStorage : IDurablePlayerItemArchiveStorage
{
    public const string CheckpointKey = "vrpg:player-items:world-checkpoint";
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.Ordinal);
    private readonly string worldIdentity;
    private readonly IPlayerItemArchiveStorage legacy;
    private readonly object gate;

    public WorldScopedPlayerItemArchiveStorage(string root, string worldIdentity, IPlayerItemArchiveStorage legacy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldIdentity);
        this.worldIdentity = worldIdentity;
        this.legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
        string worldKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(worldIdentity)));
        ArchivePath = Path.Combine(Path.GetFullPath(root), worldKey, "player-items.json");
        gate = Gates.GetOrAdd(ArchivePath, _ => new object());
    }

    public string ArchivePath { get; }
    public bool IsCrashDurable => OperatingSystem.IsLinux();

    /// <summary>Only affirmative absence is Vanilla evidence; this never reads/imports an envelope.</summary>
    public PlayerItemCustodyPresence ProbePresence()
    {
        lock (gate)
        {
            try
            {
                // Exists hides permission and I/O failures. Walk accessible parent listings so
                // a missing entry is affirmative evidence, not an exception treated as absence.
                string directory = Path.GetDirectoryName(ArchivePath)!;
                string current = Path.GetPathRoot(directory)!;
                bool present = true;
                foreach (string part in directory[current.Length..].Split(Path.DirectorySeparatorChar,
                    StringSplitOptions.RemoveEmptyEntries))
                {
                    string next = Path.Combine(current, part);
                    if (!Directory.EnumerateFileSystemEntries(current).Any(entry =>
                        string.Equals(entry, next, StringComparison.Ordinal)))
                    {
                        present = false;
                        break;
                    }
                    FileAttributes attributes = File.GetAttributes(next);
                    if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                        return PlayerItemCustodyPresence.Uncertain;
                    current = next;
                }
                if (present) return PlayerItemCustodyPresence.History;
                foreach (string key in new[] { PlayerItemArchiveRepository.SlotAKey,
                    PlayerItemArchiveRepository.SlotBKey, CheckpointKey })
                    if (legacy.Read(key) != null) return PlayerItemCustodyPresence.History;
                return PlayerItemCustodyPresence.Absent;
            }
            catch { return PlayerItemCustodyPresence.Uncertain; }
        }
    }

    public byte[]? Read(string key)
    {
        lock (gate)
        {
            Envelope state = ReadOrImport();
            return state.Slots.TryGetValue(key, out var bytes) ? (byte[])bytes.Clone() : null;
        }
    }

    public void Write(string key, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (gate)
        {
            Envelope state = ReadOrImport();
            if (!IsSlotKey(key) || !PlayerItemArchiveSlotCodec.TryDecode(data, out long generation, out _, out _)
                || generation != checked(state.CommittedGeneration!.Value + 1))
                throw new InvalidDataException("An archive write must contain the next valid committed generation.");
            state.Slots[key] = (byte[])data.Clone();
            BindSlots(state);
            Flush(state);
        }
    }

    /// <summary>A crash during the world save can refuse startup; it cannot silently mix timelines.</summary>
    public void AdvanceWorldCheckpoint()
    {
        lock (gate)
        {
            Envelope state = ReadOrImport();
            byte[] checkpoint = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
            state.WorldAnchor = Anchor(checkpoint);
            Flush(state);
            legacy.Write(CheckpointKey, checkpoint);
        }
    }

    private Envelope ReadOrImport()
    {
        if (!IsCrashDurable) throw new PlatformNotSupportedException("Durable player item storage requires directory fsync support.");
        if (!File.Exists(ArchivePath))
        {
            if (Directory.Exists(Path.GetDirectoryName(ArchivePath)) || legacy.Read(CheckpointKey) != null)
                throw new InvalidDataException("The world-bound archive is missing from an existing custody directory. Preserve it for explicit recovery.");
            var imported = new Envelope { WorldIdentity = worldIdentity, WorldAnchor = Anchor(legacy.Read(CheckpointKey)) };
            foreach (string key in new[] { PlayerItemArchiveRepository.SlotAKey, PlayerItemArchiveRepository.SlotBKey })
                if (legacy.Read(key) is { } bytes) imported.Slots[key] = bytes;
            BindSlots(imported);
            Flush(imported);
            return imported;
        }
        var info = new FileInfo(ArchivePath);
        if (info.Length > 1024L * 1024 * 1024) throw new InvalidDataException("Player item envelope exceeds its size limit.");
        var state = JsonConvert.DeserializeObject<Envelope>(File.ReadAllText(ArchivePath), new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, MaxDepth = 16
        });
        if (state == null || state.Format != 2 || state.Slots == null
            || state.WorldIdentity != worldIdentity || state.WorldAnchor != Anchor(legacy.Read(CheckpointKey)))
            throw new InvalidDataException("Player item archive identity, format or world checkpoint does not match. Preserve both copies for recovery.");
        ValidateSlots(state);
        return state;
    }

    private static bool IsSlotKey(string key) =>
        key is PlayerItemArchiveRepository.SlotAKey or PlayerItemArchiveRepository.SlotBKey;

    private static void BindSlots(Envelope state)
    {
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        long generation = 0;
        foreach (var pair in state.Slots)
        {
            if (!IsSlotKey(pair.Key) || !PlayerItemArchiveSlotCodec.TryDecode(pair.Value, out long slotGeneration, out _, out _))
                throw new InvalidDataException("An archive slot is invalid; preserve its source for recovery.");
            digests.Add(pair.Key, Convert.ToHexString(SHA256.HashData(pair.Value)));
            generation = Math.Max(generation, slotGeneration);
        }
        state.SlotDigests = digests;
        state.CommittedGeneration = generation;
    }

    private static void ValidateSlots(Envelope state)
    {
        if (state.SlotDigests == null || state.CommittedGeneration == null || state.CommittedGeneration < 0
            || state.Slots.Count != state.SlotDigests.Count || state.Slots.Count > 2
            || state.SlotDigests.Keys.Any(key => !state.Slots.ContainsKey(key)))
            throw new InvalidDataException("Archive slot presence does not match its committed manifest.");
        long actualGeneration = 0;
        foreach (var pair in state.Slots)
        {
            if (!IsSlotKey(pair.Key) || !PlayerItemArchiveSlotCodec.TryDecode(pair.Value, out long generation, out _, out _)
                || state.SlotDigests[pair.Key] != Convert.ToHexString(SHA256.HashData(pair.Value)))
                throw new InvalidDataException("Archive slot bytes do not match the committed manifest.");
            actualGeneration = Math.Max(actualGeneration, generation);
        }
        if (actualGeneration != state.CommittedGeneration)
            throw new InvalidDataException("The latest committed archive generation is missing.");
    }

    private string Anchor(byte[]? checkpoint)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[]? value in new[] { legacy.Read(PlayerItemArchiveRepository.SlotAKey),
            legacy.Read(PlayerItemArchiveRepository.SlotBKey), checkpoint })
        {
            hash.AppendData(BitConverter.GetBytes(value?.Length ?? -1));
            if (value != null) hash.AppendData(value);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void Flush(Envelope state)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));
        DurableFileWriter.Write(ArchivePath, bytes, ".player-items-");
    }

    private sealed class Envelope
    {
        [JsonProperty(Required = Required.Always)]
        public int Format { get; set; } = 2;
        [JsonProperty(Required = Required.Always)]
        public string WorldIdentity { get; set; } = "";
        [JsonProperty(Required = Required.Always)]
        public string WorldAnchor { get; set; } = "";
        [JsonProperty(Required = Required.Always)]
        public Dictionary<string, byte[]> Slots { get; set; } = new(StringComparer.Ordinal);
        [JsonProperty(Required = Required.Always)]
        public Dictionary<string, string>? SlotDigests { get; set; }
        [JsonProperty(Required = Required.Always)]
        public long? CommittedGeneration { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
    }
}
