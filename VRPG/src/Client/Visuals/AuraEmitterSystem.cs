using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>
/// Loops a subtle particle aura per synced status so primed targets read at a
/// glance. Caps at the nearest MaxAuraEntities entities; the current target is
/// always included.
/// </summary>
public sealed class AuraEmitterSystem : IDisposable
{
    public const int MaxAuraEntities = 12;
    public const float ScanRadius = 24f;

    private readonly ICoreClientAPI capi;
    private readonly VRPGDataRegistry data;
    private readonly CombatVisualsConfig config;
    private readonly VisualBudget budget;
    private readonly EntityStatusCache cache = new EntityStatusCache();
    private long listenerId;

    public AuraEmitterSystem(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config, VisualBudget budget)
    {
        this.capi = capi;
        this.data = data;
        this.config = config;
        this.budget = budget;
    }

    public void Start()
    {
        listenerId = capi.Event.RegisterGameTickListener(Tick, 250);
    }

    private void Tick(float dt)
    {
        if (!config.StatusAuras || capi.World?.Player?.Entity == null)
        {
            return;
        }

        long nowMs = capi.ElapsedMilliseconds;
        cache.Prune(nowMs);
        EntityPlayer playerEntity = capi.World.Player.Entity;
        Entity? targeted = capi.World.Player.CurrentEntitySelection?.Entity;

        Entity[] nearby = capi.World.GetEntitiesAround(playerEntity.Pos.XYZ, ScanRadius, ScanRadius,
            entity => entity != playerEntity && entity.Alive && entity.WatchedAttributes.HasAttribute(Modules.Rpg.StatusEffects.StatusSync.TreeKey));

        Array.Sort(nearby, (left, right) =>
            left.Pos.SquareDistanceTo(playerEntity.Pos.XYZ).CompareTo(right.Pos.SquareDistanceTo(playerEntity.Pos.XYZ)));

        int emitted = 0;
        bool targetHandled = false;
        for (int i = 0; i < nearby.Length && emitted < MaxAuraEntities; i++)
        {
            if (nearby[i] == targeted)
            {
                targetHandled = true;
            }

            if (EmitFor(nearby[i], nowMs))
            {
                emitted++;
            }
        }

        if (targeted != null && targeted != playerEntity && !targetHandled)
        {
            EmitFor(targeted, nowMs);
        }
    }

    private bool EmitFor(Entity entity, long nowMs)
    {
        IReadOnlyList<ActiveStatus> statuses = cache.Update(entity.EntityId, entity.WatchedAttributes, nowMs);
        bool any = false;
        foreach (ActiveStatus status in statuses)
        {
            StatusEffectDefinition? definition = data.StatusEffects.Get(status.Code);
            AuraFamily? family = definition == null ? null : AuraFamilies.Get(definition.Visual.Aura);
            if (family == null)
            {
                continue;
            }

            any = true;
            float quantity = family.QuantityPerTick
                * Math.Max(1f, status.Stacks * definition!.Visual.AuraIntensityPerStack)
                * config.Intensity
                * budget.QuantityScale(VisualPriority.Others, nowMs);
            float half = Math.Max(0.25f, entity.SelectionBox.XSize * 0.5f);
            float height = Math.Max(0.8f, entity.SelectionBox.YSize);
            Vec3d center = entity.Pos.XYZ;
            capi.World.SpawnParticles(
                quantity,
                family.ColorRgba,
                center.AddCopy(-half, height * 0.2, -half),
                center.AddCopy(half, height * 0.9, half),
                new Vec3f(-0.05f, family.RiseVelocity - 0.03f, -0.05f),
                new Vec3f(0.05f, family.RiseVelocity + 0.08f, 0.05f),
                0.9f,
                family.Gravity,
                family.Scale,
                family.Cube ? EnumParticleModel.Cube : EnumParticleModel.Quad);
            budget.Record(quantity, nowMs);
        }

        return any;
    }

    public void Dispose()
    {
        if (listenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(listenerId);
            listenerId = 0;
        }
    }
}
