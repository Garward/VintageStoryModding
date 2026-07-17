using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Patches;

public static class EntityDamageScalingPatch
{
    public static Action<Entity, Entity>? HostileDamageObserved { get; set; }
    public static System.Func<Entity, DamageSource, float, bool>? DamagePrevented { get; set; }

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
    }

    public static void OnEntityReceiveDamagePrefix(EntityBehavior __instance, DamageSource damageSource, ref float damage)
    {
        if (damage <= 0f || damageSource == null || damageSource.Type == EnumDamageType.Heal)
        {
            return;
        }

        Entity? target = __instance.entity;
        Entity? source = damageSource.GetCauseEntity();
        if (target == null || source == null || source == target)
        {
            return;
        }

        HostileDamageObserved?.Invoke(source, target);
        float sourceMultiplier = Math.Max(0.01f, source.WatchedAttributes.GetFloat("vrpgDamageMultiplier", 1f));
        int sourceLevel = Math.Max(1, source.WatchedAttributes.GetInt("vrpgLevel", 1));
        int targetLevel = Math.Max(1, target.WatchedAttributes.GetInt("vrpgLevel", 1));
        int delta = sourceLevel - targetLevel;
        float levelMultiplier = delta >= 0
            ? 1f + Math.Min(99, delta) * 0.04f
            : 1f / (1f + Math.Min(99, -delta) * 0.18f);

        damage *= sourceMultiplier * levelMultiplier;
        if (DamagePrevented?.Invoke(target, damageSource, damage) == true)
        {
            damage = 0f;
        }
    }
}
