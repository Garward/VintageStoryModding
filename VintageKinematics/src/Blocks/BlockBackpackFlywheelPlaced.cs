using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockBackpackFlywheelPlaced : Block, IKineticConnector
    {
        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            return TryConnectInline(self, other, fromPos, toPos, Variant?["side"]);
        }

        internal static KineticConnectionResult? TryConnectInline(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos, string side)
        {
            Vec3i offset = new Vec3i(toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);
            int absSum = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
            if (absSum != 1) return null;
            if (!IsInputSide(offset, side)) return null;

            EnumKineticAxis offsetAxis = EnumKineticAxisExtensions.FromVec(offset);
            if (self.Axis != offsetAxis) return null;

            if (other.Role == EnumKineticRole.Gearbox)
            {
                if (other.Axis == offsetAxis) return null;
                int dirSign = -(offset.X + offset.Y + offset.Z);
                return new KineticConnectionResult(1f, dirSign);
            }

            if (self.Axis != other.Axis) return null;
            return new KineticConnectionResult(1f, 1);
        }

        private static bool IsInputSide(Vec3i offset, string side)
        {
            return side switch
            {
                "n" => offset.X == 0 && offset.Y == 0 && offset.Z == -1,
                "e" => offset.X == 1 && offset.Y == 0 && offset.Z == 0,
                "s" => offset.X == 0 && offset.Y == 0 && offset.Z == 1,
                "w" => offset.X == -1 && offset.Y == 0 && offset.Z == 0,
                _ => offset.X == 0 && offset.Y == 0 && offset.Z == 1
            };
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            ItemStack stack = StoredStack(world, pos);
            return stack == null ? System.Array.Empty<ItemStack>() : new[] { stack };
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return StoredStack(world, pos) ?? base.OnPickBlock(world, pos);
        }

        private static ItemStack StoredStack(IWorldAccessor world, BlockPos pos)
        {
            return (world.BlockAccessor.GetBlockEntity(pos) as BEBackpackFlywheelPlaced)?.GetStoredStack();
        }
    }
}
