using System.Collections.Generic;
using System.Linq;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class StatusEffectsLibrarySource : ILibrarySource
{
    public string Code => "status-effects";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (StatusEffectDefinition effect in data.StatusEffects.All)
        {
            yield return new LibraryEntry
            {
                Code = effect.Code,
                Name = effect.Name,
                Category = "status_effects",
                Summary = effect.Description,
                Tags = BuildTags(effect),
                Fields = BuildFields(effect)
            };
        }
    }

    private static string[] BuildTags(StatusEffectDefinition effect)
    {
        return new[] { "status", effect.Kind, effect.Polarity }
            .Concat(effect.Tags ?? System.Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LibraryField[] BuildFields(StatusEffectDefinition effect)
    {
        var fields = new List<LibraryField>
        {
            new LibraryField("Kind", effect.Kind),
            new LibraryField("Polarity", effect.Polarity),
            new LibraryField("Trigger Damage", string.IsNullOrWhiteSpace(effect.TriggerDamageType) ? "manual or scripted" : effect.TriggerDamageType),
            new LibraryField("Proc Mode", effect.ProcMode),
            new LibraryField("Chance Stat", string.IsNullOrWhiteSpace(effect.ChanceStat) ? "none" : effect.ChanceStat),
            new LibraryField("Duration Stat", string.IsNullOrWhiteSpace(effect.DurationStat) ? "none" : effect.DurationStat),
            new LibraryField("Damage Profile", string.IsNullOrWhiteSpace(effect.DamageProfile) ? "none" : effect.DamageProfile),
            new LibraryField("Duration", effect.DefaultDurationSeconds.ToString("0.##") + "s"),
            new LibraryField("Stacking", effect.StackMode + " up to " + System.Math.Max(1, effect.MaxStacks))
        };

        if (effect.TickSeconds > 0)
        {
            fields.Add(new LibraryField("Tick", effect.TickSeconds.ToString("0.##") + "s"));
        }

        if (!string.IsNullOrWhiteSpace(effect.VisualHint))
        {
            fields.Add(new LibraryField("Visual", effect.VisualHint));
        }

        if (effect.Modifiers.Length > 0)
        {
            fields.Add(new LibraryField("Modifiers", string.Join(", ", effect.Modifiers.Select(FormatModifier))));
        }

        return fields.ToArray();
    }

    private static string FormatModifier(StatModifierDefinition modifier)
    {
        return $"{modifier.Operation} {modifier.Min:0.###}-{modifier.Max:0.###} {modifier.Stat}";
    }
}
