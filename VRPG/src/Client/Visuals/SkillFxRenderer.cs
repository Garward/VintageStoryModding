using System;
using VRPG.Data.Definitions;
using VRPG.Config;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

namespace VRPG.Client.Visuals;

public sealed class SkillFxRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly ProceduralImpactFx impactFx;

    public SkillFxRenderer(
        ICoreClientAPI capi,
        ImpactShockwaveRenderer shockwaves,
        FxLayerResolver layerResolver,
        VisualBudget budget,
        CombatVisualsConfig config,
        FxTrace trace)
    {
        this.capi = capi;
        impactFx = new ProceduralImpactFx(capi, shockwaves, layerResolver, budget, config, trace);
    }

    /// <summary>Set by the VisualDirector before each dispatch; 0..1.</summary>
    public float QuantityScale = 1f;

    public float Burst(
        VisualStyle style,
        Vec3d center,
        VisualPriority priority,
        string skillCode,
        FxSyncMeasurement? sync)
    {
        if (style.ImpactVisual.Enabled)
        {
            impactFx.Spawn(style, center, priority, skillCode, sync);
            return 0f;
        }

        SkillParticleDefinition particles = style.Particles;
        float quantity = particles.BurstQuantity * QuantityScale;
        if (quantity <= 0f)
        {
            return 0f;
        }

        int samples = Math.Clamp((int)Math.Ceiling(quantity / 2f), 6, 18);
        float quantityPerSample = quantity / samples;
        double ringRadius = Math.Max(0.35, style.Radius * 0.72);
        float velocity = particles.Velocity;
        for (int i = 0; i < samples; i++)
        {
            double angle = Math.PI * 2 * i / samples;
            float velocityX = (float)Math.Cos(angle) * velocity * 0.55f;
            float velocityZ = (float)Math.Sin(angle) * velocity * 0.55f;
            var point = new Vec3d(
                center.X + Math.Cos(angle) * ringRadius,
                center.Y - Math.Min(0.12, style.Radius * 0.03),
                center.Z + Math.Sin(angle) * ringRadius);
            capi.World.SpawnParticles(
                quantityPerSample,
                style.ColorRgba,
                point.Clone().Add(-0.04, -0.03, -0.04),
                point.Clone().Add(0.04, 0.04, 0.04),
                new Vec3f(velocityX - 0.04f, 0.03f, velocityZ - 0.04f),
                new Vec3f(velocityX + 0.04f, Math.Max(0.08f, velocity * 0.22f), velocityZ + 0.04f),
                particles.LifetimeSeconds * 0.8f,
                particles.Gravity,
                particles.Scale * 0.72f,
                ParticleModel(particles.Model));
        }

        return quantity;
    }

    public void FlushScheduledImpacts() => impactFx.FlushScheduled();

    public void ClearScheduledImpacts() => impactFx.ClearScheduled();

    public void Ray(VisualStyle style, Vec3d start, Vec3d end)
    {
        SkillParticleDefinition particles = style.Particles;
        float quantity = particles.TrailQuantity * QuantityScale;
        if (quantity <= 0f)
        {
            return;
        }

        const int segments = 9;
        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)(segments + 1);
            var point = new Vec3d(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t,
                start.Z + (end.Z - start.Z) * t);
            float life = Math.Max(0.06f, particles.TrailLifetimeSeconds * (0.45f + 0.55f * (float)t));
            capi.World.SpawnParticles(
                quantity,
                style.ColorRgba,
                point,
                point,
                new Vec3f(-0.05f, -0.05f, -0.05f),
                new Vec3f(0.05f, 0.05f, 0.05f),
                life,
                0f,
                particles.Scale * 0.62f,
                ParticleModel(particles.Model));
        }
    }

    public float Circle(VisualStyle style, Vec3d center)
    {
        SkillParticleDefinition particles = style.Particles;
        float radius = ParticleEffectGeometry.EffectRadius(style.Radius);
        int segments = ParticleEffectGeometry.RingSamples(
            radius,
            particles.BurstQuantity,
            QuantityScale);
        if (segments == 0)
        {
            return 0f;
        }

        for (int i = 0; i < segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            var point = new Vec3d(
                center.X + Math.Cos(angle) * radius,
                center.Y - 0.15,
                center.Z + Math.Sin(angle) * radius);
            capi.World.SpawnParticles(
                1f,
                style.ColorRgba,
                point,
                point.Clone().Add(0, 0.15, 0),
                new Vec3f(0f, 0.1f, 0f),
                new Vec3f(0f, 0.45f, 0f),
                particles.LifetimeSeconds,
                particles.Gravity,
                particles.Scale * 0.72f,
                ParticleModel(particles.Model));
        }

        return segments;
    }

    /// <summary>Origin for ray starts, matching the old server-side CastVisualOrigin.</summary>
    public static Vec3d CastVisualOrigin(Vintagestory.API.Common.Entities.Entity caster, SkillParticleDefinition particles)
    {
        Vec3d eye = new Vec3d(
            caster.Pos.X + caster.LocalEyePos.X,
            caster.Pos.InternalY + caster.LocalEyePos.Y,
            caster.Pos.Z + caster.LocalEyePos.Z);
        Vec3f view = caster.Pos.GetViewVector();
        double horizontalX = -Math.Cos(caster.Pos.Yaw) * particles.OriginHorizontalOffset;
        double horizontalZ = Math.Sin(caster.Pos.Yaw) * particles.OriginHorizontalOffset;
        return new Vec3d(
            eye.X + horizontalX + view.X * particles.OriginForwardOffset,
            eye.Y + particles.OriginVerticalOffset + view.Y * particles.OriginForwardOffset,
            eye.Z + horizontalZ + view.Z * particles.OriginForwardOffset);
    }

    private static EnumParticleModel ParticleModel(string model)
    {
        return string.Equals(model, "cube", StringComparison.OrdinalIgnoreCase)
            ? EnumParticleModel.Cube
            : EnumParticleModel.Quad;
    }
}
