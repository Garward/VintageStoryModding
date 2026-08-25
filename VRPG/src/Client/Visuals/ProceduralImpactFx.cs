using System;
using System.Collections.Generic;
using VRPG.Config;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>
/// Resolves data-authored impact layers and emits them on the client. The rim
/// is always immediate, truthful to the resolved gameplay radius, and outside
/// the degradable particle budget.
/// </summary>
public sealed class ProceduralImpactFx
{
    private const int MaximumScheduledLayers = 256;
    private readonly ICoreClientAPI capi;
    private readonly ImpactShockwaveRenderer shockwaves;
    private readonly FxLayerResolver resolver;
    private readonly VisualBudget budget;
    private readonly CombatVisualsConfig config;
    private readonly FxTrace trace;
    private readonly List<ScheduledLayer> scheduled = new List<ScheduledLayer>();

    public ProceduralImpactFx(
        ICoreClientAPI capi,
        ImpactShockwaveRenderer shockwaves,
        FxLayerResolver resolver,
        VisualBudget budget,
        CombatVisualsConfig config,
        FxTrace trace)
    {
        this.capi = capi;
        this.shockwaves = shockwaves;
        this.resolver = resolver;
        this.budget = budget;
        this.config = config;
        this.trace = trace;
    }

    public void Spawn(
        VisualStyle style,
        Vec3d center,
        VisualPriority priority,
        string skillCode,
        FxSyncMeasurement? sync)
    {
        SkillImpactVisualDefinition effect = style.ImpactVisual;
        float radius = ParticleEffectGeometry.EffectRadius(style.Radius);
        BlockPos groundPos = ResolveGround(center);
        Block ground = capi.World.BlockAccessor.GetMostSolidBlock(groundPos);
        int groundColor = ground.Id == 0
            ? unchecked((int)0xb8a99b88)
            : ground.GetRandomColor(capi, groundPos, BlockFacing.UP);
        groundColor = (groundColor & 0x00ffffff) | (190 << 24);

        ResolvedFxImpact impact = resolver.Resolve(effect, radius, style.ColorRgba, groundColor);
        long nowMs = capi.ElapsedMilliseconds;
        PendingFxTrace? pendingTrace = trace.Begin(
            skillCode,
            impact.Preset,
            radius,
            center,
            sync,
            budget.Snapshot(nowMs),
            impact.Layers.Count);
        foreach (ResolvedFxLayer layer in impact.Layers)
        {
            var pending = new ScheduledLayer
            {
                Layer = layer,
                Center = center.Clone(),
                Ground = ground,
                Radius = radius,
                Priority = priority,
                DueAtMs = nowMs + (long)Math.Round(layer.DelaySeconds * 1000f),
                Trace = pendingTrace
            };
            if (pending.DueAtMs <= nowMs)
            {
                SpawnLayer(pending, nowMs);
            }
            else
            {
                if (scheduled.Count >= MaximumScheduledLayers)
                {
                    ScheduledLayer dropped = scheduled[0];
                    scheduled.RemoveAt(0);
                    dropped.Trace?.AddLayer(TraceLayer(dropped, 0f, 0f, "scheduled-layer-capacity"));
                }

                scheduled.Add(pending);
            }
        }

        if (effect.Shockwave)
        {
            shockwaves.Add(center, radius, style.ColorRgba, effect.ShockwaveDurationSeconds);
        }

        PlaySounds(effect, center);
        ApplyCameraShake(effect, center);
    }

    public void FlushScheduled()
    {
        long nowMs = capi.ElapsedMilliseconds;
        for (int i = scheduled.Count - 1; i >= 0; i--)
        {
            ScheduledLayer layer = scheduled[i];
            if (layer.DueAtMs > nowMs)
            {
                continue;
            }

            scheduled.RemoveAt(i);
            SpawnLayer(layer, nowMs);
        }
    }

    public void ClearScheduled()
    {
        scheduled.Clear();
    }

    private void SpawnLayer(ScheduledLayer scheduledLayer, long nowMs)
    {
        ResolvedFxLayer layer = scheduledLayer.Layer;
        bool rim = string.Equals(layer.Role, "rim", StringComparison.OrdinalIgnoreCase);
        float quantityScale = rim
            ? 1f
            : budget.QuantityScale(scheduledLayer.Priority, nowMs) * config.Intensity;
        float spawned = rim
            ? SpawnRim(layer, scheduledLayer.Center, scheduledLayer.Radius)
            : SpawnInterior(layer, scheduledLayer.Center, scheduledLayer.Ground, quantityScale);

        if (!rim)
        {
            budget.Record(spawned, nowMs);
        }


        scheduledLayer.Trace?.AddLayer(TraceLayer(
            scheduledLayer,
            quantityScale,
            spawned,
            spawned > 0f
                ? null
                : layer.Quantity <= 0f ? "quantity==0" : "quantityScale==0"));
    }

