using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Vintagestory.Server;

namespace VRPG.Patches;

public static partial class NativeInventoryMutationGuardPatch
{
    private static bool ClearInventoryPrefix(InventoryBase __instance, MethodBase __originalMethod)
    {
        if (__originalMethod == NativeClearMethod && reconstructionClear is { } context
            && ReferenceEquals(context.Inventory, __instance))
        {
            reconstructionClear = null;
            return AllowsReconstructedClear(context);
        }
        bool allowed = __instance is not InventoryBasePlayer playerInventory || InventoryPrefix(playerInventory);
        if (!allowed)
        {
            if (__originalMethod == NativeClearMethod) RecordClearRefusal(__instance);
            RecordSelfClearRefusal(__instance);
        }
        return allowed;
    }

    private static IEnumerable<Boundary> ClearBoundaries()
    {
        yield return new(typeof(ServerWorldPlayerData), "Init", new[] { typeof(ServerMain) }, typeof(void),
            "B847C6AF68CB200BD7BD480285367F6DB3A1014DA53AC9C20208ED5E8752FE3F");
        yield return new(typeof(CmdPlayer), "WipePlayerInventory", new[] { typeof(PlayerUidName), typeof(TextCommandCallingArgs) }, typeof(TextCommandResult),
            "697114F0929801CCF10B8B80BF12B9C71DC6F3EBAF0BACBF4C30C9B90A60A542");
        yield return new(typeof(PlayerInventoryManager), "DiscardAll", Type.EmptyTypes, typeof(void),
            "1FB1F97B554A07D2A11618D5F751480DB1D734B6FC240BC84512CC5C7F9EB244");
        yield return new(typeof(InventoryBase), "Clear", Type.EmptyTypes, typeof(void),
            "9388F0942FCC13B261F5CF8214B872E329BDE1969367C2892B6D5ABA34F8B15F", Module: PinnedNativeBoundary.ApiModule);
        yield return new(typeof(InventoryBase), "DiscardAll", Type.EmptyTypes, typeof(void),
            "327AFD62468E4842620ECDE4B7A1C4F33A59E61E5858A732D141B7D60185C368", Module: PinnedNativeBoundary.ApiModule);
        yield return new(typeof(InventoryPlayerBackpacks), "DiscardAll", Type.EmptyTypes, typeof(void),
            "63EDF8EFF7E0D0E48DCF58CB2CE1485B35AEED12AFA2355A4DB7EE74D11AD320");
        foreach (var type in new[] { typeof(InventoryCharacter), typeof(InventoryPlayerCreative) })
            yield return new(type, "DiscardAll", Type.EmptyTypes, typeof(void),
                "684888C0EBB17F374298B65EE2807526C066094C701BCC7EBBE1C1095F494FC1", false);
    }

    private static void ValidateClearOverrides()
    {
        // Remaining concrete installed player inventories inherit the API discard effect.
        foreach (var type in new[] { typeof(InventoryPlayerMouseCursor), typeof(InventoryPlayerHotbar),
            typeof(InventoryCraftingGrid), AccessTools.TypeByName("Vintagestory.Common.InventoryPlayerGround") })
            if (type == null || type.Module.ModuleVersionId.ToString() != PinnedNativeBoundary.LibModule
                || AccessTools.Method(type, "DiscardAll", Type.EmptyTypes)?.DeclaringType != typeof(InventoryBase))
                throw new InvalidOperationException("Native player DiscardAll inheritance changed.");
    }
}
