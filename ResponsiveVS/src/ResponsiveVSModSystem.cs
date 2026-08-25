using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ResponsiveVS.Client.Preview;
using ResponsiveVS.Compatibility;
using ResponsiveVS.Config;
using ResponsiveVS.Diagnostics;
using ResponsiveVS.Network;
using ResponsiveVS.RuntimeData;
using ResponsiveVS.Threading;
using ResponsiveVS.Transactions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace ResponsiveVS;

public sealed class ResponsiveVSModSystem : ModSystem
{
    public const string HarmonyId = "garward.responsivevs";

    private static bool clientObservePatched;
    private static bool serverObservePatched;
    private static bool sharedWorldObservePatched;
    private static bool runtimeDataPatched;

    private Harmony harmony;
    private ResponsiveNetwork clientNetwork;
    private ResponsiveNetwork serverNetwork;
    private readonly PendingTransactionStore clientPending = new();
    private readonly PendingTransactionStore serverPending = new();
    private readonly TransactionIds clientTransactionIds = new();
    private readonly IncompatibleModDetector incompatibleModDetector = new();

    public override void Start(ICoreAPI api)
    {
        ResponsiveVSConfigSystem.Load(api);
        ResponsiveDiagnostics.Initialize(api);
        ResponsiveDiagnostics.RegisterCommand(api);

        harmony = new Harmony(HarmonyId);

        if (incompatibleModDetector.HasHardIncompatibility(api))
        {
            ResponsiveVSConfigSystem.Config.Transactions.EnableInventoryOwnership = false;
            ResponsiveDiagnostics.Warning("Inventory ownership disabled because prototype replacement mods are loaded.");
        }

        ResponsiveDiagnostics.Basic("ResponsiveVS foundation loaded. ownership={0} diagnostics={1}",
            ResponsiveVSConfigSystem.Config.Transactions.EnableInventoryOwnership,
            ResponsiveDiagnostics.Level);

        PatchSharedWorldObserveDiagnostics();

        if (ResponsiveVSConfigSystem.Config.RuntimeData.EnableRuntimeDataHotPathPatch)
        {
            PatchRuntimeDataHotPaths();
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        ThreadAssert.CaptureClientMainThread();
        MainThreadDispatcher dispatcher = MainThreadDispatcher.ForClient(api);
        clientNetwork = ResponsiveNetwork.RegisterClient(api, dispatcher);
        _ = new TransactionClassifier(clientNetwork, clientPending);
        PatchClientObserveDiagnostics();

        api.Event.LevelFinalize += () =>
        {
            clientNetwork.TrySendHandshake();
        };

        api.Event.LeaveWorld += () =>
        {
            clientPending.ClearAll();
            clientTransactionIds.Reset();
            ClientInventoryPreviewStore.Clear();
            clientNetwork.DisableOwnership("client world left");
        };
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ThreadAssert.CaptureServerMainThread();
        MainThreadDispatcher dispatcher = MainThreadDispatcher.ForServer(api);
        serverNetwork = ResponsiveNetwork.RegisterServer(api, dispatcher);
        PatchServerObserveDiagnostics();

        serverNetwork.ServerTransactionRequestReceived += (player, request) =>
        {
            ResponsiveDiagnostics.Basic("Observed transaction request from {0}: tx={1} op={2} target={3}[{4}]",
                player?.PlayerName,
                request?.TransactionId ?? 0,
                request?.OperationKind ?? 0,
                request?.TargetInventoryId,
                request?.TargetSlotId ?? -1);
        };

        serverNetwork.ServerSnapshotRequestReceived += (player, request) =>
        {
            ResponsiveDiagnostics.Basic("Observed snapshot request from {0}: reason={1}", player?.PlayerName, request?.Reason);
        };

        api.Event.PlayerLeave += player =>
        {
            serverPending.Clear(player.PlayerUID);
        };
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        clientPending.ClearAll();
        serverPending.ClearAll();
        ClientInventoryPreviewStore.Clear();
        clientObservePatched = false;
        serverObservePatched = false;
        sharedWorldObservePatched = false;
        runtimeDataPatched = false;
        RuntimeDataCache.Clear();
        RuntimeDataStats.Reset();
    }

    private void PatchClientObserveDiagnostics()
    {
        if (clientObservePatched)
        {
            return;
        }

        clientObservePatched = true;

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.SlotClick), new[] { typeof(ICoreClientAPI), typeof(int), typeof(EnumMouseButton), typeof(bool), typeof(bool), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.SlotClickPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.SlotClickPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseDownOnElement), new[] { typeof(ICoreClientAPI), typeof(MouseEvent) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.OnMouseDownOnElementPrefix)));

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseMove), new[] { typeof(ICoreClientAPI), typeof(MouseEvent) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.OnMouseMovePrefix)));

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.OnMouseUp), new[] { typeof(ICoreClientAPI), typeof(MouseEvent) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.OnMouseUpPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.OnMouseUpPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), "SlotMouseWheel", new[] { typeof(int), typeof(int) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.SlotMouseWheelPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.SlotMouseWheelPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.RenderInteractiveElements), new[] { typeof(float) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientPreviewRendering), nameof(Client.Patches.Patch_ClientPreviewRendering.GridRenderPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientPreviewRendering), nameof(Client.Patches.Patch_ClientPreviewRendering.GridRenderPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.RenderInteractiveElements), new[] { typeof(float) }),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe), nameof(Client.Patches.Patch_GuiElementItemSlotGridBase_Observe.RenderPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(GuiElementPassiveItemSlot), nameof(GuiElementPassiveItemSlot.RenderInteractiveElements), new[] { typeof(float) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientPreviewRendering), nameof(Client.Patches.Patch_ClientPreviewRendering.PassiveSlotRenderPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientPreviewRendering), nameof(Client.Patches.Patch_ClientPreviewRendering.PassiveSlotRenderPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.GetActivateSlotPacket), new[] { typeof(int), typeof(ItemStackMoveOperation) }),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.GetActivateSlotPacketPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.GetFlipSlotsPacket), new[] { typeof(IInventory), typeof(int), typeof(int) }),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.GetFlipSlotsPacketPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.UpdateFromPacketPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.UpdateFromPacketPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(PlayerInventoryNetworkUtil), nameof(PlayerInventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryUpdate) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.PlayerUpdateFromPacketPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.PlayerUpdateFromPacketPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryDoubleUpdate) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.DoubleUpdateFromPacketPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.DoubleUpdateFromPacketPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.UpdateFromPacket), new[] { typeof(IWorldAccessor), typeof(Packet_InventoryContents) }),
            prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.ContentsUpdateFromPacketPrefix)),
            postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientInventoryNetworkObserve), nameof(Client.Patches.Patch_ClientInventoryNetworkObserve.ContentsUpdateFromPacketPostfix)));

        Type clientMainType = AccessTools.TypeByName("Vintagestory.Client.NoObf.ClientMain");
        MethodInfo sendHandInteraction = clientMainType == null
            ? null
            : AccessTools.Method(clientMainType, "SendHandInteraction", new[] { typeof(int), typeof(BlockSelection), typeof(EntitySelection), typeof(EnumHandInteract), typeof(EnumHandInteractNw), typeof(bool), typeof(EnumItemUseCancelReason) });
        if (sendHandInteraction != null)
        {
            harmony.Patch(
                sendHandInteraction,
                prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientWorldInteractionObserve), nameof(Client.Patches.Patch_ClientWorldInteractionObserve.SendHandInteractionPrefix)));
        }
        else
        {
            ResponsiveDiagnostics.Warning("Unable to patch client world hand interaction: ClientMain.SendHandInteraction not found.");
        }

        Type mouseWorldType = AccessTools.TypeByName("Vintagestory.Client.NoObf.SystemMouseInWorldInteractions");
        MethodInfo onGameTick = mouseWorldType == null ? null : AccessTools.Method(mouseWorldType, "OnGameTick", new[] { typeof(float) });
        MethodInfo handleHandInteraction = mouseWorldType == null ? null : AccessTools.Method(mouseWorldType, "HandleHandInteraction", new[] { typeof(float) });
        if (onGameTick != null)
        {
            harmony.Patch(
                onGameTick,
                prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientWorldTimingObserve), nameof(Client.Patches.Patch_ClientWorldTimingObserve.OnGameTickPrefix)),
                postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientWorldTimingObserve), nameof(Client.Patches.Patch_ClientWorldTimingObserve.OnGameTickPostfix)));
        }
        else
        {
            ResponsiveDiagnostics.Warning("Unable to patch client world timing: SystemMouseInWorldInteractions.OnGameTick not found.");
        }

        if (handleHandInteraction != null)
        {
            harmony.Patch(
                handleHandInteraction,
                prefix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientWorldTimingObserve), nameof(Client.Patches.Patch_ClientWorldTimingObserve.HandleHandInteractionPrefix)),
                postfix: new HarmonyMethod(typeof(Client.Patches.Patch_ClientWorldTimingObserve), nameof(Client.Patches.Patch_ClientWorldTimingObserve.HandleHandInteractionPostfix)));
        }
        else
        {
            ResponsiveDiagnostics.Warning("Unable to patch client world timing: SystemMouseInWorldInteractions.HandleHandInteraction not found.");
        }
    }

    private void PatchServerObserveDiagnostics()
    {
        if (serverObservePatched)
        {
            return;
        }

        serverObservePatched = true;

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.HandleClientPacket), new[] { typeof(IPlayer), typeof(int), typeof(Packet_Client) }),
            prefix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryNetworkObserve), nameof(Server.Patches.Patch_ServerInventoryNetworkObserve.HandleClientPacketPrefix)),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryNetworkObserve), nameof(Server.Patches.Patch_ServerInventoryNetworkObserve.HandleClientPacketPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.ActivateSlot), new[] { typeof(int), typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType() }),
            prefix: new HarmonyMethod(typeof(Server.Patches.Patch_InventoryBase_ActivateSlot_Observe), nameof(Server.Patches.Patch_InventoryBase_ActivateSlot_Observe.Prefix)),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_InventoryBase_ActivateSlot_Observe), nameof(Server.Patches.Patch_InventoryBase_ActivateSlot_Observe.Postfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.TryMoveItemStack), new[] { typeof(IPlayer), typeof(string[]), typeof(int[]), typeof(ItemStackMoveOperation).MakeByRefType() }),
            prefix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryMutationObserve), nameof(Server.Patches.Patch_ServerInventoryMutationObserve.TryMoveItemStackPrefix)),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryMutationObserve), nameof(Server.Patches.Patch_ServerInventoryMutationObserve.TryMoveItemStackPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.TryFlipItemStack), new[] { typeof(IPlayer), typeof(string[]), typeof(int[]), typeof(long[]) }),
            prefix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryMutationObserve), nameof(Server.Patches.Patch_ServerInventoryMutationObserve.TryFlipItemStackPrefix)),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryMutationObserve), nameof(Server.Patches.Patch_ServerInventoryMutationObserve.TryFlipItemStackPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.DidModifyItemSlot), new[] { typeof(ItemSlot), typeof(ItemStack) }),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryMutationObserve), nameof(Server.Patches.Patch_ServerInventoryMutationObserve.DidModifyItemSlotPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryBase), nameof(InventoryBase.MarkSlotDirty), new[] { typeof(int) }),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryMutationObserve), nameof(Server.Patches.Patch_ServerInventoryMutationObserve.MarkSlotDirtyPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.getSlotUpdatePacket), new[] { typeof(IPlayer), typeof(int) }),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryNetworkObserve), nameof(Server.Patches.Patch_ServerInventoryNetworkObserve.GetSlotUpdatePacketPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(InventoryNetworkUtil), nameof(InventoryNetworkUtil.getDoubleUpdatePacket), new[] { typeof(IPlayer), typeof(string[]), typeof(int[]) }),
            postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerInventoryNetworkObserve), nameof(Server.Patches.Patch_ServerInventoryNetworkObserve.GetDoubleUpdatePacketPostfix)));

        Type connectedClientType = AccessTools.TypeByName("Vintagestory.Server.ConnectedClient");
        Type serverInventoryType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemInventory");
        MethodInfo serverHandInteraction = serverInventoryType == null || connectedClientType == null
            ? null
            : AccessTools.Method(serverInventoryType, "HandleHandInteraction", new[] { typeof(Packet_Client), connectedClientType });
        if (serverHandInteraction != null)
        {
            harmony.Patch(
                serverHandInteraction,
                prefix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerWorldInteractionObserve), nameof(Server.Patches.Patch_ServerWorldInteractionObserve.InventoryHandInteractionPrefix)),
                postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerWorldInteractionObserve), nameof(Server.Patches.Patch_ServerWorldInteractionObserve.InventoryHandInteractionPostfix)));
        }
        else
        {
            ResponsiveDiagnostics.Warning("Unable to patch server held-item hand interaction: ServerSystemInventory.HandleHandInteraction not found.");
        }

        MethodInfo serverUsingTick = serverInventoryType == null ? null : AccessTools.Method(serverInventoryType, "OnUsingTick", new[] { typeof(float) });
        if (serverUsingTick != null)
        {
            harmony.Patch(
                serverUsingTick,
                prefix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerUsingTickObserve), nameof(Server.Patches.Patch_ServerUsingTickObserve.OnUsingTickPrefix)),
                postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerUsingTickObserve), nameof(Server.Patches.Patch_ServerUsingTickObserve.OnUsingTickPostfix)));
        }
        else
        {
            ResponsiveDiagnostics.Warning("Unable to patch server using tick: ServerSystemInventory.OnUsingTick not found.");
        }

        Type blockSimulationType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemBlockSimulation");
        MethodInfo blockHandInteraction = blockSimulationType == null || connectedClientType == null
            ? null
            : AccessTools.Method(blockSimulationType, "HandleBlockInteract", new[] { typeof(Packet_Client), connectedClientType });
        if (blockHandInteraction != null)
        {
            harmony.Patch(
                blockHandInteraction,
                prefix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerWorldInteractionObserve), nameof(Server.Patches.Patch_ServerWorldInteractionObserve.BlockHandInteractionPrefix)),
                postfix: new HarmonyMethod(typeof(Server.Patches.Patch_ServerWorldInteractionObserve), nameof(Server.Patches.Patch_ServerWorldInteractionObserve.BlockHandInteractionPostfix)));
        }
        else
        {
            ResponsiveDiagnostics.Warning("Unable to patch server block hand interaction: ServerSystemBlockSimulation.HandleBlockInteract not found.");
        }
    }

    private void PatchSharedWorldObserveDiagnostics()
    {
        if (sharedWorldObservePatched)
        {
            return;
        }

        sharedWorldObservePatched = true;

        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStart), new[] { typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(EnumHandInteract), typeof(bool), typeof(EnumHandHandling).MakeByRefType() }),
            prefix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.HeldUseStartPrefix)),
            postfix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.HeldUseStartPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStep), new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection) }),
            postfix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.HeldUseStepPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseStop), new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(EnumHandInteract) }),
            prefix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.HeldUseStopPrefix)));

        harmony.Patch(
            AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.OnHeldUseCancel), new[] { typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(EnumItemUseCancelReason) }),
            postfix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.HeldUseCancelPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(Block), nameof(Block.OnBlockInteractStart), new[] { typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection) }),
            prefix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.BlockInteractStartPrefix)),
            postfix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.BlockInteractStartPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(Block), nameof(Block.OnBlockInteractStep), new[] { typeof(float), typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection) }),
            postfix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.BlockInteractStepPostfix)));

        harmony.Patch(
            AccessTools.Method(typeof(Block), nameof(Block.OnBlockInteractStop), new[] { typeof(float), typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection) }),
            prefix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.BlockInteractStopPrefix)));

        harmony.Patch(
            AccessTools.Method(typeof(Block), nameof(Block.OnBlockInteractCancel), new[] { typeof(float), typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection), typeof(EnumItemUseCancelReason) }),
            postfix: new HarmonyMethod(typeof(Common.Patches.Patch_WorldCallbackObserve), nameof(Common.Patches.Patch_WorldCallbackObserve.BlockInteractCancelPostfix)));
    }

    private void PatchRuntimeDataHotPaths()
    {
        if (runtimeDataPatched)
        {
            return;
        }

        runtimeDataPatched = true;

        MethodInfo[] asObjectMethods = typeof(JsonObject)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == nameof(JsonObject.AsObject) && method.IsGenericMethodDefinition)
            .ToArray();

        MethodInfo asObjectSimple = asObjectMethods.FirstOrDefault(method => method.GetParameters().Length == 1);
        MethodInfo asObjectDomain = asObjectMethods.FirstOrDefault(method => method.GetParameters().Length == 2);
        MethodInfo asObjectSettings = asObjectMethods.FirstOrDefault(method => method.GetParameters().Length == 3);

        PatchRuntimeDataMethod(
            asObjectSimple,
            typeof(JsonObjectHotPathPatch),
            nameof(JsonObjectHotPathPatch.AsObjectPrefix),
            nameof(JsonObjectHotPathPatch.AsObjectPostfix),
            "JsonObject.AsObject<T>(T)");

        PatchRuntimeDataMethod(
            asObjectDomain,
            typeof(JsonObjectHotPathPatch),
            nameof(JsonObjectHotPathPatch.AsObjectDomainPrefix),
            nameof(JsonObjectHotPathPatch.AsObjectDomainPostfix),
            "JsonObject.AsObject<T>(T,string)");

        PatchRuntimeDataMethod(
            asObjectSettings,
            typeof(JsonObjectHotPathPatch),
            nameof(JsonObjectHotPathPatch.AsObjectSettingsPrefix),
            nameof(JsonObjectHotPathPatch.AsObjectSettingsPostfix),
            "JsonObject.AsObject<T>(JsonSerializerSettings,T,string)");

        if (ResponsiveVSConfigSystem.Config.RuntimeData.EnableStackAttributePacketDiagnostics)
        {
            MethodInfo toPacket = AccessTools.Method(typeof(StackConverter), nameof(StackConverter.ToPacket), new[] { typeof(ItemStack) });
            if (toPacket != null)
            {
                harmony.Patch(
                    toPacket,
                    postfix: new HarmonyMethod(typeof(StackAttributePacketDiagnosticsPatch), nameof(StackAttributePacketDiagnosticsPatch.ToPacketPostfix)));
            }
            else
            {
                ResponsiveDiagnostics.Warning("Unable to patch runtime data stack diagnostics: StackConverter.ToPacket not found.");
            }
        }

        ResponsiveDiagnostics.Warning(
            "RuntimeData hot-path patch enabled. This is experimental and should be disabled if any mod expects JsonObject.AsObject<T>() to return a fresh mutable object.");

        ResponsiveDiagnostics.Basic(
            "RuntimeData hot-path patch enabled. asObjectCache={0} maxEntries={1} stackPacketDiag={2}",
            ResponsiveVSConfigSystem.Config.RuntimeData.EnableAsObjectResultCache,
            ResponsiveVSConfigSystem.Config.RuntimeData.MaxCachedAsObjectResults,
            ResponsiveVSConfigSystem.Config.RuntimeData.EnableStackAttributePacketDiagnostics);
    }

    private void PatchRuntimeDataMethod(MethodInfo original, Type patchType, string prefixName, string postfixName, string label)
    {
        if (original == null)
        {
            ResponsiveDiagnostics.Warning("Unable to patch runtime data hot path: {0} not found.", label);
            return;
        }

        MethodInfo prefix = AccessTools.Method(patchType, prefixName);
        MethodInfo postfix = AccessTools.Method(patchType, postfixName);
        if (prefix == null || postfix == null)
        {
            ResponsiveDiagnostics.Warning("Unable to patch runtime data hot path: {0} patch method missing.", label);
            return;
        }

        harmony.Patch(original, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
    }
}
