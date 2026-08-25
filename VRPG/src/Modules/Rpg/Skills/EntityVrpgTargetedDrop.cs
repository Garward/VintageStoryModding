using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Modules.Rpg.Skills;

/// <summary>
/// Deterministic visual carrier for a model dropped onto an authored target.
/// It deliberately does not inherit projectile physics: it moves straight down
/// at blocks per second and resolves exactly at its synchronized target point.
/// </summary>
public sealed class EntityVrpgTargetedDrop : Entity
{
    private bool impacted;
    private long removeAtMs;
    private long launchedAtMs;
    private long lastTrailMs;
    private double fallVelocity;
    private string preparedTextureModel = "";

    public Entity? FiredBy { get; private set; }
    public string SkillCode => WatchedAttributes.GetString("vrpgSkillCode", "");
    public int SkillLevel => Math.Max(1, WatchedAttributes.GetInt("vrpgSkillLevel", 1));
    public Vec3d Target => new Vec3d(
        WatchedAttributes.GetDouble("vrpgTargetX", Pos.X),
        WatchedAttributes.GetDouble("vrpgTargetY", Pos.Y),
        WatchedAttributes.GetDouble("vrpgTargetZ", Pos.Z));

    public override bool ApplyGravity => false;
    public override bool IsInteractable => false;

    public void Configure(Entity caster, SkillDefinition skill, int skillLevel, Vec3d target)
    {
        FiredBy = caster;
        WatchedAttributes.SetLong("vrpgFiredBy", caster.EntityId);
        WatchedAttributes.SetString("vrpgSkillCode", skill.Code);
        WatchedAttributes.SetInt("vrpgSkillLevel", skillLevel);
        WatchedAttributes.SetString("vrpgSkillModel", skill.Model);
        WatchedAttributes.SetString("vrpgSkillColor", skill.Color);
        WatchedAttributes.SetString("vrpgParticleModel", skill.Particles.Model);
        WatchedAttributes.SetFloat("vrpgParticleTrail", skill.Particles.TrailQuantity);
        WatchedAttributes.SetFloat("vrpgParticleLife", skill.Particles.TrailLifetimeSeconds);
        WatchedAttributes.SetFloat("vrpgParticleScale", skill.Particles.Scale);
        WatchedAttributes.SetFloat("vrpgFallSpeed", skill.TargetedDrop.FallSpeed);
        WatchedAttributes.SetFloat("vrpgFallGravity", skill.TargetedDrop.Gravity);
        WatchedAttributes.SetFloat("vrpgLifetime", skill.TargetedDrop.LifetimeSeconds);
        WatchedAttributes.SetDouble("vrpgTargetX", target.X);
        WatchedAttributes.SetDouble("vrpgTargetY", target.Y);
        WatchedAttributes.SetDouble("vrpgTargetZ", target.Z);
    }

    public override void Initialize(EntityProperties properties, ICoreAPI api, long inChunkIndex3d)
    {
        base.Initialize(properties, api, inChunkIndex3d);
        launchedAtMs = World.ElapsedMilliseconds;
        fallVelocity = Math.Max(0f, WatchedAttributes.GetFloat("vrpgFallSpeed", 1.5f));
        if (api.Side == EnumAppSide.Client)
        {
            FiredBy = api.World.GetEntityById(WatchedAttributes.GetLong("vrpgFiredBy"));
        }
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (!Alive)
        {
            return;
        }

        if (impacted)
        {
            if (Api.Side == EnumAppSide.Server && World.ElapsedMilliseconds >= removeAtMs)
            {
                Die(EnumDespawnReason.Removed);
            }

            return;
        }

        Vec3d target = Target;
        double seconds = Math.Max(0f, dt);
        double gravity = Math.Max(0.01f, WatchedAttributes.GetFloat("vrpgFallGravity", 18f));
        double fallDistance = fallVelocity * seconds + 0.5d * gravity * seconds * seconds;
        fallVelocity += gravity * seconds;
        double nextY = Math.Max(target.Y, Pos.Y - fallDistance);
        Pos.X = target.X;
        Pos.Y = nextY;
        Pos.Z = target.Z;
        Pos.Pitch = 0f;
        Pos.Yaw = 0f;
        Pos.Roll = 0f;

        if (Api.Side == EnumAppSide.Client)
        {
            if (nextY > target.Y + 0.0001d)
            {
                SpawnTrail();
            }
            return;
        }

        if (nextY <= target.Y + 0.0001d)
        {
            Impact(target);
            return;
        }

        float lifetime = Math.Max(0.5f, WatchedAttributes.GetFloat("vrpgLifetime", 10f));
        if (World.ElapsedMilliseconds - launchedAtMs >= lifetime * 1000f)
        {
            // Validated content cannot normally reach this path. Resolve at the
            // target instead of silently deleting a damaging skill.
            Impact(target);
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

    private void Impact(Vec3d target)
    {
        if (impacted || Api.Side != EnumAppSide.Server)
        {
            return;
        }

        impacted = true;
        Pos.X = target.X;
        Pos.Y = target.Y;
        Pos.Z = target.Z;
        Api.ModLoader.GetModSystem<VRPGModSystem>()?.HandleTargetedDropImpact(this, target);
        // Keep the carrier at the contact point briefly so its final synchronized
        // position reaches clients before the removal packet.
        removeAtMs = World.ElapsedMilliseconds + 750;
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

        foreach (KeyValuePair<string, AssetLocation> entry in shape.Textures)
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
            new Vec3f(-0.04f, 0.02f, -0.04f),
            new Vec3f(0.04f, 0.08f, 0.04f),
            WatchedAttributes.GetFloat("vrpgParticleLife", 0.3f),
            0.2f,
            WatchedAttributes.GetFloat("vrpgParticleScale", 0.25f),
            string.Equals(particleModel, "cube", StringComparison.OrdinalIgnoreCase)
                ? EnumParticleModel.Cube
                : EnumParticleModel.Quad);
    }
}
