using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace VRPG.Patches;

public static partial class NativeInventoryMutationGuardPatch
{
    private static MethodInfo? playerClearCallback;
    private static MethodInfo? selfClearCallback;
    [ThreadStatic] private static ClearCommandInvocation? clearCommandInvocation;
    private sealed record ClearCommandInvocation(TextCommandCallingArgs Args, ClearCommandInvocation? Previous, bool Self = false)
    {
        public bool Refused { get; set; }
    }

    private static bool PlayerClearCommandPrefix(TextCommandCallingArgs args, ref TextCommandResult __result, out ClearCommandInvocation? __state)
    {
        __state = null;
        try
        {
            if (args?.Parsers?.Count > 0 && args.Parsers[0].GetValue() is PlayerUidName[] targets
                && targets.All(target => AllowsUid(target?.Uid)))
            {
                // An empty selector stays on the native Each error path. This invocation
                // context records later failures; it never authorizes any item mutation.
                __state = new(args, clearCommandInvocation);
                clearCommandInvocation = __state;
                return true;
            }
        }
        catch { }
        __result = ClearRefusal();
        return false;
    }

    private static void PlayerClearCommandPostfix(ClearCommandInvocation? __state, ref TextCommandResult __result)
    {
        if (__state?.Refused == true)
            __result = TextCommandResult.Error("vrpg:inventory-clear-interrupted", "inventory-clear-interrupted");
    }

    private static Exception? PlayerClearCommandFinalizer(Exception? __exception, ClearCommandInvocation? __state)
    {
        if (__state != null) clearCommandInvocation = __state.Previous;
        return __exception;
    }

    private static bool ClearCommandPrefix(TextCommandCallingArgs args, ref TextCommandResult __result, out ClearCommandInvocation? __state)
    {
        __state = null;
        try
        {
            if (args?.Caller?.Player is IServerPlayer player
                && player.InventoryManager is PlayerInventoryManager manager
                && ReferenceEquals(manager.player, player) && AllowsManager(manager))
            {
                __state = new(args, clearCommandInvocation, Self: true);
                clearCommandInvocation = __state;
                return true;
            }
        }
        catch { }
        __result = ClearRefusal();
        return false;
    }

    private static bool ManagerDiscardPrefix(PlayerInventoryManager __instance)
    {
        bool allowed = AllowsManager(__instance);
        if (!allowed && clearCommandInvocation is { Self: true } invocation
            && ReferenceEquals(invocation.Args.Caller.Player?.InventoryManager, __instance)) invocation.Refused = true;
        return allowed;
    }

    private static void RecordSelfClearRefusal(InventoryBase inventory)
    {
        if (clearCommandInvocation is not { Self: true } invocation) return;
        try
        {
            if (invocation.Args.Caller.Player?.InventoryManager is PlayerInventoryManager manager
                && manager.Inventories.TryGetValue(inventory.InventoryID, out var selected)
                && ReferenceEquals(selected, inventory)) invocation.Refused = true;
        }
        catch { invocation.Refused = true; }
    }

    // The engine localizes TextCommandResult.StatusMessage at presentation time.
    private static TextCommandResult ClearRefusal() => TextCommandResult.Error("vrpg:inventory-clear-refused", "inventory-clear-refused");

