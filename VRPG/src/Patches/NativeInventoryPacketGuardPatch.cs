using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using VRPG.Inventory;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace VRPG.Patches;

/// <summary>Refuses unsupported native inventory packets before slot access or arbitrary callbacks.</summary>
public static class NativeInventoryPacketGuardPatch
{
    public static System.Func<string, PlayerItemCustodySnapshot>? CustodyResolver { get; set; }
    public static Action<string>? CursorRecoveryObserver { get; set; }
    private static Installation? installation;
    public static bool IsInstalled => installation?.HasOnlyExpectedPatches() == true;
    public static bool IsReady => IsInstalled && installation?.Failed == false && CustodyResolver != null && CursorRecoveryObserver != null;

    public static void Patch(Harmony harmony, ILogger logger,
        System.Func<Type, string, Type[], MethodInfo?>? resolve = null)
    {
        ResetInstallation();
        try
        {
            resolve ??= (owner, name, args) => AccessTools.Method(owner, name, args);
            var targets = new List<MethodInfo>();
            foreach (var (ownerName, name, hash) in AuditedBodies)
            {
                Type type = AccessTools.TypeByName(ownerName)
                    ?? throw new InvalidOperationException(ownerName + " is unavailable.");
                var parameters = name == "secureMouseSlotContents" ? new[] { typeof(ServerPlayer) }
                    : new[] { typeof(Packet_Client), typeof(ConnectedClient) };
                MethodInfo? method = resolve(type, name, parameters);
                if (method?.DeclaringType != type || method.Name != name || method.IsStatic || method.ReturnType != typeof(void)
                    || !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)
                    || method.Module.ModuleVersionId.ToString() != "8a3ad3aa-2940-4c39-ae1b-881de9e65346"
                    || Convert.ToHexString(SHA256.HashData(method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>())) != hash
                    || Harmony.GetPatchInfo(method)?.Owners.Count > 0)
                    throw new InvalidOperationException("Unsupported native inventory boundary: " + name);
                targets.Add(method);
            }
            var witnesses = new List<MethodBase>();
            void Witness(string ownerName, string name, Type[] parameters, string hash)
            {
                Type type = AccessTools.TypeByName(ownerName)
                    ?? throw new InvalidOperationException(ownerName + " is unavailable.");
                MethodBase? method = name == ".ctor" ? AccessTools.Constructor(type, parameters) : resolve(type, name, parameters);
                if (method?.DeclaringType != type || method.Name != name || method.IsStatic
                    || !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)
                    || method.Module.ModuleVersionId.ToString() != "8a3ad3aa-2940-4c39-ae1b-881de9e65346"
                    || Convert.ToHexString(SHA256.HashData(method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>())) != hash
                    || Harmony.GetPatchInfo(method)?.Owners.Count > 0)
                    throw new InvalidOperationException("Unsupported native inventory route: " + name);
                witnesses.Add(method);
            }
            foreach (var (ownerName, name, parameters, hash) in AuditedRoutes)
                Witness(ownerName, name, parameters, hash);
            installation = new Installation(harmony, targets.ToArray(), witnesses.ToArray());
            foreach (var method in targets)
                harmony.Patch(method, prefix: new HarmonyMethod(PrefixFor(method)) { priority = Priority.First });
            if (!IsInstalled) throw new InvalidOperationException("Native inventory prefix installation was not confirmed.");
            logger.Notification("[VRPG/Inventory] Validated native 1.22.7 inventory and world-item packet refusal boundaries installed.");
        }
        catch (Exception error)
        {
            ResetInstallation();
            logger.Error("[VRPG/Inventory] Native packet/cursor refusal is unavailable; activation must remain blocked: {0}", error.Message);
        }
    }

    public static bool PacketPrefix(ConnectedClient? client) => Resolve(client?.Player).AllowsVanilla;

    public static bool CursorPrefix(ServerPlayer? player)
    {
        if (Resolve(player).AllowsVanilla) return true;
        if (player == null) return false;
        // Retaining this source is deliberately not a durability claim. Any stray cursor blocks
        // activation until explicit recovery; disconnect and shutdown must not transfer/drop it.
        bool needsRecovery;
        try { needsRecovery = player.InventoryManager?.GetOwnInventory("mouse") is not { Empty: true }; }
        catch { needsRecovery = true; }
        if (needsRecovery)
        {
            try
            {
                if (CursorRecoveryObserver == null) throw new InvalidOperationException("Cursor recovery observer is unavailable.");
                CursorRecoveryObserver(player.PlayerUID);
            }
            catch
            {
                // Never replay a failed observer or renew readiness with an unrecorded source.
                if (installation != null) installation.Failed = true;
            }
        }
        return false;
    }

    private static PlayerItemCustodySnapshot Resolve(ServerPlayer? player)
    {
        try
        {
            string? uid = player?.PlayerUID;
            return string.IsNullOrWhiteSpace(uid) ? PlayerItemCustodySnapshot.Unresolved
                : CustodyResolver?.Invoke(uid) ?? PlayerItemCustodySnapshot.Unresolved;
        }
        catch { return PlayerItemCustodySnapshot.Unresolved; }
    }

    public static void Reset()
    {
        ResetInstallation();
        CustodyResolver = null;
        CursorRecoveryObserver = null;
    }

    private static void ResetInstallation()
    {
        var previous = installation;
        installation = null;
        if (previous == null) return;
        foreach (var method in previous.Targets) previous.Harmony.Unpatch(method, PrefixFor(method));
    }

    private static MethodInfo PrefixFor(MethodInfo target) => AccessTools.Method(typeof(NativeInventoryPacketGuardPatch),
        target.Name == "secureMouseSlotContents" ? nameof(CursorPrefix) : nameof(PacketPrefix));

    private sealed record Installation(Harmony Harmony, MethodInfo[] Targets, MethodBase[] Witnesses)
    {
        public bool Failed { get; set; }
        public bool HasOnlyExpectedPatches() => Targets.All(method =>
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            return info != null && info.Prefixes.Count == 1 && info.Prefixes[0].owner == Harmony.Id
                && info.Prefixes[0].PatchMethod == PrefixFor(method)
                && info.Postfixes.Count == 0 && info.Transpilers.Count == 0 && info.Finalizers.Count == 0;
        }) && Witnesses.All(method => HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Count is null or 0);
    }

    private static readonly (string Owner, string Name, string Hash)[] AuditedBodies =
    {
        ("Vintagestory.Server.ServerSystemInventory", "HandleSelectedHotbarSlot", "F8656C046A4BA2895933516FBA2958B809E1A333466E99D7F938C62A49F28FC8"),
        ("Vintagestory.Server.ServerSystemInventory", "HandleActivateInventorySlot", "A12F43545FCA10B3B2C7C10DC2A18ED02B3151561A337DFA2B651AC2AFBF84BA"),
        ("Vintagestory.Server.ServerSystemInventory", "HandleCreateItemstack", "FA5EE8BDA5F4606B8EDADDEBF18D67D61DD299FA294469B2128112C1FA80A857"),
        ("Vintagestory.Server.ServerSystemInventory", "HandleMoveItemstack", "F226B3AF97202F0BD7444F86715832E762B718798B2E66FF2DF198A594D78CC7"),
        ("Vintagestory.Server.ServerSystemInventory", "HandleFlipItemStacks", "3CFC2BA6C9BB6C3C5B20447E50C606EA661160138FDAF9C4D0C283B119F9D649"),
        ("Vintagestory.Server.ServerSystemInventory", "HandleHandInteraction", "29D0737E63D7BA59A969ADD521C01A26F8AA43F4B683D392CFB626CA7E4195B5"),
        ("Vintagestory.Server.ServerSystemInventory", "HandleToolMode", "E63847AEC98E023A6939C5B1608C60B06644B9DDD1C5C72F2723F7A8C1945C6A"),
        ("Vintagestory.Server.ServerSystemInventory", "HandleInvOpenClose", "BF3990E1AC28AC2B5FA4151D6A493C64610F4D80848D6D2520F4E39F99856C32"),
        ("Vintagestory.Server.ServerSystemInventory", "secureMouseSlotContents", "FDC43A098E8B16C79AB3F9B0D160CFFB222D033F3B6339A02B8814AB1EB6045B"),
        ("Vintagestory.Server.ServerSystemBlockSimulation", "HandleBlockPlaceOrBreak", "8BF099DE44F80AC84D3019FFD0779355FF593D4AC2C5F3A21297180DB22FF355"),
        ("Vintagestory.Server.ServerSystemBlockSimulation", "HandleBlockEntityPacket", "814B4A3FF08C6A204333FF805D15A2FB25C3B52518DD818E748110564573E7E8"),
        ("Vintagestory.Server.ServerMain", "HandleEntityPacket", "92C421A4DEFAB2D9EC83BE894068A3DA49420454984780A02F5B6B0EC3FD3966")
    };

    private static readonly (string Owner, string Name, Type[] Parameters, string Hash)[] AuditedRoutes =
    {
        ("Vintagestory.Server.ServerSystemInventory", ".ctor", new[] { typeof(ServerMain) }, "617752DD61B9B8627C58277209865472BC1AD54C1B09B8164C0D38A426C02691"),
        ("Vintagestory.Server.ServerSystemInventory", "OnPlayerDisconnect", new[] { typeof(ServerPlayer) }, "2CA7C5DCC77945A9C525A95439A8C0A6849889C89035E837BE919FE7F2DD0320"),
        ("Vintagestory.Server.ServerSystemInventory", "OnBeginShutdown", Type.EmptyTypes, "FCB7B6D4FA33A31A85013B43AAC9BB7B0E57F6F10965E2552E02EC14B1BF7925"),
        ("Vintagestory.Server.ServerSystemBlockSimulation", ".ctor", new[] { typeof(ServerMain) }, "D68C1AA9DCEE80AE52DB22FAE55A67FBFCEA8105AD2DE1337CE5F146E5E94CBF"),
        ("Vintagestory.Server.ServerMain", "InitBasicPacketHandlers", Type.EmptyTypes, "DE1CF0E6B35FE1DEA644B4EFB85D5B23343CDC374F96FE8F9141BB06F033063B")
    };
}
