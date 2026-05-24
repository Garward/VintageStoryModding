using System;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Serializable block snapshot used by moving contraption controllers.
    /// Offsets are relative to the controller block position.
    /// </summary>
    public class ContraptionSnapshot
    {
        public Vec3i LocalMin { get; set; }
        public Vec3i LocalMax { get; set; }
        public Vec3i[] Offsets { get; set; }
        public string[] BlockCodes { get; set; }
        public TreeAttribute[] BlockEntityTrees { get; set; }

        public int Count => Math.Min(Offsets?.Length ?? 0, BlockCodes?.Length ?? 0);

        public ContraptionSnapshot()
        {
            LocalMin = new Vec3i(0, 0, 0);
            LocalMax = new Vec3i(0, 0, 0);
            Offsets = Array.Empty<Vec3i>();
            BlockCodes = Array.Empty<string>();
            BlockEntityTrees = Array.Empty<TreeAttribute>();
        }

        public ContraptionSnapshot Clone()
        {
            return new ContraptionSnapshot
            {
                LocalMin = LocalMin?.Clone(),
                LocalMax = LocalMax?.Clone(),
                Offsets = CloneOffsets(Offsets),
                BlockCodes = (string[])BlockCodes?.Clone() ?? Array.Empty<string>(),
                BlockEntityTrees = ContraptionApi.CloneBlockEntityTrees(BlockEntityTrees, BlockCodes?.Length ?? 0)
            };
        }

        private static Vec3i[] CloneOffsets(Vec3i[] offsets)
        {
            if (offsets == null || offsets.Length == 0) return Array.Empty<Vec3i>();

            Vec3i[] cloned = new Vec3i[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
            {
                cloned[i] = offsets[i]?.Clone();
            }
            return cloned;
        }
    }
}
