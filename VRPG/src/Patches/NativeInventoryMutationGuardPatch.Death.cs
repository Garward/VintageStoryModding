using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VRPG.Patches;

public static partial class NativeInventoryMutationGuardPatch
{
    private static bool DeathPrefix(EntityBehavior __instance)
    {
        try
        {
            if (__instance.entity?.Api?.Side == EnumAppSide.Client) return true;
            if (__instance.entity?.Api?.Side != EnumAppSide.Server || __instance.entity is not EntityPlayer entity) return false;
            return entity.Player is IServerPlayer player && player.PlayerUID == entity.PlayerUID && AllowsUid(entity.PlayerUID);
        }
        catch { return false; }
    }

    private static MethodInfo FindDeathCallback(MethodInfo enqueue)
    {
        var code = PatchProcessor.GetOriginalInstructions(enqueue);
        // Pin the actual native Action target from the audited producer IL. No generated
        // method name is guessed, and the original queue/delegate remain untouched.
        if (code.Count != 9 || code[0].opcode != OpCodes.Ldarg_0
            || !code[1].LoadsField(AccessTools.Field(typeof(EntityBehaviorContainer), "Api"))
            || !code[2].Calls(AccessTools.PropertyGetter(typeof(ICoreAPI), "Event"))
            || code[3].opcode != OpCodes.Ldarg_0 || code[4].opcode != OpCodes.Ldftn
            || code[4].operand is not MethodInfo callback || callback.DeclaringType != enqueue.DeclaringType
            || callback.IsStatic || callback.ReturnType != typeof(void) || callback.GetParameters().Length != 0
            || !callback.IsDefined(typeof(CompilerGeneratedAttribute), false)
            || code[5].opcode != OpCodes.Newobj || !Equals(code[5].operand, AccessTools.Constructor(typeof(Action), new[] { typeof(object), typeof(IntPtr) }))
            || code[6].opcode != OpCodes.Ldstr || !Equals(code[6].operand, "dropinventoryondeath")
            || !code[7].Calls(AccessTools.Method(typeof(IEventAPI), nameof(IEventAPI.EnqueueMainThreadTask), new[] { typeof(Action), typeof(string) }))
            || code[8].opcode != OpCodes.Ret)
            throw new InvalidOperationException("Native deferred death route changed.");
        return callback;
    }

    private static void ValidatePlayerOverrides()
    {
        // Source audit of pinned 1.22.7 native player inventories: these four inherit
        // the base effect; hotbar/backpacks override it and creative is an empty body.
        // Avoid loading every engine type (including unrelated optional client drivers).
        var inherited = new[] { typeof(InventoryPlayerMouseCursor), typeof(InventoryCraftingGrid),
            typeof(InventoryCharacter), AccessTools.TypeByName("Vintagestory.Common.InventoryPlayerGround") };
        foreach (var type in inherited)
            if (type == null || type.Module.ModuleVersionId.ToString() != PinnedNativeBoundary.LibModule
                || AccessTools.Method(type, "DropAll", new[] { typeof(Vec3d), typeof(int) })?.DeclaringType != typeof(InventoryBasePlayer))
                throw new InvalidOperationException("Native player DropAll inheritance changed.");
    }

    private sealed record Boundary(Type Owner, string Name, Type[] Parameters, Type ReturnType, string Hash,
        bool Guarded = true, string Module = PinnedNativeBoundary.LibModule);

    private static IEnumerable<Boundary> AuditedBoundaries()
    {
        yield return new(typeof(PlayerInventoryManager), "TryGiveItemstack", new[] { typeof(ItemStack), typeof(bool) }, typeof(bool),
            "08B424ABF20E80B13DBEDE42B47F45333726B01FCC35AA37DCF5A643CCBDEB25");
        yield return new(typeof(PlayerInventoryManager), "TryTransferAway", new[] { typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType(), typeof(bool), typeof(StringBuilder), typeof(bool) }, typeof(object[]),
            "3F55BFBDA0E12EFFE50322A6A4C77F3D41AA19636E40CFC524ABD42B1EAC9673");
        yield return new(typeof(PlayerInventoryManager), "TryTransferAway", new[] { typeof(ItemSlot), typeof(ItemStackMoveOperation).MakeByRefType(), typeof(bool), typeof(bool) }, typeof(object[]),
            "1C63C8D918909477EFF109DF2A5AA71C16DAD9113B36A303B22A7A8649EA583D", false);
        yield return new(typeof(ServerPlayerInventoryManager), "DropItem", new[] { typeof(ItemSlot), typeof(bool) }, typeof(bool),
            "B4DFEE4B2F19EB6A319C8CB9B5B34C0E92C10FF3AA5F5773E0579CF1E4CBDE41");
        yield return new(typeof(PlayerInventoryManager), "OnDeath", Type.EmptyTypes, typeof(void),
            "64387CAC59D8471805D9E881CFF802B41267C759071EBE05A5E6C5D4E1AC230A");
        yield return new(typeof(InventoryBasePlayer), "DropAll", new[] { typeof(Vec3d), typeof(int) }, typeof(void),
            "BD47BB649DB20A4358F329712D0893275C7AFD4020B8FCBEC2F6B33E7CBBA9D6", Module: PinnedNativeBoundary.ApiModule);
        yield return new(typeof(InventoryPlayerHotbar), "DropAll", new[] { typeof(Vec3d), typeof(int) }, typeof(void),
            "32C22A36F85B44EBE2D657A7BBD10C416CD0B089411E2A6682F7556A9EE60493");
        yield return new(typeof(InventoryPlayerBackpacks), "DropAll", new[] { typeof(Vec3d), typeof(int) }, typeof(void),
            "A7D9F82E983119D006BBF5A57533718D8FBC5241B89CCA40238EC00943FEFBD4");
        // Creative's native override is empty: witness it, do not claim it calls the base.
        yield return new(typeof(InventoryPlayerCreative), "DropAll", new[] { typeof(Vec3d), typeof(int) }, typeof(void),
            "684888C0EBB17F374298B65EE2807526C066094C701BCC7EBBE1C1095F494FC1", false);
        yield return new(typeof(EntityBehaviorPlayerInventory), "OnEntityDeath", new[] { typeof(DamageSource) }, typeof(void),
            "45D3B303AA9C73B3CE906293C19D359E83DD01345DA339CA1DFA080B471DFE14", false, PinnedNativeBoundary.EssentialsModule);
        yield return new(typeof(InventoryBase), "OnOwningEntityDeath", new[] { typeof(Vec3d) }, typeof(void),
            "DE032C090663F3E342A54E217838F629C76188C4237C272DCA506C050D2AC5D8", false, PinnedNativeBoundary.ApiModule);
        yield return new(typeof(EntityPlayer), "TryGiveItemStack", new[] { typeof(ItemStack) }, typeof(bool),
            "5B560C6ADB7445643F5B4410B1967F70CD22EE1FFD49016F6642152422BE51C3", false, PinnedNativeBoundary.ApiModule);
        yield return new(AccessTools.TypeByName("Vintagestory.Server.CmdGive"), "give", new[] { typeof(Entity), typeof(TextCommandCallingArgs) }, typeof(TextCommandResult),
            "58ACF7C517320B79261C0D8FB7AA44DCE138A74789B4C23CBFE49A2D54A3289B", false);
    }
}
