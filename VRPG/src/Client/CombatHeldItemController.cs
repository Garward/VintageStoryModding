using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VRPG.Client;

/// <summary>Keeps field loot out of the active hand and owns weapon-wheel selection.</summary>
public sealed class CombatHeldItemController
{
    private const int VanillaHotbarSlotCount = 10;
    private const int VanillaSkillSlot = 10;
    private readonly ICoreClientAPI api;
    private bool wasActive;
    private int restoreSlot = -1;
    private int selectedWeaponSlot = -1;
    private int neutralSlot = -1;

    public CombatHeldItemController(ICoreClientAPI api)
    {
        this.api = api;
    }

    public void Tick()
    {
        bool active = VrpgClientHudRuntime.CombatActionBarActive;
        if (!active)
        {
            LeaveCombat();
            return;
        }

        IPlayer? player = api.World?.Player;
        IInventory? hotbar = player?.InventoryManager?.GetHotbarInventory();
        if (player == null || hotbar == null)
        {
            return;
        }

        if (!wasActive)
        {
            EnterCombat(player, hotbar);
            return;
        }

        if (selectedWeaponSlot >= 0 && IsWeapon(SlotAt(hotbar, selectedWeaponSlot)?.Itemstack))
        {
            if (player.InventoryManager.ActiveHotbarSlotNumber != selectedWeaponSlot)
            {
                SelectSlot(player, selectedWeaponSlot);
            }
            return;
        }

        selectedWeaponSlot = -1;
        EnsureNeutralHand(player, hotbar);
    }

    public bool CycleWeapon(int wheelDelta)
    {
        if (!VrpgClientHudRuntime.CombatActionBarActive || wheelDelta == 0)
        {
            return false;
        }

        IPlayer? player = api.World?.Player;
        IInventory? hotbar = player?.InventoryManager?.GetHotbarInventory();
        if (player == null || hotbar == null)
        {
            return true;
        }

        List<int> weapons = WeaponSlots(hotbar);
        if (weapons.Count == 0)
        {
            selectedWeaponSlot = -1;
            EnsureNeutralHand(player, hotbar);
            return true;
        }

        selectedWeaponSlot = CombatWeaponSelection.Cycle(
            weapons,
            selectedWeaponSlot,
            wheelDelta);
        SelectSlot(player, selectedWeaponSlot);
        return true;
    }

    public void Deactivate()
    {
        LeaveCombat();
    }

    public static bool IsWeapon(ItemStack? stack)
    {
        if (stack?.Collectible == null || stack.Class != EnumItemClass.Item)
        {
            return false;
        }

        if (stack.Collectible.Attributes?["vrpgWeapon"].Exists == true)
        {
            return stack.Collectible.Attributes["vrpgWeapon"].AsBool(false);
        }

        return stack.Collectible.GetAttackPower(stack) > 0.5f;
    }

    private void EnterCombat(IPlayer player, IInventory hotbar)
    {
        wasActive = true;
        restoreSlot = player.InventoryManager.ActiveHotbarSlotNumber;
        if (IsWeapon(player.InventoryManager.ActiveHotbarSlot?.Itemstack))
        {
            selectedWeaponSlot = restoreSlot;
            return;
        }

        List<int> weapons = WeaponSlots(hotbar);
        selectedWeaponSlot = weapons.Count > 0 ? weapons[0] : -1;
        if (selectedWeaponSlot >= 0)
        {
            SelectSlot(player, selectedWeaponSlot);
        }
        else
        {
            EnsureNeutralHand(player, hotbar);
        }
    }

    private void LeaveCombat()
    {
        if (!wasActive)
        {
            return;
        }

        IPlayer? player = api.World?.Player;
        if (player != null && restoreSlot >= 0)
        {
            SelectSlot(player, restoreSlot);
        }

        wasActive = false;
        restoreSlot = -1;
        selectedWeaponSlot = -1;
        neutralSlot = -1;
    }

    private void EnsureNeutralHand(IPlayer player, IInventory hotbar)
    {
        if (neutralSlot >= 0 && SlotAt(hotbar, neutralSlot)?.Empty == true)
        {
            if (player.InventoryManager.ActiveHotbarSlotNumber != neutralSlot)
            {
                SelectSlot(player, neutralSlot);
            }
            return;
        }

        neutralSlot = FindNeutralSlot(hotbar);
        if (neutralSlot >= 0)
        {
            SelectSlot(player, neutralSlot);
        }
    }

    private static int FindNeutralSlot(IInventory hotbar)
    {
        if (SlotAt(hotbar, VanillaSkillSlot)?.Empty == true)
        {
            return VanillaSkillSlot;
        }

        for (int i = 0; i < Math.Min(VanillaHotbarSlotCount, hotbar.Count); i++)
        {
            if (hotbar[i]?.Empty == true)
            {
                return i;
            }
        }
        return -1;
    }

    private static List<int> WeaponSlots(IInventory hotbar)
    {
        var result = new List<int>();
        for (int i = 0; i < Math.Min(VanillaHotbarSlotCount, hotbar.Count); i++)
        {
            if (IsWeapon(hotbar[i]?.Itemstack))
            {
                result.Add(i);
            }
        }
        return result;
    }

    private void SelectSlot(IPlayer player, int slot)
    {
        if (slot < 0 || player.InventoryManager.ActiveHotbarSlotNumber == slot)
        {
            return;
        }

        if (player.Entity.Controls.HandUse != EnumHandInteract.None)
        {
            EnumHandInteract beforeUse = player.Entity.Controls.HandUse;
            if (!player.Entity.TryStopHandAction(false, EnumItemUseCancelReason.ChangeSlot))
            {
                return;
            }
            api.Network.SendHandInteraction(
                2,
                player.CurrentBlockSelection,
                player.CurrentEntitySelection,
                beforeUse,
                1,
                firstEvent: false,
                EnumItemUseCancelReason.ChangeSlot);
        }

        player.InventoryManager.ActiveHotbarSlotNumber = slot;
    }

    private static ItemSlot? SlotAt(IInventory inventory, int slot)
    {
        return slot >= 0 && slot < inventory.Count ? inventory[slot] : null;
    }
}
