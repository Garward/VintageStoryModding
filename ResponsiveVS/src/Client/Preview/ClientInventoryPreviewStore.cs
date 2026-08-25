using System;
using System.Collections.Generic;
using ResponsiveVS.Config;
using ResponsiveVS.Diagnostics;
using ResponsiveVS.Transactions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ResponsiveVS.Client.Preview;

public static class ClientInventoryPreviewStore
{
    private static readonly Dictionary<SlotKey, PreviewSlot> slots = new();
    private static readonly Dictionary<ItemSlot, PreviewSlot> slotsByReference = new();

    public static bool HasAny => slots.Count > 0;

    public static void Set(SlotKey key, ItemSlot slot, ItemStack previewStack)
    {
        slots.TryGetValue(key, out PreviewSlot existing);
        if (existing?.Slot != null)
        {
            slotsByReference.Remove(existing.Slot);
        }

        PreviewSlot preview = new PreviewSlot
        {
            Key = key,
            Slot = slot,
            OriginalStack = existing?.OriginalStack?.Clone() ?? slot?.Itemstack?.Clone(),
            PreviewStack = previewStack?.Clone(),
            CreatedUtc = DateTime.UtcNow
        };

        slots[key] = preview;
        if (slot != null)
        {
            slotsByReference[slot] = preview;
        }
    }

    public static void ReconcileServerApplied(SlotKey key, ItemSlot slot, IWorldAccessor world)
    {
        if (!slots.TryGetValue(key, out PreviewSlot preview))
        {
            return;
        }

        ItemStack serverStack = slot?.Itemstack;
        if (StackEquals(world, serverStack, preview.PreviewStack))
        {
            Remove(key);
            ResponsiveDiagnostics.Verbose("CLIENT preview confirmed {0}[{1}]", key.InventoryId, key.SlotId);
            return;
        }

        if (ResponsiveVSConfigSystem.Config.Transactions.HoldPreviewThroughStaleServerEchoes
            && StackEquals(world, serverStack, preview.OriginalStack)
            && !StackEquals(world, preview.OriginalStack, preview.PreviewStack))
        {
            ResponsiveDiagnostics.Verbose("CLIENT preview held through stale echo {0}[{1}]", key.InventoryId, key.SlotId);
            return;
        }

        Remove(key);
        ResponsiveDiagnostics.Verbose("CLIENT preview rejected {0}[{1}]", key.InventoryId, key.SlotId);
    }

    public static void ReconcileInventoryServerApplied(string inventoryId, IInventory inventory, IWorldAccessor world)
    {
        if (string.IsNullOrEmpty(inventoryId) || slots.Count == 0)
        {
            return;
        }

        List<SlotKey> keys = null;
        foreach (SlotKey key in slots.Keys)
        {
            if (string.Equals(key.InventoryId, inventoryId, StringComparison.Ordinal))
            {
                keys ??= new List<SlotKey>();
                keys.Add(key);
            }
        }

        if (keys == null)
        {
            return;
        }

        foreach (SlotKey key in keys)
        {
            if (inventory == null || key.SlotId < 0 || key.SlotId >= inventory.Count)
            {
                Remove(key);
                continue;
            }

            ReconcileServerApplied(key, inventory[key.SlotId], world);
        }
    }

    public static void Remove(SlotKey key)
    {
        if (!slots.TryGetValue(key, out PreviewSlot existing))
        {
            return;
        }

        slots.Remove(key);
        if (existing.Slot != null)
        {
            slotsByReference.Remove(existing.Slot);
        }
    }

    public static void RemoveInventory(string inventoryId)
    {
        if (string.IsNullOrEmpty(inventoryId) || slots.Count == 0)
        {
            return;
        }

        List<SlotKey> remove = null;
        foreach (SlotKey key in slots.Keys)
        {
            if (string.Equals(key.InventoryId, inventoryId, StringComparison.Ordinal))
            {
                remove ??= new List<SlotKey>();
                remove.Add(key);
            }
        }

        if (remove == null)
        {
            return;
        }

        foreach (SlotKey key in remove)
        {
            Remove(key);
        }
    }

    public static void Clear()
    {
        slots.Clear();
        slotsByReference.Clear();
    }

