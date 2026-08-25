using System;
using System.Collections.Generic;
using VRPG.Client;
using VRPG.Data;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>
/// Sole client-side consumer of combat visual channels. Owns budgets and
/// options; renderers never decide what to skip on their own.
/// </summary>
public sealed class VisualDirector : IDisposable
{
    private readonly ICoreClientAPI capi;
    private readonly VisualStyleResolver styles;
    private readonly SkillFxRenderer skillFx;
    private readonly CombatTextSettings textSettings;
    private readonly List<PendingImpact> pendingImpacts = new List<PendingImpact>();
    private readonly FxSyncTracker syncTracker = new FxSyncTracker();

    public CombatVisualsConfig Config { get; }
    public GroundAreaStore Areas { get; } = new GroundAreaStore();
    public VisualStyleResolver Styles => styles;
    public CombatTextModel CombatText { get; }
    public ImpactShockwaveRenderer Shockwaves { get; }
    public HudElementVRPGWindowPulse? WindowPulse { get; set; }
    public VisualBudget Budget { get; } = new VisualBudget();
    public FxTrace Trace { get; }

    public VisualDirector(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config)
    {
        this.capi = capi;
        Config = config;
        styles = new VisualStyleResolver(code => data.Skills.Get(code), code => data.StatusEffects.Get(code));
        Shockwaves = new ImpactShockwaveRenderer(capi);
        Shockwaves.BeforeRender = FlushPendingImpacts;
        Trace = new FxTrace(capi);
        Trace.RegisterCommand();
        var layerResolver = new FxLayerResolver(code => data.SkillFxPresets.Get(code));
        skillFx = new SkillFxRenderer(capi, Shockwaves, layerResolver, Budget, config, Trace);
        textSettings = new CombatTextSettings { MergeNumbers = config.MergeNumbers };
        CombatText = new CombatTextModel(textSettings);
    }

    public void HandleAreaUpsert(GroundAreaUpsertPacket packet)
    {
        Areas.Upsert(packet, capi.ElapsedMilliseconds);
    }

    public void HandleAreaRemove(GroundAreaRemovePacket packet)
    {
        Areas.Remove(packet.Id);
    }

    public void HandleEvent(CombatVisualEventPacket packet)
    {
        long receivedAtMs = capi.ElapsedMilliseconds;
        FxSyncObservation? observation = syncTracker.Observe(
            packet.StyleCode,
            packet.ServerEventMs,
            receivedAtMs);
        if (ShouldSynchronizeToCarrier(packet))
        {
            if (pendingImpacts.Count >= 32)
            {
                PendingImpact oldest = pendingImpacts[0];
                pendingImpacts.RemoveAt(0);
                DispatchEvent(oldest.Packet, oldest.SyncObservation, null);
            }

            pendingImpacts.Add(new PendingImpact(packet, receivedAtMs, observation)
            {
                SeenCarrier = capi.World.GetEntityById(packet.TargetEntityId) != null
            });
            return;
        }

        DispatchEvent(packet, observation, null);
    }

