using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Client.Visuals;

public sealed class ResolvedFxImpact
{
    public string Preset { get; init; } = "";
    public IReadOnlyList<ResolvedFxLayer> Layers { get; init; } = Array.Empty<ResolvedFxLayer>();
}

public sealed class ResolvedFxLayer
{
    public string Role { get; init; } = "custom";
    public string Model { get; init; } = "quad";
    public int ColorRgba { get; init; }
    public bool ColorByGround { get; init; }
    public float Quantity { get; init; }
    public float SizeMin { get; init; }
    public float SizeMax { get; init; }
    public float LifetimeSeconds { get; init; }
    public float Gravity { get; init; }
    public float Coverage { get; init; }
    public float OriginCoverage { get; init; }
    public float Extent { get; init; }
    public bool ExtentClamped { get; init; }
    public int Glow { get; init; }
    public float DelaySeconds { get; init; }
    public bool TerrainCollision { get; init; }
    public bool Informative { get; init; }
    public SkillFxEvolveDefinition? OpacityEvolve { get; init; }
    public SkillFxEvolveDefinition? SizeEvolve { get; init; }
}

public sealed class FxLayerResolver
{
    private readonly Func<string, SkillFxPresetDefinition?> presets;

    public FxLayerResolver(Func<string, SkillFxPresetDefinition?> presets)
    {
        this.presets = presets;
    }

    public ResolvedFxImpact Resolve(
        SkillImpactVisualDefinition effect,
        float resolvedRadius,
        int skillColorRgba,
        int groundColorRgba)
    {
        float radius = ParticleEffectGeometry.EffectRadius(resolvedRadius);
        SkillFxLayerDefinition[] authored = ResolveAuthoredLayers(effect);
        var resolved = new List<ResolvedFxLayer>(authored.Length);
        foreach (SkillFxLayerDefinition layer in authored)
        {
            if (layer == null) continue;
            if (!SkillDefinitionValidator.IsFxRole(layer.Role)
                || !SkillDefinitionValidator.IsParticleModel(layer.Model))
            {
                continue;
            }

            bool rim = string.Equals(layer.Role, "rim", StringComparison.OrdinalIgnoreCase);
            float interiorExtent = ParticleEffectGeometry.InteriorExtent(
                radius,
                layer.Coverage,
                effect.ExpansionSpeedScale,
                effect.ParticleDurationScale,
                out bool clamped);
            float extent = rim ? radius : interiorExtent;
            clamped = !rim && clamped;
            bool informative = rim || layer.Informative;
            if (!rim && informative && layer.DelaySeconds > 0.2f)
            {
                informative = false;
            }

            resolved.Add(new ResolvedFxLayer
            {
                Role = layer.Role.ToLowerInvariant(),
                Model = layer.Model.ToLowerInvariant(),
                ColorRgba = ResolveColor(layer.Color, skillColorRgba, groundColorRgba),
                ColorByGround = string.Equals(layer.Color, "$ground", StringComparison.OrdinalIgnoreCase),
                Quantity = Math.Max(0f, layer.Quantity),
                SizeMin = Math.Max(0.001f, layer.SizeMin),
                SizeMax = Math.Max(Math.Max(0.001f, layer.SizeMin), layer.SizeMax),
                LifetimeSeconds = Math.Max(0.01f, layer.LifetimeSeconds * effect.ParticleDurationScale),
                Gravity = layer.Gravity,
                Coverage = Math.Max(0f, layer.Coverage),
                OriginCoverage = Math.Clamp(layer.OriginCoverage, 0f, 1f),
                Extent = extent,
                ExtentClamped = clamped,
                Glow = Math.Clamp(layer.Glow, 0, 255),
                DelaySeconds = rim ? 0f : Math.Max(0f, layer.DelaySeconds),
                TerrainCollision = layer.TerrainCollision,
                Informative = informative,
                OpacityEvolve = Clone(layer.OpacityEvolve),
                SizeEvolve = Clone(layer.SizeEvolve)
            });
        }

        return new ResolvedFxImpact { Preset = effect.Preset ?? "", Layers = resolved };
    }

