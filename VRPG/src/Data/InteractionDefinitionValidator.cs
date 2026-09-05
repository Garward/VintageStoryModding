using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data;

public static class InteractionDefinitionValidator
{
    private static readonly HashSet<string> Contexts = new(StringComparer.OrdinalIgnoreCase)
    {
        "adventure", "home", "build"
    };

    public static void ValidateOrThrow(IReadOnlyCollection<InteractionDefinition> definitions)
    {
        IReadOnlyList<string> errors = CollectErrors(definitions);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "VRPG interaction data validation failed:\n- " + string.Join("\n- ", errors));
        }
    }

    public static IReadOnlyList<string> CollectErrors(IReadOnlyCollection<InteractionDefinition> definitions)
    {
        var errors = new List<string>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (InteractionDefinition definition in definitions)
        {
            string code = definition.Code?.Trim() ?? "";
            string label = string.IsNullOrWhiteSpace(code) ? "<missing code>" : code;
            if (string.IsNullOrWhiteSpace(code) || !code.Contains(':'))
            {
                errors.Add($"interaction '{label}' must have a namespaced code.");
            }
            else if (!codes.Add(code))
            {
                errors.Add($"duplicate interaction code '{code}'.");
            }

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                errors.Add($"interaction '{label}' requires a name.");
            }
            if (string.IsNullOrWhiteSpace(definition.PromptKey))
            {
                errors.Add($"interaction '{label}' requires a promptKey.");
            }
            if (string.IsNullOrWhiteSpace(definition.HandlerCode)
                || !definition.HandlerCode.Contains(':'))
            {
                errors.Add($"interaction '{label}' requires a namespaced handlerCode.");
            }
            if (!float.IsFinite(definition.Range) || definition.Range < 0.25f || definition.Range > 32f)
            {
                errors.Add($"interaction '{label}' range must be from 0.25 through 32 blocks.");
            }
            if (!float.IsFinite(definition.CooldownSeconds) || definition.CooldownSeconds < 0f)
            {
                errors.Add($"interaction '{label}' cooldownSeconds cannot be negative.");
            }
            if (definition.AllowedContexts == null || definition.AllowedContexts.Length == 0)
            {
                errors.Add($"interaction '{label}' must allow at least one context.");
            }
            else
            {
                foreach (string context in definition.AllowedContexts)
                {
                    if (!Contexts.Contains(context ?? ""))
                    {
                        errors.Add($"interaction '{label}' has unknown context '{context}'.");
                    }
                }
            }
        }

        return errors;
    }
}