    private void DispatchEvent(
        CombatVisualEventPacket packet,
        FxSyncObservation? observation,
        long? carrierLandMs)
    {
        VisualStyle style = styles.Resolve(packet.StyleCode, packet.FallbackColorRgba, packet.Radius);
        var position = new Vec3d(packet.X, packet.Y, packet.Z);
        Budget.OwnFirst = !string.Equals(Config.DegradationPolicy, "uniform", StringComparison.OrdinalIgnoreCase);
        long nowMs = capi.ElapsedMilliseconds;
        VisualPriority priority = packet.SourceEntityId == capi.World.Player?.Entity?.EntityId
            ? VisualPriority.Own
            : VisualPriority.Others;
        skillFx.QuantityScale = Budget.QuantityScale(priority, nowMs) * Config.Intensity;
        textSettings.MergeNumbers = Config.MergeNumbers;
        CombatVisualKind kind = (CombatVisualKind)packet.Kind;
        bool impactEvent = kind is CombatVisualKind.Impact or CombatVisualKind.Burst or CombatVisualKind.Consume;
        FxSyncMeasurement? sync = observation == null || !style.ImpactVisual.Enabled || !impactEvent
            ? null
            : syncTracker.Complete(observation, nowMs, carrierLandMs);

        switch (kind)
        {
            case CombatVisualKind.Impact:
            case CombatVisualKind.Burst:
                Budget.Record(skillFx.Burst(style, position, priority, packet.StyleCode, sync), nowMs);
                break;
            case CombatVisualKind.Ray:
                skillFx.Ray(style, RayStart(packet, style), position);
                Budget.Record(style.Particles.TrailQuantity * 9f * skillFx.QuantityScale, nowMs);
                break;
            case CombatVisualKind.Circle:
                Budget.Record(skillFx.Circle(style, position), nowMs);
                break;
            case CombatVisualKind.Damage:
                if (Config.CombatTextEnabled && Config.DamageNumbers)
                {
                    CombatText.AddNumber(packet.TargetEntityId, packet.DamageType, packet.Magnitude,
                        (packet.Flags & (int)CombatVisualFlags.Crit) != 0,
                        packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
                }

                break;
            case CombatVisualKind.Heal:
                if (Config.CombatTextEnabled && Config.DamageNumbers)
                {
                    CombatText.AddNumber(packet.TargetEntityId, VisualDamageTypes.Heal, packet.Magnitude, false,
                        packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
                }

                break;
            case CombatVisualKind.Shield:
                if (Config.CombatTextEnabled && Config.DamageNumbers)
                {
                    CombatText.AddNumber(packet.TargetEntityId, VisualDamageTypes.Cold, packet.Magnitude, false,
                        packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
                }

                break;
            case CombatVisualKind.Break:
                AddWord(packet, "BREAK", 110);
                break;
            case CombatVisualKind.Counter:
                AddWord(packet, "COUNTER", 105);
                break;
            case CombatVisualKind.Consume:
                AddWord(packet, "CONSUMED", 100);
                Budget.Record(skillFx.Burst(style, position, priority, packet.StyleCode, sync), nowMs);
                break;
            case CombatVisualKind.Mark:
                AddWord(packet, "MARKED", 95);
                break;
            case CombatVisualKind.WindowOpen:
                if (packet.TargetEntityId == capi.World.Player?.Entity?.EntityId)
                {
                    WindowPulse?.Trigger(style.ColorRgba, packet.Magnitude);
                }

                break;
            default:
                break;
        }
    }

    private void AddWord(CombatVisualEventPacket packet, string word, int priority)
    {
        if (Config.CombatTextEnabled && Config.EventWords)
        {
            CombatText.AddWord(packet.TargetEntityId, word, priority, packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
        }
    }

    private Vec3d RayStart(CombatVisualEventPacket packet, VisualStyle style)
    {
        Entity? source = packet.SourceEntityId != 0 ? capi.World.GetEntityById(packet.SourceEntityId) : null;
        return source != null
            ? SkillFxRenderer.CastVisualOrigin(source, style.Particles)
            : new Vec3d(packet.X, packet.Y + 1.2, packet.Z);
    }

    private bool ShouldSynchronizeToCarrier(CombatVisualEventPacket packet)
    {
        // Always defer flagged events to the OIT callback. Pos can temporarily
        // say "at target" during a game tick before the interpolation renderer
        // restores the position that the player will actually see this frame.
        return (packet.Flags & (int)CombatVisualFlags.SynchronizeToCarrier) != 0
            && packet.TargetEntityId != 0;
    }

    private void FlushPendingImpacts()
    {
        skillFx.FlushScheduledImpacts();
        long nowMs = capi.ElapsedMilliseconds;
        for (int i = pendingImpacts.Count - 1; i >= 0; i--)
        {
            PendingImpact pending = pendingImpacts[i];
            Entity? carrier = capi.World.GetEntityById(pending.Packet.TargetEntityId);
            if (carrier != null)
            {
                pending.SeenCarrier = true;
            }

            bool reachedContact = carrier != null
                && ImpactCarrierGeometry.ReachedImpact(
                    carrier.Pos.XYZ,
                    new Vec3d(pending.Packet.X, pending.Packet.Y, pending.Packet.Z));
            bool carrierRemoved = carrier == null && pending.SeenCarrier;
            bool spawnPacketMissing = carrier == null
                && !pending.SeenCarrier
                && nowMs - pending.ReceivedAtMs >= 250;
            bool timedOut = nowMs - pending.ReceivedAtMs >= 1000;
            if (!reachedContact && !carrierRemoved && !spawnPacketMissing && !timedOut)
            {
                continue;
            }

            pendingImpacts.RemoveAt(i);
            DispatchEvent(pending.Packet, pending.SyncObservation, nowMs);
        }
    }

    public void Dispose()
    {
        Shockwaves.BeforeRender = null;
        pendingImpacts.Clear();
        skillFx.ClearScheduledImpacts();
        Trace.Dispose();
        Shockwaves.Dispose();
    }

    private sealed class PendingImpact
    {
        public CombatVisualEventPacket Packet { get; }
        public long ReceivedAtMs { get; }
        public bool SeenCarrier { get; set; }
        public FxSyncObservation? SyncObservation { get; }

        public PendingImpact(
            CombatVisualEventPacket packet,
            long receivedAtMs,
            FxSyncObservation? syncObservation)
        {
            Packet = packet;
            ReceivedAtMs = receivedAtMs;
            SeenCarrier = false;
            SyncObservation = syncObservation;
        }
    }
}
