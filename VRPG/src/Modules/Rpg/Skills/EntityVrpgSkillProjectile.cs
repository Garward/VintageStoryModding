using System;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VRPG.Modules.Rpg.Skills;

public sealed class EntityVrpgSkillProjectile : EntityProjectile
{
    private bool exploded;
    private long lastTrailMs;
    private Vec3d? previousServerPosition;
    private Vec3d? explosionPosition;

    public string SkillCode => WatchedAttributes.GetString("vrpgSkillCode", "");
    public int SkillLevel => Math.Max(1, WatchedAttributes.GetInt("vrpgSkillLevel", 1));
    public Vec3d ExplosionPosition => explosionPosition?.Clone() ?? Pos.XYZ.Clone();
    public override bool ApplyGravity => false;

    public void Configure(SkillDefinition skill, int skillLevel)
    {
        WatchedAttributes.SetString("vrpgSkillCode", skill.Code);
        WatchedAttributes.SetInt("vrpgSkillLevel", skillLevel);
        WatchedAttributes.SetString("vrpgSkillModel", skill.Model);
        WatchedAttributes.SetString("vrpgSkillColor", skill.Color);
        WatchedAttributes.SetString("vrpgParticleModel", skill.Particles.Model);
        WatchedAttributes.SetFloat("vrpgParticleTrail", skill.Particles.TrailQuantity);
        WatchedAttributes.SetFloat("vrpgParticleLife", skill.Particles.TrailLifetimeSeconds);
        WatchedAttributes.SetFloat("vrpgParticleScale", skill.Particles.Scale);
        WatchedAttributes.SetFloat("vrpgLifetime", skill.Projectile.LifetimeSeconds);
        WatchedAttributes.SetFloat("vrpgRange", skill.Range);
        WatchedAttributes.SetString("vrpgImpactMode", skill.Projectile.ImpactMode);
        Collectible = false;
        Damage = 0f;
        DropOnImpactChance = 0f;
    }

    public override bool CanCollect(Entity byEntity)
    {
        return false;
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (!Alive)
        {
            return;
        }

        if (Api.Side == EnumAppSide.Client)
        {
            SpawnTrail();
            return;
        }

        Vec3d current = Pos.XYZ.Clone();
        Vec3d previous = previousServerPosition ?? current;
        if (IsGroundImpact())
        {
            if (ReachedGroundTarget(previous, current, out Vec3d target))
            {
                Explode(target);
                return;
            }
        }
        else if (TryFindSweptCreature(previous, current, out Entity hit))
        {
            ImpactOnEntity(hit);
            return;
        }

        previousServerPosition = current;

        float lifetime = Math.Max(0.1f, WatchedAttributes.GetFloat("vrpgLifetime", 5f));
        float range = Math.Max(0.1f, WatchedAttributes.GetFloat("vrpgRange", 32f));
        double originX = WatchedAttributes.GetDouble("vrpgOriginX", Pos.X);
        double originY = WatchedAttributes.GetDouble("vrpgOriginY", Pos.Y);
        double originZ = WatchedAttributes.GetDouble("vrpgOriginZ", Pos.Z);
        double dx = Pos.X - originX;
        double dy = Pos.Y - originY;
        double dz = Pos.Z - originZ;
        if (World.ElapsedMilliseconds - msLaunch >= lifetime * 1000f
            || dx * dx + dy * dy + dz * dz >= range * range)
        {
            Die(EnumDespawnReason.Removed);
        }
    }

    public override void Initialize(EntityProperties properties, ICoreAPI api, long inChunkIndex3d)
    {
        base.Initialize(properties, api, inChunkIndex3d);
        if (api.Side == EnumAppSide.Server)
        {
            WatchedAttributes.SetDouble("vrpgOriginX", Pos.X);
            WatchedAttributes.SetDouble("vrpgOriginY", Pos.Y);
            WatchedAttributes.SetDouble("vrpgOriginZ", Pos.Z);
            previousServerPosition = Pos.XYZ.Clone();
        }
    }

    public void SetGroundTarget(Vec3d target)
    {
        WatchedAttributes.SetBool("vrpgHasImpactTarget", true);
        WatchedAttributes.SetDouble("vrpgImpactTargetX", target.X);
        WatchedAttributes.SetDouble("vrpgImpactTargetY", target.Y);
        WatchedAttributes.SetDouble("vrpgImpactTargetZ", target.Z);
    }

    protected override void ImpactOnEntity(Entity target)
    {
        Explode(EntityCenter(target));
    }

    protected override bool CanHitTarget(Entity target)
    {
        return !IsGroundImpact()
            && target is EntityAgent
            && target is not EntityPlayer
            && target.Alive
            && base.CanHitTarget(target);
    }

    protected override void IsColliding(EntityPos pos, double impactSpeed)
    {
        if (impactSpeed > 0.01)
        {
            Explode(Pos.XYZ.Clone());
        }
    }

    public override void OnTesselation(ref Shape entityShape, string shapePathForLogging)
    {
        string model = WatchedAttributes.GetString("vrpgSkillModel", "");
        if (!string.IsNullOrWhiteSpace(model))
        {
            Shape? customShape = Shape.TryGet(Api, SkillDefinitionValidator.ShapeLocation(model));
            if (customShape != null)
            {
                entityShape = customShape;
                shapePathForLogging = model;
            }
        }

        base.OnTesselation(ref entityShape, shapePathForLogging);
    }

    private void Explode(Vec3d position)
    {
        if (exploded || Api.Side != EnumAppSide.Server)
        {
            return;
        }

        exploded = true;
        explosionPosition = position.Clone();
        Api.ModLoader.GetModSystem<VRPGModSystem>()?.HandleSkillProjectileImpact(this);
        Die(EnumDespawnReason.Removed);
    }

