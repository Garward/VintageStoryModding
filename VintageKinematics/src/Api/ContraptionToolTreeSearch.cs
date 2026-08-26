using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    /// <summary>Resolves saw targets with the same tree-group traversal used by vanilla axes.</summary>
    internal static class ContraptionToolTreeSearch
    {
        public static List<BlockPos> Find(IWorldAccessor world, BlockPos contact, System.Func<BlockPos, bool> canBreak)
        {
            List<BlockPos> targets = new List<BlockPos>();
            if (world == null || contact == null || !(canBreak?.Invoke(contact) ?? true)) return targets;

            Block contactBlock = world.BlockAccessor.GetBlock(contact);
            if (ContraptionToolRules.CanSawLeaves(world, contactBlock, contact, canBreak))
            {
                targets.Add(contact.Copy());
                return targets;
            }

            if (!ContraptionToolRules.CanSawWood(world, contactBlock, contact, canBreak)) return targets;

            Stack<BlockPos> vanillaTargets = new ItemAxe().FindTree(world, contact, out _, out _);
            if (vanillaTargets.Count == 0)
            {
                targets.Add(contact.Copy());
                return targets;
            }

            HashSet<string> seen = new HashSet<string>();
            foreach (BlockPos pos in vanillaTargets)
            {
                if (pos == null || !(canBreak?.Invoke(pos) ?? true) || !seen.Add(Key(pos))) continue;

                Block block = world.BlockAccessor.GetBlock(pos);
                if (ContraptionToolRules.CanSawWood(world, block, pos, canBreak)
                    || ContraptionToolRules.CanSawLeaves(world, block, pos, canBreak))
                {
                    targets.Add(pos.Copy());
                }
            }

            return targets;
        }

        private static string Key(BlockPos pos) => pos.dimension + ":" + pos.X + "," + pos.InternalY + "," + pos.Z;
    }
}
