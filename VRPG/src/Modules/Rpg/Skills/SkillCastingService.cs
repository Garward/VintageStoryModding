using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VRPG.Modules.Rpg.Skills;

public sealed class SkillCastingService
{
    private static readonly AssetLocation ProjectileEntityCode = new AssetLocation("vrpg:skillprojectile");
    private readonly ICoreServerAPI api;
    private readonly VRPGDataRegistry data;
    private readonly RpgPlayerStore playerStore;
    private readonly RpgResourceService resources;
    private readonly SkillDamageResolver damageResolver;
    private readonly CombatVisualBroadcaster visuals;
    private readonly GroundAreaService groundAreas;
    private readonly Dictionary<string, Dictionary<string, long>> cooldownEnds = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveTimedCast> activeChannels = new Dictionary<string, ActiveTimedCast>(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActiveTimedCast> activeSequences = new List<ActiveTimedCast>();
    private readonly Dictionary<string, HashSet<string>> empoweredSkills = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

    public SkillCastingService(
        ICoreServerAPI api,
        VRPGDataRegistry data,
        RpgPlayerStore playerStore,
        RpgResourceService resources,
        SkillDamageResolver damageResolver,
        CombatVisualBroadcaster visuals,
        GroundAreaService groundAreas)
    {
        this.api = api;
        this.data = data;
        this.playerStore = playerStore;
        this.resources = resources;
        this.damageResolver = damageResolver;
        this.visuals = visuals;
        this.groundAreas = groundAreas;
    }

    public void SetEmpowered(string playerUid, string skillCode, bool on)
    {
        if (!empoweredSkills.TryGetValue(playerUid, out HashSet<string>? codes))
        {
            codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            empoweredSkills[playerUid] = codes;
        }

        string normalized = NormalizeCode(skillCode);
        if (on)
        {
            codes.Add(normalized);
        }
        else
        {
            codes.Remove(normalized);
        }
    }

    public bool TryCastSlot(IServerPlayer player, int slot, out string error)
    {
        return TryCastSlot(player, slot, out error, out _);
    }

    public bool TryCastSlot(IServerPlayer player, int slot, out string error, out AbilityFailureKind failureKind)
    {
        return TryHandleSlotInput(player, slot, true, out error, out failureKind);
    }

    public bool TryHandleSlotInput(
        IServerPlayer player,
        int slot,
        bool pressed,
        out string error,
        out AbilityFailureKind failureKind)
    {
        error = "";
        failureKind = AbilityFailureKind.Other;
        if (!pressed)
        {
            StopChannel(player.PlayerUID, slot);
            return true;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        if (slot < 0 || slot >= state.EquippedSkills.Length)
        {
            error = "Skill slot must be between 1 and 8.";
            return false;
        }

        string code = state.EquippedSkills[slot] ?? "";
        if (string.IsNullOrWhiteSpace(code))
        {
            error = "No skill is equipped in slot " + (slot + 1) + ".";
            return false;
        }

        SkillDefinition? skill = data.Skills.Get(code);
        if (skill == null)
        {
            error = "Equipped skill no longer exists: " + code;
            return false;
        }

        int skillLevel = state.GetSkillLevel(skill.Code);
        if (skillLevel <= 0)
        {
            error = "Skill is not learned: " + skill.Name;
            return false;
        }

        if (state.Level < skill.RequiredLevel)
        {
            error = $"{skill.Name} requires player level {skill.RequiredLevel}.";
            return false;
        }

        if (IsTimingMode(skill, "channel")
            && activeChannels.TryGetValue(player.PlayerUID, out ActiveTimedCast? existingChannel)
            && existingChannel.Slot == slot
            && string.Equals(existingChannel.SkillCode, skill.Code, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        long now = api.World.ElapsedMilliseconds;
        long cooldownEnd = GetCooldownEnd(player.PlayerUID, skill.Code);
        if (cooldownEnd > now)
        {
            error = $"{skill.Name} is ready in {(cooldownEnd - now) / 1000f:0.0}s.";
            failureKind = AbilityFailureKind.Cooldown;
            return false;
        }

        if (string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
            && api.World.GetEntityType(ProjectileEntityCode) == null)
        {
            error = "VRPG projectile entity type is not loaded.";
            return false;
        }

        bool channel = IsTimingMode(skill, "channel");
        bool costPerSecond = channel && string.Equals(skill.Resource.CostMode, "per_second", StringComparison.OrdinalIgnoreCase);
        float cost = skill.ResourceCostAtLevel(skillLevel);
        if (!costPerSecond
            && !resources.TrySpend(player, skill.Resource.Type, cost, out error, out bool insufficientResource))
        {
            if (insufficientResource)
            {
                failureKind = AbilityFailureKind.InsufficientResource;
            }

            return false;
        }

        if (costPerSecond && !TrySpendChannelTick(player, skill, skillLevel, out error))
        {
            failureKind = AbilityFailureKind.InsufficientResource;
            return false;
        }

        if (activeChannels.ContainsKey(player.PlayerUID))
        {
            EndChannel(player.PlayerUID, now);
        }

        bool cast = ExecuteInitial(player, slot, skill, skillLevel, now, out error);
        if (!cast)
        {
            return false;
        }

        if (!channel)
        {
            SetCooldown(player.PlayerUID, skill.Code, now + (long)(skill.CooldownSeconds * 1000f));
        }
        return true;
    }

    public void Tick(float deltaTime)
    {
        _ = deltaTime;
        long now = api.World.ElapsedMilliseconds;
        TickSequences(now);
        TickChannels(now);
    }

    public bool Learn(IServerPlayer player, string code, int level, out string message)
    {
        SkillDefinition? skill = data.Skills.Get(NormalizeCode(code));
        if (skill == null)
        {
            message = "Unknown skill: " + code;
            return false;
        }

        int clamped = Math.Clamp(level, 1, Math.Max(1, skill.MaxLevel));
        RpgPlayerState state = playerStore.GetOrCreate(player);
        state.SkillLevels[skill.Code] = clamped;
        playerStore.Save();
        message = $"Set {skill.Name} to skill level {clamped}.";
        return true;
    }

    public int LearnAll(IServerPlayer player, int level)
    {
        int count = 0;
        foreach (SkillDefinition skill in data.Skills.All)
        {
            int clamped = Math.Clamp(level, 1, Math.Max(1, skill.MaxLevel));
            playerStore.GetOrCreate(player).SkillLevels[skill.Code] = clamped;
            count++;
        }

        playerStore.Save();
        return count;
    }

    public bool Equip(IServerPlayer player, int slot, string code, out string message)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        if (slot < 0 || slot >= state.EquippedSkills.Length)
        {
            message = "Skill slot must be between 1 and 8.";
            return false;
        }

        if (string.Equals(code, "clear", StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, "none", StringComparison.OrdinalIgnoreCase))
        {
            state.EquippedSkills[slot] = "";
            playerStore.Save();
            message = "Cleared skill slot " + (slot + 1) + ".";
            return true;
        }

        SkillDefinition? skill = data.Skills.Get(NormalizeCode(code));
        if (skill == null)
        {
            message = "Unknown skill: " + code;
            return false;
        }

        if (state.GetSkillLevel(skill.Code) <= 0)
        {
            message = "Learn the skill before equipping it: " + skill.Name;
            return false;
        }

        state.EquippedSkills[slot] = skill.Code;
        playerStore.Save();
        message = $"Equipped {skill.Name} in slot {slot + 1}.";
        return true;
    }

    public string FormatLoadout(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        var lines = new List<string> { "VRPG skill loadout (slots 1 through 8):" };
        for (int i = 0; i < state.EquippedSkills.Length; i++)
        {
            string code = state.EquippedSkills[i] ?? "";
            SkillDefinition? skill = data.Skills.Get(code);
            lines.Add(skill == null
                ? $"- {i + 1}: empty"
                : $"- {i + 1}: {skill.Name} (level {state.GetSkillLevel(skill.Code)}, {skill.Code})");
        }

        return string.Join("\n", lines);
    }

    public SkillLoadoutPacket BuildLoadout(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        var slots = new SkillLoadoutSlotPacket[state.EquippedSkills.Length];
        long now = api.World.ElapsedMilliseconds;
        for (int i = 0; i < state.EquippedSkills.Length; i++)
        {
            string code = state.EquippedSkills[i] ?? "";
            SkillDefinition? skill = data.Skills.Get(code);
            int level = skill == null ? 0 : state.GetSkillLevel(skill.Code);
            slots[i] = new SkillLoadoutSlotPacket
            {
                Slot = i + 1,
                Code = skill?.Code ?? "",
                Name = skill?.Name ?? "",
                Icon = skill?.Icon ?? "skill",
                Color = skill?.Color ?? "#ff9f0d",
                LearnedLevel = Math.Max(0, level),
                CooldownSeconds = Math.Max(0f, skill?.CooldownSeconds ?? 0f),
                CooldownRemainingSeconds = skill == null ? 0f : Math.Max(0f, (GetCooldownEnd(player.PlayerUID, skill.Code) - now) / 1000f),
                ResourceType = skill?.Resource.Type ?? "none",
                ResourceCost = skill == null || level <= 0 ? 0f : skill.ResourceCostAtLevel(level),
                TimingMode = skill?.Timing.Mode ?? "instant",
                ResourceCostMode = skill?.Resource.CostMode ?? "cast",
                HitIntervalSeconds = skill?.Timing.HitIntervalSeconds ?? 0f,
                Empowered = skill != null
                    && empoweredSkills.TryGetValue(player.PlayerUID, out HashSet<string>? playerEmpowered)
                    && playerEmpowered.Contains(skill.Code)
            };
        }

        return new SkillLoadoutPacket { Slots = slots };
    }

    public bool HandleProjectileImpact(EntityVrpgSkillProjectile projectile)
    {
        if (projectile.FiredBy is not EntityPlayer entityPlayer
            || entityPlayer.Player is not IServerPlayer player)
        {
            return false;
        }

        SkillDefinition? skill = data.Skills.Get(projectile.SkillCode);
        if (skill == null || !string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Vec3d center = projectile.ExplosionPosition;
        ApplyAreaDamage(player, skill, projectile.SkillLevel, center, projectile);
        visuals.Send(Event(CombatVisualKind.Burst, skill, center));
        return true;
    }

    private bool ExecuteInitial(
        IServerPlayer player,
        int slot,
        SkillDefinition skill,
        int skillLevel,
        long now,
        out string error)
    {
        if (!ExecuteHit(player, skill, skillLevel, out error))
        {
            return false;
        }

        if (IsTimingMode(skill, "sequence"))
        {
            activeSequences.Add(new ActiveTimedCast
            {
                PlayerUid = player.PlayerUID,
                Slot = slot,
                SkillCode = skill.Code,
                SkillLevel = skillLevel,
                RemainingHits = Math.Max(0, skill.Timing.HitCount - 1),
                NextHitMilliseconds = now + IntervalMilliseconds(skill),
                EndMilliseconds = long.MaxValue
            });
        }
        else if (IsTimingMode(skill, "channel"))
        {
            activeChannels[player.PlayerUID] = new ActiveTimedCast
            {
                PlayerUid = player.PlayerUID,
                Slot = slot,
                SkillCode = skill.Code,
                SkillLevel = skillLevel,
                RemainingHits = -1,
                NextHitMilliseconds = now + IntervalMilliseconds(skill),
                EndMilliseconds = now + (long)(skill.Timing.MaxDurationSeconds * 1000f)
            };
        }

        return true;
    }

    private bool ExecuteHit(IServerPlayer player, SkillDefinition skill, int skillLevel, out string error)
    {
        error = "";
        switch (skill.Delivery.ToLowerInvariant())
        {
            case "raycast_aoe":
                CastRaycastArea(player, skill, skillLevel);
                return true;
            case "projectile_aoe":
                return SpawnProjectile(player, skill, skillLevel, out error);
            case "circle":
                CastCircle(player, skill, skillLevel);
                return true;
            case "melee_arc":
                CastMelee(player, skill, skillLevel, MeleeShape.Arc);
                return true;
            case "melee_line":
                CastMelee(player, skill, skillLevel, MeleeShape.Line);
                return true;
            case "melee_single":
                CastMelee(player, skill, skillLevel, MeleeShape.Single);
                return true;
            default:
                error = "Unsupported delivery type: " + skill.Delivery;
                return false;
        }
    }

    private void TickSequences(long now)
    {
        for (int i = activeSequences.Count - 1; i >= 0; i--)
        {
            ActiveTimedCast cast = activeSequences[i];
            if (cast.RemainingHits <= 0)
            {
                activeSequences.RemoveAt(i);
                continue;
            }

            SkillDefinition? skill = data.Skills.Get(cast.SkillCode);
            IServerPlayer? player = api.World.PlayerByUid(cast.PlayerUid) as IServerPlayer;
            if (skill == null || player?.Entity == null || !player.Entity.Alive)
            {
                activeSequences.RemoveAt(i);
                continue;
            }

            long interval = IntervalMilliseconds(skill);
            while (cast.RemainingHits > 0 && now >= cast.NextHitMilliseconds)
            {
                if (!ExecuteHit(player, skill, cast.SkillLevel, out _))
                {
                    cast.RemainingHits = 0;
                    break;
                }

                cast.RemainingHits--;
                cast.NextHitMilliseconds += interval;
            }

            if (cast.RemainingHits <= 0)
            {
                activeSequences.RemoveAt(i);
            }
        }
    }

    private void TickChannels(long now)
    {
        if (activeChannels.Count == 0)
        {
            return;
        }

        var ended = new List<string>();
        foreach (KeyValuePair<string, ActiveTimedCast> entry in activeChannels)
        {
            ActiveTimedCast cast = entry.Value;
            SkillDefinition? skill = data.Skills.Get(cast.SkillCode);
            IServerPlayer? player = api.World.PlayerByUid(cast.PlayerUid) as IServerPlayer;
            if (skill == null || player?.Entity == null || !player.Entity.Alive || now >= cast.EndMilliseconds)
            {
                ended.Add(entry.Key);
                continue;
            }

            long interval = IntervalMilliseconds(skill);
            while (now >= cast.NextHitMilliseconds)
            {
                if (!TrySpendChannelTick(player, skill, cast.SkillLevel, out _)
                    || !ExecuteHit(player, skill, cast.SkillLevel, out _))
                {
                    ended.Add(entry.Key);
                    break;
                }

                cast.NextHitMilliseconds += interval;
            }
        }

        foreach (string playerUid in ended)
        {
            EndChannel(playerUid, now);
        }
    }

    private bool TrySpendChannelTick(IServerPlayer player, SkillDefinition skill, int skillLevel, out string error)
    {
        if (!string.Equals(skill.Resource.CostMode, "per_second", StringComparison.OrdinalIgnoreCase))
        {
            error = "";
            return true;
        }

        float intervalSeconds = Math.Max(0.05f, skill.Timing.HitIntervalSeconds);
        float tickCost = skill.ResourceCostAtLevel(skillLevel) * intervalSeconds;
        return resources.TrySpend(player, skill.Resource.Type, tickCost, out error, out _);
    }

    private void StopChannel(string playerUid, int slot)
    {
        if (activeChannels.TryGetValue(playerUid, out ActiveTimedCast? cast) && cast.Slot == slot)
        {
            EndChannel(playerUid, api.World.ElapsedMilliseconds);
        }
    }

    private void EndChannel(string playerUid, long now)
    {
        if (!activeChannels.Remove(playerUid, out ActiveTimedCast? cast))
        {
            return;
        }

        SkillDefinition? skill = data.Skills.Get(cast.SkillCode);
        if (skill != null)
        {
            SetCooldown(playerUid, skill.Code, now + (long)(skill.CooldownSeconds * 1000f));
        }
    }

    private static bool IsTimingMode(SkillDefinition skill, string mode)
    {
        return string.Equals(skill.Timing.Mode, mode, StringComparison.OrdinalIgnoreCase);
    }

    private static long IntervalMilliseconds(SkillDefinition skill)
    {
        return Math.Max(50L, (long)(skill.Timing.HitIntervalSeconds * 1000f));
    }

    private void CastRaycastArea(IServerPlayer player, SkillDefinition skill, int skillLevel)
    {
        Vec3d start = EyePosition(player.Entity);
        Vec3f view = player.Entity.Pos.GetViewVector();
        Vec3d end = new Vec3d(
            start.X + view.X * skill.Range,
            start.Y + view.Y * skill.Range,
            start.Z + view.Z * skill.Range);

        BlockSelection? blockSelection = null;
        EntitySelection? entitySelection = null;
        api.World.RayTraceForSelection(
            start,
            end,
            ref blockSelection,
            ref entitySelection,
            null,
            entity => entity.EntityId != player.Entity.EntityId
                && entity.IsInteractable
                && entity is EntityAgent
                && entity is not EntityPlayer);

        Vec3d center = end;
        if (entitySelection?.Entity != null)
        {
            center = EntityCenter(entitySelection.Entity);
        }
        else if (blockSelection != null)
        {
            center = blockSelection.FullPosition;
        }

        ApplyAreaDamage(player, skill, skillLevel, center, player.Entity);
        CombatVisualEventPacket ray = Event(CombatVisualKind.Ray, skill, center);
        ray.SourceEntityId = player.Entity.EntityId;
        visuals.Send(ray);
        visuals.Send(Event(CombatVisualKind.Burst, skill, center));
    }

    private void CastCircle(IServerPlayer player, SkillDefinition skill, int skillLevel)
    {
        Vec3d center = player.Entity.Pos.XYZ.Clone().Add(0, 0.2, 0);
        ApplyAreaDamage(player, skill, skillLevel, center, player.Entity);
        visuals.Send(Event(CombatVisualKind.Burst, skill, center));
        groundAreas.Place(player.PlayerUID, skill.Code, GroundAreaShape.Disc, center, skill.Radius, GroundAreaState.Active, durationSeconds: 0.8f);
    }

    private void CastMelee(IServerPlayer player, SkillDefinition skill, int skillLevel, MeleeShape shape)
    {
        if (!player.HasPrivilege(Privilege.attackcreatures))
        {
            return;
        }

        Vec3d origin = player.Entity.Pos.XYZ.Clone();
        Vec3f view = player.Entity.Pos.GetViewVector();
        double forwardX = view.X;
        double forwardZ = view.Z;
        double forwardLength = Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
        if (forwardLength <= 0.000001d)
        {
            forwardX = 0d;
            forwardZ = 1d;
        }
        else
        {
            forwardX /= forwardLength;
            forwardZ /= forwardLength;
        }

        double searchRadius = skill.Range + Math.Max(1f, skill.Melee.Width);
        Entity[] nearby = api.World.GetEntitiesAround(
            origin,
            (float)searchRadius,
            skill.Melee.VerticalTolerance + 3f,
            entity => CanDamage(player.Entity, entity));
        var candidates = new List<MeleeTargetCandidate>();
        foreach (Entity target in nearby)
        {
            Vec3d center = EntityCenter(target);
            double verticalAllowance = skill.Melee.VerticalTolerance + Math.Max(0.1f, target.SelectionBox.YSize) * 0.5;
            if (Math.Abs(center.Y - EntityCenter(player.Entity).Y) > verticalAllowance)
            {
                continue;
            }

            double offsetX = center.X - origin.X;
            double offsetZ = center.Z - origin.Z;
            double targetRadius = EntityHorizontalRadius(target);
            double projection = offsetX * forwardX + offsetZ * forwardZ;
            double lateralSquared = 0d;
            bool inside = shape switch
            {
                MeleeShape.Arc => MeleeTargetingMath.IsWithinArc(
                    offsetX,
                    offsetZ,
                    forwardX,
                    forwardZ,
                    skill.Range,
                    targetRadius,
                    skill.Melee.ArcDegrees),
                _ => MeleeTargetingMath.IsWithinLine(
                    offsetX,
                    offsetZ,
                    forwardX,
                    forwardZ,
                    skill.Range,
                    skill.Melee.Width * 0.5f,
                    targetRadius,
                    out projection,
                    out lateralSquared)
            };
            if (!inside || !HasLineOfSight(player.Entity, target))
            {
                continue;
            }

            candidates.Add(new MeleeTargetCandidate
            {
                Entity = target,
                DistanceSquared = offsetX * offsetX + offsetZ * offsetZ,
                Projection = projection,
                LateralDistanceSquared = lateralSquared
            });
        }

        candidates.Sort((left, right) => CompareMeleeCandidates(left, right, shape));
        int requestedLimit = shape == MeleeShape.Single ? 1 : skill.MaxTargets;
        int limit = requestedLimit <= 0 ? candidates.Count : Math.Min(requestedLimit, candidates.Count);
        float damage = damageResolver.Resolve(playerStore.GetOrCreate(player), skill, skillLevel);
        EnumDamageType damageType = ResolveDamageType(skill.Damage.Type);
        for (int i = 0; i < limit; i++)
        {
            Entity target = candidates[i].Entity;
            target.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Player,
                SourceEntity = player.Entity,
                CauseEntity = player.Entity,
                HitPosition = EntityCenter(target),
                Type = damageType,
                DamageTier = skill.Damage.Tier,
                IgnoreInvFrames = skill.Damage.IgnoreInvFrames,
                KnockbackStrength = 0.25f
            }, damage);

            Vec3d targetCenter = EntityCenter(target);
            CombatVisualEventPacket damageEvent = Event(CombatVisualKind.Damage, skill, targetCenter);
            damageEvent.SourceEntityId = player.Entity.EntityId;
            damageEvent.TargetEntityId = target.EntityId;
            damageEvent.Magnitude = damage;
            visuals.Send(damageEvent);
        }

        Vec3d visualStart = CastVisualOrigin(player.Entity, skill.Particles);
        if (shape == MeleeShape.Arc)
        {
            Vec3d arcCenter = new Vec3d(
                visualStart.X + forwardX * skill.Range,
                visualStart.Y,
                visualStart.Z + forwardZ * skill.Range);
            visuals.Send(Event(CombatVisualKind.Burst, skill, arcCenter));
        }
        else
        {
            Vec3d visualEnd = limit > 0
                ? EntityCenter(candidates[0].Entity)
                : new Vec3d(
                    visualStart.X + forwardX * skill.Range,
                    visualStart.Y,
                    visualStart.Z + forwardZ * skill.Range);
            CombatVisualEventPacket ray = Event(CombatVisualKind.Ray, skill, visualEnd);
            ray.SourceEntityId = player.Entity.EntityId;
            visuals.Send(ray);
        }
    }

    private bool HasLineOfSight(EntityPlayer caster, Entity target)
    {
        Vec3d start = CastVisualOrigin(caster, new SkillParticleDefinition
        {
            OriginVerticalOffset = -0.35f,
            OriginForwardOffset = 0.2f,
            OriginHorizontalOffset = 0f
        });
        Vec3d end = EntityCenter(target);
        BlockSelection? blockSelection = null;
        EntitySelection? entitySelection = null;
        api.World.RayTraceForSelection(
            start,
            end,
            ref blockSelection,
            ref entitySelection,
            null,
            entity => entity.EntityId == target.EntityId);
        return entitySelection?.Entity?.EntityId == target.EntityId || blockSelection == null;
    }

    private static int CompareMeleeCandidates(MeleeTargetCandidate left, MeleeTargetCandidate right, MeleeShape shape)
    {
        int comparison;
        if (shape == MeleeShape.Single)
        {
            comparison = left.LateralDistanceSquared.CompareTo(right.LateralDistanceSquared);
            if (comparison != 0) return comparison;
        }
        else if (shape == MeleeShape.Line)
        {
            comparison = left.Projection.CompareTo(right.Projection);
            if (comparison != 0) return comparison;
        }

        comparison = left.DistanceSquared.CompareTo(right.DistanceSquared);
        return comparison != 0 ? comparison : left.Entity.EntityId.CompareTo(right.Entity.EntityId);
    }

    private bool SpawnProjectile(IServerPlayer player, SkillDefinition skill, int skillLevel, out string error)
    {
        error = "";
        EntityProperties? entityType = api.World.GetEntityType(ProjectileEntityCode);
        if (entityType == null)
        {
            error = "VRPG projectile entity type is not loaded.";
            return false;
        }

        if (api.World.ClassRegistry.CreateEntity(entityType) is not EntityVrpgSkillProjectile projectile)
        {
            error = "VRPG projectile entity class could not be created.";
            return false;
        }

        projectile.FiredBy = player.Entity;
        projectile.Configure(skill, skillLevel);
        LaunchProjectile(player.Entity, projectile, skill);
        return true;
    }

    private void LaunchProjectile(EntityPlayer caster, EntityVrpgSkillProjectile projectile, SkillDefinition skill)
    {
        SkillProjectileDefinition settings = skill.Projectile;
        Vec3d eye = EyePosition(caster);
        Vec3f view = caster.Pos.GetViewVector();
        double horizontalX = -Math.Cos(caster.Pos.Yaw) * settings.HorizontalOffset;
        double horizontalZ = Math.Sin(caster.Pos.Yaw) * settings.HorizontalOffset;
        var start = new Vec3d(
            eye.X + horizontalX + view.X * settings.ForwardOffset,
            eye.Y + settings.VerticalOffset + view.Y * settings.ForwardOffset,
            eye.Z + horizontalZ + view.Z * settings.ForwardOffset);

        Vec3d target;
        if (string.Equals(settings.ImpactMode, "ground", StringComparison.OrdinalIgnoreCase))
        {
            target = ResolveGroundProjectileTarget(eye, view, skill.Range);
            projectile.SetGroundTarget(target);
        }
        else
        {
            double convergence = Math.Max(1f, settings.AimConvergenceDistance);
            target = new Vec3d(
                eye.X + view.X * convergence,
                eye.Y + view.Y * convergence,
                eye.Z + view.Z * convergence);
        }
        double directionX = target.X - start.X;
        double directionY = target.Y - start.Y;
        double directionZ = target.Z - start.Z;
        double length = Math.Max(0.0001, Math.Sqrt(directionX * directionX + directionY * directionY + directionZ * directionZ));

        projectile.Pos.SetPosWithDimension(start);
        projectile.Pos.Motion.Set(
            directionX / length * settings.Speed,
            directionY / length * settings.Speed,
            directionZ / length * settings.Speed);
        projectile.World = caster.World;
        ((IProjectile)projectile).PreInitialize();
        caster.World.SpawnPriorityEntity(projectile);
    }

    private Vec3d ResolveGroundProjectileTarget(Vec3d eye, Vec3f view, float range)
    {
        double distance = Math.Max(1f, range);
        var end = new Vec3d(
            eye.X + view.X * distance,
            eye.Y + view.Y * distance,
            eye.Z + view.Z * distance);
        BlockSelection? blockSelection = null;
        EntitySelection? entitySelection = null;
        api.World.RayTraceForSelection(
            eye,
            end,
            ref blockSelection,
            ref entitySelection,
            null,
            _ => false);
        return blockSelection?.FullPosition?.Clone() ?? end;
    }

    private void ApplyAreaDamage(IServerPlayer player, SkillDefinition skill, int skillLevel, Vec3d center, Entity sourceEntity)
    {
        if (!player.HasPrivilege(Privilege.attackcreatures))
        {
            return;
        }

        Entity[] nearby = api.World.GetEntitiesAround(
            center,
            skill.Radius,
            skill.Radius,
            entity => CanDamage(player.Entity, entity) && DistanceSquared(EntityCenter(entity), center) <= skill.Radius * skill.Radius);

        Array.Sort(nearby, (left, right) => DistanceSquared(EntityCenter(left), center).CompareTo(DistanceSquared(EntityCenter(right), center)));
        int limit = skill.MaxTargets <= 0 ? nearby.Length : Math.Min(skill.MaxTargets, nearby.Length);
        float damage = damageResolver.Resolve(playerStore.GetOrCreate(player), skill, skillLevel);
        EnumDamageType damageType = ResolveDamageType(skill.Damage.Type);

        for (int i = 0; i < limit; i++)
        {
            Entity target = nearby[i];
            target.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Player,
                SourceEntity = sourceEntity,
                CauseEntity = player.Entity,
                HitPosition = center,
                Type = damageType,
                DamageTier = skill.Damage.Tier,
                IgnoreInvFrames = skill.Damage.IgnoreInvFrames,
                KnockbackStrength = 0.25f
            }, damage);

            Vec3d targetCenter = EntityCenter(target);
            CombatVisualEventPacket damageEvent = Event(CombatVisualKind.Damage, skill, targetCenter);
            damageEvent.SourceEntityId = player.Entity.EntityId;
            damageEvent.TargetEntityId = target.EntityId;
            damageEvent.Magnitude = damage;
            visuals.Send(damageEvent);
        }
    }

