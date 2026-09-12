using System;
using System.Reflection;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Inventory.Bridges;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Patches;

/// <summary>Intercepts identifiable pickup before Vanilla selects any player slot.</summary>
public static partial class EngineItemFlowPatch
{
    public static AdventureItemSuppression? AdventureSuppression { get; set; }
    public static System.Func<string, PlayerItemCustodySnapshot>? CustodyResolver { get; set; }
    public static System.Func<PlayerItemBridgeCoordinator?>? CoordinatorResolver { get; set; }
    private static Harmony? pickupHarmony;
    private static MethodInfo? pickupTarget;
    public static bool IsInstalled
    {
        get
        {
            var info = pickupTarget == null ? null : Harmony.GetPatchInfo(pickupTarget);
            return info != null && info.Prefixes.Count == 1 && info.Prefixes[0].owner == pickupHarmony?.Id
                && info.Prefixes[0].PatchMethod == AccessTools.Method(typeof(EngineItemFlowPatch), nameof(OnFoundCollectiblePrefix))
                && info.Postfixes.Count == 0 && info.Transpilers.Count == 0 && info.Finalizers.Count == 0;
        }
    }

    public static void Patch(Harmony harmony, ILogger logger,
        System.Func<Type, string, Type[], MethodInfo?>? resolveBirthMethod = null)
    {
        ResetPickup();
        Type? behavior = AccessTools.TypeByName("Vintagestory.GameContent.EntityBehaviorCollectEntities");
        MethodInfo? target = behavior == null ? null : AccessTools.Method(behavior, "OnFoundCollectible", new[] { typeof(Entity) });
        if (target == null)
        {
            logger.Error("[VRPG/Inventory] The supported pickup boundary is unavailable; canonical activation must remain blocked.");
            return;
        }
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(EngineItemFlowPatch), nameof(OnFoundCollectiblePrefix)));
        pickupHarmony = harmony;
        pickupTarget = target;
        PatchBirth(harmony, logger, resolveBirthMethod ?? ((type, name, parameters) => AccessTools.Method(type, name, parameters)));
    }

    public static bool OnFoundCollectiblePrefix(EntityBehavior __instance, Entity foundEntity, ref bool __result)
    {
        if (foundEntity is EntityItem && AdventureSuppression?.Enabled == true)
        {
            __result = false;
            return false;
        }
        var coordinator = CoordinatorResolver?.Invoke();
        var source = foundEntity.GetType() == typeof(EntityItem) ? new EngineItemPickupSource((EntityItem)foundEntity) : null;
        if (source != null && coordinator?.HasPickupClaim(source.Token) == true)
        {
            // Tombstones apply even when the next collector is outside canonical custody.
            __result = coordinator.SuppressClaimedPickup(source);
            return false;
        }
        string uid = (__instance.entity as EntityPlayer)?.PlayerUID ?? "";
        PlayerItemCustodySnapshot custody;
        try { custody = CustodyResolver?.Invoke(uid) ?? PlayerItemCustodySnapshot.Unresolved; }
        catch { custody = PlayerItemCustodySnapshot.Unresolved; }
        if (custody.AllowsVanilla) return true;
        __result = false;
        if (!custody.AllowsCanonical || coordinator == null || source?.Stack?.Collectible is not { } collectible
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
        AdventureSuppression = null;
        ResetBirth();
        CustodyResolver = null;
        CoordinatorResolver = null;
        ResetPickup();
    }

    private static void ResetPickup()
    {
        if (pickupTarget != null)
            pickupHarmony?.Unpatch(pickupTarget, AccessTools.Method(typeof(EngineItemFlowPatch), nameof(OnFoundCollectiblePrefix)));
        pickupTarget = null;
        pickupHarmony = null;
    }
}
