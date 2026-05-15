using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VintageKinematics.Items
{
    public class InventoryPoweredDrillFuel : InventoryGeneric
    {
        public const string InventoryClassName = "vkdrillfuel";
        public const string InventoryAttributeName = "drillFuelInventory";

        public InventoryPoweredDrillFuel(ICoreAPI api) : base(api)
        {
            Init(1, InventoryClassName, "unbound", NewSlot);
        }

        public InventoryPoweredDrillFuel(string instanceId, ICoreAPI api) : base(1, InventoryClassName, instanceId, api, NewSlot)
        {
        }

        public override void OnItemSlotModified(ItemSlot slot)
        {
            base.OnItemSlotModified(slot);
            SaveToSourceSlot();
        }

        public void LoadFrom(ItemStack drillStack)
        {
            ITreeAttribute tree = drillStack?.Attributes?.GetTreeAttribute(InventoryAttributeName);
            if (tree != null)
            {
                FromTreeAttributes(tree);
            }
        }

        public void SaveTo(ItemStack drillStack)
        {
            if (drillStack == null) return;
            TreeAttribute tree = new TreeAttribute();
            ToTreeAttributes(tree);
            drillStack.Attributes[InventoryAttributeName] = tree;
        }

        public bool SaveToSourceSlot()
        {
            ItemSlot sourceSlot = GetSourceSlot();
            if (sourceSlot?.Itemstack == null) return false;
            SaveTo(sourceSlot.Itemstack);
            sourceSlot.MarkDirty();
            return true;
        }

        public ItemSlot GetSourceSlot()
        {
            if (Api?.World == null || instanceID == null) return null;

            string[] parts = instanceID.Split(new[] { ':' }, 2);
            if (parts.Length != 2) return null;

            IPlayer player = Api.World.PlayerByUid(parts[0]);
            if (player == null) return null;

            string drillId = parts[1];
            if (string.IsNullOrEmpty(drillId)) return null;

            foreach (InventoryBase inventory in player.InventoryManager.InventoriesOrdered)
            {
                if (inventory == null || ReferenceEquals(inventory, this) || inventory is InventoryPoweredDrillFuel) continue;

                for (int slotId = 0; slotId < inventory.Count; slotId++)
                {
                    ItemSlot slot = inventory[slotId];
                    ItemStack stack = slot?.Itemstack;
                    if (stack?.Collectible is not ItemPoweredDrill) continue;
                    if (stack.Attributes.GetString(ItemPoweredDrill.DrillIdAttribute) == drillId) return slot;
                }
            }

            return null;
        }

        public override object Close(IPlayer player)
        {
            if (!SaveToSourceSlot() && Api.Side == EnumAppSide.Server)
            {
                DropContents(player);
            }
            return base.Close(player);
        }

        private void DropContents(IPlayer player)
        {
            Vec3d position = player?.Entity?.Pos?.XYZ ?? new Vec3d();
            foreach (ItemSlot slot in slots)
            {
                if (slot?.Itemstack == null) continue;
                Api.World.SpawnItemEntity(slot.Itemstack, position);
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }

        private static ItemSlot NewSlot(int slotId, InventoryGeneric self)
        {
            return new ItemSlotDrillFuel(self);
        }
    }
}