    private static bool CanDamage(Entity caster, Entity target)
    {
        return target.EntityId != caster.EntityId
            && target.Alive
            && target.IsInteractable
            && target is EntityAgent
            && target is not EntityPlayer;
    }

    private CombatVisualEventPacket Event(CombatVisualKind kind, SkillDefinition skill, Vec3d position)
    {
        return new CombatVisualEventPacket
        {
            Kind = (byte)kind,
            StyleCode = skill.Code,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Radius = skill.Radius,
            DamageType = VisualDamageTypes.FromCode(skill.Damage.Type)
        };
    }

    private long GetCooldownEnd(string playerUid, string skillCode)
    {
        return cooldownEnds.TryGetValue(playerUid, out Dictionary<string, long>? playerCooldowns)
            && playerCooldowns.TryGetValue(skillCode, out long end)
            ? end
            : 0L;
    }

    private void SetCooldown(string playerUid, string skillCode, long end)
    {
        if (!cooldownEnds.TryGetValue(playerUid, out Dictionary<string, long>? playerCooldowns))
        {
            playerCooldowns = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            cooldownEnds[playerUid] = playerCooldowns;
        }

        playerCooldowns[skillCode] = end;
    }

    private static Vec3d EyePosition(EntityPlayer entity)
    {
        return new Vec3d(
            entity.Pos.X + entity.LocalEyePos.X,
            entity.Pos.InternalY + entity.LocalEyePos.Y,
            entity.Pos.Z + entity.LocalEyePos.Z);
    }

