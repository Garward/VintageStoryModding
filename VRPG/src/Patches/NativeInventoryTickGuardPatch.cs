using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using VRPG.Inventory;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace VRPG.Patches;

/// <summary>Per-player refusal inside the two audited native background inventory loops.</summary>
public static class NativeInventoryTickGuardPatch
{
    public static System.Func<string, PlayerItemCustodySnapshot>? CustodyResolver { get; set; }
    private static Installation? installation;
    private static readonly MethodInfo TranspilerMethod = AccessTools.Method(typeof(NativeInventoryTickGuardPatch), nameof(Transpiler));
    public static bool IsInstalled => installation?.HasOnlyExpectedPatches() == true;
    public static bool IsReady => IsInstalled && CustodyResolver != null;

    public static void Patch(Harmony harmony, ILogger logger,
        System.Func<Type, string, Type[], MethodInfo?>? resolve = null)
    {
        ResetInstallation();
        try
        {
            var type = AccessTools.TypeByName("Vintagestory.Server.ServerSystemInventory")
                ?? throw new InvalidOperationException("ServerSystemInventory is unavailable.");
            resolve ??= (owner, name, args) => AccessTools.Method(owner, name, args);
            var targets = new List<MethodInfo>();
            foreach (var (name, hash) in AuditedBodies)
            {
                var method = resolve(type, name, new[] { typeof(float) });
                Validate(method, type, name, new[] { typeof(float) }, hash);
                if (method!.ReturnType != typeof(void)) throw new InvalidOperationException("Unsupported tick signature: " + name);
                FindInsertion(PatchProcessor.GetOriginalInstructions(method), method);
                targets.Add(method);
            }
            // The constructor is the registration witness: both callbacks and their periods
            // must still be routed by the exact audited installed engine constructor.
            var constructor = AccessTools.Constructor(type, new[] { typeof(ServerMain) });
            Validate(constructor, type, ".ctor", new[] { typeof(ServerMain) },
                "617752DD61B9B8627C58277209865472BC1AD54C1B09B8164C0D38A426C02691");
            installation = new(harmony, targets.ToArray(), constructor);
            foreach (var target in targets)
                harmony.Patch(target, transpiler: new HarmonyMethod(TranspilerMethod));
            if (!IsInstalled) throw new InvalidOperationException("Both native tick insertions were not confirmed.");
            logger.Notification("[VRPG/Inventory] Validated per-player refusal in ServerSystemInventory.OnUsingTick(float) and UpdateTransitionStates(float).");
        }
        catch (Exception error)
        {
            ResetInstallation();
            logger.Error("[VRPG/Inventory] Native background inventory refusal is unavailable; startup protection is incomplete: {0}", error.Message);
        }
    }

