using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Heat-safe liquid storage. Lava is intentionally non-containable in ordinary containers.
    /// </summary>
    public class BlockIronFluidTank : BlockLiquidContainerBase
    {
        public static bool IsLava(ItemStack stack)
        {
            return stack?.Collectible?.Code?.Domain == "vintagekinematics"
                && stack.Collectible.Code.Path == "lavaportion";
        }

        public override int TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
        {
            if (!IsLava(liquidStack)) return base.TryPutLiquid(pos, liquidStack, desiredLitres);

            WaterTightContainableProps props = GetContainableProps(liquidStack);
            if (props == null || props.ItemsPerLitre <= 0f) return 0;

            int desiredItems = Math.Min(
                liquidStack.StackSize,
                (int)Math.Floor(desiredLitres * props.ItemsPerLitre + 0.0001f));
            if (desiredItems <= 0) return 0;

            ItemStack existing = GetContent(pos);
            if (existing != null && !existing.Equals(api.World, liquidStack, GlobalConstants.IgnoredStackAttributes))
            {
                return 0;
            }

            int storedItems = existing?.StackSize ?? 0;
            int freeItems = Math.Max(0, (int)Math.Floor(CapacityLitres * props.ItemsPerLitre) - storedItems);
            int movedItems = Math.Min(desiredItems, freeItems);
            if (movedItems <= 0) return 0;

            if (existing == null)
            {
                ItemStack placed = liquidStack.Clone();
                placed.StackSize = movedItems;
                SetContent(pos, placed);
            }
            else
            {
                existing.StackSize += movedItems;
                BlockEntity blockEntity = api.World.BlockAccessor.GetBlockEntity(pos);
                blockEntity?.MarkDirty(true);
                if (blockEntity is BlockEntityContainer container)
                {
                    container.Inventory[GetContainerSlotId(pos)].MarkDirty();
                }
            }

            return movedItems;
        }
    }
}
