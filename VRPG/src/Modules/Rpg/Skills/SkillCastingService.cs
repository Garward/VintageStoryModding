using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.StatusEffects;
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
    private static readonly AssetLocation BallisticProjectileEntityCode = new AssetLocation("vrpg:skillballisticprojectile");
    private static readonly AssetLocation TargetedDropEntityCode = new AssetLocation("vrpg:skilltargeteddrop");
    private readonly ICoreServerAPI api;
    private readonly VRPGDataRegistry data;
    private readonly RpgPlayerStore playerStore;
    private readonly RpgResourceService resources;
    private readonly SkillDamageResolver damageResolver;
    private readonly IServerNetworkChannel channel;
    private readonly CombatVisualBroadcaster visuals;
    private readonly GroundAreaService groundAreas;
    private readonly SkillStatusEffectService statusEffects;
    private readonly SkillChargeTracker chargeTracker = new SkillChargeTracker();
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
        IServerNetworkChannel channel,
        CombatVisualBroadcaster visuals,
        GroundAreaService groundAreas,
        StatusEffectTracker statusTracker)
    {
        this.api = api;
        this.data = data;
        this.playerStore = playerStore;
        this.resources = resources;
        this.damageResolver = damageResolver;
        this.channel = channel;
        this.visuals = visuals;
        this.groundAreas = groundAreas;
        statusEffects = new SkillStatusEffectService(statusTracker, visuals);
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
        if (TryGetTargetedReleaseSkill(player, slot, out SkillDefinition? targetedSkill))
        {
            return CommitTargetedCast(player, targetedSkill, out error, out failureKind);
        }

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
            if (TryGetTargetedReleaseSkill(player, slot, out SkillDefinition? targetedSkill))
            {
                return CommitTargetedCast(player, targetedSkill, out error, out failureKind);
            }

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
        if (!IsReady(player.PlayerUID, skill, now, out float readyInSeconds))
        {
            error = ReadyError(skill, readyInSeconds);
            failureKind = AbilityFailureKind.Cooldown;
            return false;
        }

        AssetLocation? requiredEntityCode = string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
            ? (skill.Projectile?.Ballistic == true ? BallisticProjectileEntityCode : ProjectileEntityCode)
            : string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase)
                ? TargetedDropEntityCode
                : null;
        if (requiredEntityCode != null && api.World.GetEntityType(requiredEntityCode) == null)
        {
            error = "VRPG skill model entity type is not loaded.";
            return false;
        }

        if (IsTimingMode(skill, "targeted_release"))
        {
            // Targeted-release skills preview entirely on the client. Current
            // clients send only release, but tolerate a press from older ones.
            return true;
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
            CommitRecovery(player.PlayerUID, skill, now);
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
            int maximumCharges = MaximumCharges(skill);
            SkillChargeSnapshot chargeSnapshot = skill != null && maximumCharges > 1
                ? chargeTracker.Snapshot(
                    player.PlayerUID,
                    skill.Code,
                    maximumCharges,
                    CooldownMilliseconds(skill),
                    now)
                : new SkillChargeSnapshot(1, 1, 0);
            float cooldownRemaining = skill == null
                ? 0f
                : maximumCharges > 1
                    ? chargeSnapshot.RechargeRemainingSeconds(now)
                    : Math.Max(0f, (GetCooldownEnd(player.PlayerUID, skill.Code) - now) / 1000f);
            slots[i] = new SkillLoadoutSlotPacket
            {
                Slot = i + 1,
                Code = skill?.Code ?? "",
                Name = skill?.Name ?? "",
                Icon = skill?.Icon ?? "skill",
                Color = skill?.Color ?? "#ff9f0d",
                LearnedLevel = Math.Max(0, level),
                CooldownSeconds = Math.Max(0f, skill?.CooldownSeconds ?? 0f),
                CooldownRemainingSeconds = cooldownRemaining,
                ResourceType = skill?.Resource.Type ?? "none",
                ResourceCost = skill == null || level <= 0 ? 0f : skill.ResourceCostAtLevel(level),
                TimingMode = skill?.Timing.Mode ?? "instant",
                ResourceCostMode = skill?.Resource.CostMode ?? "cast",
                HitIntervalSeconds = skill?.Timing.HitIntervalSeconds ?? 0f,
                CurrentCharges = chargeSnapshot.Current,
                MaximumCharges = chargeSnapshot.Maximum,
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

        // Clients can still be rendering an interpolated projectile behind the
        // authoritative server position. Defer every impact layer until that
        // visible carrier reaches the contact point or its despawn arrives.
        ResolveModelImpact(player, skill, projectile.SkillLevel, projectile.ExplosionPosition, projectile, true);
        return true;
    }

    public bool HandleTargetedDropImpact(EntityVrpgTargetedDrop drop, Vec3d target)
    {
        if (drop.FiredBy is not EntityPlayer entityPlayer
            || entityPlayer.Player is not IServerPlayer player)
        {
            return false;
        }

        SkillDefinition? skill = data.Skills.Get(drop.SkillCode);
        if (skill == null || !string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ResolveModelImpact(player, skill, drop.SkillLevel, target, drop, true);
        return true;
    }

    private void ResolveModelImpact(
        IServerPlayer player,
        SkillDefinition skill,
        int skillLevel,
        Vec3d center,
        Entity sourceEntity,
        bool synchronizeVisualToCarrier)
    {
        ApplyAreaDamage(player, skill, skillLevel, center, sourceEntity);
        CombatVisualEventPacket burst = Event(CombatVisualKind.Burst, skill, center.Clone().Add(0, 0.12, 0));
        burst.SourceEntityId = player.Entity.EntityId;
        if (synchronizeVisualToCarrier)
        {
            burst.TargetEntityId = sourceEntity.EntityId;
            burst.Flags |= (int)CombatVisualFlags.SynchronizeToCarrier;
        }
        visuals.Send(burst);
        if (skill.GroundArea?.Enabled == true)
        {
            float radius = skill.GroundArea.Radius > 0f ? skill.GroundArea.Radius : skill.Radius;
            groundAreas.Place(
                player.PlayerUID,
                skill.Code,
                GroundAreaShape.Disc,
                center,
                radius,
                GroundAreaState.Active,
                skill.GroundArea.DurationSeconds);
        }
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
            SendChannelState(player, skill, slot, active: true);
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
            case "targeted_drop":
                return SpawnTargetedDrop(player, skill, skillLevel, out error);
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
            if (api.World.PlayerByUid(playerUid) is IServerPlayer player)
            {
                SendChannelState(player, skill, cast.Slot, active: false);
            }
        }
    }

    private void SendChannelState(IServerPlayer player, SkillDefinition skill, int slot, bool active)
    {
        channel.SendPacket(new SkillChannelStatePacket
        {
            Active = active,
            Slot = slot,
            SkillCode = skill.Code,
            SkillName = skill.Name,
            Color = skill.Color,
            MaxDurationSeconds = skill.Timing.MaxDurationSeconds
        }, player);
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
        CombatVisualEventPacket circle = Event(CombatVisualKind.Circle, skill, center);
        circle.SourceEntityId = player.Entity.EntityId;
        visuals.Send(circle);
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
            bool damaged = target.ReceiveDamage(new DamageSource
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

            if (damaged && target.Alive)
            {
                statusEffects.ApplyOnHit(player.Entity, target, skill, primaryTarget: i == 0);
            }

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
        AssetLocation entityCode = skill.Projectile.Ballistic ? BallisticProjectileEntityCode : ProjectileEntityCode;
        EntityProperties? entityType = api.World.GetEntityType(entityCode);
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
        projectile.Configure(skill, skillLevel, SelectProjectileModel(skill));
        LaunchProjectile(player.Entity, projectile, skill);
        return true;
    }

    private string SelectProjectileModel(SkillDefinition skill)
    {
        string[] variants = skill.Projectile.ModelVariants ?? Array.Empty<string>();
        int selected = api.World.Rand.Next(variants.Length + 1);
        return selected == 0 ? skill.Model : variants[selected - 1];
    }

    private bool SpawnTargetedDrop(IServerPlayer player, SkillDefinition skill, int skillLevel, out string error)
    {
        error = "";
        Vec3d eye = EyePosition(player.Entity);
        Vec3f view = player.Entity.Pos.GetViewVector();
        if (!TryResolveGroundTarget(eye, view, skill.Range, out Vec3d target))
        {
            error = "Aim at a solid surface within range.";
            return false;
        }

        EntityProperties? entityType = api.World.GetEntityType(TargetedDropEntityCode);
        if (entityType == null)
        {
            error = "VRPG targeted-drop entity type is not loaded.";
            return false;
        }

        if (api.World.ClassRegistry.CreateEntity(entityType) is not EntityVrpgTargetedDrop drop)
        {
            error = "VRPG targeted-drop entity class could not be created.";
            return false;
        }

        Vec3d start = target.Clone().Add(0, skill.TargetedDrop.Height, 0);
        drop.Configure(player.Entity, skill, skillLevel, target);
        drop.Pos.SetPosWithDimension(start);
        drop.World = player.Entity.World;
        player.Entity.World.SpawnPriorityEntity(drop);
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
        if (settings.Ballistic
            || string.Equals(settings.ImpactMode, "ground", StringComparison.OrdinalIgnoreCase))
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
        projectile.Pos.SetPosWithDimension(start);
        if (settings.Ballistic)
        {
            BallisticSolution solution = BallisticTrajectory.Solve(
                start,
                target,
                settings.Speed,
                settings.MinimumFlightSeconds);
            projectile.Pos.Motion.Set(solution.InitialMotion);
        }
        else
        {
            double directionX = target.X - start.X;
            double directionY = target.Y - start.Y;
            double directionZ = target.Z - start.Z;
            double length = Math.Max(0.0001, Math.Sqrt(directionX * directionX + directionY * directionY + directionZ * directionZ));
            projectile.Pos.Motion.Set(
                directionX / length * settings.Speed,
                directionY / length * settings.Speed,
                directionZ / length * settings.Speed);
        }
        projectile.World = caster.World;
        ((IProjectile)projectile).PreInitialize();
        caster.World.SpawnPriorityEntity(projectile);
    }

    private Vec3d ResolveGroundProjectileTarget(Vec3d eye, Vec3f view, float range)
    {
        if (TryResolveGroundTarget(eye, view, range, out Vec3d target))
        {
            return target;
        }

        double distance = Math.Max(1f, range);
        return new Vec3d(
            eye.X + view.X * distance,
            eye.Y + view.Y * distance,
            eye.Z + view.Z * distance);
    }

    private bool TryResolveGroundTarget(Vec3d eye, Vec3f view, float range, out Vec3d target)
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
        target = blockSelection?.FullPosition?.Clone() ?? end;
        return blockSelection != null;
    }

    private bool CommitTargetedCast(
        IServerPlayer player,
        SkillDefinition skill,
        out string error,
        out AbilityFailureKind failureKind)
    {
        error = "";
        failureKind = AbilityFailureKind.Other;
        RpgPlayerState state = playerStore.GetOrCreate(player);
        int skillLevel = state.GetSkillLevel(skill.Code);
        if (skillLevel <= 0 || !UsesTargetedRelease(skill))
        {
            error = "The targeted skill is no longer available.";
            return false;
        }

        if (state.Level < skill.RequiredLevel)
        {
            error = $"{skill.Name} requires player level {skill.RequiredLevel}.";
            return false;
        }

        long now = api.World.ElapsedMilliseconds;
        if (!IsReady(player.PlayerUID, skill, now, out float readyInSeconds))
        {
            error = ReadyError(skill, readyInSeconds);
            failureKind = AbilityFailureKind.Cooldown;
            return false;
        }

        Vec3d eye = EyePosition(player.Entity);
        Vec3f view = player.Entity.Pos.GetViewVector();
        if (!TryResolveGroundTarget(eye, view, skill.Range, out _))
        {
            error = "Aim at a solid surface within range.";
            return false;
        }

        float cost = skill.ResourceCostAtLevel(skillLevel);
        if (!resources.TrySpend(player, skill.Resource.Type, cost, out error, out bool insufficientResource))
        {
            if (insufficientResource)
            {
                failureKind = AbilityFailureKind.InsufficientResource;
            }

            return false;
        }

        if (!ExecuteHit(player, skill, skillLevel, out error))
        {
            return false;
        }

        CommitRecovery(player.PlayerUID, skill, now);
        return true;
    }

    private bool TryGetTargetedReleaseSkill(IServerPlayer player, int slot, out SkillDefinition skill)
    {
        skill = null!;
        RpgPlayerState state = playerStore.GetOrCreate(player);
        if (slot < 0 || slot >= state.EquippedSkills.Length)
        {
            return false;
        }

        SkillDefinition? equipped = data.Skills.Get(state.EquippedSkills[slot] ?? "");
        if (equipped == null || !UsesTargetedRelease(equipped))
        {
            return false;
        }

        skill = equipped;
        return true;
    }

    private static bool UsesTargetedRelease(SkillDefinition skill)
    {
        return IsTimingMode(skill, "targeted_release")
            && (string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
                    && skill.Projectile?.Ballistic == true));
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
            bool damaged = target.ReceiveDamage(new DamageSource
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

            if (damaged && target.Alive)
            {
                statusEffects.ApplyOnHit(player.Entity, target, skill, primaryTarget: i == 0);
            }

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

    private bool IsReady(string playerUid, SkillDefinition skill, long now, out float readyInSeconds)
    {
        int maximum = MaximumCharges(skill);
        if (maximum > 1)
        {
            SkillChargeSnapshot snapshot = chargeTracker.Snapshot(
                playerUid,
                skill.Code,
                maximum,
                CooldownMilliseconds(skill),
                now);
            readyInSeconds = snapshot.RechargeRemainingSeconds(now);
            return snapshot.Current > 0;
        }

        long cooldownEnd = GetCooldownEnd(playerUid, skill.Code);
        readyInSeconds = Math.Max(0f, (cooldownEnd - now) / 1000f);
        return cooldownEnd <= now;
    }

    private void CommitRecovery(string playerUid, SkillDefinition skill, long now)
    {
        int maximum = MaximumCharges(skill);
        if (maximum > 1)
        {
            // Readiness was checked immediately before execution on the same
            // server thread, so a successful activation always owns a charge.
            chargeTracker.TryConsume(
                playerUid,
                skill.Code,
                maximum,
                CooldownMilliseconds(skill),
                now,
                out _);
            return;
        }

        SetCooldown(playerUid, skill.Code, now + CooldownMilliseconds(skill));
    }

    private static int MaximumCharges(SkillDefinition? skill)
    {
        return Math.Max(1, skill?.Charges?.Maximum ?? 1);
    }

    private static long CooldownMilliseconds(SkillDefinition skill)
    {
        return Math.Max(100L, (long)(skill.CooldownSeconds * 1000f));
    }

    private static string ReadyError(SkillDefinition skill, float readyInSeconds)
    {
        return MaximumCharges(skill) > 1
            ? $"{skill.Name} restores a charge in {readyInSeconds:0.0}s."
            : $"{skill.Name} is ready in {readyInSeconds:0.0}s.";
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
