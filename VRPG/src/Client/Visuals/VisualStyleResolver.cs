using System;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Client.Visuals;

public sealed class VisualStyle
{
    public int ColorRgba;
    public SkillParticleDefinition Particles = new SkillParticleDefinition();
    public SkillImpactVisualDefinition ImpactVisual = new SkillImpactVisualDefinition();
    public float Radius;
}

public sealed class VisualStyleResolver
{
    private static readonly int NeutralColor = unchecked((int)0xccf2ede4);
    private readonly Func<string, SkillDefinition?> skills;
    private readonly Func<string, StatusEffectDefinition?> statuses;

    public VisualStyleResolver(Func<string, SkillDefinition?> skills, Func<string, StatusEffectDefinition?> statuses)
    {
        this.skills = skills;
        this.statuses = statuses;
    }

    public VisualStyle Resolve(string styleCode, int fallbackColorRgba, float radius)
    {
        SkillDefinition? skill = string.IsNullOrWhiteSpace(styleCode) ? null : skills(styleCode);
        if (skill != null && SkillDefinitionValidator.TryParseColor(skill.Color, out int skillColor))
        {
            return new VisualStyle
            {
                ColorRgba = skillColor,
                Particles = skill.Particles,
                ImpactVisual = skill.ImpactVisual,
                Radius = radius > 0f ? radius : skill.Radius
            };
        }

        StatusEffectDefinition? status = string.IsNullOrWhiteSpace(styleCode) ? null : statuses(styleCode);
        if (status != null && SkillDefinitionValidator.TryParseColor(status.Visual.Color, out int statusColor))
        {
            return new VisualStyle { ColorRgba = statusColor, Radius = radius };
        }

        return new VisualStyle
        {
            ColorRgba = fallbackColorRgba != 0 ? fallbackColorRgba : NeutralColor,
            Radius = radius
        };
    }
}
