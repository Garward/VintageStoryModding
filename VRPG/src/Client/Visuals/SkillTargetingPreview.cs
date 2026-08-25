using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Skills;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>Client-only preview for skills committed when their hotkey is released.</summary>
public sealed class SkillTargetingPreview
{
    private readonly ICoreClientAPI api;
    private SkillDefinition? skill;

    public int Slot { get; private set; } = -1;
    public bool Active => skill != null;
    public bool HasTarget { get; private set; }
    public Vec3d Target { get; private set; } = new Vec3d();
    public string StyleCode => skill?.Code ?? "";
    public float Radius => skill?.Radius ?? 0f;
    public bool ShowsTrajectory => skill?.Projectile?.Ballistic == true;
    public Vec3d LaunchOrigin { get; private set; } = new Vec3d();
    public Vec3d InitialMotion { get; private set; } = new Vec3d();
    public float FlightSeconds { get; private set; }

    public SkillTargetingPreview(ICoreClientAPI api)
    {
        this.api = api;
    }

    public void Begin(int slot, SkillDefinition definition)
    {
        Slot = slot;
        skill = definition;
        Update();
    }

    public void End(int slot)
    {
        if (Slot != slot)
        {
            return;
        }

        Slot = -1;
        skill = null;
        HasTarget = false;
    }

    public void Clear()
    {
        Slot = -1;
        skill = null;
        HasTarget = false;
    }

    public void Update()
    {
        EntityPlayer? player = api.World.Player?.Entity;
        if (skill == null || player == null)
        {
            HasTarget = false;
            return;
        }

        Vec3d eye = new Vec3d(
            player.Pos.X + player.LocalEyePos.X,
            player.Pos.InternalY + player.LocalEyePos.Y,
            player.Pos.Z + player.LocalEyePos.Z);
        Vec3f view = player.Pos.GetViewVector();
        double range = System.Math.Max(1f, skill.Range);
        var end = new Vec3d(
            eye.X + view.X * range,
            eye.Y + view.Y * range,
            eye.Z + view.Z * range);
        BlockSelection? blockSelection = null;
        EntitySelection? entitySelection = null;
        api.World.RayTraceForSelection(
            eye,
            end,
            ref blockSelection,
            ref entitySelection,
            null,
            _ => false);

        HasTarget = blockSelection != null;
        if (blockSelection?.FullPosition != null)
        {
            Target = blockSelection.FullPosition.Clone();
        }

        if (!HasTarget || !ShowsTrajectory)
        {
            FlightSeconds = 0f;
            return;
        }

        SkillProjectileDefinition settings = skill.Projectile;
        double horizontalX = -System.Math.Cos(player.Pos.Yaw) * settings.HorizontalOffset;
        double horizontalZ = System.Math.Sin(player.Pos.Yaw) * settings.HorizontalOffset;
        LaunchOrigin = new Vec3d(
            eye.X + horizontalX + view.X * settings.ForwardOffset,
            eye.Y + settings.VerticalOffset + view.Y * settings.ForwardOffset,
            eye.Z + horizontalZ + view.Z * settings.ForwardOffset);
        BallisticSolution solution = BallisticTrajectory.Solve(
            LaunchOrigin,
            Target,
            settings.Speed,
            settings.MinimumFlightSeconds);
        InitialMotion = solution.InitialMotion;
        FlightSeconds = solution.FlightSeconds;
    }

    public Vec3d TrajectoryPosition(float progress)
    {
        float clamped = GameMath.Clamp(progress, 0f, 1f);
        return BallisticTrajectory.Position(LaunchOrigin, InitialMotion, FlightSeconds * clamped);
    }
}
