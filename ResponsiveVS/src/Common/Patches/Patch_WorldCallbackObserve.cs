using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;

namespace ResponsiveVS.Common.Patches;

public static class Patch_WorldCallbackObserve
{
    public static void HeldUseStartPrefix(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract useType,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK held-start {0} first={1} handlingBefore={2}",
            WorldInteractionDiagFormat.HeldUse("held-start", slot, byEntity, blockSel, entitySel, useType),
            firstEvent,
            handling);
    }

    public static void HeldUseStartPostfix(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract useType,
        bool firstEvent,
        ref EnumHandHandling handling)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK held-start-done {0} handlingAfter={1}",
            WorldInteractionDiagFormat.HeldUse("held-start", slot, byEntity, blockSel, entitySel, useType),
            handling);
    }

    public static void HeldUseStepPostfix(
        float secondsPassed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract __result)
    {
        if (!ResponsiveDiagnostics.TraceEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Trace(
            "WORLD CALLBACK held-step {0} result={1}",
            WorldInteractionDiagFormat.HeldUse("held-step", slot, byEntity, blockSel, entitySel, __result, secondsPassed),
            __result);
    }

    public static void HeldUseStopPrefix(
        float secondsPassed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumHandInteract useType)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK held-stop {0}",
            WorldInteractionDiagFormat.HeldUse("held-stop", slot, byEntity, blockSel, entitySel, useType, secondsPassed));
    }

    public static void HeldUseCancelPostfix(
        float secondsPassed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumItemUseCancelReason cancelReason,
        EnumHandInteract __result)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK held-cancel {0} result={1}",
            WorldInteractionDiagFormat.HeldUse("held-cancel", slot, byEntity, blockSel, entitySel, __result, secondsPassed, cancelReason),
            __result);
    }

    public static void BlockInteractStartPrefix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Block __instance)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK block-start {0}",
            WorldInteractionDiagFormat.BlockUse("block-start", world, byPlayer, __instance, blockSel));
    }

    public static void BlockInteractStartPostfix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Block __instance, bool __result)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK block-start-done {0} result={1}",
            WorldInteractionDiagFormat.BlockUse("block-start", world, byPlayer, __instance, blockSel),
            __result);
    }

    public static void BlockInteractStepPostfix(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Block __instance, bool __result)
    {
        if (!ResponsiveDiagnostics.TraceEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Trace(
            "WORLD CALLBACK block-step {0} result={1}",
            WorldInteractionDiagFormat.BlockUse("block-step", world, byPlayer, __instance, blockSel, secondsUsed),
            __result);
    }

    public static void BlockInteractStopPrefix(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, Block __instance)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK block-stop {0}",
            WorldInteractionDiagFormat.BlockUse("block-stop", world, byPlayer, __instance, blockSel, secondsUsed));
    }

    public static void BlockInteractCancelPostfix(
        float secondsUsed,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        EnumItemUseCancelReason cancelReason,
        Block __instance,
        bool __result)
    {
        if (!ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        ResponsiveDiagnostics.Basic(
            "WORLD CALLBACK block-cancel {0} result={1}",
            WorldInteractionDiagFormat.BlockUse("block-cancel", world, byPlayer, __instance, blockSel, secondsUsed, cancelReason),
            __result);
    }
}
