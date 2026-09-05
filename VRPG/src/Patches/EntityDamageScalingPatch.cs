using System;
using System.Reflection;
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
    public static System.Func<Entity, bool>? PlayerDefenseActive { get; set; }

    public static void Patch(Harmony harmony, System.Func<Type, string, Type[], MethodInfo?>? findMethod = null)
    {
        findMethod ??= (type, name, parameters) => AccessTools.Method(type, name, parameters);
        Type health = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorHealth")
            ?? throw new InvalidOperationException("Missing required damage authority type EntityBehaviorHealth.");
        Type wearables = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemWearableStats")
            ?? throw new InvalidOperationException("Missing required damage authority type ModSystemWearableStats.");
        MethodInfo Require(Type type, string name, Type returns, params Type[] parameters)
        {
            MethodInfo? method = findMethod(type, name, parameters);
            if (method == null || method.IsStatic || method.ReturnType != returns)
                throw new InvalidOperationException($"Missing or incompatible required damage hook {type.Name}.{name}.");
            return method;
        }
        var receive = Require(health, "OnEntityReceiveDamage", typeof(void), typeof(DamageSource), typeof(float).MakeByRefType());
        var callbacks = Require(health, "ApplyOnDamageDelegates", typeof(void), typeof(DamageSource), typeof(float).MakeByRefType());
        var wearable = Require(wearables, "handleDamaged", typeof(float), typeof(IPlayer), typeof(float), typeof(DamageSource));
        var queued = Require(health, "TurnIntoDoTEffect", typeof(bool), typeof(DamageSource), typeof(float));
        var ticks = Require(health, "ProcessDoTEffects", typeof(void), typeof(float));
        var targets = new[] { receive, callbacks, wearable, queued, ticks };
        try
        {
            harmony.Patch(receive, prefix: new HarmonyMethod(typeof(EntityDamageScalingPatch), nameof(OnEntityReceiveDamagePrefix)));
            harmony.Patch(callbacks, postfix: new HarmonyMethod(typeof(EntityDamageScalingPatch), nameof(AfterDamageCallbacks)));
            harmony.Patch(wearable, prefix: new HarmonyMethod(typeof(EntityDamageScalingPatch), nameof(WearableDamagePrefix)));
            NativeDamageOverTimePatch.Patch(harmony, queued, ticks);
        }
        catch (Exception exception)
        {
            foreach (var target in targets) harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            throw new InvalidOperationException("VRPG damage authority could not be installed; all damage hooks were rolled back.", exception);
        }
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
        if (damageSource.Duration > TimeSpan.Zero && IsPlayerDefenseActive(target))
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
        if (damage > 0 && damageSource.Type != EnumDamageType.Heal && IsPlayerDefenseActive(__instance.entity))
            damage = Math.Max(0f, PlayerDamageResolver!(__instance.entity, damageSource, damage));
    }

    public static bool WearableDamagePrefix(IPlayer player, float damage, DamageSource dmgSource, ref float __result)
    {
        if (dmgSource.Type == EnumDamageType.Heal || !IsPlayerDefenseActive(player.Entity)) return true;
        __result = damage;
        return false;
    }

    private static bool IsPlayerDefenseActive(Entity target) => target is EntityPlayer
        && PlayerDamageResolver != null
        && (PlayerDefenseActive?.Invoke(target) ?? SurvivalCharacterRuntimePatch.OwnershipResolver?.Invoke(target)) == true;
}