    private static MethodInfo FindClearCallback(ConstructorInfo constructor)
    {
        var code = PatchProcessor.GetOriginalInstructions(constructor);
        var starts = Enumerable.Range(0, code.Count).Where(i => code[i].opcode == OpCodes.Ldstr
            && Equals(code[i].operand, "Empties your inventory")).ToArray();
        if (constructor.DeclaringType != typeof(CmdPlayer) || starts.Length != 1)
            throw new InvalidOperationException("Native clearinv registration is missing or ambiguous.");
        int i = starts[0];
        if (i < 6 || i + 12 >= code.Count
            || code[i - 6].opcode != OpCodes.Ldstr || !Equals(code[i - 6].operand, "clearinv")
            || code[i - 5].operand is not MethodInfo { Name: "BeginSub" }
            || !code[i - 4].LoadsField(AccessTools.Field(typeof(Privilege), nameof(Privilege.gamemode)))
            || !code[i - 3].Calls(AccessTools.Method(typeof(IChatCommand), nameof(IChatCommand.RequiresPrivilege)))
            || code[i - 2].opcode != OpCodes.Ldstr || !Equals(code[i - 2].operand, "clearinv")
            || !code[i - 1].Calls(AccessTools.Method(typeof(IChatCommand), nameof(IChatCommand.WithRootAlias)))
            || code[i + 7].opcode != OpCodes.Ldftn || code[i + 7].operand is not MethodInfo callback
            || callback.DeclaringType?.DeclaringType != typeof(CmdPlayer)
            || !callback.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)
            || code[i + 8].opcode != OpCodes.Newobj
            || !Equals(code[i + 8].operand, AccessTools.Constructor(typeof(OnCommandDelegate), new[] { typeof(object), typeof(IntPtr) }))
            || !code[i + 11].Calls(AccessTools.Method(typeof(IChatCommand), nameof(IChatCommand.HandleWith))))
            throw new InvalidOperationException("Native clearinv delegate/permission route changed.");
        PinnedNativeBoundary.Validate(callback, callback.DeclaringType!, callback.Name, new[] { typeof(TextCommandCallingArgs) },
            "4E461F2B2B2AF4ED905BDC4B4D3D53BC475AA511A633CB9CEADA5D6ADF133F06");
        if (callback.ReturnType != typeof(TextCommandResult)) throw new InvalidOperationException("Native clearinv result changed.");
        return callback;
    }

    private static MethodInfo FindPlayerClearCallback(ConstructorInfo constructor)
    {
        var code = PatchProcessor.GetOriginalInstructions(constructor);
        var starts = Enumerable.Range(0, code.Count).Where(i => code[i].opcode == OpCodes.Ldstr
            && Equals(code[i].operand, "Clear the player's entire inventory")).ToArray();
        if (constructor.DeclaringType != typeof(CmdPlayer) || starts.Length != 1)
            throw new InvalidOperationException("Native selected-player clear registration changed.");
        int i = starts[0];
        if (i < 4 || i + 5 >= code.Count || code[i - 4].opcode != OpCodes.Ldstr || !Equals(code[i - 4].operand, "clearinv")
            || code[i - 3].operand is not MethodInfo { Name: "BeginSub" }
            || !code[i - 2].LoadsField(AccessTools.Field(typeof(Privilege), nameof(Privilege.controlserver)))
            || !code[i - 1].Calls(AccessTools.Method(typeof(IChatCommand), nameof(IChatCommand.RequiresPrivilege)))
            || code[i + 3].opcode != OpCodes.Ldftn || code[i + 3].operand is not MethodInfo callback
            || callback.DeclaringType?.DeclaringType != typeof(CmdPlayer)
            || !callback.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)
            || code[i + 4].opcode != OpCodes.Newobj
            || !Equals(code[i + 4].operand, AccessTools.Constructor(typeof(OnCommandDelegate), new[] { typeof(object), typeof(IntPtr) }))
            || !code[i + 5].Calls(AccessTools.Method(typeof(IChatCommand), nameof(IChatCommand.HandleWith))))
            throw new InvalidOperationException("Native selected-player clear delegate route changed.");
        PinnedNativeBoundary.Validate(callback, callback.DeclaringType!, callback.Name, new[] { typeof(TextCommandCallingArgs) },
            "809540BEABB54AC6396361DACC2D017F88C5460B5ABA3988CAC0183CE919D903");
        var body = PatchProcessor.GetOriginalInstructions(callback);
        if (callback.ReturnType != typeof(TextCommandResult) || body.Count != 7
            || body[3].opcode != OpCodes.Ldftn || !Equals(body[3].operand, AccessTools.Method(typeof(CmdPlayer), "WipePlayerInventory"))
            || !body[5].Calls(AccessTools.Method(typeof(CmdPlayer), "Each")))
            throw new InvalidOperationException("Native selected-player clear aggregation changed.");
        return callback;
    }
}
