using System;
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

    public CombatVisualsConfig Config { get; }
    public GroundAreaStore Areas { get; } = new GroundAreaStore();
    public VisualStyleResolver Styles => styles;
    public CombatTextModel CombatText { get; }
    public HudElementVRPGWindowPulse? WindowPulse { get; set; }
    public VisualBudget Budget { get; } = new VisualBudget();

    public VisualDirector(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config)
    {
        this.capi = capi;
        Config = config;
        styles = new VisualStyleResolver(code => data.Skills.Get(code), code => data.StatusEffects.Get(code));
        skillFx = new SkillFxRenderer(capi);
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
        VisualStyle style = styles.Resolve(packet.StyleCode, packet.FallbackColorRgba, packet.Radius);
        var position = new Vec3d(packet.X, packet.Y, packet.Z);
        Budget.OwnFirst = !string.Equals(Config.DegradationPolicy, "uniform", StringComparison.OrdinalIgnoreCase);
        long nowMs = capi.ElapsedMilliseconds;
        VisualPriority priority = packet.SourceEntityId == capi.World.Player?.Entity?.EntityId
            ? VisualPriority.Own
            : VisualPriority.Others;
        skillFx.QuantityScale = Budget.QuantityScale(priority, nowMs) * Config.Intensity;
        textSettings.MergeNumbers = Config.MergeNumbers;

        switch ((CombatVisualKind)packet.Kind)
        {
            case CombatVisualKind.Impact:
            case CombatVisualKind.Burst:
                skillFx.Burst(style, position);
                Budget.Record(style.Particles.BurstQuantity * skillFx.QuantityScale, nowMs);
                break;
            case CombatVisualKind.Ray:
                skillFx.Ray(style, RayStart(packet, style), position);
                Budget.Record(style.Particles.TrailQuantity * 9f * skillFx.QuantityScale, nowMs);
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
                skillFx.Burst(style, position);
                Budget.Record(style.Particles.BurstQuantity * skillFx.QuantityScale, nowMs);
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

    public void Dispose()
    {
    }
}
