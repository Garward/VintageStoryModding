using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRPG.Inventory;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;

namespace VRPG.Patches;

/// <summary>Refuses audited manager, death and clear effects before native source or callback mutations.</summary>
public static partial class NativeInventoryMutationGuardPatch
{
    public static System.Func<string, PlayerItemCustodySnapshot>? CustodyResolver { get; set; }
    private static Installation? installation;
    private static readonly FieldInfo OwnerUid = AccessTools.Field(typeof(InventoryBasePlayer), "playerUID");
    public static bool IsInstalled => installation?.HasOnlyExpectedPatches() == true;
    public static bool IsReady => IsInstalled && CustodyResolver != null;

    public static void Patch(Harmony harmony, ILogger logger,
        System.Func<Type, string, Type[], MethodInfo?>? resolve = null)
    {
        ResetInstallation();
        try
        {
            resolve ??= (owner, name, args) => AccessTools.Method(owner, name, args);
            var targets = new List<MethodInfo>();
            var witnesses = new List<MethodBase>();
            foreach (var boundary in AuditedBoundaries().Concat(ClearBoundaries()))
            {
                var method = resolve(boundary.Owner, boundary.Name, boundary.Parameters);
                PinnedNativeBoundary.Validate(method, boundary.Owner, boundary.Name, boundary.Parameters, boundary.Hash, boundary.Module);
                if (method!.ReturnType != boundary.ReturnType) throw new InvalidOperationException("Unsupported native return type.");
                if (boundary.Guarded) targets.Add(method); else witnesses.Add(method);
            }
            ValidatePlayerOverrides();
            ValidateClearOverrides();
            var enqueue = (MethodInfo)witnesses.Single(method => method.Name == "OnEntityDeath");
            var deferred = FindDeathCallback(enqueue);
            PinnedNativeBoundary.Validate(deferred, enqueue.DeclaringType!, deferred.Name, Type.EmptyTypes,
                "7FD9797AD6F934F9F6D455448B6851ACF718FFA623C1DE73C040FD5D27A85F81", PinnedNativeBoundary.EssentialsModule);
            targets.Add(deferred);
            var commands = AccessTools.Constructor(typeof(CmdPlayer), new[] { typeof(ServerMain) });
            PinnedNativeBoundary.Validate(commands, typeof(CmdPlayer), ".ctor", new[] { typeof(ServerMain) },
                "44CB4FF126595160CF68A94EC59C9DA2074B1713E46F33A29736CE25D6E496C1");
            witnesses.Add(commands);
            selfClearCallback = FindClearCallback(commands);
            targets.Add(selfClearCallback);
            playerClearCallback = FindPlayerClearCallback(commands);
            targets.Add(playerClearCallback);
            var each = AccessTools.Method(typeof(CmdPlayer), "Each");
            PinnedNativeBoundary.Validate(each, typeof(CmdPlayer), "Each", new[] { typeof(TextCommandCallingArgs), typeof(CmdPlayer.PlayerEachDelegate) },
                "9C41A148B0BA2A7E06CBD14F33091F26D71826E0EFA22CA018E64D655BBD40CF", isStatic: true);
            witnesses.Add(each);
            FindQueueSites(PatchProcessor.GetOriginalInstructions(InitMethod));
            installation = new(harmony, targets.ToArray(), witnesses.ToArray());
            foreach (var method in targets)
                if (method == InitMethod) harmony.Patch(method, transpiler: new HarmonyMethod(QueueTranspilerMethod));
                else if (ResultPostfixFor(method) is { } postfix) harmony.Patch(method,
                    prefix: new HarmonyMethod(PrefixFor(method)) { priority = Priority.First },
                    postfix: new HarmonyMethod(postfix), finalizer: new HarmonyMethod(ResultFinalizerFor(method)));
                else harmony.Patch(method, prefix: new HarmonyMethod(PrefixFor(method)) { priority = Priority.First });
            if (!IsInstalled) throw new InvalidOperationException("Native manager/death/clear installation was not confirmed.");
            logger.Notification("[VRPG/Inventory] Validated native manager, death, clear commands and queued-clear refusal installed.");
        }
        catch (Exception error)
        {
            ResetInstallation();
            logger.Error("[VRPG/Inventory] Native manager/death/clear refusal is unavailable; startup protection is incomplete: {0}", error.Message);
        }
    }

    private static bool AllowsUid(string? uid)
    {
        try { return !string.IsNullOrWhiteSpace(uid) && CustodyResolver?.Invoke(uid).AllowsVanilla == true; }
        catch { return false; }
    }

    private static bool AllowsManager(PlayerInventoryManager? manager)
    {
        try
        {
            // This concrete native manager is exclusively client-local. A server manager
            // with a missing or misidentified player never inherits this permission.
            if (manager is ClientPlayerInventoryManager) return true;
            return manager?.player is IServerPlayer player && AllowsUid(player.PlayerUID);
        }
        catch { return false; }
    }

    private static bool ManagerPrefix(PlayerInventoryManager __instance) => AllowsManager(__instance);

