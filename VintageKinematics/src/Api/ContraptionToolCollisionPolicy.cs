using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Keeps an assembled tool head's physical hull out of the block it is meant to work on.
    /// The block collision and selection boxes remain unchanged, so visual reach and targeting
    /// can still extend beyond the tool's own cell.
    /// </summary>
    public static class ContraptionToolCollisionPolicy
    {
        public static Cuboidf[] ForAssembledEntity(string domain, string path, Cuboidf[] boxes)
        {
            if (!IsWorkingHead(domain, path) || boxes == null || boxes.Length == 0) return boxes;

            List<Cuboidf> clamped = new List<Cuboidf>(boxes.Length);
            for (int i = 0; i < boxes.Length; i++)
            {
                Cuboidf box = boxes[i];
                if (box == null) continue;

                float x1 = Math.Clamp(box.X1, 0f, 1f);
                float y1 = Math.Clamp(box.Y1, 0f, 1f);
                float z1 = Math.Clamp(box.Z1, 0f, 1f);
                float x2 = Math.Clamp(box.X2, 0f, 1f);
                float y2 = Math.Clamp(box.Y2, 0f, 1f);
                float z2 = Math.Clamp(box.Z2, 0f, 1f);
                if (x2 <= x1 || y2 <= y1 || z2 <= z1) continue;

                clamped.Add(new Cuboidf(x1, y1, z1, x2, y2, z2));
            }

            return clamped.ToArray();
        }

        private static bool IsWorkingHead(string domain, string path)
        {
            if (!string.Equals(domain, "vintagekinematics", StringComparison.Ordinal)) return false;
            return path?.StartsWith("contraptiondrill-", StringComparison.Ordinal) == true
                || path?.StartsWith("contraptionsaw-", StringComparison.Ordinal) == true;
        }
    }
}
