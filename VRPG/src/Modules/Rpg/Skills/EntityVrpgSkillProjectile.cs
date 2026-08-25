using System;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;
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
    private string preparedTextureModel = "";

    public string SkillCode => WatchedAttributes.GetString("vrpgSkillCode", "");
    public int SkillLevel => Math.Max(1, WatchedAttributes.GetInt("vrpgSkillLevel", 1));
    public Vec3d ExplosionPosition => explosionPosition?.Clone() ?? Pos.XYZ.Clone();
    public override bool ApplyGravity => WatchedAttributes.GetBool("vrpgBallistic");

    public void Configure(SkillDefinition skill, int skillLevel, string model)
    {
        WatchedAttributes.SetString("vrpgSkillCode", skill.Code);
        WatchedAttributes.SetInt("vrpgSkillLevel", skillLevel);
        WatchedAttributes.SetString("vrpgSkillModel", model);
        WatchedAttributes.SetString("vrpgSkillColor", skill.Color);
        WatchedAttributes.SetString("vrpgParticleModel", skill.Particles.Model);
        WatchedAttributes.SetFloat("vrpgParticleTrail", skill.Particles.TrailQuantity);
        WatchedAttributes.SetFloat("vrpgParticleLife", skill.Particles.TrailLifetimeSeconds);
        WatchedAttributes.SetFloat("vrpgParticleScale", skill.Particles.Scale);
        WatchedAttributes.SetBool("vrpgBallistic", skill.Projectile.Ballistic);
        WatchedAttributes.SetString("vrpgRotationMode", skill.Projectile.RotationMode);
        bool targetedDrop = string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase);
        WatchedAttributes.SetBool("vrpgTargetedDrop", targetedDrop);
        WatchedAttributes.SetFloat("vrpgLifetime", targetedDrop ? skill.TargetedDrop.LifetimeSeconds : skill.Projectile.LifetimeSeconds);
        float despawnRange = targetedDrop
            ? Math.Max(skill.Range, skill.TargetedDrop.Height + 2f)
            : skill.Projectile.Ballistic ? skill.Range + 12f : skill.Range;
        WatchedAttributes.SetFloat("vrpgRange", despawnRange);
        WatchedAttributes.SetString("vrpgImpactMode", skill.Projectile.ImpactMode);
        WatchedAttributes.SetFloat("vrpgCreatureCollisionRadius", skill.Projectile.CreatureCollisionRadius);
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
        if (IsTargetedDrop())
        {
            // EntityProjectile aligns roll to its flight vector. That is useful
            // for arrows, but turns an upright block model sideways while falling.
            Pos.Pitch = 0f;
            Pos.Yaw = 0f;
            Pos.Roll = 0f;
        }

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
        // "either" means a creature intercepts the carrier before its authored
        // ground destination wins. Ground-only projectiles skip this branch.
        if (CanImpactCreatures() && TryFindSweptCreature(previous, current, out Entity hit))
        {
            ImpactOnEntity(hit);
            return;
        }
        if (HasImpactTarget())
        {
            if (ReachedGroundTarget(previous, current, out Vec3d target))
            {
                Explode(target);
                return;
            }
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
        return CanImpactCreatures()
            && target is EntityAgent
            && target is not EntityPlayer
            && target.Alive
            && base.CanHitTarget(target);
    }

    protected override void IsColliding(EntityPos pos, double impactSpeed)
    {
        // Passive physics can zero motion before reporting the first collision.
        // Ground-targeted projectiles must still resolve instead of remaining
        // stuck until their despawn timer elapses.
        if (IsGroundImpact() || impactSpeed > 0.01)
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
                PrepareShapeTextures(customShape, model);
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

    private bool CanImpactCreatures()
    {
        string mode = WatchedAttributes.GetString("vrpgImpactMode", "entity");
        return string.Equals(mode, "entity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "either", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasImpactTarget()
    {
        return WatchedAttributes.GetBool("vrpgHasImpactTarget");
    }

    private bool IsTargetedDrop()
    {
        return WatchedAttributes.GetBool("vrpgTargetedDrop");
    }

    public override void SetInitialRotation()
    {
        SetRotationFromMotion();
    }

    public override void SetRotationFromMotion()
    {
        string mode = WatchedAttributes.GetString("vrpgRotationMode", "flight");
        if (string.Equals(mode, "stable", StringComparison.OrdinalIgnoreCase))
        {
            Pos.Pitch = 0f;
            Pos.Yaw = 0f;
            Pos.Roll = 0f;
            return;
        }

        if (string.Equals(mode, "tumble", StringComparison.OrdinalIgnoreCase))
        {
            float seconds = (World.ElapsedMilliseconds - msLaunch) / 1000f;
            Pos.Pitch = seconds * 3.7f;
            Pos.Yaw = seconds * 5.1f;
            Pos.Roll = seconds * 4.3f;
            return;
        }

        base.SetRotationFromMotion();
    }

    private void PrepareShapeTextures(Shape shape, string model)
    {
        if (Api is not ICoreClientAPI capi
            || string.Equals(preparedTextureModel, model, StringComparison.Ordinal)
            || shape.Textures == null
            || Properties.Client?.Textures == null)
        {
            return;
        }

        foreach (System.Collections.Generic.KeyValuePair<string, AssetLocation> entry in shape.Textures)
        {
            var texture = new CompositeTexture(entry.Value.Clone());
            texture.Bake(Api.Assets);
            capi.EntityTextureAtlas.GetOrInsertTexture(
                texture.Baked.TextureFilenames[0],
                out int textureSubId,
                out _);
            texture.Baked.TextureSubId = textureSubId;
            Properties.Client.Textures[entry.Key] = texture;
        }

        preparedTextureModel = model;
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
        double projectileRadius = Math.Max(
            WatchedAttributes.GetFloat("vrpgCreatureCollisionRadius", 0.2f),
            Math.Max(CollisionBox.XSize, CollisionBox.ZSize) * 0.5);
        for (int i = 0; i < candidates.Length; i++)
        {
            Entity candidate = candidates[i];
            if (ProjectileHitGeometry.Intersects(
                    start,
                    end,
                    candidate.CollisionBox,
                    candidate.Pos.XYZ,
                    projectileRadius,
                    out double at)
                && at < earliest)
            {
                earliest = at;
                hit = candidate;
            }
        }

        return earliest < double.MaxValue;
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