    private static void Validate(MethodBase? method, Type type, string name, Type[] parameters, string hash)
    {
        if (method?.DeclaringType != type || method.Name != name || method.IsStatic
            || !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)
            || method.Module.ModuleVersionId.ToString() != "8a3ad3aa-2940-4c39-ae1b-881de9e65346"
            || Convert.ToHexString(SHA256.HashData(method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>())) != hash
            || Harmony.GetPatchInfo(method)?.Owners.Count > 0)
            throw new InvalidOperationException("Unsupported native tick boundary or registration: " + name);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        installation?.Inserted.Remove(original);
        var code = instructions.ToList();
        var (index, nextPlayer) = FindInsertion(code, original);
        var guard = AccessTools.Method(typeof(NativeInventoryTickGuardPatch),
            original.Name == "OnUsingTick" ? nameof(AllowsPlayer) : nameof(AllowsClient));
        // This site has no incoming label or exception boundary (validated below). Branch
        // to the original outer MoveNext, inside its try; retain all native finally blocks.
        code.InsertRange(index, new[] { new CodeInstruction(OpCodes.Ldloc_1),
            new CodeInstruction(OpCodes.Call, guard), new CodeInstruction(OpCodes.Brfalse, nextPlayer) });
        installation?.Inserted.Add(original);
        return code;
    }

    private static (int Index, Label NextPlayer) FindInsertion(IList<CodeInstruction> code, MethodBase original)
    {
        bool usingTick = original.Name == "OnUsingTick";
        if (!usingTick && original.Name != "UpdateTransitionStates")
            throw new InvalidOperationException("Unexpected native tick method.");
        var getter = AccessTools.PropertyGetter(usingTick ? typeof(ServerPlayer) : typeof(ConnectedClient),
            usingTick ? nameof(ServerPlayer.ConnectionState) : nameof(ConnectedClient.IsPlayingClient));
        var matches = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(getter)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Native playing check is missing or ambiguous.");
        int index = matches[0] - 1;
        int branch = index + (usingTick ? 3 : 2);
        if (index < 1 || branch + 2 >= code.Count || code[index - 1].opcode != OpCodes.Stloc_1
            || code[index].opcode != OpCodes.Ldloc_1 || code[index + 1].opcode != OpCodes.Callvirt
            || usingTick && code[index + 2].opcode != OpCodes.Ldc_I4_4
            || code[branch].opcode != (usingTick ? OpCodes.Bne_Un : OpCodes.Brfalse)
            || code[branch].operand is not Label nextPlayer
            || code[branch + 1].opcode != OpCodes.Ldloc_1
            || !code[branch + 2].LoadsField(AccessTools.Field(usingTick ? typeof(ServerPlayer) : typeof(ConnectedClient),
                usingTick ? "inventoryMgr" : nameof(ConnectedClient.Player)))
            || Enumerable.Range(index, branch - index + 1).Any(i => code[i].labels.Count != 0 || code[i].blocks.Count != 0))
            throw new InvalidOperationException("Native pre-inventory insertion shape changed.");
        int target = -1;
        for (int i = 0; i < code.Count; i++) if (code[i].labels.Contains(nextPlayer))
        {
            if (target != -1) throw new InvalidOperationException("Duplicate outer-loop branch label.");
            target = i;
        }
        if (target <= branch || target + 2 >= code.Count || code[target].blocks.Count != 0
            || code[target].opcode != (usingTick ? OpCodes.Ldloca_S : OpCodes.Ldloc_0)
            || code[target + 1].operand is not MethodInfo { Name: "MoveNext", ReturnType: var returnType }
            || returnType != typeof(bool) || code[target + 2].opcode != OpCodes.Brtrue)
            throw new InvalidOperationException("Native outer-loop continuation changed.");
        // Input is the audited body, and the insertion and target remain in the same
        // outer try, before any nested inventory enumerator is acquired.
        return (index, nextPlayer);
    }

    private static bool AllowsClient(ConnectedClient? client) => AllowsPlayer(client?.Player);

    private static bool AllowsPlayer(ServerPlayer? player)
    {
        try
        {
            string? uid = player?.PlayerUID;
            return !string.IsNullOrWhiteSpace(uid) && CustodyResolver?.Invoke(uid).AllowsVanilla == true;
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
        if (previous == null) return;
        foreach (var method in previous.Targets) previous.Harmony.Unpatch(method, TranspilerMethod);
    }

    private sealed record Installation(Harmony Harmony, MethodInfo[] Targets, ConstructorInfo Constructor)
    {
        public HashSet<MethodBase> Inserted { get; } = new();
        public bool HasOnlyExpectedPatches() => Targets.All(method =>
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            return Inserted.Contains(method) && info != null && info.Transpilers.Count == 1
                && info.Transpilers[0].owner == Harmony.Id && info.Transpilers[0].PatchMethod == TranspilerMethod
                && info.Prefixes.Count == 0 && info.Postfixes.Count == 0 && info.Finalizers.Count == 0;
        }) && HarmonyLib.Harmony.GetPatchInfo(Constructor)?.Owners.Count is null or 0;
    }

    private static readonly (string Name, string Hash)[] AuditedBodies =
    {
        ("OnUsingTick", "0F8E037B6D7E54F81093350434B6A15D02B065411411C56FBBE65DE3FE8D4B87"),
        ("UpdateTransitionStates", "FA705EAFA20131F82011DA6F4D83547F8C17D81F08ECF7005CFB8EA2E2A476D9")
    };
}
