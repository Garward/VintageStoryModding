using System;
using System.Reflection;
using HarmonyLib;
using VRPG.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace VRPG.Patches;

/// <summary>
/// Prevents inherited Survival character behaviors from retaining gameplay
/// authority while the VRPG Adventure profile is active.
/// </summary>
public static class SurvivalCharacterRuntimePatch
{
    private const string HarmonyOwner = "vrpg.character-runtime";
    private const string NutrientHealthModifier = "nutrientHealthMod";
    private static FieldInfo? normalBodyTemperatureField;
    private static FieldInfo? damagingFreezeHoursField;
    private static FieldInfo? sprinterCounterField;
    private static FieldInfo? firstTemperatureTickField;
    private static FieldInfo? entityStatsOwnerField;

    private static readonly string[] HungerMethods =
    {
        "DidAttack",
        "ConsumeSaturation",
        "OnEntityReceiveSaturation",
        "OnGameTick",
        "SlowTick",
        "OnEntityReceiveDamage"
    };

    private static readonly string[] BodyTemperatureMethods =
    {
        "OnGameTick",
        "updateFreezingAnimState",
        "didConsume",
        "updateWearableConditions",
        "OnEntityRevive"
    };

    public static System.Func<Entity, bool>? OwnershipResolver { get; set; }
    public static System.Func<Entity, string, float?>? MovementStatResolver { get; set; }

    public static void Patch(Harmony harmony, ILogger logger)
    {
        Type? hungerType = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorHunger");
        Type? temperatureType = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorBodyTemperature");
        Type? healthType = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorHealth");
        entityStatsOwnerField = AccessTools.Field(typeof(EntityStats), "entity");

        if (temperatureType != null)
        {
            normalBodyTemperatureField = AccessTools.Field(temperatureType, "NormalBodyTemperature");
            damagingFreezeHoursField = AccessTools.Field(temperatureType, "damagingFreezeHours");
            sprinterCounterField = AccessTools.Field(temperatureType, "sprinterCounter");
            firstTemperatureTickField = AccessTools.Field(temperatureType, "firstTick");
        }

        PatchMethods(harmony, logger, hungerType, HungerMethods, nameof(SuppressOwnedBehaviorPrefix));
        PatchMethod(harmony, logger, hungerType, "UpdateNutrientHealthBoost", nameof(NutrientHealthPrefix));
        PatchMethod(harmony, logger, hungerType, "Initialize", nameof(HungerInitializePostfix), postfix: true);

        PatchMethods(harmony, logger, temperatureType, BodyTemperatureMethods, nameof(BodyTemperaturePrefix));
        PatchMethod(harmony, logger, temperatureType, "Initialize", nameof(BodyTemperatureInitializePostfix), postfix: true);

        PatchMethod(harmony, logger, healthType, "OnEntityReceiveDamage", nameof(HealthDamagePrefix));
        PatchMethod(harmony, logger, typeof(EntityStats), "GetBlended", nameof(GetBlendedPrefix));
    }

    public static bool SuppressOwnedBehaviorPrefix(EntityBehavior __instance)
    {
        return !IsOwned(__instance.entity);
    }

    public static bool NutrientHealthPrefix(EntityBehavior __instance)
    {
        if (!IsOwned(__instance.entity))
        {
            return true;
        }

        ClearNutrientHealthModifier(__instance.entity);
        return false;
    }

    public static void HungerInitializePostfix(EntityBehavior __instance)
    {
        if (IsOwned(__instance.entity))
        {
            ClearNutrientHealthModifier(__instance.entity);
        }
    }

    public static bool BodyTemperaturePrefix(EntityBehavior __instance)
    {
        if (!IsOwned(__instance.entity))
        {
            return true;
        }

        NormalizeBodyTemperature(__instance);
        return false;
    }

    public static void BodyTemperatureInitializePostfix(EntityBehavior __instance)
    {
        if (IsOwned(__instance.entity))
        {
            NormalizeBodyTemperature(__instance);
        }
    }

    public static void HealthDamagePrefix(EntityBehavior __instance, DamageSource damageSource, ref float damage)
    {
        if (damage <= 0f || damageSource == null || !IsOwned(__instance.entity))
        {
            return;
        }

        if (ShouldSuppressDamage(damageSource))
        {
            damage = 0f;
        }
    }

    public static bool ShouldSuppressDamage(DamageSource source)
    {
        return source.Type == EnumDamageType.Hunger
            || source.Type == EnumDamageType.Frost && source.Source is EnumDamageSource.Internal or EnumDamageSource.Weather;
    }