    private float SpawnRim(ResolvedFxLayer layer, Vec3d center, float radius)
    {
        int samples = ParticleEffectGeometry.RingSamples(radius, layer.Quantity, 1f);
        for (int i = 0; i < samples; i++)
        {
            double angle = Math.PI * 2d * i / samples;
            var point = new Vec3d(
                center.X + Math.Cos(angle) * radius,
                center.Y + 0.045,
                center.Z + Math.Sin(angle) * radius);
            var particles = new SimpleParticleProperties(
                1f,
                1f,
                layer.ColorRgba,
                point.Clone().Add(-0.025, 0, -0.025),
                point.Clone().Add(0.025, 0.08, 0.025),
                new Vec3f(0f, 0.04f, 0f),
                new Vec3f(0f, 0.2f, 0f),
                EngineLifetime(layer.LifetimeSeconds),
                layer.Gravity,
                layer.SizeMin,
                layer.SizeMax,
                ParticleModel(layer.Model))
            {
                VertexFlags = layer.Glow,
                WithTerrainCollision = layer.TerrainCollision
            };
            ApplyEvolution(particles, layer);
            capi.World.SpawnParticles(particles);
        }

        return samples;
    }

    private float SpawnInterior(ResolvedFxLayer layer, Vec3d center, Block ground, float quantityScale)
    {
        float quantity = layer.Quantity * Math.Max(0f, quantityScale);
        if (quantity <= 0f)
        {
            return 0f;
        }

        int samples = Math.Clamp((int)Math.Ceiling(quantity / 3f), 6, 32);
        float perSample = quantity / samples;
        float radialSpeed = layer.Extent / Math.Max(0.08f, layer.LifetimeSeconds);
        float originSpread = ParticleEffectGeometry.OriginSpread(
            layer.Extent,
            layer.OriginCoverage,
            0.05f,
            Math.Max(0.05f, layer.Extent * 0.9f));
        (float verticalMin, float verticalMax) = VerticalVelocity(layer.Role);
        float jitter = Math.Max(0.08f, radialSpeed * 0.08f);
        for (int i = 0; i < samples; i++)
        {
            double angle = Math.PI * 2d * i / samples;
            float x = (float)Math.Cos(angle);
            float z = (float)Math.Sin(angle);
            Vec3d point = center.Clone().Add(x * originSpread, 0.06, z * originSpread);
            var particles = new SimpleParticleProperties(
                perSample * 0.8f,
                perSample * 1.2f,
                layer.ColorRgba,
                point.Clone().Add(-0.06, 0, -0.06),
                point.Clone().Add(0.06, 0.14, 0.06),
                new Vec3f(x * radialSpeed - jitter, verticalMin, z * radialSpeed - jitter),
                new Vec3f(x * radialSpeed + jitter, verticalMax, z * radialSpeed + jitter),
                EngineLifetime(layer.LifetimeSeconds),
                layer.Gravity,
                layer.SizeMin,
                layer.SizeMax,
                ParticleModel(layer.Model))
            {
                VertexFlags = layer.Glow,
                WithTerrainCollision = layer.TerrainCollision,
                Bounciness = layer.TerrainCollision ? 0.08f : 0f,
                ColorByBlock = layer.ColorByGround && ground.Id != 0 ? ground : null
            };
            ApplyEvolution(particles, layer);
            capi.World.SpawnParticles(particles);
        }

        return quantity;
    }

    private float EngineLifetime(float intendedRealSeconds)
    {
        // ParticlePoolQuads multiplies provider life by
        // 5 / sqrt(calendarSpeed / 60). Invert the actual world multiplier so
        // authored values remain real seconds at every calendar speed.
        return ParticleEffectGeometry.ProviderLifetime(
            intendedRealSeconds,
            capi.World.Calendar.SpeedOfTime);
    }