    private static Vec3d CastVisualOrigin(EntityPlayer entity, SkillParticleDefinition particles)
    {
        Vec3d eye = EyePosition(entity);
        Vec3f view = entity.Pos.GetViewVector();
        double horizontalX = -Math.Cos(entity.Pos.Yaw) * particles.OriginHorizontalOffset;
        double horizontalZ = Math.Sin(entity.Pos.Yaw) * particles.OriginHorizontalOffset;
        return new Vec3d(
            eye.X + horizontalX + view.X * particles.OriginForwardOffset,
            eye.Y + particles.OriginVerticalOffset + view.Y * particles.OriginForwardOffset,
            eye.Z + horizontalZ + view.Z * particles.OriginForwardOffset);
    }

    private static Vec3d EntityCenter(Entity entity)
    {
        return new Vec3d(
            entity.Pos.X,
            entity.Pos.InternalY + Math.Max(0.1f, entity.SelectionBox.YSize) * 0.5,
            entity.Pos.Z);
    }

    private static double EntityHorizontalRadius(Entity entity)
    {
        return Math.Max(0.1d, Math.Max(entity.SelectionBox.XSize, entity.SelectionBox.ZSize) * 0.5d);
    }

    private static double DistanceSquared(Vec3d left, Vec3d right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        double z = left.Z - right.Z;
        return x * x + y * y + z * z;
    }

    private static EnumDamageType ResolveDamageType(string code)
    {
        string path = NormalizeCode(code).Split(':')[1].ToLowerInvariant();
        return path switch
        {
            "fire" => EnumDamageType.Fire,
            "cold" => EnumDamageType.Frost,
            "lightning" => EnumDamageType.Electricity,
            "rust" => EnumDamageType.Acid,
            _ => EnumDamageType.BluntAttack
        };
    }

    private static string NormalizeCode(string code)
    {
        return code != null && code.Contains(':') ? code : "vrpg:" + code;
    }

    private enum MeleeShape
    {
        Arc,
        Line,
        Single
    }

    private sealed class MeleeTargetCandidate
    {
        public required Entity Entity { get; init; }
        public double DistanceSquared { get; init; }
        public double Projection { get; init; }
        public double LateralDistanceSquared { get; init; }
    }

    private sealed class ActiveTimedCast
    {
        public string PlayerUid { get; init; } = "";
        public int Slot { get; init; }
        public string SkillCode { get; init; } = "";
        public int SkillLevel { get; init; }
        public int RemainingHits { get; set; }
        public long NextHitMilliseconds { get; set; }
        public long EndMilliseconds { get; init; }
    }
}