    public static bool TryGet(SlotKey key, out ItemStack stack)
    {
        PurgeExpired();
        if (slots.TryGetValue(key, out PreviewSlot preview))
        {
            stack = preview.PreviewStack;
            return true;
        }

        stack = null;
        return false;
    }

    public static bool TryGet(ItemSlot slot, out ItemStack stack)
    {
        PurgeExpired();
        if (slot != null && slotsByReference.TryGetValue(slot, out PreviewSlot preview))
        {
            stack = preview.PreviewStack;
            return true;
        }

        stack = null;
        return false;
    }

    public static PreviewRenderScope ApplyToInventory(IInventory inventory)
    {
        PurgeExpired();
        if (inventory == null || slots.Count == 0)
        {
            return default;
        }

        List<PreviewRestore> restores = null;
        foreach (KeyValuePair<SlotKey, PreviewSlot> entry in slots)
        {
            SlotKey key = entry.Key;
            if (!string.Equals(key.InventoryId, inventory.InventoryID, StringComparison.Ordinal))
            {
                continue;
            }

            if (key.SlotId < 0 || key.SlotId >= inventory.Count)
            {
                continue;
            }

            ItemSlot slot = inventory[key.SlotId];
            if (slot == null)
            {
                continue;
            }

            // Render-only mutation: vanilla slot grids skip empty slots before they call the
            // item renderer, so a preview for an empty real slot has to be visible here.
            restores ??= new List<PreviewRestore>();
            restores.Add(new PreviewRestore(slot, slot.Itemstack));
            slot.Itemstack = entry.Value.PreviewStack?.Clone();
        }

        return new PreviewRenderScope(restores);
    }

    public static PreviewRenderScope ApplyToSlot(ItemSlot slot)
    {
        PurgeExpired();
        if (slot == null || !slotsByReference.TryGetValue(slot, out PreviewSlot preview))
        {
            return default;
        }

        List<PreviewRestore> restores = new List<PreviewRestore>
        {
            new PreviewRestore(slot, slot.Itemstack)
        };
        slot.Itemstack = preview.PreviewStack?.Clone();
        return new PreviewRenderScope(restores);
    }

    private static void PurgeExpired()
    {
        int timeout = ResponsiveVSConfigSystem.Config.Transactions.ClientPreviewTimeoutMs;
        if (timeout <= 0 || slots.Count == 0)
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow.AddMilliseconds(-timeout);
        List<SlotKey> remove = null;
        foreach (KeyValuePair<SlotKey, PreviewSlot> entry in slots)
        {
            if (entry.Value.CreatedUtc <= cutoff)
            {
                remove ??= new List<SlotKey>();
                remove.Add(entry.Key);
            }
        }

        if (remove == null)
        {
            return;
        }

        foreach (SlotKey key in remove)
        {
            Remove(key);
        }

        ResponsiveDiagnostics.Verbose("CLIENT preview timeout cleared {0} slot(s)", remove.Count);
    }

    private static bool StackEquals(IWorldAccessor world, ItemStack a, ItemStack b)
    {
        if (a == null || b == null)
        {
            return a == null && b == null;
        }

        return a.Equals(world, b, GlobalConstants.IgnoredStackAttributes);
    }

    private sealed class PreviewSlot
    {
        public SlotKey Key;
        public ItemSlot Slot;
        public ItemStack OriginalStack;
        public ItemStack PreviewStack;
        public DateTime CreatedUtc;
    }

    public readonly struct PreviewRenderScope
    {
        private readonly List<PreviewRestore> restores;

        internal PreviewRenderScope(List<PreviewRestore> restores)
        {
            this.restores = restores;
        }

        public void Restore()
        {
            if (restores == null)
            {
                return;
            }

            for (int i = 0; i < restores.Count; i++)
            {
                restores[i].Slot.Itemstack = restores[i].OriginalStack;
            }
        }
    }

    internal readonly struct PreviewRestore
    {
        public PreviewRestore(ItemSlot slot, ItemStack originalStack)
        {
            Slot = slot;
            OriginalStack = originalStack;
        }

        public ItemSlot Slot { get; }
        public ItemStack OriginalStack { get; }
    }
}
