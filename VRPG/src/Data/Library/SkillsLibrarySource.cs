using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class SkillsLibrarySource : ILibrarySource
{
    public string Code => "skills";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (SkillDefinition skill in data.Skills.All)
        {
            yield return new LibraryEntry
            {
                Code = skill.Code,
                Name = skill.Name,
                Category = "classes/skills",
                Summary = skill.Description,
                Tags = skill.Tags,
                Fields = BuildFields(skill)
            };
        }
    }

    private static LibraryField[] BuildFields(SkillDefinition skill)
    {
        var fields = new List<LibraryField>
        {
            new LibraryField("Class", skill.ClassCode),
            new LibraryField("Delivery", skill.Delivery)
        };
        if (string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase))
        {
            fields.Add(new LibraryField("Projectile impact", skill.Projectile.ImpactMode));
        }

        fields.Add(new LibraryField("Damage", $"{skill.Damage.Base:0.##} + {skill.Damage.PerLevel:0.##}/level {skill.Damage.Type}"));
        string resourceRate = string.Equals(skill.Resource.CostMode, "per_second", StringComparison.OrdinalIgnoreCase) ? "/second" : "";
        fields.Add(new LibraryField("Resource", $"{skill.Resource.Base:0.##} + {skill.Resource.PerLevel:0.##}/level {skill.Resource.Type}{resourceRate}"));
        fields.Add(new LibraryField("Cooldown", skill.CooldownSeconds.ToString("0.##") + "s"));
        fields.Add(new LibraryField("Timing", TimingSummary(skill)));
        fields.Add(new LibraryField("Targeting", TargetingSummary(skill)));
        fields.Add(new LibraryField("Max level", skill.MaxLevel.ToString()));
        fields.Add(new LibraryField("Model", string.IsNullOrWhiteSpace(skill.Model) ? "none" : skill.Model));
        fields.Add(new LibraryField("Color", skill.Color));
        return fields.ToArray();
    }

    private static string TimingSummary(SkillDefinition skill)
    {
        if (string.Equals(skill.Timing.Mode, "sequence", StringComparison.OrdinalIgnoreCase))
        {
            return $"{skill.Timing.HitCount} hits, {skill.Timing.HitIntervalSeconds:0.##}s apart";
        }

        if (string.Equals(skill.Timing.Mode, "channel", StringComparison.OrdinalIgnoreCase))
        {
            return $"Channeled, every {skill.Timing.HitIntervalSeconds:0.##}s, max {skill.Timing.MaxDurationSeconds:0.##}s";
        }

        return "Instant";
    }

    private static string TargetingSummary(SkillDefinition skill)
    {
        if (string.Equals(skill.Delivery, "melee_arc", StringComparison.OrdinalIgnoreCase))
        {
            return $"{skill.Range:0.##}m · {skill.Melee.ArcDegrees:0.#}° arc";
        }

        if (string.Equals(skill.Delivery, "melee_line", StringComparison.OrdinalIgnoreCase))
        {
            return $"{skill.Range:0.##}m × {skill.Melee.Width:0.##}m line";
        }

        if (string.Equals(skill.Delivery, "melee_single", StringComparison.OrdinalIgnoreCase))
        {
            return $"{skill.Range:0.##}m single · {skill.Melee.Width:0.##}m aim width";
        }

        return string.Equals(skill.Delivery, "circle", StringComparison.OrdinalIgnoreCase)
            ? $"Self · {skill.Radius:0.##}m radius"
            : $"{skill.Range:0.##}m range · {skill.Radius:0.##}m radius";
    }
}
