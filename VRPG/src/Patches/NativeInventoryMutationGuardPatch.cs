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

namespace VRPG.Patches;

/// <summary>Refuses audited direct manager and death effects before native source or callback mutations.</summary>
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
            var witnesses = new List<MethodInfo>();
            foreach (var boundary in AuditedBoundaries())
            {
                var method = resolve(boundary.Owner, boundary.Name, boundary.Parameters);
                PinnedNativeBoundary.Validate(method, boundary.Owner, boundary.Name, boundary.Parameters, boundary.Hash, boundary.Module);
                if (method!.ReturnType != boundary.ReturnType) throw new InvalidOperationException("Unsupported native return type.");
                (boundary.Guarded ? targets : witnesses).Add(method);
            }
            ValidatePlayerOverrides();
            var enqueue = witnesses.Single(method => method.Name == "OnEntityDeath");
            var deferred = FindDeathCallback(enqueue);
            PinnedNativeBoundary.Validate(deferred, enqueue.DeclaringType!, deferred.Name, Type.EmptyTypes,
                "7FD9797AD6F934F9F6D455448B6851ACF718FFA623C1DE73C040FD5D27A85F81", PinnedNativeBoundary.EssentialsModule);
            targets.Add(deferred);
            installation = new(harmony, targets.ToArray(), witnesses.ToArray());
            foreach (var method in targets)
                harmony.Patch(method, prefix: new HarmonyMethod(PrefixFor(method)) { priority = Priority.First });
            if (!IsInstalled) throw new InvalidOperationException("Native manager/death prefix installation was not confirmed.");
            logger.Notification("[VRPG/Inventory] Validated native direct manager and deferred player death refusal installed.");
        }
        catch (Exception error)
        {
            ResetInstallation();
            logger.Error("[VRPG/Inventory] Native manager/death refusal is unavailable; startup protection is incomplete: {0}", error.Message);
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
        if (previous != null)
            foreach (var method in previous.Targets) previous.Harmony.Unpatch(method, PrefixFor(method));
    }

    private static MethodInfo PrefixFor(MethodInfo method) => AccessTools.Method(typeof(NativeInventoryMutationGuardPatch),
        method.Name == "DropAll" ? nameof(InventoryPrefix)
        : method.Name == "TryTransferAway" ? nameof(TransferPrefix)
        : method.ReturnType == typeof(bool) ? nameof(ManagerBooleanPrefix)
        : method.Name == "OnDeath" ? nameof(ManagerPrefix) : nameof(DeathPrefix));

    private sealed record Installation(Harmony Harmony, MethodInfo[] Targets, MethodInfo[] Witnesses)
    {
        public bool HasOnlyExpectedPatches() => Targets.All(method =>
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            return info != null && info.Prefixes.Count == 1 && info.Prefixes[0].owner == Harmony.Id
                && info.Prefixes[0].PatchMethod == PrefixFor(method) && info.Postfixes.Count == 0
                && info.Transpilers.Count == 0 && info.Finalizers.Count == 0;
        }) && Witnesses.All(method => HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Count is null or 0);
    }
}
