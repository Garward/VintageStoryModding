using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        private BlockPos SegmentPosForProgress(float progress)
        {
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            int index = (int)MathF.Floor(progress);
            if (index < 0) index = 0;
            if (index >= ChainLength) index = ChainLength - 1;
            return Pos.AddCopy(fwd.X * index, 0, fwd.Z * index);
        }

        public void InsertItem(ItemStack stack, float progress)
        {
            if (stack == null) return;
            items.Add(new BeltItem { Stack = stack, Progress = progress });
            MarkDirty(true);
        }

        public bool TryInsertItem(ItemStack stack, float progress)
        {
            if (stack == null || stack.StackSize <= 0) return true;

            for (int i = 0; i < items.Count; i++)
            {
                if (MathF.Abs(items[i].Progress - progress) < ItemInsertClearance) return false;
            }

            InsertItem(stack, progress);
            return true;
        }

        /// <summary>Project a world position onto the chain axis, returning the progress value.</summary>
        public float ProjectOntoChain(Vec3d worldPos)
        {
            if (Direction == null) return 0f;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            double dx = worldPos.X - (Pos.X + 0.5);
            double dz = worldPos.Z - (Pos.Z + 0.5);
            return (float)(dx * fwd.X + dz * fwd.Z) + 0.5f;
        }

        /// <summary>Map a chain progress value to a world position on the belt-top surface.</summary>
        public Vec3d ProgressToWorld(float progress)
        {
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            return new Vec3d(
                Pos.X + 0.5 + (progress - 0.5f) * fwd.X,
                Pos.Y + BeltTopY,
                Pos.Z + 0.5 + (progress - 0.5f) * fwd.Z);
        }

        /// <summary>
        /// Per-variant sign that maps "positive RPM around the pulley axis" onto
        /// "progress increases (items travel toward the head end)".
        /// </summary>
        public static int HeadDirSign(string direction) => direction switch
        {
            "n" => -1,
            "e" => -1,
            "s" =>  1,
            "w" =>  1,
            _   =>  1
        };

        private void DumpAllItems()
        {
            if (items.Count == 0) return;
            for (int i = 0; i < items.Count; i++)
            {
                Vec3d pos = ProgressToWorld(items[i].Progress);
                Api.World.SpawnItemEntity(items[i].Stack, pos);
            }
            items.Clear();
            MarkDirty(true);
        }
    }
}
