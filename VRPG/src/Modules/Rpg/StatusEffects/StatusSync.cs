using System.Collections.Generic;
using System;
using Vintagestory.API.Datastructures;

namespace VRPG.Modules.Rpg.StatusEffects;

/// <summary>
/// Wire format for the vrpgStatus WatchedAttributes tree. Durations sync as
/// remaining-time-at-write plus a revision counter because server and client
/// clocks are different domains; the client counts down locally between writes.
/// </summary>
public static class StatusSync
{
    public const string TreeKey = "vrpgStatus";

    public static void Write(ITreeAttribute entityAttributes, IReadOnlyList<StatusEffectInstance> effects)
    {
        int rev = (entityAttributes.GetTreeAttribute(TreeKey)?.GetInt("rev") ?? 0) + 1;
        var tree = new TreeAttribute();
        tree.SetInt("rev", rev);
        var list = new TreeAttribute();
        var aggregate = new Dictionary<string, SyncedStatus>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < effects.Count; i++)
        {
            StatusEffectInstance effect = effects[i];
            if (!aggregate.TryGetValue(effect.Definition.Code, out SyncedStatus? status))
            {
                status = new SyncedStatus { Code = effect.Definition.Code };
                aggregate[effect.Definition.Code] = status;
            }

            status.Stacks += effect.Stacks;
            status.Magnitude = System.Math.Max(status.Magnitude, effect.Magnitude);
            status.RemainingMs = System.Math.Max(status.RemainingMs, (int)(System.Math.Max(0f, effect.RemainingSeconds) * 1000f));
            status.DurationMs = System.Math.Max(status.DurationMs, (int)(System.Math.Max(0f, effect.DurationSeconds) * 1000f));
        }

        foreach (SyncedStatus status in aggregate.Values)
        {
            var node = new TreeAttribute();
            node.SetInt("stacks", status.Stacks);
            node.SetFloat("magnitude", status.Magnitude);
            node.SetInt("remainingMs", status.RemainingMs);
            node.SetInt("durationMs", status.DurationMs);
            list[status.Code] = node;
        }

        tree["effects"] = list;
        entityAttributes[TreeKey] = tree;
    }

    public static List<SyncedStatus> Read(ITreeAttribute? entityAttributes)
    {
        var result = new List<SyncedStatus>();
        ITreeAttribute? tree = entityAttributes?.GetTreeAttribute(TreeKey);
        ITreeAttribute? list = tree?.GetTreeAttribute("effects");
        if (tree == null || list == null)
        {
            return result;
        }

        int rev = tree.GetInt("rev");
        foreach (KeyValuePair<string, IAttribute> pair in list)
        {
            if (pair.Value is not ITreeAttribute node)
            {
                continue;
            }

            result.Add(new SyncedStatus
            {
                Code = pair.Key,
                Stacks = node.GetInt("stacks", 1),
                Magnitude = node.GetFloat("magnitude"),
                RemainingMs = node.GetInt("remainingMs"),
                DurationMs = node.GetInt("durationMs"),
                Rev = rev
            });
        }

        return result;
    }
}

public sealed class SyncedStatus
{
    public string Code = "";
    public int Stacks;
    public float Magnitude;
    public int RemainingMs;
    public int DurationMs;
    public int Rev;
}