    private BlockPos ResolveGround(Vec3d center)
    {
        int dimension = capi.World.Player.Entity.Pos.Dimension;
        var position = new BlockPos(
            (int)Math.Floor(center.X),
            (int)Math.Floor(center.Y - 0.08),
            (int)Math.Floor(center.Z),
            dimension);
        if (capi.World.BlockAccessor.GetMostSolidBlock(position).Id == 0)
        {
            position.Down();
        }

        return position;
    }

    private static (float Min, float Max) VerticalVelocity(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "debris" => (1.1f, 3.8f),
            "sparks" => (1.3f, 4.6f),
            "fire" => (0.45f, 4.2f),
            "dust" => (0.28f, 0.9f),
            _ => (0.15f, 1.1f)
        };
    }

    private static EnumParticleModel ParticleModel(string model)
    {
        return string.Equals(model, "cube", StringComparison.OrdinalIgnoreCase)
            ? EnumParticleModel.Cube
            : EnumParticleModel.Quad;
    }

    private static void ApplyEvolution(SimpleParticleProperties particles, ResolvedFxLayer layer)
    {
        if (layer.OpacityEvolve != null)
        {
            particles.OpacityEvolve = Evolve(layer.OpacityEvolve);
        }

        if (layer.SizeEvolve != null)
        {
            particles.SizeEvolve = Evolve(layer.SizeEvolve);
        }
    }

    private static EvolvingNatFloat Evolve(SkillFxEvolveDefinition definition)
    {
        EnumTransformFunction function = definition.Fn.ToLowerInvariant() switch
        {
            "quadratic" => EnumTransformFunction.QUADRATIC,
            "root" => EnumTransformFunction.ROOT,
            "sinus" => EnumTransformFunction.SINUS,
            "clamp" => EnumTransformFunction.CLAMPEDPOSITIVESINUS,
            "identical" => EnumTransformFunction.IDENTICAL,
            _ => EnumTransformFunction.LINEAR
        };
        return EvolvingNatFloat.create(function, definition.Rate);
    }

    private void PlaySounds(SkillImpactVisualDefinition effect, Vec3d center)
    {
        foreach (string sound in effect.Sounds)
        {
            if (string.IsNullOrWhiteSpace(sound)) continue;
            capi.World.PlaySoundAt(
                AssetLocation.Create(sound),
                center.X,
                center.Y,
                center.Z,
                null,
                true,
                effect.SoundRange,
                effect.SoundVolume);
        }
    }

    private void ApplyCameraShake(SkillImpactVisualDefinition effect, Vec3d center)
    {
        if (effect.CameraShake <= 0f || effect.CameraShakeRange <= 0f) return;
        double distance = capi.World.Player.Entity.CameraPos.DistanceTo(center);
        float attenuation = GameMath.Clamp(1f - (float)(distance / effect.CameraShakeRange), 0f, 1f);
        if (attenuation > 0f)
        {
            capi.World.AddCameraShake(effect.CameraShake * attenuation);
        }
    }

    private static FxTraceLayer TraceLayer(
        ScheduledLayer scheduledLayer,
        float quantityScale,
        float outputQuantity,
        string? skipReason)
    {
        ResolvedFxLayer layer = scheduledLayer.Layer;
        bool rim = string.Equals(layer.Role, "rim", StringComparison.OrdinalIgnoreCase);
        return new FxTraceLayer
        {
            Role = layer.Role,
            Fired = outputQuantity > 0f,
            SkipReason = skipReason,
            Priority = rim ? VisualPriority.Critical.ToString() : scheduledLayer.Priority.ToString(),
            QuantityScale = quantityScale,
            RequestedQuantity = layer.Quantity,
            OutputQuantity = outputQuantity,
            Color = FxTraceLayer.FormatColor(layer.ColorRgba),
            Coverage = layer.Coverage,
            OriginCoverage = layer.OriginCoverage,
            Extent = layer.Extent,
            ExtentClamped = layer.ExtentClamped,
            Lifetime = layer.LifetimeSeconds,
            Delay = layer.DelaySeconds,
            Informative = layer.Informative,
            Size = new[] { layer.SizeMin, layer.SizeMax }
        };
    }

    private sealed class ScheduledLayer
    {
        public ResolvedFxLayer Layer { get; init; } = null!;
        public Vec3d Center { get; init; } = new Vec3d();
        public Block Ground { get; init; } = null!;
        public float Radius { get; init; }
        public VisualPriority Priority { get; init; }
        public long DueAtMs { get; init; }
        public PendingFxTrace? Trace { get; init; }
    }
}
