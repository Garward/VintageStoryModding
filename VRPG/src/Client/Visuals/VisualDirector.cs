using System;
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

    public CombatVisualsConfig Config { get; }

    public VisualDirector(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config)
    {
        this.capi = capi;
        Config = config;
        styles = new VisualStyleResolver(code => data.Skills.Get(code), code => data.StatusEffects.Get(code));
        skillFx = new SkillFxRenderer(capi);
    }

    public void HandleEvent(CombatVisualEventPacket packet)
    {
        VisualStyle style = styles.Resolve(packet.StyleCode, packet.FallbackColorRgba, packet.Radius);
        var position = new Vec3d(packet.X, packet.Y, packet.Z);
        skillFx.QuantityScale = Config.Intensity;

        switch ((CombatVisualKind)packet.Kind)
        {
            case CombatVisualKind.Impact:
            case CombatVisualKind.Burst:
                skillFx.Burst(style, position);
                break;
            case CombatVisualKind.Ray:
                skillFx.Ray(style, RayStart(packet, style), position);
                break;
            // Damage, Heal, Shield, Break, Counter, Consume, Mark: combat text (Task 9).
            // WindowOpen: crosshair pulse (Task 13).
            default:
                break;
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
