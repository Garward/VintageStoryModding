using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Data;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory.Gear;

public sealed record GearReadinessResult(bool Ready, int ValidEntries, int RecoveredEntries, string Error);

public static class GearReadinessValidator
{
    public static GearReadinessResult ValidateAndRecover(PlayerItemArchiveService archive,
        VRPGDataRegistry definitions, Func<string, bool> chassisExists, string fallbackChassisAssetCode)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(chassisExists);
        if (!archive.IsLoaded) return new(false, 0, 0, "Player-item persistence has not loaded.");

        int valid = 0, recovered = 0;
        var errors = new List<string>();
        var prototypes = new GearPrototypeFactory(chassisExists, fallbackChassisAssetCode);
        foreach ((string uid, IPlayerItemStore store) in archive.Directory.Snapshot())
        {
            if (store is not InMemoryPlayerItemStore memory)
            {
                errors.Add($"Player '{uid}' uses an unsupported persistent store.");
                continue;
            }
            // Capture once so recovery cannot shift a paginated query past another bad entry.
            PlayerItemStoreState state = memory.Capture().State;
            foreach (PlayerItemEntryState entry in state.Entries.Where(entry =>
                entry.Prototype.Category == PlayerItemCategories.Gear
                && entry.Location is PlayerItemLocation.Stored or PlayerItemLocation.Equipped))
            {
                string error = "";
                if (entry.Prototype.Kind != PlayerItemStorageKind.Unique || entry.Quantity != 1)
                    error = "Gear must be one unique entry.";
                else if (!GearItemStateCodec.TryDecode(entry.Prototype.ExactState, out var gear, out error)) { }
                else if (!ValidateDefinitions(gear!, definitions, out error)) { }
                else if (entry.Location == PlayerItemLocation.Equipped
                    && (state.Equipment.Count(item => item.EntryId == entry.EntryId) != 1
                        || state.Equipment.Single(item => item.EntryId == entry.EntryId).RoleCode != gear!.EquipmentRole))
                    error = "Stored equipment role does not match the instance.";
                else
                {
                    try { prototypes.Materialize(entry.Prototype); }
                    catch (InvalidOperationException exception) { error = exception.Message; }
                }

                if (error.Length == 0) { valid++; continue; }
                PlayerItemOperationResult moved = store.MoveToRecovery(
                    "gear-readiness-" + Guid.NewGuid().ToString("N"), entry.EntryId,
                    PlayerItemLocation.Unresolved, PlayerItemRecoveryReason.UnsupportedContent,
                    "gear-readiness", store.Revision);
                if (moved.Success) recovered++;
                errors.Add($"Player '{uid}', entry '{entry.EntryId}': {error}"
                    + (moved.Success ? " Moved to recovery." : $" Recovery failed: {moved.Rejection}."));
            }
        }
        // Always retry dirty persistence, including recovery moved during an earlier failed save.
        PlayerItemArchiveSaveResult saved = archive.SaveDirty();
        if (!saved.Success) errors.Add("Recovery persistence failed: " + saved.Error);
        return new(errors.Count == 0 && saved.Success, valid, recovered, string.Join(" ", errors));
    }

    internal static bool ValidateDefinitions(GearInstance gear, VRPGDataRegistry definitions, out string error)
    {
        error = "";
        var gearBase = definitions.GearBases.Get(gear.BaseCode);
        if (gearBase == null) error = $"Missing base '{gear.BaseCode}'.";
        else if (definitions.Rarities.Get(gear.RarityCode) == null) error = $"Missing rarity '{gear.RarityCode}'.";
        else if (gearBase.EquipmentRole != gear.EquipmentRole) error = "Base and instance roles do not match.";
        else
        {
            foreach (GearAffixRoll roll in gear.Affixes)
            {
                if (definitions.Affixes.Get(roll.AffixCode) == null)
                {
                    error = $"Missing affix '{roll.AffixCode}'.";
                    break;
                }
                foreach (GearModifierRoll modifier in roll.Modifiers)
                {
                    if (GearModifierContract.IsSupported(modifier.Stat, modifier.Operation)) continue;
                    error = $"Unsupported gear modifier '{modifier.Stat}' with operation '{modifier.Operation}'.";
                    break;
                }
                if (error.Length > 0) break;
            }
        }
        // Current authoring ranges, counts and scalars are not historical acceptance rules.
        return error.Length == 0;
    }
}
