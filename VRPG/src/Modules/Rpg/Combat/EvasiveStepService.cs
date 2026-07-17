using System;
using System.Collections.Generic;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

public sealed class EvasiveStepService
{
    private const string KnockbackAttribute = "dmgkb";
    private const string KnockbackXAttribute = "kbdirX";
    private const string KnockbackYAttribute = "kbdirY";
    private const string KnockbackZAttribute = "kbdirZ";

    private readonly ICoreServerAPI api;
    private readonly RpgPlayerStore playerStore;
    private readonly IServerNetworkChannel channel;
    private readonly TalentTreeCatalog talents;
    private readonly CombatVisualBroadcaster visuals;
    private readonly long baseCooldownMs;
    private readonly long baseInvulnerabilityMs;
    private readonly double baseImpulse;
    private readonly Dictionary<string, long> cooldownUntilByPlayer = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> invulnerableUntilByPlayer = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    public EvasiveStepService(
        ICoreServerAPI api,
        RpgModuleConfig config,
        TalentTreeCatalog talents,
        RpgPlayerStore playerStore,
        IServerNetworkChannel channel)
    {
        this.api = api;
        this.talents = talents;
        this.playerStore = playerStore;
        this.channel = channel;
        visuals = new CombatVisualBroadcaster(api, channel);
        baseCooldownMs = (long)(Math.Max(0.5f, config.EvasiveStepCooldownSeconds) * 1000f);
        baseInvulnerabilityMs = (long)(Math.Clamp(config.EvasiveStepInvulnerabilitySeconds, 0.1f, 0.75f) * 1000f);
        baseImpulse = Math.Clamp(config.EvasiveStepImpulse, 0.05f, 0.6f);
    }

