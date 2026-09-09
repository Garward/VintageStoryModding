using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace VRPG.Patches;

public static partial class NativeInventoryMutationGuardPatch
{
    private static readonly MethodInfo InitMethod = AccessTools.Method(typeof(ServerWorldPlayerData), "Init", new[] { typeof(ServerMain) });
    private static readonly MethodInfo QueueTranspilerMethod = AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), nameof(QueueTranspiler));
    private static readonly MethodInfo NativeClearMethod = AccessTools.Method(typeof(InventoryBase), "Clear", Type.EmptyTypes);
    private static readonly FieldInfo LoadedUid = AccessTools.Field(typeof(ServerWorldPlayerData), "PlayerUID");
    private static readonly FieldInfo LoadedInventories = AccessTools.Field(typeof(ServerWorldPlayerData), "inventories");
    [ThreadStatic] private static ReconstructionClear? reconstructionClear;

    private sealed record ReconstructionClear(InventoryBase Inventory, ServerWorldPlayerData Data, ServerMain Server);

    private static bool ConsumeQueuedClear(HashSet<string> requests, string uid, ServerWorldPlayerData data, ServerMain server)
    {
        // Keep the native set's comparer and presence semantics; retire exactly this request,
        // even when its formerly Vanilla owner is now protected or authority is unavailable.
        bool requested = requests.Remove(uid);
        return requested && ReferenceEquals(requests, server.ClearPlayerInvs)
            && Equals(uid, LoadedUid.GetValue(data)) && AllowsUid(uid);
    }

    private static void ClearReconstructedInventory(InventoryBase inventory, ServerWorldPlayerData data, ServerMain server)
    {
        // This helper replaces only Init's pinned Clear call, after native deserialization
        // and insertion into this exact player's inventory dictionary. It carries identity,
        // never a cached custody decision. Clear consumes it before any callback can re-enter.
        reconstructionClear = new(inventory, data, server);
        try { inventory.Clear(); }
        finally { reconstructionClear = null; }
    }

    private static bool AllowsReconstructedClear(ReconstructionClear context)
    {
        try
        {
            var inventory = context.Inventory;
            var api = context.Server.Api;
            string? uid = LoadedUid.GetValue(context.Data) as string;
            var inventories = LoadedInventories.GetValue(context.Data)
                as Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>;
            return inventory is InventoryBasePlayer && !string.IsNullOrWhiteSpace(uid)
                && Equals(OwnerUid.GetValue(inventory), uid)
                && ReferenceEquals(inventory.Api, api) && api?.Side == EnumAppSide.Server
                && ReferenceEquals(api.World, context.Server)
                && inventories?.TryGetValue(inventory.InventoryID, out var loaded) == true
                && ReferenceEquals(loaded, inventory) && AllowsUid(uid);
        }
        catch { return false; }
    }

    private static IEnumerable<CodeInstruction> QueueTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        if (installation != null) installation.QueueInserted = false;
        var code = instructions.ToList();
        var (remove, clear) = FindQueueSites(code);
        // The original receiver and arguments are already on the evaluation stack.
        // Both replacement sites are label/exception-boundary free. All native branches,
        // reconstruction, catch/finally regions and non-clear instructions are retained.
        code[clear] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), nameof(ClearReconstructedInventory)));
        code.InsertRange(clear, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1) });
        code[remove] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeInventoryMutationGuardPatch), nameof(ConsumeQueuedClear)));
        code.InsertRange(remove, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1) });
        if (installation != null) installation.QueueInserted = true;
        return code;
    }

    private static (int Remove, int Clear) FindQueueSites(IList<CodeInstruction> code)
    {
        var removeMethod = AccessTools.Method(typeof(HashSet<string>), nameof(HashSet<string>.Remove), new[] { typeof(string) });
        var removes = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(removeMethod)).ToArray();
        var clears = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(NativeClearMethod)).ToArray();
        if (removes.Length != 1 || clears.Length != 1) throw new InvalidOperationException("Native queued-clear sites are missing or ambiguous.");
        int r = removes[0], c = clears[0];
        if (r < 4 || c <= r || c + 1 >= code.Count
            || code[r - 4].opcode != OpCodes.Ldarg_1
            || !code[r - 3].LoadsField(AccessTools.Field(typeof(ServerMain), nameof(ServerMain.ClearPlayerInvs)))
            || code[r - 2].opcode != OpCodes.Ldarg_0 || !code[r - 1].LoadsField(LoadedUid)
            || code[r].opcode != OpCodes.Callvirt || code[r + 1].opcode != OpCodes.Stloc_0
            || code[c - 3].opcode != OpCodes.Ldloc_0 || code[c - 2].opcode != OpCodes.Brfalse_S
            || code[c - 2].operand is not Label afterClear || !code[c + 1].labels.Contains(afterClear)
            || code[c - 1].opcode != OpCodes.Ldloc_S || code[c - 1].operand is not LocalBuilder { LocalIndex: 4, LocalType: var type }
            || type != typeof(InventoryBase) || code[c].opcode != OpCodes.Callvirt
            || new[] { r, c }.Any(i => code[i].labels.Count != 0 || code[i].blocks.Count != 0))
            throw new InvalidOperationException("Native queued-clear reconstruction route changed.");
        return (r, c);
    }
}
