using System;
using Vintagestory.API.Datastructures;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Prevents a carried gantry controller from embedding historical contraption snapshots.
    /// Its selection metadata is retained; block-entity state is freshly captured from the
    /// world whenever that controller next assembles a contraption.
    /// </summary>
    public static class GantrySnapshotTreeSanitizer
    {
        public static void SanitizeCapturedBlock(string blockCode, ITreeAttribute tree)
        {
            if (!IsGantryController(blockCode) || tree == null) return;

            tree.RemoveAttribute("snapshotBlockEntityTrees");
            tree.SetLong("linkedEntityId", 0);
            tree.SetBool("assembled", false);
        }

        public static void SanitizeNestedControllers(string[] blockCodes, TreeAttribute[] blockEntityTrees)
        {
            int count = Math.Min(blockCodes?.Length ?? 0, blockEntityTrees?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                SanitizeCapturedBlock(blockCodes[i], blockEntityTrees[i]);
            }
        }

        private static bool IsGantryController(string blockCode)
        {
            if (string.IsNullOrEmpty(blockCode)) return false;

            int separator = blockCode.IndexOf(':');
            string domain = separator >= 0 ? blockCode.Substring(0, separator) : "game";
            string path = separator >= 0 ? blockCode.Substring(separator + 1) : blockCode;
            if (!string.Equals(domain, "vintagekinematics", StringComparison.Ordinal)) return false;

            return path.StartsWith("gantrycarriage-", StringComparison.Ordinal)
                || path.StartsWith("contraptioncontroller", StringComparison.Ordinal);
        }
    }
}
