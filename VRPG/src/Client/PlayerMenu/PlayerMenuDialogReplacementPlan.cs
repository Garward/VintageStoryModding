using System;
using System.Collections.Generic;
using System.Linq;

namespace VRPG.Client.PlayerMenu;

/// <summary>Validates a complete Inventory and Character dialog replacement before mutation.</summary>
public sealed record PlayerMenuDialogReplacementPlan(string InventoryDialogId, string CharacterDialogId)
{
    public static bool TryCreate(
        IEnumerable<PlayerMenuDialogCandidate> candidates,
        out PlayerMenuDialogReplacementPlan? plan)
    {
        PlayerMenuDialogCandidate[] available = candidates.ToArray();
        PlayerMenuDialogCandidate[] inventory = Matching(available, VanillaMenuActions.Inventory);
        PlayerMenuDialogCandidate[] character = Matching(available, VanillaMenuActions.Character);

        if (inventory.Length != 1 || character.Length != 1)
        {
            plan = null;
            return false;
        }

        plan = new PlayerMenuDialogReplacementPlan(inventory[0].Id, character[0].Id);
        return true;
    }

    private static PlayerMenuDialogCandidate[] Matching(
        IEnumerable<PlayerMenuDialogCandidate> candidates,
        string menuAction)
    {
        return candidates
            .Where(candidate => string.Equals(candidate.MenuAction, menuAction, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}

/// <summary>A registered dialog reduced to the identity needed for replacement planning.</summary>
public readonly record struct PlayerMenuDialogCandidate(string Id, string? MenuAction);