    public static bool GetBlendedPrefix(EntityStats __instance, string category, ref float __result)
    {
        if (entityStatsOwnerField?.GetValue(__instance) is not Entity entity)
        {
            return true;
        }

        float? ownedValue = MovementStatResolver?.Invoke(entity, category);
        if (!ownedValue.HasValue)
        {
            return true;
        }

        __result = ownedValue.Value;
        return false;
    }

    public static void NormalizePlayer(Entity entity)
    {
        if (!IsOwned(entity))
        {
            return;
        }

        ClearNutrientHealthModifier(entity);
        EntityBehavior? temperature = entity.GetBehavior("bodytemperature");
        if (temperature != null)
        {
            NormalizeBodyTemperature(temperature);
        }
    }

    private static bool IsOwned(Entity? entity)
    {
        return entity != null && OwnershipResolver?.Invoke(entity) == true;
    }

    private static void ClearNutrientHealthModifier(Entity entity)
    {
        EntityBehavior? health = entity.GetBehavior("health");
        MethodInfo? setter = health == null
            ? null
            : AccessTools.Method(health.GetType(), "SetMaxHealthModifiers", new[] { typeof(string), typeof(float) });
        setter?.Invoke(health, new object[] { NutrientHealthModifier, 0f });
    }

    private static void NormalizeBodyTemperature(EntityBehavior behavior)
    {
        Entity entity = behavior.entity;
        float normalTemperature = normalBodyTemperatureField?.GetValue(behavior) is float value
            ? value
            : 37f;
        ITreeAttribute? temperature = entity.WatchedAttributes.GetTreeAttribute("bodyTemp");
        bool dirty = false;

        if (temperature != null)
        {
            if (Math.Abs(temperature.GetFloat("bodytemp", normalTemperature) - normalTemperature) > 0.001f)
            {
                temperature.SetFloat("bodytemp", normalTemperature);
                dirty = true;
            }

            if (Math.Abs(temperature.GetFloat("nearHeatSourceStrength")) > 0.001f)
            {
                temperature.SetFloat("nearHeatSourceStrength", 0f);
                dirty = true;
            }
        }

        if (Math.Abs(entity.WatchedAttributes.GetFloat("freezingEffectStrength")) > 0.001f)
        {
            entity.WatchedAttributes.SetFloat("freezingEffectStrength", 0f);
            entity.WatchedAttributes.MarkPathDirty("freezingEffectStrength");
        }

        if (Math.Abs(entity.WatchedAttributes.GetFloat("wetness")) > 0.001f)
        {
            entity.WatchedAttributes.SetFloat("wetness", 0f);
            entity.WatchedAttributes.MarkPathDirty("wetness");
        }

        damagingFreezeHoursField?.SetValue(behavior, 0f);
        sprinterCounterField?.SetValue(behavior, 0);
        firstTemperatureTickField?.SetValue(behavior, true);

        if (entity.AnimManager?.IsAnimationActive("coldidle") == true)
        {
            entity.StopAnimation("coldidle");
        }
        if (entity.AnimManager?.IsAnimationActive("coldidleheld") == true)
        {
            entity.StopAnimation("coldidleheld");
        }

        if (dirty)
        {
            entity.WatchedAttributes.MarkPathDirty("bodyTemp");
        }
    }

    private static void PatchMethods(
        Harmony harmony,
        ILogger logger,
        Type? targetType,
        string[] methods,
        string prefixName)
    {
        foreach (string method in methods)
        {
            PatchMethod(harmony, logger, targetType, method, prefixName);
        }
    }

    private static void PatchMethod(
        Harmony harmony,
        ILogger logger,
        Type? targetType,
        string methodName,
        string patchName,
        bool postfix = false)
    {
        if (targetType == null)
        {
            logger.Error("[VRPG/Character] Required Survival behavior type for {0} was not found.", methodName);
            return;
        }

        MethodInfo? original = AccessTools.DeclaredMethod(targetType, methodName);
        MethodInfo? patch = AccessTools.Method(typeof(SurvivalCharacterRuntimePatch), patchName);
        if (original == null || patch == null)
        {
            logger.Error("[VRPG/Character] Could not install runtime authority patch for {0}.{1}.", targetType.FullName, methodName);
            return;
        }

        HarmonyLib.Patches? existing = Harmony.GetPatchInfo(original);
        if (existing?.Owners.Contains(HarmonyOwner) == true)
        {
            return;
        }

        HarmonyMethod harmonyPatch = new HarmonyMethod(patch);
        harmony.Patch(
            original,
            prefix: postfix ? null : harmonyPatch,
            postfix: postfix ? harmonyPatch : null);
    }
}
