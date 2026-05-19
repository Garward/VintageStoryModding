using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VintageKinematics.BlockEntities
{
    public abstract class BEVKStorage : BlockEntityOpenableContainer
    {
        private readonly InventoryGeneric inventory;
        private readonly int columns;
        private readonly string inventoryClassName;
        private readonly string titleLangCode;
        private readonly string fallbackTitle;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => inventoryClassName;

        protected BEVKStorage(int slots, int columns, string inventoryClassName, string titleLangCode, string fallbackTitle, bool singleItemType = false)
        {
            this.columns = columns;
            this.inventoryClassName = inventoryClassName;
            this.titleLangCode = titleLangCode;
            this.fallbackTitle = fallbackTitle;
            inventory = singleItemType
                ? new InventoryGeneric(slots, null, null, null, (slotId, self) => new ItemSlotSingleItemBulk(self))
                : new InventoryGeneric(slots, null, null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.SlotModified += OnSlotModified;
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get(titleLangCode);
                if (string.IsNullOrEmpty(title) || title == titleLangCode) title = fallbackTitle;

                byte[] data = BlockEntityContainerOpen.ToBytes("BlockEntityInventory", title, (byte)columns, inventory);
                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer,
                    Pos,
                    (int)EnumBlockContainerPacketId.OpenInventory,
                    data);
                byPlayer.InventoryManager.OpenInventory(inventory);
            }

            return true;
        }

        private void OnSlotModified(int slotId)
        {
            MarkDirty(true);
            Api?.World?.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        }
    }

    public class BEReinforcedChest : BEVKStorage
    {
        public BEReinforcedChest()
            : base(32, 8, "vkreinforcedchest", "vintagekinematics:reinforcedchest-title", "Reinforced Chest")
        {
        }
    }

    public class BEDoubleReinforcedChest : BEVKStorage
    {
        public BEDoubleReinforcedChest()
            : base(70, 10, "vkdoublechest", "vintagekinematics:doublechest-title", "Double Reinforced Chest")
        {
        }
    }

    public class BEBulkCrate : BEVKStorage
    {
        public BEBulkCrate()
            : base(64, 8, "vkbulkcrate", "vintagekinematics:bulkcrate-title", "Reinforced Crate", true)
        {
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            ItemSlot firstSlot = Inventory.FirstNonEmptySlot;
            if (firstSlot == null || firstSlot.Empty)
            {
                dsc.AppendLine(Lang.Get("Empty"));
                return;
            }

            int totalItems = 0;
            foreach (ItemSlot slot in Inventory)
            {
                if (slot.Empty) continue;
                totalItems += slot.StackSize;
            }

            dsc.AppendLine(Lang.Get("vintagekinematics:bulkcrate-contents", firstSlot.GetStackName(), totalItems));
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer?.InventoryManager?.ActiveHotbarSlot == null) return true;

            bool put = byPlayer.Entity?.Controls?.ShiftKey == true;
            bool take = !put;
            bool bulk = byPlayer.Entity?.Controls?.CtrlKey == true;
            ItemSlot hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (take)
            {
                return TryTake(byPlayer, bulk);
            }

            if (put && !hotbarSlot.Empty)
            {
                TryPut(byPlayer, hotbarSlot, bulk);
            }

            return true;
        }

        private bool TryTake(IPlayer byPlayer, bool bulk)
        {
            ItemSlot sourceSlot = Inventory.FirstNonEmptySlot;
            if (sourceSlot == null || sourceSlot.Empty) return true;

            int requestedQuantity = bulk ? sourceSlot.Itemstack.Collectible.MaxStackSize : 1;
            for (int i = 0; i < Inventory.Count && sourceSlot.StackSize < requestedQuantity; i++)
            {
                ItemSlot other = Inventory[i];
                if (other == sourceSlot || other.Empty) continue;
                other.TryPutInto(Api.World, sourceSlot, requestedQuantity - sourceSlot.StackSize);
            }

            ItemStack stack = sourceSlot.TakeOut(requestedQuantity);
            int originalQuantity = stack.StackSize;
            bool gave = byPlayer.InventoryManager.TryGiveItemstack(stack, true);
            int taken = originalQuantity - stack.StackSize;

            if (gave)
            {
                if (taken == 0) taken = originalQuantity;
                if (originalQuantity > taken)
                {
                    new DummySlot(stack).TryPutInto(Api.World, sourceSlot, originalQuantity - taken);
                }
                DidMoveItems(sourceSlot.Itemstack ?? stack, byPlayer);
            }
            else
            {
                new DummySlot(stack).TryPutInto(Api.World, sourceSlot, originalQuantity - taken);
            }

            if (taken > 0)
            {
                Api.World.Logger.Audit("{0} Took {1}x{2} from {3} at {4}.",
                    byPlayer.PlayerName,
                    taken,
                    stack?.Collectible.Code,
                    Block?.Code,
                    Pos);
                sourceSlot.MarkDirty();
                MarkDirty(true);
            }

            return true;
        }

        private void TryPut(IPlayer byPlayer, ItemSlot hotbarSlot, bool bulk)
        {
            int quantity = bulk ? hotbarSlot.StackSize : 1;
            if (quantity <= 0) return;

            ItemSlot first = Inventory.FirstNonEmptySlot;
            if (first == null)
            {
                int moved = hotbarSlot.TryPutInto(Api.World, Inventory[0], quantity);
                if (moved > 0)
                {
                    DidMoveItems(Inventory[0].Itemstack, byPlayer);
                    LogPut(byPlayer, moved, Inventory[0].Itemstack);
                }
                hotbarSlot.MarkDirty();
                MarkDirty(true);
                return;
            }

            if (!hotbarSlot.Itemstack.Equals(Api.World, first.Itemstack, GlobalConstants.IgnoredStackAttributes))
            {
                return;
            }

            var skipSlots = new List<ItemSlot>();
            while (!hotbarSlot.Empty && hotbarSlot.StackSize > 0 && skipSlots.Count < Inventory.Count)
            {
                WeightedSlot weighted = Inventory.GetBestSuitedSlot(hotbarSlot, null, skipSlots);
                ItemSlot target = weighted.slot;
                if (target == null) break;

                int moved = hotbarSlot.TryPutInto(Api.World, target, quantity);
                if (moved > 0)
                {
                    DidMoveItems(target.Itemstack, byPlayer);
                    LogPut(byPlayer, moved, target.Itemstack);
                    if (!bulk) break;
                }

                skipSlots.Add(target);
            }

            hotbarSlot.MarkDirty();
            MarkDirty(true);
        }

        private void LogPut(IPlayer byPlayer, int moved, ItemStack stack)
        {
            Api.World.Logger.Audit("{0} Put {1}x{2} into {3} at {4}.",
                byPlayer.PlayerName,
                moved,
                stack?.Collectible.Code,
                Block?.Code,
                Pos);
        }

        private void DidMoveItems(ItemStack stack, IPlayer byPlayer)
        {
            byPlayer.Entity?.World?.PlaySoundAt(
                new AssetLocation("game:sounds/player/build"),
                byPlayer.Entity,
                byPlayer,
                true,
                16);
        }
    }

    public class ItemSlotSingleItemBulk : ItemSlotSurvival
    {
        public ItemSlotSingleItemBulk(InventoryBase inventory) : base(inventory)
        {
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return base.CanTakeFrom(sourceSlot, priority) && MatchesCrateContent(sourceSlot);
        }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            return base.CanHold(sourceSlot) && MatchesCrateContent(sourceSlot);
        }

        private bool MatchesCrateContent(ItemSlot sourceSlot)
        {
            if (sourceSlot?.Itemstack == null) return false;

            ItemStack existing = FirstStoredStack();
            if (existing == null) return true;

            return sourceSlot.Itemstack.Equals(
                inventory.Api.World,
                existing,
                GlobalConstants.IgnoredStackAttributes);
        }

        private ItemStack FirstStoredStack()
        {
            foreach (ItemSlot slot in inventory)
            {
                if (slot.Empty) continue;
                return slot.Itemstack;
            }

            return null;
        }
    }
}
