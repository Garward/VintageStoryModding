using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

namespace VintageKinematics.Api
{
    public static class MachineIoLayouts
    {
        public static IOFaceMap SideInputOppositeAndDownOutput(BlockPos pos, BlockFacing inputFace, int inputSlot, int outputFirst, int outputLast)
        {
            return SideInputOppositeAndDownOutput(pos, inputFace, inputSlot, inputSlot, outputFirst, outputLast);
        }

        public static IOFaceMap SideInputOppositeAndDownOutput(BlockPos pos, BlockFacing inputFace, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            return SideInputOppositeAndDownOutput(pos, inputFace, inputFirst, inputLast, outputFirst, outputLast, true);
        }

        public static IOFaceMap SideInputOppositeAndDownOutput(BlockPos pos, BlockFacing inputFace, int inputFirst, int inputLast, int outputFirst, int outputLast, bool includeTopInput)
        {
            BlockFacing outputFace = inputFace.Opposite;
            IOFaceMap map = new IOFaceMap(pos);

            for (int i = inputFirst; i <= inputLast; i++)
            {
                map.MapInput(inputFace, i);
                if (includeTopInput) map.MapInput(BlockFacing.UP, i);
            }

            for (int i = outputFirst; i <= outputLast; i++)
            {
                map.MapOutput(outputFace, i);
                map.MapOutput(BlockFacing.DOWN, i);
            }
            return map;
        }

        public static IOFaceMap TopInputDownOutput(BlockPos pos, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            IOFaceMap map = new IOFaceMap(pos);
            for (int i = inputFirst; i <= inputLast; i++) map.MapInput(BlockFacing.UP, i);
            for (int i = outputFirst; i <= outputLast; i++) map.MapOutput(BlockFacing.DOWN, i);
            return map;
        }

        public static IOFaceMap SideInputOppositeOutput(BlockPos pos, BlockFacing inputFace, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            IOFaceMap map = new IOFaceMap(pos);
            for (int i = inputFirst; i <= inputLast; i++) map.MapInput(inputFace, i);
            for (int i = outputFirst; i <= outputLast; i++) map.MapOutput(inputFace.Opposite, i);
            return map;
        }

        public static IOFaceMap MultiblockSideInputOppositeAndDownOutput(Block block, BlockPos pos, BlockFacing inputFace, int inputSlot, int outputFirst, int outputLast)
        {
            return MultiblockSideInputOppositeAndDownOutput(block, pos, inputFace, inputSlot, inputSlot, outputFirst, outputLast);
        }

        public static IOFaceMap MultiblockSideInputOppositeAndDownOutput(Block block, BlockPos pos, BlockFacing inputFace, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            BlockFacing outputFace = inputFace.Opposite;
            IOFaceMap map = new IOFaceMap(pos);

            MapInputRangeOnFace(map, block, pos, BlockFacing.UP, inputFirst, inputLast);
            MapInputRangeOnFace(map, block, pos, inputFace, inputFirst, inputLast);
            MapOutputRangeOnFace(map, block, pos, BlockFacing.DOWN, outputFirst, outputLast);
            MapOutputRangeOnFace(map, block, pos, outputFace, outputFirst, outputLast);
            return map;
        }

        public static IOFaceMap MultiblockTopInputDownOutput(Block block, BlockPos pos, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            IOFaceMap map = new IOFaceMap(pos);
            MapInputRangeOnFace(map, block, pos, BlockFacing.UP, inputFirst, inputLast);
            MapOutputRangeOnFace(map, block, pos, BlockFacing.DOWN, outputFirst, outputLast);
            return map;
        }

        public static IOFaceMap MultiblockSideInputOppositeOutput(Block block, BlockPos pos, BlockFacing inputFace, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            IOFaceMap map = new IOFaceMap(pos);
            MapInputRangeOnFace(map, block, pos, inputFace, inputFirst, inputLast);
            MapOutputRangeOnFace(map, block, pos, inputFace.Opposite, outputFirst, outputLast);
            return map;
        }

