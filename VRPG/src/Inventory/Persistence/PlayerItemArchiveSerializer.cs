using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using VRPG.Inventory.Bridges;

namespace VRPG.Inventory.Persistence;

public sealed class PlayerItemArchiveSerializer
{
    private const int MaximumUncompressedBytes = 512 * 1024 * 1024;
    private readonly JsonSerializerSettings settings = new()
    {
        Formatting = Formatting.None,
        MaxDepth = 128,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        TypeNameHandling = TypeNameHandling.None
    };

    internal PlayerItemMigrationJournalState CloneMigrationJournal(PlayerItemMigrationJournalState journal)
    {
        string json = JsonConvert.SerializeObject(journal, settings);
        return JsonConvert.DeserializeObject<PlayerItemMigrationJournalState>(json, settings)
            ?? throw new JsonSerializationException("The migration journal contains no document.");
    }

    public byte[] Serialize(PlayerItemArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        string json = JsonConvert.SerializeObject(archive, settings);
        byte[] source = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(source, 0, source.Length);
        return output.ToArray();
    }

    public bool TryDeserialize(byte[]? compressed, out PlayerItemArchive archive, out string error)
    {
        archive = new PlayerItemArchive();
        error = "";
        if (compressed == null || compressed.Length == 0)
        {
            error = "The archive payload is empty.";
            return false;
        }

        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            while (true)
            {
                int read = gzip.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                if (output.Length + read > MaximumUncompressedBytes)
                    throw new InvalidDataException("The inventory archive exceeds its decoded size limit.");
                output.Write(buffer, 0, read);
            }

            string json = Encoding.UTF8.GetString(output.ToArray());
            PlayerItemArchive? decoded = JsonConvert.DeserializeObject<PlayerItemArchive>(json, settings);
            if (decoded == null)
                throw new InvalidDataException("The inventory archive contains no document.");
            if (!TryMigrate(decoded, out error)) return false;
            archive = decoded;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or JsonException
            or ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryMigrate(PlayerItemArchive archive, out string error)
    {
        error = "";
        if (archive.SchemaVersion < 0
            || archive.SchemaVersion > PlayerItemPersistenceSchema.CurrentVersion)
        {
            error = $"Unsupported player-item archive schema {archive.SchemaVersion}.";
            return false;
        }

        bool migrateProtection = archive.SchemaVersion < 2;
        archive.Bridges ??= new Dictionary<string, PlayerItemBridgeReceipt>(StringComparer.Ordinal);
        foreach (var pair in archive.Bridges)
        {
            if (!PlayerItemBridgeJournal.Validate(pair.Key, pair.Value))
            {
                error = "Unsupported player-item archive schema or invalid bridge receipt; preserve the archive for reconciliation.";
                return false;
            }
        }
        archive.Players ??= new Dictionary<string, PlayerItemStoreState>(
            StringComparer.OrdinalIgnoreCase);
        archive.Migrations ??= new Dictionary<string, PlayerItemMigrationJournalState>(
            StringComparer.OrdinalIgnoreCase);

        var normalized = new Dictionary<string, PlayerItemStoreState>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string playerUid, PlayerItemStoreState state) in archive.Players)
        {
            if (string.IsNullOrWhiteSpace(playerUid) || state == null
                || !normalized.TryAdd(playerUid.Trim(), state))
            {
                error = "The archive contains an invalid or duplicated player UID.";
                return false;
            }
            state.Entries ??= Array.Empty<PlayerItemEntryState>();
            state.Equipment ??= Array.Empty<EquippedPlayerItemRecord>();
            state.Reservations ??= Array.Empty<PlayerItemReservationState>();
            state.CompletedRequests ??= Array.Empty<PlayerItemCompletedRequestState>();
            if (migrateProtection) MigrateProtection(state);
        }
        archive.Players = normalized;

        var migrations = new Dictionary<string, PlayerItemMigrationJournalState>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string playerUid, PlayerItemMigrationJournalState migration) in archive.Migrations)
        {
            if (string.IsNullOrWhiteSpace(playerUid) || migration == null
                || !migrations.TryAdd(playerUid.Trim(), migration))
            {
                error = "The archive contains an invalid or duplicated migration player UID.";
                return false;
            }
            migration.SourceSlots ??= Array.Empty<LegacyItemSlotState>();
            migration.PriorStore ??= new PlayerItemStoreState();
            if (migrateProtection) MigrateProtection(migration.PriorStore);
        }
        archive.Migrations = migrations;
        archive.SchemaVersion = PlayerItemPersistenceSchema.CurrentVersion;
        return true;
    }

    private static void MigrateProtection(PlayerItemStoreState state)
    {
        var newlyProtected = new Dictionary<string, PlayerItemEntryState>(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerItemEntryState entry in state.Entries ?? Array.Empty<PlayerItemEntryState>())
        {
            if (entry?.Prototype == null) continue;
            bool wasProtected = entry.Flags.HasFlag(PlayerItemFlags.Protected);
            entry.ProtectionSource ??= PlayerItemPolicy.LegacyProtection(entry.Prototype, entry.Flags);
            entry.Flags = PlayerItemPolicy.EffectiveFlags(entry.Flags, entry.ProtectionSource.Value);
            if (!wasProtected && entry.ProtectionSource.Value.HasFlag(PlayerItemProtectionSource.System)
                && !string.IsNullOrWhiteSpace(entry.EntryId))
                newlyProtected.TryAdd(entry.EntryId, entry);
        }

        // A legacy reservation was legal before its quest record acquired system protection.
        // Preserve the complete transaction and every quantity, but require explicit rollback.
        foreach (PlayerItemReservationState reservation in state.Reservations ?? Array.Empty<PlayerItemReservationState>())
        {
            if (reservation?.Amounts == null) continue;
            foreach (PlayerItemAmount amount in reservation.Amounts)
            {
                if (amount?.EntryId == null || !newlyProtected.TryGetValue(amount.EntryId, out var entry)) continue;
                reservation.RequiresRecovery = true;
                entry.Location = PlayerItemLocation.Unresolved;
                entry.RecoveryReason = PlayerItemRecoveryReason.InterruptedTransaction;
                entry.RecoverySource = PlayerItemPolicy.ProtectionMigrationRecoverySource;
            }
        }
    }
}
