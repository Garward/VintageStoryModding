using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        private bool CaptureNearbyItems()
        {
            if (ChainLength <= 0) return false;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            BlockPos endPos = Pos.AddCopy(fwd.X * (ChainLength - 1), 0, fwd.Z * (ChainLength - 1));
            BlockPos minBp = new BlockPos(
                Math.Min(Pos.X, endPos.X),
                Pos.Y,
                Math.Min(Pos.Z, endPos.Z),
                Pos.dimension);
            BlockPos maxBp = new BlockPos(
                Math.Max(Pos.X, endPos.X) + 1,
                Pos.Y + 2,
                Math.Max(Pos.Z, endPos.Z) + 1,
                Pos.dimension);

            Entity[] found = Api.World.GetEntitiesInsideCuboid(minBp, maxBp,
                e => e is EntityItem ei && ei.Alive && ei.Itemstack != null);
            if (found.Length == 0) return false;

            bool changed = false;
            for (int i = 0; i < found.Length; i++)
            {
                EntityItem ei = (EntityItem)found[i];
                Vec3d epos = ei.Pos.XYZ;
                if (epos.Y < Pos.Y + BeltTopY - 0.1
                 || epos.Y > Pos.Y + BeltTopY + ItemCaptureMargin) continue;

                float p = ProjectOntoChain(epos);
                if (p <= 0.05f || p >= ChainLength - 0.05f) continue;

                items.Add(new BeltItem { Stack = ei.Itemstack.Clone(), Progress = p });
                ei.Die(EnumDespawnReason.PickedUp);
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Hand every item on this chain to <paramref name="player"/>, and also vacuum up any
        /// EntityItems hovering above the clicked segment.
        /// </summary>
        public int ClaimItemsAt(BlockPos clickedSegmentPos, IPlayer player)
        {
            int claimed = 0;

            BEBelt ctl = IsController
                ? this
                : Api.World.BlockAccessor.GetBlockEntity(ControllerPos) as BEBelt;
            if (ctl != null && ctl.items.Count > 0)
            {
                for (int i = ctl.items.Count - 1; i >= 0; i--)
                {
                    ItemStack stack = ctl.items[i].Stack;
                    if (stack == null || stack.StackSize <= 0) { ctl.items.RemoveAt(i); continue; }
                    Vec3d at = ctl.ProgressToWorld(ctl.items[i].Progress);
                    if (!player.InventoryManager.TryGiveItemstack(stack, true))
                    {
                        Api.World.SpawnItemEntity(stack, at);
                    }
                    claimed += stack.StackSize;
                    ctl.items.RemoveAt(i);
                }
                ctl.MarkDirty(true);
            }

            claimed += ClaimLooseEntitiesNearChain(ctl ?? this, player);
            return claimed;
        }

        private int ClaimLooseEntitiesNearChain(BEBelt sweepCtl, IPlayer player)
        {
            Vec3i sweepFwd = BlockBelt.HeadOffset(sweepCtl.Direction ?? Direction);
            BlockPos endPos = sweepCtl.Pos.AddCopy(
                sweepFwd.X * (sweepCtl.ChainLength - 1), 0, sweepFwd.Z * (sweepCtl.ChainLength - 1));
            BlockPos minBp = new BlockPos(
                Math.Min(sweepCtl.Pos.X, endPos.X) - 1,
                sweepCtl.Pos.Y,
                Math.Min(sweepCtl.Pos.Z, endPos.Z) - 1,
                sweepCtl.Pos.dimension);
            BlockPos maxBp = new BlockPos(
                Math.Max(sweepCtl.Pos.X, endPos.X) + 2,
                sweepCtl.Pos.Y + 2,
                Math.Max(sweepCtl.Pos.Z, endPos.Z) + 2,
                sweepCtl.Pos.dimension);
            Entity[] found = Api.World.GetEntitiesInsideCuboid(minBp, maxBp,
                e => e is EntityItem ei && ei.Alive && ei.Itemstack != null);

            int claimed = 0;
            for (int i = 0; i < found.Length; i++)
            {
                EntityItem ei = (EntityItem)found[i];
                ItemStack stack = ei.Itemstack;
                int before = stack.StackSize;
                if (player.InventoryManager.TryGiveItemstack(stack, true))
                {
                    claimed += before;
                    ei.Die(EnumDespawnReason.PickedUp);
                }
                else if (stack.StackSize < before)
                {
                    claimed += before - stack.StackSize;
                }
            }
            return claimed;
        }
    }
}