    private static bool ManagerBooleanPrefix(PlayerInventoryManager __instance, ref bool __result)
    {
        if (AllowsManager(__instance)) return true;
        __result = false;
        return false;
    }

    private static bool TransferPrefix(PlayerInventoryManager __instance, ref object[]? __result)
    {
        if (AllowsManager(__instance)) return true;
        __result = null;
        return false;
    }

    private static bool InventoryPrefix(InventoryBasePlayer __instance)
    {
        try
        {
            if (__instance.Api?.Side == EnumAppSide.Client) return true;
            if (__instance.Api?.Side != EnumAppSide.Server) return false;
            string? uid = OwnerUid.GetValue(__instance) as string;
            return !string.IsNullOrWhiteSpace(uid) && __instance.Player is IServerPlayer player
                && player.PlayerUID == uid && AllowsUid(uid);
        }
        catch { return false; }
    }

    public static void Reset()
    {
        ResetInstallation();
        CustodyResolver = null;
    }

    private static void ResetInstallation()
    {
        var previous = installation;
        installation = null;
        reconstructionClear = null;
        clearCommandInvocation = null;
        wipeAttempt = null;
        if (previous != null)
            foreach (var method in previous.Targets)
            {
                previous.Harmony.Unpatch(method, method == InitMethod ? QueueTranspilerMethod : PrefixFor(method));
                if (ResultPostfixFor(method) is { } postfix)
                {
                    previous.Harmony.Unpatch(method, postfix);
                    previous.Harmony.Unpatch(method, ResultFinalizerFor(method));
                }
            }
        playerClearCallback = null;
        selfClearCallback = null;
    }

    private static readonly MethodInfo PlayerClearPostfix = AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), nameof(PlayerClearCommandPostfix));
    private static readonly MethodInfo PlayerClearFinalizer = AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), nameof(PlayerClearCommandFinalizer));
    private static readonly MethodInfo WipePostfix = AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), nameof(WipeInventoryPostfix));
    private static readonly MethodInfo WipeFinalizer = AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), nameof(WipeInventoryFinalizer));
    private static MethodInfo? ResultPostfixFor(MethodInfo method) => method == playerClearCallback || method == selfClearCallback ? PlayerClearPostfix
        : method.Name == "WipePlayerInventory" ? WipePostfix : null;
    private static MethodInfo? ResultFinalizerFor(MethodInfo method) => method == playerClearCallback || method == selfClearCallback ? PlayerClearFinalizer
        : method.Name == "WipePlayerInventory" ? WipeFinalizer : null;

    private static MethodInfo PrefixFor(MethodInfo method) => AccessTools.Method(typeof(NativeInventoryMutationGuardPatch),
        method == playerClearCallback ? nameof(PlayerClearCommandPrefix)
        : method.Name == "WipePlayerInventory" ? nameof(WipeInventoryPrefix)
        : method.ReturnType == typeof(TextCommandResult) ? nameof(ClearCommandPrefix)
        : method.Name == "Clear" || method.Name == "DiscardAll" && method.DeclaringType != typeof(PlayerInventoryManager) ? nameof(ClearInventoryPrefix)
        : method.Name == "DiscardAll" ? nameof(ManagerDiscardPrefix)
        : method.Name == "DropAll" ? nameof(InventoryPrefix)
        : method.Name == "TryTransferAway" ? nameof(TransferPrefix)
        : method.ReturnType == typeof(bool) ? nameof(ManagerBooleanPrefix)
        : method.Name == "OnDeath" ? nameof(ManagerPrefix) : nameof(DeathPrefix));

    private sealed record Installation(Harmony Harmony, MethodInfo[] Targets, MethodBase[] Witnesses)
    {
        public bool QueueInserted { get; set; }
        public bool HasOnlyExpectedPatches() => Targets.All(method =>
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            if (method == InitMethod)
                return QueueInserted && info != null && info.Transpilers.Count == 1
                    && info.Transpilers[0].owner == Harmony.Id && info.Transpilers[0].PatchMethod == QueueTranspilerMethod
                    && info.Prefixes.Count == 0 && info.Postfixes.Count == 0 && info.Finalizers.Count == 0;
            if (ResultPostfixFor(method) is { } postfix)
                return info != null && info.Prefixes.Count == 1 && info.Prefixes[0].owner == Harmony.Id
                    && info.Prefixes[0].PatchMethod == PrefixFor(method) && info.Transpilers.Count == 0
                    && info.Postfixes.Count == 1 && info.Postfixes[0].owner == Harmony.Id && info.Postfixes[0].PatchMethod == postfix
                    && info.Finalizers.Count == 1 && info.Finalizers[0].owner == Harmony.Id && info.Finalizers[0].PatchMethod == ResultFinalizerFor(method);
            return info != null && info.Prefixes.Count == 1 && info.Prefixes[0].owner == Harmony.Id
                && info.Prefixes[0].PatchMethod == PrefixFor(method) && info.Postfixes.Count == 0
                && info.Transpilers.Count == 0 && info.Finalizers.Count == 0;
        }) && Witnesses.All(method => HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Count is null or 0);
    }
}
