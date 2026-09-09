using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            state.Slots[key] = (byte[])data.Clone();
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
            Flush(imported);
            return imported;
        }
        var info = new FileInfo(ArchivePath);
        if (info.Length > 1024L * 1024 * 1024) throw new InvalidDataException("Player item envelope exceeds its size limit.");
        var state = JsonConvert.DeserializeObject<Envelope>(File.ReadAllText(ArchivePath), new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, MaxDepth = 16
        });
        if (state == null || state.Format != 1 || state.Slots == null
            || state.WorldIdentity != worldIdentity || state.WorldAnchor != Anchor(legacy.Read(CheckpointKey)))
            throw new InvalidDataException("Player item archive identity, format or world checkpoint does not match. Preserve both copies for recovery.");
        return state;
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
        string directory = Path.GetDirectoryName(ArchivePath)!;
        EnsureDirectory(directory);
        string temporary = Path.Combine(directory, ".player-items-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, ArchivePath, overwrite: true);
            SyncDirectory(directory);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void EnsureDirectory(string directory)
    {
        if (Directory.Exists(directory)) return;
        string? parent = Path.GetDirectoryName(directory);
        if (parent == null) throw new IOException("A durable archive parent directory is required.");
        EnsureDirectory(parent);
        Directory.CreateDirectory(directory);
        SyncDirectory(directory);
        SyncDirectory(parent);
    }

    private static void SyncDirectory(string directory)
    {
        int descriptor = open(directory, 0x10000 /* O_DIRECTORY */);
        if (descriptor < 0) throw new IOException("Cannot open the archive directory for fsync.");
        try { if (fsync(descriptor) != 0) throw new IOException("Cannot durably flush the archive directory."); }
        finally { close(descriptor); }
    }

    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);

    private sealed class Envelope
    {
        public int Format { get; set; } = 1;
        public string WorldIdentity { get; set; } = "";
        public string WorldAnchor { get; set; } = "";
        public Dictionary<string, byte[]> Slots { get; set; } = new(StringComparer.Ordinal);
        [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
    }
}
