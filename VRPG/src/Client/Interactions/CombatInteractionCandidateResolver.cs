using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Client.Interactions;

public static class CombatInteractionCandidateResolver
{
    public static InteractionDefinition? Resolve(
        IReadOnlyList<InteractionDefinition> definitions,
        IReadOnlyCollection<string> offeredCodes,
        IReadOnlyCollection<string> targetTags,
        string context,
        double distance,
        bool hasLineOfSight)
    {
        var offered = new HashSet<string>(offeredCodes, StringComparer.OrdinalIgnoreCase);
        var tags = new HashSet<string>(targetTags, StringComparer.OrdinalIgnoreCase);
        InteractionDefinition? best = null;
        foreach (InteractionDefinition definition in definitions)
        {
            if (!offered.Contains(definition.Code)
                || distance > definition.Range
                || definition.RequireLineOfSight && !hasLineOfSight
                || !Contains(definition.AllowedContexts, context)
                || !ContainsAll(tags, definition.TargetTags))
            {
                continue;
            }

            if (best == null
                || definition.Priority > best.Priority
                || definition.Priority == best.Priority
                    && string.Compare(definition.Code, best.Code, StringComparison.OrdinalIgnoreCase) < 0)
            {
                best = definition;
            }
        }
        return best;
    }

    private static bool Contains(IEnumerable<string>? values, string expected)
    {
        foreach (string value in values ?? Array.Empty<string>())
        {
            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool ContainsAll(HashSet<string> actual, IEnumerable<string>? required)
    {
        foreach (string value in required ?? Array.Empty<string>())
        {
            if (!actual.Contains(value)) return false;
        }
        return true;
    }
}
