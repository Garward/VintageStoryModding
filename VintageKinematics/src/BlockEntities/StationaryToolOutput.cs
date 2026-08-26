using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities
{
    internal static class StationaryToolOutput
    {
        public static void EmitBelow(BlockEntity owner, ItemStack stack)
        {
            if (owner?.Api?.World == null || stack == null || stack.StackSize <= 0) return;

            DummySlot output = new DummySlot(stack);
            InventoryPusher.TryPush(owner.Api.World, owner.Pos, BlockFacing.DOWN, output, stack.StackSize);
            if (output.Empty || output.Itemstack.StackSize <= 0) return;

            Vec3d centerBelow = new Vec3d(
                owner.Pos.X + 0.5,
                owner.Pos.InternalY - 0.05,
                owner.Pos.Z + 0.5);
            owner.Api.World.SpawnItemEntity(output.Itemstack, centerBelow, new Vec3d(0, -0.02, 0));
        }
    }
}
