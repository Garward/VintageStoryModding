using System;
using System.Reflection;
using HarmonyLib;
using VRPG.Inventory.Bridges;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Patches;

/// <summary>Intercepts identifiable pickup before Vanilla selects any player slot.</summary>
public static partial class EngineItemFlowPatch
{
    public static System.Func<string, bool>? OwnershipResolver { get; set; }
    public static System.Func<PlayerItemBridgeCoordinator?>? CoordinatorResolver { get; set; }
    public static bool IsInstalled { get; private set; }

    public static void Patch(Harmony harmony, ILogger logger,
        System.Func<Type, string, Type[], MethodInfo?>? resolveBirthMethod = null)
    {
        Type? behavior = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorCollectEntities");
        MethodInfo? target = behavior == null ? null : AccessTools.Method(behavior, "OnFoundCollectible", new[] { typeof(Entity) });
        if (target == null)
        {
            logger.Error("[VRPG/Inventory] The supported pickup boundary is unavailable; canonical activation must remain blocked.");
            return;
        }
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(EngineItemFlowPatch), nameof(OnFoundCollectiblePrefix)));
        IsInstalled = true;
        PatchBirth(harmony, logger, resolveBirthMethod ?? ((type, name, parameters) => AccessTools.Method(type, name, parameters)));
    }

    public static bool OnFoundCollectiblePrefix(EntityBehavior __instance, Entity foundEntity, ref bool __result)
    {
        var coordinator = CoordinatorResolver?.Invoke();
        var source = foundEntity.GetType() == typeof(EntityItem) ? new EngineItemPickupSource((EntityItem)foundEntity) : null;
        if (source != null && coordinator?.HasPickupClaim(source.Token) == true)
        {
            // Tombstones apply even when the next collector is outside canonical custody.
            __result = coordinator.SuppressClaimedPickup(source);
            return false;
        }
        string uid = (__instance.entity as EntityPlayer)?.PlayerUID ?? "";
        if (OwnershipResolver?.Invoke(uid) != true) return true;
        __result = false;
        if (coordinator == null || source?.Stack?.Collectible is not { } collectible
            || collectible.GetType().GetMethod(nameof(CollectibleObject.OnCollected))?.DeclaringType != typeof(CollectibleObject))
            return false;
        try
        {
            var receipt = coordinator.Pickup(uid, source);
            __result = receipt.Stage is PlayerItemBridgeStage.Completed or PlayerItemBridgeStage.Prepared;
        }
        catch { /* Encoding/refusal leaves the source or its durable tombstone in custody. */ }
        return false;
    }

    public static void Reset()
    {
        ResetBirth();
        OwnershipResolver = null;
        CoordinatorResolver = null;
        IsInstalled = false;
    }
}