        public static IOFaceMap MultiblockLeftInputRightAndDownOutput(Block block, BlockPos pos, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            BlockFacing facing = MultiblockHelper.PlacementFacingFromVariant(block);
            BlockFacing inputFace = MultiblockHelper.LeftOf(facing);
            BlockFacing outputFace = MultiblockHelper.RightOf(facing);
            IOFaceMap map = new IOFaceMap(pos);

            MapInputRangeOnFace(map, block, pos, BlockFacing.UP, inputFirst, inputLast);
            MapInputRangeOnFace(map, block, pos, inputFace, inputFirst, inputLast);
            MapOutputRangeOnFace(map, block, pos, BlockFacing.DOWN, outputFirst, outputLast);
            MapOutputRangeOnFace(map, block, pos, outputFace, outputFirst, outputLast);
            return map;
        }

        public static IOFaceMap MultiblockUpperBackCenterOutput(Block block, BlockPos pos, int outputFirst, int outputLast)
        {
            IOFaceMap map = new IOFaceMap(pos);
            if (block == null) return map;
            if (!MultiblockHelper.TryGetClaim(block, pos, out BlockPos baseCorner, out Vec3i size)) return map;

            BlockFacing backFace = BlockFacing.FromFirstLetter(block.Variant?["side"] ?? "n") ?? BlockFacing.NORTH;
            BlockPos outputCell = MultiblockHelper.CellAtFaceCenter(baseCorner, size, backFace, baseCorner.Y + size.Y - 1, pos.dimension);
            for (int i = outputFirst; i <= outputLast; i++) map.MapOutput(outputCell, backFace, i);
            return map;
        }

        public static IOFaceMap MultiblockFaceCenterInputTopCenterInputOppositeAndDownOutput(
            Block block,
            BlockPos pos,
            BlockFacing inputFace,
            int inputFirst,
            int inputLast,
            int outputFirst,
            int outputLast)
        {
            IOFaceMap map = new IOFaceMap(pos);
            BlockFacing outputFace = inputFace.Opposite;

            if (!MultiblockHelper.TryGetClaim(block, pos, out BlockPos baseCorner, out Vec3i size))
            {
                for (int i = inputFirst; i <= inputLast; i++)
                {
                    map.MapInput(inputFace, i);
                    map.MapInput(BlockFacing.UP, i);
                }
                for (int i = outputFirst; i <= outputLast; i++)
                {
                    map.MapOutput(outputFace, i);
                    map.MapOutput(BlockFacing.DOWN, i);
                }
                return map;
            }

            int bottomY = baseCorner.Y;
            int topY = baseCorner.Y + size.Y - 1;
            BlockPos inputCell = MultiblockHelper.CellAtFaceCenter(baseCorner, size, inputFace, bottomY, pos.dimension);
            BlockPos topInputCell = MultiblockHelper.CellAtClaimCenter(baseCorner, size, topY, pos.dimension);
            BlockPos outputCell = MultiblockHelper.CellAtFaceCenter(baseCorner, size, outputFace, bottomY, pos.dimension);
            BlockPos downOutputCell = MultiblockHelper.CellAtClaimCenter(baseCorner, size, bottomY, pos.dimension);

            for (int i = inputFirst; i <= inputLast; i++)
            {
                map.MapInput(inputCell, inputFace, i);
                map.MapInput(topInputCell, BlockFacing.UP, i);
            }
            for (int i = outputFirst; i <= outputLast; i++)
            {
                map.MapOutput(outputCell, outputFace, i);
                map.MapOutput(downOutputCell, BlockFacing.DOWN, i);
            }
            return map;
        }

        private static void MapInputRangeOnFace(IOFaceMap map, Block block, BlockPos pos, BlockFacing face, int first, int last)
        {
            foreach (BlockPos cell in MultiblockHelper.CellsOnFace(block, pos, face))
            {
                for (int i = first; i <= last; i++) map.MapInput(cell, face, i);
            }
        }

        private static void MapOutputRangeOnFace(IOFaceMap map, Block block, BlockPos pos, BlockFacing face, int first, int last)
        {
            foreach (BlockPos cell in MultiblockHelper.CellsOnFace(block, pos, face))
            {
                for (int i = first; i <= last; i++) map.MapOutput(cell, face, i);
            }
        }

    }
}
