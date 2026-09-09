using System;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class NativeInventoryDeathGuardTests
{
    [Theory]
    [InlineData("owner")]
    [InlineData("uid")]
    [InlineData("api")]
    [InlineData("ambiguous")]
    public void DeferredEffectRefusesIdentityLossAfterEnqueue(string loss)
    {
        using var scope = new NativeMutationScope("vanilla");
        var f = new NativeMutationFixture();
        f.Item.Attributes = new JsonObject(JObject.Parse("{\"protectionModifiers\":{}}"));
        f.Entity.Properties.Server.Attributes.SetBool("keepContents", true);
        f.Entity.Properties.Server.Attributes.SetBool("dropArmorOnDeath", true);
        var stack = f.Inventory[0].Itemstack = new(f.Item, 7);
        var behavior = new EntityBehaviorPlayerInventory(f.Entity);
        AccessTools.Field(typeof(EntityBehaviorContainer), "Api").SetValue(behavior, f.Api);
        behavior.OnEntityDeath(null!);
        if (loss == "owner") f.OwnerAvailable = false;
        if (loss == "uid") f.Entity.WatchedAttributes.SetString("playerUID", " ");
        if (loss == "api") f.Entity.Api = null;
        if (loss == "ambiguous") f.Side = EnumAppSide.Universal;
        Assert.Single(f.Queue)();
        Assert.Same(stack, f.Inventory[0].Itemstack);
        Assert.Equal(7, stack.StackSize);
        Assert.Empty(f.Spawns);
        Assert.Empty(f.Inventory.DirtySlots);
        Assert.Equal(0, f.Inventory.ModifiedCalls);
    }

    [Fact]
    public void ExplicitClientDeferredArmorEffectKeepsNativeBehaviorWithoutServerResolver()
    {
        using var scope = new NativeMutationScope("throw");
        var f = new NativeMutationFixture { Side = EnumAppSide.Client };
        f.Item.Attributes = new JsonObject(JObject.Parse("{\"protectionModifiers\":{}}"));
        f.Entity.Properties.Server.Attributes.SetBool("keepContents", true);
        f.Entity.Properties.Server.Attributes.SetBool("dropArmorOnDeath", true);
        f.Inventory[0].Itemstack = new(f.Item, 7);
        var behavior = new EntityBehaviorPlayerInventory(f.Entity);
        AccessTools.Field(typeof(EntityBehaviorContainer), "Api").SetValue(behavior, f.Api);
        behavior.OnEntityDeath(null!);
        Assert.Single(f.Queue)();
        Assert.Equal(7, Assert.Single(f.Spawns).StackSize);
        Assert.True(f.Inventory[0].Empty);
    }

    [Theory]
    [InlineData("base", "absent")]
    [InlineData("hotbar", "throw")]
    [InlineData("backpack", "recovery")]
    [InlineData("base", "owner")]
    [InlineData("hotbar", "uid")]
    [InlineData("backpack", "api")]
    [InlineData("base", "ambiguous")]
    [InlineData("base", "client")]
    [InlineData("hotbar", "client")]
    [InlineData("backpack", "client")]
    public void DropAllUsesStoredOwnerAndExplicitClientSide(string kind, string state)
    {
        using var scope = new NativeMutationScope(state is "absent" or "throw" or "recovery" ? state : "vanilla");
        var f = new NativeMutationFixture();
        InventoryBasePlayer inventory = kind == "hotbar" ? new InventoryPlayerHotbar("hotbar", "player", null!)
            : kind == "backpack" ? new InventoryPlayerBackpacks("backpack", "player", null!) : f.Inventory;
        inventory.Api = f.Api;
        var stack = inventory[0].Itemstack = new(f.Item, 7);
        if (state == "owner") f.OwnerAvailable = false;
        if (state == "uid") AccessTools.Field(typeof(InventoryBasePlayer), "playerUID").SetValue(inventory, " ");
        if (state == "api") inventory.Api = null;
        if (state == "ambiguous") f.Side = EnumAppSide.Universal;
        if (state == "client") { f.Side = EnumAppSide.Client; scope.Resolve("throw"); }
        inventory.DropAll(new Vec3d());
        Assert.Equal(state == "client" ? 1 : 0, f.Spawns.Count);
        Assert.Equal(state == "client" ? 1 : 0, f.Item.DropCalls);
        if (state != "client")
        {
            Assert.Same(stack, inventory[0].Itemstack);
            Assert.Equal(7, stack.StackSize);
            Assert.Empty(inventory.DirtySlots);
        }
    }

    [Theory]
    [InlineData("canonical", false, true)]
    [InlineData("recovery", false, true)]
    [InlineData("absent", true, true)]
    [InlineData("throw", false, true)]
    [InlineData("vanilla", false, true)]
    [InlineData("vanilla", true, true)]
    [InlineData("vanilla", false, false)]
    [InlineData("vanilla", true, false)]
    public void RealDeferredDeathChecksCustodyAtExecutionAndPreservesArmor(string custody, bool keep, bool armor)
    {
        using var scope = new NativeMutationScope("vanilla");
        var f = new NativeMutationFixture();
        f.Item.Attributes = new JsonObject(JObject.Parse("{\"protectionModifiers\":{}}"));
        var armorStack = f.Inventory[0].Itemstack = new(f.Item, 7);
        var heldStack = f.Inventory[1].Itemstack = new(f.Item, 3);
        f.Entity.Properties.Server.Attributes.SetBool("keepContents", keep);
        f.Entity.Properties.Server.Attributes.SetBool("dropArmorOnDeath", armor);
        var behavior = new EntityBehaviorPlayerInventory(f.Entity);
        AccessTools.Field(typeof(EntityBehaviorContainer), "Api").SetValue(behavior, f.Api);
        behavior.OnEntityDeath(null!);
        var callback = Assert.Single(f.Queue);
        Assert.Same(behavior, callback.Target);
        Assert.Same(armorStack, f.Inventory[0].Itemstack);
        scope.Resolve(custody);
        callback();
        bool allowed = custody == "vanilla";
        Assert.Equal(allowed && !keep ? 1 : 0, f.Inventory.DeathCalls);
        Assert.Equal(allowed && armor ? keep ? 2 : 1 : 0, f.Spawns.Count);
        if (!allowed)
        {
            Assert.Same(armorStack, f.Inventory[0].Itemstack);
            Assert.Same(heldStack, f.Inventory[1].Itemstack);
            Assert.Equal(7, armorStack.StackSize);
            Assert.Equal(3, heldStack.StackSize);
            Assert.Equal(0, f.Inventory.ModifiedCalls);
            Assert.Empty(f.Inventory.DirtySlots);
        }
    }

    [Theory]
    [InlineData("base", false)]
    [InlineData("hotbar", false)]
    [InlineData("backpack", false)]
    [InlineData("base", true)]
    [InlineData("hotbar", true)]
    [InlineData("backpack", true)]
    public void ActualPlayerDropOverridesRefuseBeforeClearOrCallback(string kind, bool vanilla)
    {
        using var scope = new NativeMutationScope(vanilla ? "vanilla" : "canonical");
        var f = new NativeMutationFixture();
        InventoryBasePlayer inventory = kind == "hotbar" ? new InventoryPlayerHotbar("hotbar", "player", null!)
            : kind == "backpack" ? new InventoryPlayerBackpacks("backpack", "player", null!) : f.Inventory;
        inventory.Api = f.Api;
        var stack = inventory[0].Itemstack = new(f.Item, 7);
        ItemStack? special = kind == "hotbar" ? inventory[10].Itemstack = new(f.Item, 2) : null;
        inventory.DropAll(new Vec3d());
        Assert.Equal(vanilla ? 1 : 0, f.Item.DropCalls);
        Assert.Equal(vanilla ? 1 : 0, f.Spawns.Count);
        Assert.Equal(vanilla ? 0 : 7, inventory[0].StackSize);
        if (!vanilla)
        {
            Assert.Same(stack, inventory[0].Itemstack);
            Assert.Empty(inventory.DirtySlots);
            if (kind == "hotbar") Assert.Same(special, inventory[10].Itemstack);
        }
        else if (kind == "hotbar") Assert.Null(inventory[10].Itemstack);
    }
}
