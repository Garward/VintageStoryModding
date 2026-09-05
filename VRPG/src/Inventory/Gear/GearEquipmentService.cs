using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Inventory.Gear;

public sealed record GearEquipmentEntry(string RoleCode, string EntryId, GearInstance Instance);

/// <summary>An exact equipped-gear view validated against one item-store revision.</summary>
public sealed record GearEquipmentSnapshot
{
    private readonly GearEquipmentEntry[] entries;

    public GearEquipmentSnapshot(
        long revision,
        bool valid,
        GearEquipmentEntry[] entries,
        string error)
    {
        Revision = revision;
        Valid = valid;
        this.entries = entries == null
            ? Array.Empty<GearEquipmentEntry>()
            : (GearEquipmentEntry[])entries.Clone();
        Error = error ?? "";
    }

    public long Revision { get; init; }
    public bool Valid { get; init; }
    public GearEquipmentEntry[] Entries => (GearEquipmentEntry[])entries.Clone();
    public string Error { get; init; }
}

/// <summary>Validates all RPG gear semantics before delegating custody changes to the item store.</summary>
public sealed class GearEquipmentService
{
    private readonly VRPGDataRegistry data;

    public GearEquipmentService(VRPGDataRegistry data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public PlayerItemOperationResult TryEquip(
        IPlayerItemStore store,
        int playerLevel,
        string requestId,
        string entryId,
        string roleCode,
        long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (store.TryGetCompletedEquip(
            requestId,
            entryId,
            roleCode,
            expectedRevision,
            out PlayerItemOperationResult? completed))
        {
            return completed!;
        }

        if (!store.TryGet(entryId, out PlayerItemDetail? detail)
            || detail == null
            || !IsGearStorage(detail)
            || !GearItemStateCodec.TryDecode(detail.Prototype.ExactState, out GearInstance? gear, out _))
        {
            return PlayerItemOperationResult.Rejected(
                store.Revision,
                requestId,
                PlayerItemOperationRejection.InvalidGear);
        }

        GearBaseDefinition? gearBase = data.GearBases.Get(gear!.BaseCode);
        if (gearBase == null)
        {
            return PlayerItemOperationResult.Rejected(
                store.Revision,
                requestId,
                PlayerItemOperationRejection.InvalidGear);
        }

        if (!string.Equals(gearBase.EquipmentRole, gear.EquipmentRole, StringComparison.Ordinal)
            || !string.Equals(gear.EquipmentRole, roleCode, StringComparison.Ordinal)
            || detail.Location == PlayerItemLocation.Equipped
                && !HasExactStoredRole(store, detail.EntryId, roleCode))
        {
            return PlayerItemOperationResult.Rejected(
                store.Revision,
                requestId,
                PlayerItemOperationRejection.EquipmentRoleMismatch);
        }

        if (playerLevel < gear.RequiredLevel)
        {
            return PlayerItemOperationResult.Rejected(
                store.Revision,
                requestId,
                PlayerItemOperationRejection.RequirementNotMet);
        }

        if (!GearReadinessValidator.ValidateDefinitions(gear, data, out _))
            return PlayerItemOperationResult.Rejected(store.Revision, requestId, PlayerItemOperationRejection.InvalidGear);

        return store.Equip(requestId, entryId, roleCode, expectedRevision);
    }

    public GearEquipmentSnapshot Snapshot(IPlayerItemStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        PlayerEquipmentSnapshot equipment = store.Equipment();
        long revision = equipment.Revision;
        var entries = new List<GearEquipmentEntry>(equipment.Entries.Length);
        var entryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roles = new HashSet<string>(StringComparer.Ordinal);

        foreach (EquippedPlayerItemRecord? equipped in equipment.Entries)
        {
            string offendingId = equipped?.EntryId ?? "<missing>";
            if (equipped == null
                || string.IsNullOrWhiteSpace(equipped.EntryId)
                || string.IsNullOrWhiteSpace(equipped.RoleCode)
                || !entryIds.Add(equipped.EntryId)
                || !roles.Add(equipped.RoleCode))
            {
                return Failed(revision, offendingId, "duplicate or malformed equipment record");
            }

            if (!store.TryGet(equipped.EntryId, out PlayerItemDetail? detail)
                || detail == null
                || !string.Equals(detail.EntryId, equipped.EntryId, StringComparison.OrdinalIgnoreCase)
                || detail.Location != PlayerItemLocation.Equipped
                || !IsGearStorage(detail)
                || !GearItemStateCodec.TryDecode(detail.Prototype.ExactState, out GearInstance? gear, out _))
            {
                return Failed(revision, offendingId, "missing or invalid equipped gear");
            }

            GearBaseDefinition? gearBase = data.GearBases.Get(gear!.BaseCode);
            if (gearBase == null
                || !GearReadinessValidator.ValidateDefinitions(gear, data, out _)
                || !string.Equals(equipped.RoleCode, gear.EquipmentRole, StringComparison.Ordinal)
                || !string.Equals(equipped.RoleCode, gearBase.EquipmentRole, StringComparison.Ordinal))
            {
                return Failed(revision, offendingId, "equipment role mismatch");
            }

            entries.Add(new GearEquipmentEntry(equipped.RoleCode, detail.EntryId, gear));
        }

        if (store.Revision != revision)
        {
            string offendingId = equipment.Entries.FirstOrDefault()?.EntryId ?? "<none>";
            return Failed(revision, offendingId, "store revision changed during snapshot");
        }

        return new GearEquipmentSnapshot(revision, true, entries.ToArray(), "");
    }

    private static bool IsGearStorage(PlayerItemDetail detail) =>
        detail.Prototype.Kind == PlayerItemStorageKind.Unique
        && detail.Quantity == 1
        && string.Equals(detail.Prototype.Category, PlayerItemCategories.Gear, StringComparison.Ordinal)
        && detail.Location is PlayerItemLocation.Stored or PlayerItemLocation.Equipped;

    private static bool HasExactStoredRole(IPlayerItemStore store, string entryId, string roleCode)
    {
        PlayerEquipmentSnapshot equipment = store.Equipment();
        return equipment.Entries.Count(entry =>
            string.Equals(entry.EntryId, entryId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.RoleCode, roleCode, StringComparison.Ordinal)) == 1;
    }

    private static GearEquipmentSnapshot Failed(long revision, string entryId, string reason) => new(
        revision,
        false,
        Array.Empty<GearEquipmentEntry>(),
        $"Equipped entry '{entryId}' failed validation: {reason}.");
}
