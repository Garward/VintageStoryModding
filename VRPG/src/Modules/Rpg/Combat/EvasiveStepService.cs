using System;
using System.Collections.Generic;
using VRPG.Config;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Movement;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

public sealed class EvasiveStepService
{
    private readonly ICoreServerAPI api;
    private readonly RpgPlayerStore playerStore;
    private readonly TalentTreeCatalog talents;
    private readonly CombatVisualBroadcaster visuals;
    private readonly PlayerMovementEffectService movementEffects;
    private readonly System.Func<EntityPlayer, bool> dexterityPrimary;
    private readonly System.Func<EntityPlayer, Entity, bool> hostile;
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
        IServerNetworkChannel channel,
        PlayerMovementEffectService movementEffects,
        System.Func<EntityPlayer, bool> dexterityPrimary,
        System.Func<EntityPlayer, Entity, bool> hostile)
    {
        this.api = api;
        this.talents = talents;
        this.playerStore = playerStore;
        this.movementEffects = movementEffects;
        this.dexterityPrimary = dexterityPrimary;
        this.hostile = hostile;
        visuals = new CombatVisualBroadcaster(api, channel);
        baseCooldownMs = (long)(Math.Max(0.5f, config.EvasiveStepCooldownSeconds) * 1000f);
        baseInvulnerabilityMs = (long)(Math.Clamp(config.EvasiveStepInvulnerabilitySeconds, 0.1f, 0.75f) * 1000f);
        baseImpulse = Math.Clamp(config.EvasiveStepImpulse, 0.05f, 0.6f);
    }

    public bool TryActivate(EntityPlayer entityPlayer, DamageSource source, bool wouldBeLethal)
    {
        if (!wouldBeLethal || entityPlayer.Player is not IServerPlayer player
            || !entityPlayer.Alive
            || !IsHostile(entityPlayer, source) || !IsDexterityPrimary(entityPlayer)
            || IsOnCooldown(player))
        {
            return false;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        ApplyEscape(player, source, state);
        StartCooldownAndInvulnerability(player, state);
        SpawnBurst(entityPlayer);
        return true;
    }

    private bool IsHostile(EntityPlayer player, DamageSource source) =>
        source.Type != EnumDamageType.Heal
        && source.Source is EnumDamageSource.Entity or EnumDamageSource.Player
        && source.GetCauseEntity() is Entity attacker && attacker != player && hostile(player, attacker);

    private bool IsDexterityPrimary(EntityPlayer player) => dexterityPrimary(player);

    private bool IsOnCooldown(IServerPlayer player) =>
        GetUntil(cooldownUntilByPlayer, player.PlayerUID) > api.World.ElapsedMilliseconds;

    private void ApplyEscape(IServerPlayer player, DamageSource source, RpgPlayerState state)
    {
        double impulse = Math.Clamp(Modifiers(state, "evasive_step_impulse").Resolve(baseImpulse), 0.05, 0.8);
        (double x, double z) = EscapeDirection(player.Entity, source.GetCauseEntity());
        double motionX = x * impulse;
        double motionZ = z * impulse;
        movementEffects.Apply(player, new MovementEffectRequest(
            "vrpg:evasive-step",
            MovementEffectMode.SetVelocity,
            MovementEffectAxes.Horizontal,
            new MovementVector(motionX, 0, motionZ),
            Priority: 100));
    }

    private void StartCooldownAndInvulnerability(IServerPlayer player, RpgPlayerState state)
    {
        long now = api.World.ElapsedMilliseconds;
        long cooldownMs = ModifiedDuration(state, "evasive_step_cooldown", baseCooldownMs, 500L);
        long invulnerabilityMs = ModifiedDuration(state, "evasive_step_invulnerability", baseInvulnerabilityMs, 100L);
        cooldownUntilByPlayer[player.PlayerUID] = now + cooldownMs;
        invulnerableUntilByPlayer[player.PlayerUID] = now + invulnerabilityMs;
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
