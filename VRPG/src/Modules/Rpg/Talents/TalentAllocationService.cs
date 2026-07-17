using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Talents;

public sealed class TalentAllocationService
{
    private readonly TalentTreeCatalog talents;
    private readonly RpgPlayerStore playerStore;
    private readonly RpgResourceService resources;
    private readonly CombatStateService combatStates;

    public TalentAllocationService(TalentTreeCatalog talents, RpgPlayerStore playerStore, RpgResourceService resources, CombatStateService combatStates)
    {
        this.talents = talents;
        this.playerStore = playerStore;
        this.resources = resources;
        this.combatStates = combatStates;
    }

    public bool TryAllocate(IServerPlayer player, string code, out string message)
    {
        if (combatStates.IsInCombat(player, out float remainingSeconds))
        {
            message = $"Talents are locked during combat. Try again in {Math.Ceiling(remainingSeconds):0} second(s).";
            return false;
        }

        TalentNodeDefinition? talent = talents.Get(NormalizeCode(code));
        if (talent == null)
        {
            message = "Unknown talent: " + code;
            return false;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        if (state.HasTalent(talent.Code))
        {
            message = "Talent already allocated: " + talent.Name;
            return false;
        }

        int cost = talent.Starter ? 0 : Math.Max(1, talent.Cost);
        if (state.UnspentTalentPoints < cost)
        {
            message = $"Not enough talent points. Need {cost}, have {state.UnspentTalentPoints}.";
            return false;
        }

        if (!CanConnect(state, talent))
        {
            message = state.Talents.Count == 0
                ? "Choose one of the three starting foundations first."
                : "Talent is not connected to the allocated path.";
            return false;
        }

        state.UnspentTalentPoints -= cost;
        state.Talents.Add(talent.Code);
        state.Talents.Sort(StringComparer.OrdinalIgnoreCase);
        playerStore.Save();
        resources.SendSnapshot(player);
        message = "Allocated " + talent.Name + ".";
        return true;
    }

    public bool TryReset(IServerPlayer player, out string message)
    {
        if (combatStates.IsInCombat(player, out float remainingSeconds))
        {
            message = $"Talents are locked during combat. Try again in {Math.Ceiling(remainingSeconds):0} second(s).";
            return false;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        int refund = 0;
        for (int i = 0; i < state.Talents.Count; i++)
        {
            TalentNodeDefinition? talent = talents.Get(state.Talents[i]);
            refund += talent?.Starter == true ? 0 : Math.Max(1, talent?.Cost ?? 1);
        }

        state.Talents.Clear();
        state.UnspentTalentPoints += refund;
        playerStore.Save();
        resources.SendSnapshot(player);
        message = "Refunded " + refund + " talent point(s).";
        return true;
    }

    public bool TryApplyPlan(IServerPlayer player, string[] allocateCodes, string[] refundCodes, out string message)
    {
        if (combatStates.IsInCombat(player, out float remainingSeconds))
        {
            message = $"Talents are locked during combat. Try again in {Math.Ceiling(remainingSeconds):0} second(s).";
            return false;
        }

        allocateCodes ??= Array.Empty<string>();
        refundCodes ??= Array.Empty<string>();
        if (allocateCodes.Length + refundCodes.Length == 0)
        {
            message = "No talent changes were queued.";
            return false;
        }
        if (allocateCodes.Length + refundCodes.Length > 512)
        {
            message = "Too many talent changes in one plan.";
            return false;
        }

        var allocate = NormalizeSet(allocateCodes);
        var refund = NormalizeSet(refundCodes);
        if (allocate.Overlaps(refund))
        {
            message = "A talent cannot be allocated and refunded in the same plan.";
            return false;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        var current = NormalizeSet(state.Talents.ToArray());
        foreach (string code in refund)
        {
            if (!current.Contains(code))
            {
                message = "Cannot refund an unallocated talent: " + code;
                return false;
            }
        }
        foreach (string code in allocate)
        {
            if (current.Contains(code))
            {
                message = "Talent is already allocated: " + code;
                return false;
            }
        }
        if (state.RespecPoints < refund.Count)
        {
            message = $"Not enough respec points. Need {refund.Count}, have {state.RespecPoints}.";
            return false;
        }

        int refundedTalentPoints = 0;
        foreach (string code in refund)
        {
            TalentNodeDefinition? node = talents.Get(code);
            if (node == null)
            {
                message = "Unknown refunded talent: " + code;
                return false;
            }
            refundedTalentPoints += node.Starter ? 0 : Math.Max(1, node.Cost);
        }

        int allocationCost = 0;
        foreach (string code in allocate)
        {
            TalentNodeDefinition? node = talents.Get(code);
            if (node == null)
            {
                message = "Unknown allocated talent: " + code;
                return false;
            }
            allocationCost += node.Starter ? 0 : Math.Max(1, node.Cost);
        }
        int available = state.UnspentTalentPoints + refundedTalentPoints;
        if (allocationCost > available)
        {
            message = $"Not enough talent points. Need {allocationCost}, have {available} after refunds.";
            return false;
        }

        var final = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        final.ExceptWith(refund);
        final.UnionWith(allocate);
        if (!ValidateConnectedFinalTree(final, out message))
        {
            return false;
        }

        state.Talents.Clear();
        foreach (string code in final)
        {
            TalentNodeDefinition node = talents.Get(code)!;
            state.Talents.Add(node.Code);
        }
        state.Talents.Sort(StringComparer.OrdinalIgnoreCase);
        state.UnspentTalentPoints = available - allocationCost;
        state.RespecPoints -= refund.Count;
        playerStore.Save();
        resources.RefreshAfterTalentTreeSave(player);
        message = $"Applied talent plan: {allocate.Count} allocated, {refund.Count} refunded.";
        return true;
    }

    private bool ValidateConnectedFinalTree(HashSet<string> final, out string message)
    {
        message = "";
        if (final.Count == 0)
        {
            return true;
        }

        string starter = "";
        int starterCount = 0;
        foreach (string code in final)
        {
            TalentNodeDefinition? node = talents.Get(code);
            if (node == null)
            {
                message = "Unknown talent in final plan: " + code;
                return false;
            }
            if (node.Starter)
            {
                starter = code;
                starterCount++;
            }
        }
        if (starterCount != 1)
        {
            message = starterCount == 0 ? "The talent plan must include one starting route." : "Only one starting route may be allocated.";
            return false;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { starter };
        var frontier = new Queue<string>();
        frontier.Enqueue(starter);
        while (frontier.Count > 0)
        {
            string current = frontier.Dequeue();
            foreach (string candidate in final)
            {
                if (visited.Contains(candidate) || !AreLinked(current, candidate)) continue;
                visited.Add(candidate);
                frontier.Enqueue(candidate);
            }
        }
        if (visited.Count != final.Count)
        {
            message = "Refunding that node would disconnect part of the allocated tree.";
            return false;
        }
        return true;
    }

    private bool AreLinked(string first, string second)
    {
        TalentNodeDefinition? a = talents.Get(first);
        TalentNodeDefinition? b = talents.Get(second);
        if (a == null || b == null) return false;
        for (int i = 0; i < a.Links.Length; i++) if (SameCode(a.Links[i], second)) return true;
        for (int i = 0; i < b.Links.Length; i++) if (SameCode(b.Links[i], first)) return true;
        return false;
    }

    private static HashSet<string> NormalizeSet(string[] codes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < codes.Length; i++) result.Add(NormalizeCode(codes[i]));
        return result;
    }

    private bool CanConnect(RpgPlayerState state, TalentNodeDefinition talent)
    {
        if (state.Talents.Count == 0)
        {
            return talent.Starter;
        }

        if (talent.Starter)
        {
            return false;
        }

        for (int i = 0; i < talent.Links.Length; i++)
        {
            if (state.HasTalent(NormalizeCode(talent.Links[i])))
            {
                return true;
            }
        }

        foreach (TalentNodeDefinition other in talents.All)
        {
            if (!state.HasTalent(other.Code))
            {
                continue;
            }

            for (int i = 0; i < other.Links.Length; i++)
            {
                if (SameCode(other.Links[i], talent.Code))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SameCode(string left, string right)
    {
        return string.Equals(NormalizeCode(left), NormalizeCode(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCode(string code)
    {
        string value = (code ?? "").Trim();
        return value.Contains(':') ? value : "vrpg:" + value;
    }
}
