using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    public static class ContraptionToolRules
    {
        public const float DrillWorkPerResistance = 85f;
        public const float DrillMinimumWork = 160f;
        public const float SawWorkPerWoodBlock = 6f;
        public const float SawWorkPerLeafBlock = 0.5f;
        public const float SawMinimumWork = 160f;
        public const float SawLeafMinimumWork = 40f;
        public const int MaxDrillMiningTier = 3;

        private const float DrillSlowRpm = 8f;
        private const float DrillFastRpm = 256f;
        private const float DrillSlowSeconds = 3f;
        private const float DrillFastSeconds = 0.5f;
        private const float DrillRpmCurveExponent = 0.55f;

        public static bool TryGetFacing(Block block, out BlockFacing facing)
        {
            facing = block?.Variant?["side"] switch
            {
                "n" => BlockFacing.NORTH,
                "e" => BlockFacing.EAST,
                "s" => BlockFacing.SOUTH,
                "w" => BlockFacing.WEST,
                "u" => BlockFacing.UP,
                "d" => BlockFacing.DOWN,
                _ => null
            };
            return facing != null;
        }

        public static bool CanDrillBreak(IWorldAccessor world, Block block, BlockPos pos, System.Func<BlockPos, bool> canBreak)
        {
            if (!CanBreakCommon(block, pos, canBreak)) return false;
            if (block.RequiredMiningTier > MaxDrillMiningTier) return false;

            EnumBlockMaterial material = block.GetBlockMaterial(world.BlockAccessor, pos);
            return material == EnumBlockMaterial.Stone
                || material == EnumBlockMaterial.Ore
                || material == EnumBlockMaterial.Soil
                || material == EnumBlockMaterial.Gravel
                || material == EnumBlockMaterial.Sand;
        }

        public static bool CanSawWood(IWorldAccessor world, Block block, BlockPos pos, System.Func<BlockPos, bool> canBreak)
        {
            return CanBreakCommon(block, pos, canBreak)
                && block.GetBlockMaterial(world.BlockAccessor, pos) == EnumBlockMaterial.Wood;
        }

        public static bool CanSawLeaves(IWorldAccessor world, Block block, BlockPos pos, System.Func<BlockPos, bool> canBreak)
        {
            return CanBreakCommon(block, pos, canBreak)
                && block.GetBlockMaterial(world.BlockAccessor, pos) == EnumBlockMaterial.Leaves;
        }

        public static float DrillRequiredWork(Block block)
        {
            return MathF.Max(DrillMinimumWork, MathF.Max(1f, block?.Resistance ?? 1f) * DrillWorkPerResistance);
        }

        public static float DrillWorkAmount(float rpm, float dt, float required)
        {
            float speed = Math.Clamp(MathF.Abs(rpm), DrillSlowRpm, DrillFastRpm);
            float curve = MathF.Pow(DrillSlowRpm / speed, DrillRpmCurveExponent);
            float seconds = DrillFastSeconds + (DrillSlowSeconds - DrillFastSeconds) * curve;
            seconds *= MathF.Sqrt(MathF.Max(1f, required) / DrillMinimumWork);
            return required * dt / MathF.Max(0.05f, seconds);
        }

        public static float SawRequiredWork(IWorldAccessor world, IReadOnlyList<BlockPos> targets)
        {
            float required = 0f;
            if (world != null && targets != null)
            {
                foreach (BlockPos pos in targets)
                {
                    Block block = world.BlockAccessor.GetBlock(pos);
                    required += block?.GetBlockMaterial(world.BlockAccessor, pos) == EnumBlockMaterial.Leaves
                        ? SawWorkPerLeafBlock
                        : SawWorkPerWoodBlock;
                }
            }

            return MathF.Max(SawMinimumWork, required);
        }

        public static float SawLeafRequiredWork(Block leaf)
        {
            return MathF.Max(SawLeafMinimumWork, MathF.Max(1f, leaf?.Resistance ?? 1f) * SawWorkPerLeafBlock);
        }

        private static bool CanBreakCommon(Block block, BlockPos pos, System.Func<BlockPos, bool> canBreak)
        {
            return block != null
                && block.Id != 0
                && block.Resistance < 99999f
                && (canBreak?.Invoke(pos) ?? true);
        }
    }
}