    private bool IsGroundImpact()
    {
        return string.Equals(WatchedAttributes.GetString("vrpgImpactMode", "entity"), "ground", StringComparison.OrdinalIgnoreCase);
    }

    private bool ReachedGroundTarget(Vec3d start, Vec3d end, out Vec3d target)
    {
        target = new Vec3d(
            WatchedAttributes.GetDouble("vrpgImpactTargetX", end.X),
            WatchedAttributes.GetDouble("vrpgImpactTargetY", end.Y),
            WatchedAttributes.GetDouble("vrpgImpactTargetZ", end.Z));
        return WatchedAttributes.GetBool("vrpgHasImpactTarget")
            && DistanceSquaredToSegment(target, start, end) <= 0.24 * 0.24;
    }

    private bool TryFindSweptCreature(Vec3d start, Vec3d end, out Entity hit)
    {
        hit = null!;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        double segmentLength = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (segmentLength < 0.001)
        {
            return false;
        }

        var midpoint = new Vec3d((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5, (start.Z + end.Z) * 0.5);
        float searchRadius = (float)Math.Max(2.0, segmentLength * 0.5 + 2.0);
        Entity[] candidates = World.GetEntitiesAround(midpoint, searchRadius, searchRadius, CanHitTarget);
        double earliest = double.MaxValue;
        double projectileRadius = Math.Max(0.12, Math.Max(CollisionBox.XSize, CollisionBox.ZSize) * 0.5);
        for (int i = 0; i < candidates.Length; i++)
        {
            Entity candidate = candidates[i];
            Cuboidf box = candidate.CollisionBox;
            double minX = candidate.Pos.X + box.X1 - projectileRadius;
            double minY = candidate.Pos.Y + box.Y1 - projectileRadius;
            double minZ = candidate.Pos.Z + box.Z1 - projectileRadius;
            double maxX = candidate.Pos.X + box.X2 + projectileRadius;
            double maxY = candidate.Pos.Y + box.Y2 + projectileRadius;
            double maxZ = candidate.Pos.Z + box.Z2 + projectileRadius;
            if (SegmentIntersectsBox(start, end, minX, minY, minZ, maxX, maxY, maxZ, out double at) && at < earliest)
            {
                earliest = at;
                hit = candidate;
            }
        }

        return earliest < double.MaxValue;
    }

    private static bool SegmentIntersectsBox(
        Vec3d start,
        Vec3d end,
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ,
        out double at)
    {
        double enter = 0.0;
        double exit = 1.0;
        if (!ClipAxis(start.X, end.X - start.X, minX, maxX, ref enter, ref exit)
            || !ClipAxis(start.Y, end.Y - start.Y, minY, maxY, ref enter, ref exit)
            || !ClipAxis(start.Z, end.Z - start.Z, minZ, maxZ, ref enter, ref exit))
        {
            at = 0.0;
            return false;
        }

        at = enter;
        return true;
    }

    private static bool ClipAxis(double origin, double delta, double minimum, double maximum, ref double enter, ref double exit)
    {
        if (Math.Abs(delta) < 0.000001)
        {
            return origin >= minimum && origin <= maximum;
        }

        double first = (minimum - origin) / delta;
        double second = (maximum - origin) / delta;
        if (first > second) (first, second) = (second, first);
        enter = Math.Max(enter, first);
        exit = Math.Min(exit, second);
        return enter <= exit;
    }

    private static double DistanceSquaredToSegment(Vec3d point, Vec3d start, Vec3d end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double dz = end.Z - start.Z;
        double lengthSquared = dx * dx + dy * dy + dz * dz;
        if (lengthSquared <= 0.000001)
        {
            return point.SquareDistanceTo(start);
        }

        double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy + (point.Z - start.Z) * dz) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        double nearestX = start.X + dx * t;
        double nearestY = start.Y + dy * t;
        double nearestZ = start.Z + dz * t;
        double offsetX = point.X - nearestX;
        double offsetY = point.Y - nearestY;
        double offsetZ = point.Z - nearestZ;
        return offsetX * offsetX + offsetY * offsetY + offsetZ * offsetZ;
    }

    private static Vec3d EntityCenter(Entity entity)
    {
        return new Vec3d(
            entity.Pos.X,
            entity.Pos.Y + Math.Max(0.1f, entity.CollisionBox.YSize) * 0.5,
            entity.Pos.Z);
    }

    private void SpawnTrail()
    {
        long now = World.ElapsedMilliseconds;
        if (now - lastTrailMs < 65)
        {
            return;
        }

        lastTrailMs = now;
        float quantity = WatchedAttributes.GetFloat("vrpgParticleTrail", 0f);
        string colorHex = WatchedAttributes.GetString("vrpgSkillColor", "#ffffff");
        if (quantity <= 0f || !SkillDefinitionValidator.TryParseColor(colorHex, out int color))
        {
            return;
        }

        string particleModel = WatchedAttributes.GetString("vrpgParticleModel", "quad");
        World.SpawnParticles(
            quantity,
            color,
            Pos.XYZ.Clone().Add(-0.08, -0.08, -0.08),
            Pos.XYZ.Clone().Add(0.08, 0.08, 0.08),
            new Vec3f(-0.05f, -0.05f, -0.05f),
            new Vec3f(0.05f, 0.05f, 0.05f),
            WatchedAttributes.GetFloat("vrpgParticleLife", 0.5f),
            0f,
            WatchedAttributes.GetFloat("vrpgParticleScale", 0.3f),
            string.Equals(particleModel, "cube", StringComparison.OrdinalIgnoreCase) ? EnumParticleModel.Cube : EnumParticleModel.Quad);
    }
}
