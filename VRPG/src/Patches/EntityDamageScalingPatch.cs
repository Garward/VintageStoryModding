using System;
using HarmonyLib;
using VRPG.Modules.Rpg.Combat;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Patches;

public static class EntityDamageScalingPatch
{
    public static Action<Entity, Entity>? HostileDamageObserved { get; set; }
    public static System.Func<Entity, DamageSource, float, float>? PlayerDamageResolver { get; set; }
    public static System.Func<Entity, DamageSource, float, float?>? PlayerOutgoingDamageResolver { get; set; }

    public static void Patch(Harmony harmony)
    {
        Type? healthBehavior = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorHealth");
        if (healthBehavior == null)
        {
            return;
        }

        var method = AccessTools.Method(healthBehavior, "OnEntityReceiveDamage");
        if (method != null)
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(EntityDamageScalingPatch), nameof(OnEntityReceiveDamagePrefix)));
        }
        var callbacks = AccessTools.Method(healthBehavior, "ApplyOnDamageDelegates");
        if (callbacks != null)
            harmony.Patch(callbacks, postfix: new HarmonyMethod(typeof(EntityDamageScalingPatch), nameof(AfterDamageCallbacks)));
        Type? wearables = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemWearableStats");
        var wearableDamage = wearables == null ? null : AccessTools.Method(wearables, "handleDamaged");
        if (wearableDamage != null)
            harmony.Patch(wearableDamage, prefix: new HarmonyMethod(typeof(EntityDamageScalingPatch), nameof(WearableDamagePrefix)));
        NativeDamageOverTimePatch.Patch(harmony);
    }

    public static void OnEntityReceiveDamagePrefix(EntityBehavior __instance, DamageSource damageSource, ref float damage)
    {
        if (damage <= 0f || damageSource == null || damageSource.Type == EnumDamageType.Heal)
        {
            return;
        }

        Entity? target = __instance.entity;
        Entity? source = damageSource.GetCauseEntity();
        if (target == null)
        {
            return;
        }
        if (damageSource.Duration > TimeSpan.Zero && target is EntityPlayer && PlayerDamageResolver != null
            && SurvivalCharacterRuntimePatch.OwnershipResolver?.Invoke(target) == true)
        {
            // This is a future budget, not a landed hit. Capture context without spending defenses.
            PlayerDamageResolver?.Invoke(target, damageSource, damage);
            return;
        }

        if (source != null && source != target)
        {
            HostileDamageObserved?.Invoke(source, target);
            bool resolvedPlayerHit = source is EntityPlayer && RpgDamageSourceTagger.IsSkill(damageSource);
            if (source is EntityPlayer && !resolvedPlayerHit && PlayerDamageOverTimeContext.TickContext(damageSource) == null
                && PlayerOutgoingDamageResolver?.Invoke(target, damageSource, damage) is float resolved)
            {
                damage = resolved;
                resolvedPlayerHit = true;
            }
            if (!resolvedPlayerHit)
            {
                float sourceMultiplier = Math.Max(0.01f, source.WatchedAttributes.GetFloat("vrpgDamageMultiplier", 1f));
                int sourceLevel = Math.Max(1, source.WatchedAttributes.GetInt("vrpgLevel", 1));
                int targetLevel = Math.Max(1, target.WatchedAttributes.GetInt("vrpgLevel", 1));
                int delta = sourceLevel - targetLevel;
                float levelMultiplier = delta >= 0
                    ? 1f + Math.Min(99, delta) * 0.04f
                    : 1f / (1f + Math.Min(99, -delta) * 0.18f);

                damage *= sourceMultiplier * levelMultiplier;
            }
        }
    }

    public static void AfterDamageCallbacks(EntityBehavior __instance, DamageSource damageSource, ref float damage)
    {
        if (__instance.entity is EntityPlayer && damage > 0 && damageSource.Type != EnumDamageType.Heal
            && PlayerDamageResolver != null)
            damage = Math.Max(0f, PlayerDamageResolver(__instance.entity, damageSource, damage));
    }

    public static bool WearableDamagePrefix(IPlayer player, float damage, DamageSource dmgSource, ref float __result)
    {
        if (PlayerDamageResolver == null || dmgSource.Type == EnumDamageType.Heal
            || SurvivalCharacterRuntimePatch.OwnershipResolver?.Invoke(player.Entity) != true) return true;
        __result = damage;
        return false;
    }
}
