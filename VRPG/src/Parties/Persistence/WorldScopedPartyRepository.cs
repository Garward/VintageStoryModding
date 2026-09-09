using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRPG.Persistence;

namespace VRPG.Parties.Persistence;

/// <summary>
/// Linux world-scoped storage with an exclusive lifetime flock. Dispose at world shutdown. The storage
/// root and history/lock files must remain server-owned; manual edits, unlinking locks, and whole-directory
/// rollback while the server is running are unsupported. Validated snapshots may authorize without disk I/O.
/// </summary>
public sealed class WorldScopedPartyRepository : IPartyRepository, IDisposable
{
    private readonly string worldIdentity;
    private readonly PartyWorldLease lease;
    private bool disposed;

    public WorldScopedPartyRepository(string root, string worldIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldIdentity);
        this.worldIdentity = worldIdentity;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(worldIdentity)));
        string fullRoot = Path.GetFullPath(root);
        ArchivePath = Path.Combine(fullRoot, "parties", key, "parties.json");
        lease = PartyWorldLease.Retain(Path.Combine(fullRoot, "party-locks", key + ".lock"));
    }

    public string ArchivePath { get; }
    public object SyncRoot => lease;
    public long? CommittedGeneration { get { lock (SyncRoot) return disposed ? null : lease.CommittedGeneration; } }
    public bool IsCrashDurable => OperatingSystem.IsLinux();

    public PartyRepositoryLoadResult Load()
    {
        lock (SyncRoot)
        {
            if (!disposed) lease.CommittedGeneration = null;
            try
            {
                EnsureLease();
                var envelope = ReadEnvelope();
                if (envelope == null)
                {
                    lease.CommittedGeneration = 0;
                    return new(PartyRepositoryLoadStatus.Absent, null);
                }
                var archive = Decode(envelope);
                // A previous rename may have succeeded before directory fsync failed. Reconciliation
                // must make that validated generation durable before it can authorize shared pickup.
                DurableFileWriter.SyncDirectory(Path.GetDirectoryName(ArchivePath)!);
                lease.CommittedGeneration = archive.Generation;
                return new(PartyRepositoryLoadStatus.Loaded, archive);
            }
            catch { return new(PartyRepositoryLoadStatus.Unavailable, null); }
        }
    }

    public PartyRepositorySaveStatus Save(PartyArchive archive, long expectedGeneration)
    {
        lock (SyncRoot)
        {
            if (!disposed) lease.CommittedGeneration = null;
            bool writing = false;
            try
            {
                EnsureLease();
                Envelope? previous = ReadEnvelope();
                long current = previous == null ? 0 : Decode(previous).Generation;
                if (current != expectedGeneration || archive.Generation != checked(current + 1)
                    || !PartyArchiveRules.Validate(archive)) return PartyRepositorySaveStatus.Failed;
                byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(archive, PartyArchiveRules.Settings));
                var envelope = new Envelope
                {
                    WorldIdentity = worldIdentity, Generation = archive.Generation, Payload = payload,
                    Checksum = Convert.ToHexString(SHA256.HashData(payload)), ExtensionData = previous?.ExtensionData
                };
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope, PartyArchiveRules.Settings));
                if (bytes.Length > PartyArchiveRules.MaximumBytes) return PartyRepositorySaveStatus.Failed;
                writing = true;
                lease.MarkHistory();
                DurableFileWriter.Write(ArchivePath, bytes, ".parties-");
                lease.CommittedGeneration = archive.Generation;
                return PartyRepositorySaveStatus.Committed;
            }
            catch { return writing ? PartyRepositorySaveStatus.Uncertain : PartyRepositorySaveStatus.Failed; }
        }
    }

    private void EnsureLease()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsCrashDurable) throw new PlatformNotSupportedException("Durable party storage requires Linux flock and directory fsync.");
        lease.Acquire();
    }

    private Envelope? ReadEnvelope()
    {
        string directory = Path.GetDirectoryName(ArchivePath)!;
        if (!PathPresent(directory))
        {
            if (lease.HasHistory) throw new InvalidDataException("Existing party history is missing.");
            return null;
        }
        if (!PathPresent(ArchivePath)) throw new InvalidDataException("The party archive is missing from an existing history directory.");
        if (!lease.HasHistory) throw new InvalidDataException("The party archive has lost its history marker.");
        using var stream = new FileStream(ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > PartyArchiveRules.MaximumBytes) throw new InvalidDataException("Party archive exceeds its size bound.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        using var json = new JsonTextReader(reader) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        var parsed = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (json.Read()) throw new InvalidDataException("Trailing party archive content.");
        return parsed.ToObject<Envelope>(JsonSerializer.Create(PartyArchiveRules.Settings))
            ?? throw new InvalidDataException("Empty party archive.");
    }

    private PartyArchive Decode(Envelope envelope)
    {
        if (envelope.Format != 1 || envelope.WorldIdentity != worldIdentity || envelope.Generation < 1
            || envelope.Payload == null || envelope.Payload.Length > PartyArchiveRules.MaximumBytes
            || envelope.Checksum != Convert.ToHexString(SHA256.HashData(envelope.Payload)))
            throw new InvalidDataException("Party archive format, identity, generation or checksum is invalid.");
        using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(envelope.Payload)))
            { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        var payload = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("Trailing party payload content.");
        var state = payload.ToObject<PartyArchive>(JsonSerializer.Create(PartyArchiveRules.Settings));
        if (state == null || state.Generation != envelope.Generation || !PartyArchiveRules.Validate(state))
            throw new InvalidDataException("Party membership structure is invalid.");
        return state;
    }

    // Enumerating accessible parents gives affirmative absence. File.Exists alone hides access failures.
    internal static bool PathPresent(string path)
    {
        string current = Path.GetPathRoot(path)!;
        string[] parts = path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string next = Path.Combine(current, parts[i]);
            if (!Directory.EnumerateFileSystemEntries(current).Contains(next, StringComparer.Ordinal)) return false;
            FileAttributes attributes = File.GetAttributes(next);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (i < parts.Length - 1 && (attributes & FileAttributes.Directory) == 0))
                throw new InvalidDataException("Party storage paths must not traverse symbolic links or non-directories.");
            current = next;
        }
        return true;
    }

    public void Dispose()
    {
        lock (SyncRoot)
        {
            if (disposed) return;
            disposed = true;
            lease.Release();
        }
    }

    private sealed class Envelope
    {
        [JsonProperty(Required = Required.Always)] public int Format { get; set; } = 1;
        [JsonProperty(Required = Required.Always)] public string WorldIdentity { get; set; } = "";
        [JsonProperty(Required = Required.Always)] public long Generation { get; set; }
        [JsonProperty(Required = Required.Always)] public byte[] Payload { get; set; } = Array.Empty<byte>();
        [JsonProperty(Required = Required.Always)] public string Checksum { get; set; } = "";
        [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
    }
}
