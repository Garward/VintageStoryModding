using System;
using System.Collections.Generic;
using System.Linq;

namespace VRPG.Inventory.Persistence;

/// <summary>Rotates checksummed world-save slots without overwriting the newest valid generation.</summary>
public sealed class PlayerItemArchiveRepository
{
    public const string SlotAKey = "vrpg:player-items:a";
    public const string SlotBKey = "vrpg:player-items:b";

    private readonly IPlayerItemArchiveStorage storage;
    private readonly PlayerItemArchiveSerializer serializer;

    public bool IsCrashDurable => storage is IDurablePlayerItemArchiveStorage { IsCrashDurable: true };

    internal PlayerItemMigrationJournalState CloneMigrationJournal(PlayerItemMigrationJournalState journal)
        => serializer.CloneMigrationJournal(journal);

    public PlayerItemArchiveRepository(
        IPlayerItemArchiveStorage storage,
        PlayerItemArchiveSerializer? serializer = null)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.serializer = serializer ?? new PlayerItemArchiveSerializer();
    }

    public PlayerItemArchiveLoadResult Load()
    {
        try
        {
            SlotCandidate[] candidates = ReadCandidates();
            if (candidates.All(candidate => !candidate.Exists))
                return PlayerItemArchiveLoadResult.Empty();

            SlotCandidate[] valid = candidates
                .Where(candidate => candidate.Archive != null)
                .OrderByDescending(candidate => candidate.Generation)
                .ToArray();
            if (valid.Length == 0)
            {
                return PlayerItemArchiveLoadResult.Failed(string.Join(
                    " ",
                    candidates.Where(candidate => candidate.Exists).Select(candidate => candidate.Error)));
            }

            SlotCandidate selected = valid[0];
            SlotCandidate? incompatible = candidates
                .Where(candidate => candidate.Incompatible
                    && (candidate.Generation == 0 || candidate.Generation >= selected.Generation))
                .OrderByDescending(candidate => candidate.Generation)
                .FirstOrDefault();
            if (incompatible != null)
                return PlayerItemArchiveLoadResult.Failed(incompatible.Error);
            bool recovered = candidates.Any(candidate => candidate.Exists && candidate.Archive == null);
            return new PlayerItemArchiveLoadResult(
                true,
                false,
                recovered,
                selected.Generation,
                selected.Archive!,
                "");
        }
        catch (Exception exception)
        {
            return PlayerItemArchiveLoadResult.Failed(exception.Message);
        }
    }

    public PlayerItemArchiveSaveResult Save(PlayerItemArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        bool storageWriteAttempted = false;
        try
        {
            SlotCandidate[] candidates = ReadCandidates();
            SlotCandidate? incompatible = candidates.FirstOrDefault(candidate => candidate.Incompatible);
            if (incompatible != null)
                return PlayerItemArchiveSaveResult.Failed(incompatible.Generation, incompatible.Error);
            SlotCandidate? newest = candidates
                .Where(candidate => candidate.Archive != null)
                .OrderByDescending(candidate => candidate.Generation)
                .FirstOrDefault();
            long generation = checked((newest?.Generation ?? 0) + 1);
            string target = newest?.Key == SlotAKey ? SlotBKey : SlotAKey;
            byte[] payload = serializer.Serialize(archive);
            byte[] encoded = PlayerItemArchiveSlotCodec.Encode(generation, payload);
            if (!PlayerItemArchiveSlotCodec.TryDecode(encoded, out _, out byte[] verified, out string error)
                || !serializer.TryDeserialize(verified, out _, out error))
            {
                return PlayerItemArchiveSaveResult.Failed(generation, error);
            }
            storageWriteAttempted = true;
            storage.Write(target, encoded);
            return new PlayerItemArchiveSaveResult(true, generation, "") { StorageWriteAttempted = true };
        }
        catch (Exception exception)
        {
            return PlayerItemArchiveSaveResult.Failed(0, exception.Message, storageWriteAttempted);
        }
    }

    private SlotCandidate[] ReadCandidates() => new[]
    {
        ReadCandidate(SlotAKey),
        ReadCandidate(SlotBKey)
    };

    private SlotCandidate ReadCandidate(string key)
    {
        byte[]? encoded = storage.Read(key);
        if (encoded == null || encoded.Length == 0)
            return new SlotCandidate(key, false, false, 0, null, "");
        if (!PlayerItemArchiveSlotCodec.TryDecode(encoded, out long generation, out byte[] payload, out string error))
        {
            bool incompatible = error.StartsWith("Unsupported snapshot slot format", StringComparison.Ordinal);
            return new SlotCandidate(key, true, incompatible, generation, null, $"{key}: {error}");
        }
        if (!serializer.TryDeserialize(payload, out PlayerItemArchive archive, out error))
        {
            bool incompatible = error.StartsWith(
                "Unsupported player-item archive schema",
                StringComparison.Ordinal);
            return new SlotCandidate(key, true, incompatible, generation, null, $"{key}: {error}");
        }
        return new SlotCandidate(key, true, false, generation, archive, "");
    }

    private sealed record SlotCandidate(
        string Key,
        bool Exists,
        bool Incompatible,
        long Generation,
        PlayerItemArchive? Archive,
        string Error);
}