    public bool TryPreventDamage(Entity entity, DamageSource source, float incomingDamage)
    {
        if (entity is not EntityPlayer entityPlayer
            || entityPlayer.Player is not IServerPlayer player
            || !entityPlayer.Alive
            || incomingDamage <= 0f)
        {
            return false;
        }

        if (IsInvulnerable(entityPlayer))
        {
            return true;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        if (CoreAttributeResolver.ResolvePrimary(state) != CoreAttributeResolver.Dexterity)
        {
            return false;
        }

        ITreeAttribute? health = entityPlayer.WatchedAttributes.GetTreeAttribute("health");
        float currentHealth = health?.GetFloat("currenthealth", 0f) ?? 0f;
        if (currentHealth <= 0f || incomingDamage + 0.001f < currentHealth)
        {
            return false;
        }

        long now = api.World.ElapsedMilliseconds;
        long cooldownUntil = GetUntil(cooldownUntilByPlayer, player.PlayerUID);
        if (cooldownUntil > now)
        {
            return false;
        }

        long cooldownMs = ModifiedDuration(state, "evasive_step_cooldown", baseCooldownMs, 500L);
        long invulnerabilityMs = ModifiedDuration(state, "evasive_step_invulnerability", baseInvulnerabilityMs, 100L);
        double impulse = Math.Clamp(Modifiers(state, "evasive_step_impulse").Resolve(baseImpulse), 0.05, 0.8);
        (double x, double z) = EscapeDirection(entityPlayer, source.GetCauseEntity());
        double motionX = x * impulse;
        double motionZ = z * impulse;
        ApplyMotion(player.Entity, motionX, motionZ);
        cooldownUntilByPlayer[player.PlayerUID] = now + cooldownMs;
        invulnerableUntilByPlayer[player.PlayerUID] = now + invulnerabilityMs;
        channel.SendPacket(new EvasiveStepActivatedPacket { MotionX = motionX, MotionZ = motionZ }, player);
        SpawnBurst(player.Entity);
        return true;
    }

    public float CooldownRemainingSeconds(IServerPlayer player)
    {
        long remaining = GetUntil(cooldownUntilByPlayer, player.PlayerUID) - api.World.ElapsedMilliseconds;
        return Math.Max(0f, remaining / 1000f);
    }

    private long ModifiedDuration(RpgPlayerState state, string statCode, long baseMilliseconds, long minimumMilliseconds)
    {
        double seconds = Modifiers(state, statCode).Resolve(baseMilliseconds / 1000.0);
        double milliseconds = seconds * 1000.0;
        return (long)Math.Max(minimumMilliseconds, milliseconds);
    }

    private StatModifierAccumulator Modifiers(RpgPlayerState state, string statCode)
    {
        var total = new StatModifierAccumulator();
        for (int talentIndex = 0; talentIndex < state.Talents.Count; talentIndex++)
        {
            TalentNodeDefinition? talent = talents.Get(state.Talents[talentIndex]);
            if (talent == null)
            {
                continue;
            }

            for (int modifierIndex = 0; modifierIndex < talent.Modifiers.Length; modifierIndex++)
            {
                StatModifierDefinition modifier = talent.Modifiers[modifierIndex];
                if (!SameStat(modifier.Stat, statCode))
                {
                    continue;
                }

                double value = modifier.Max != 0f || modifier.Min != 0f ? (modifier.Min + modifier.Max) / 2f : 0f;
                total.Add(modifier.Operation, value);
            }
        }

        return total;
    }

    private static bool SameStat(string value, string expected)
    {
        static string Normalize(string code) => (code ?? "").Trim().ToLowerInvariant().Replace("vrpg:", "").Replace("-", "_");
        return Normalize(value) == Normalize(expected);
    }

    public bool IsInvulnerable(Entity entity)
    {
        if (entity is not EntityPlayer player || string.IsNullOrWhiteSpace(player.PlayerUID))
        {
            return false;
        }

        long now = api.World.ElapsedMilliseconds;
        long invulnerableUntil = GetUntil(invulnerableUntilByPlayer, player.PlayerUID);
        if (invulnerableUntil <= now)
        {
            invulnerableUntilByPlayer.Remove(player.PlayerUID);
            return false;
        }

        return true;
    }

    private static (double X, double Z) EscapeDirection(EntityPlayer player, Entity? attacker)
    {
        if (attacker != null && attacker != player)
        {
            double awayX = player.Pos.X - attacker.Pos.X;
            double awayZ = player.Pos.Z - attacker.Pos.Z;
            double awayLength = Math.Sqrt(awayX * awayX + awayZ * awayZ);
            if (awayLength > 0.001)
            {
                return (awayX / awayLength, awayZ / awayLength);
            }
        }

        Vec3f view = player.Pos.GetViewVector();
        double length = Math.Sqrt(view.X * view.X + view.Z * view.Z);
        if (length > 0.001)
        {
            return (-view.X / length, -view.Z / length);
        }

        return (Math.Sin(player.Pos.Yaw), -Math.Cos(player.Pos.Yaw));
    }

    private static void ApplyMotion(EntityPlayer player, double motionX, double motionZ)
    {
        double motionY = player.Pos.Motion.Y;
        player.Pos.Motion.Set(motionX, motionY, motionZ);
        player.WatchedAttributes.SetDouble(KnockbackXAttribute, motionX);
        player.WatchedAttributes.SetDouble(KnockbackYAttribute, motionY);
        player.WatchedAttributes.SetDouble(KnockbackZAttribute, motionZ);
        player.Attributes.SetInt(KnockbackAttribute, 1);
    }

    private void SpawnBurst(EntityPlayer player)
    {
        Vec3d center = player.Pos.XYZ.Clone().Add(0, 0.15, 0);
        visuals.Send(new CombatVisualEventPacket
        {
            Kind = (byte)CombatVisualKind.Burst,
            StyleCode = "",
            FallbackColorRgba = unchecked((int)0x8a7cff66),
            X = center.X,
            Y = center.Y,
            Z = center.Z,
            Radius = 0.6f,
            SourceEntityId = player.EntityId
        });
    }

    private static long GetUntil(Dictionary<string, long> values, string playerUid)
    {
        return values.TryGetValue(playerUid, out long until) ? until : 0L;
    }
}