    private SkillFxLayerDefinition[] ResolveAuthoredLayers(SkillImpactVisualDefinition effect)
    {
        SkillFxLayerDefinition[] source;
        if (effect.Layers is { Length: > 0 })
        {
            source = effect.Layers;
        }
        else
        {
            source = presets(effect.Preset ?? "")?.Layers ?? Array.Empty<SkillFxLayerDefinition>();
        }

        var result = new SkillFxLayerDefinition[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == null)
            {
                result[i] = new SkillFxLayerDefinition { Role = "invalid" };
                continue;
            }

            SkillFxLayerDefinition copy = Clone(source[i]);
            if (TryGetOverride(effect.Overrides, copy.Role, out SkillFxLayerOverrideDefinition? layerOverride))
            {
                Apply(copy, layerOverride!);
            }

            result[i] = copy;
        }

        return result;
    }

    private static bool TryGetOverride(
        Dictionary<string, SkillFxLayerOverrideDefinition>? overrides,
        string role,
        out SkillFxLayerOverrideDefinition? value)
    {
        if (overrides != null)
        {
            foreach (KeyValuePair<string, SkillFxLayerOverrideDefinition> entry in overrides)
            {
                if (string.Equals(entry.Key, role, StringComparison.OrdinalIgnoreCase))
                {
                    value = entry.Value;
                    return value != null;
                }
            }
        }

        value = null;
        return false;
    }

    private static int ResolveColor(string token, int skillColor, int groundColor)
    {
        if (string.Equals(token, "$ground", StringComparison.OrdinalIgnoreCase))
        {
            return groundColor;
        }

        if (string.Equals(token, "$skill", StringComparison.OrdinalIgnoreCase))
        {
            return skillColor;
        }

        return SkillDefinitionValidator.TryParseColor(token, out int literal) ? literal : skillColor;
    }

    private static SkillFxLayerDefinition Clone(SkillFxLayerDefinition source)
    {
        return new SkillFxLayerDefinition
        {
            Role = source.Role,
            Model = source.Model,
            Color = source.Color,
            Quantity = source.Quantity,
            SizeMin = source.SizeMin,
            SizeMax = source.SizeMax,
            LifetimeSeconds = source.LifetimeSeconds,
            Gravity = source.Gravity,
            Coverage = source.Coverage,
            OriginCoverage = source.OriginCoverage,
            Glow = source.Glow,
            DelaySeconds = source.DelaySeconds,
            TerrainCollision = source.TerrainCollision,
            Informative = source.Informative,
            OpacityEvolve = Clone(source.OpacityEvolve),
            SizeEvolve = Clone(source.SizeEvolve)
        };
    }

    private static SkillFxEvolveDefinition? Clone(SkillFxEvolveDefinition? source)
    {
        return source == null ? null : new SkillFxEvolveDefinition { Fn = source.Fn, Rate = source.Rate };
    }

    private static void Apply(SkillFxLayerDefinition target, SkillFxLayerOverrideDefinition value)
    {
        target.Model = value.Model ?? target.Model;
        target.Color = value.Color ?? target.Color;
        target.Quantity = value.Quantity ?? target.Quantity;
        target.SizeMin = value.SizeMin ?? target.SizeMin;
        target.SizeMax = value.SizeMax ?? target.SizeMax;
        target.LifetimeSeconds = value.LifetimeSeconds ?? target.LifetimeSeconds;
        target.Gravity = value.Gravity ?? target.Gravity;
        target.Coverage = value.Coverage ?? target.Coverage;
        target.OriginCoverage = value.OriginCoverage ?? target.OriginCoverage;
        target.Glow = value.Glow ?? target.Glow;
        target.DelaySeconds = value.DelaySeconds ?? target.DelaySeconds;
        target.TerrainCollision = value.TerrainCollision ?? target.TerrainCollision;
        target.Informative = value.Informative ?? target.Informative;
        target.OpacityEvolve = value.OpacityEvolve ?? target.OpacityEvolve;
        target.SizeEvolve = value.SizeEvolve ?? target.SizeEvolve;
    }
}
