using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    public static class JsonMachineIoBuilder
    {
        public static IOFaceMap Build(
            JsonObject ioEntries,
            Block block,
            BlockPos controllerPos,
            int inputFirst,
            int inputLast,
            int outputFirst,
            int outputLast)
        {
            if (ioEntries == null || !ioEntries.Exists) return null;

            JsonObject[] entries = ioEntries.AsArray();
            if (entries == null || entries.Length == 0) return null;

            var map = new IOFaceMap(controllerPos);
            foreach (JsonObject entry in entries)
            {
                if (entry == null || !entry.Exists) continue;

                BlockFacing face = ResolveFace(block, entry["face"].AsString("up"));
                if (face == null) continue;

                string type = entry["type"].AsString("input");
                bool output = type == "output" || type == "out" || type == "pull";
                SlotRange slots = ResolveSlots(entry, output, inputFirst, inputLast, outputFirst, outputLast);
                if (!slots.Valid) continue;

                foreach (BlockPos cell in ResolveCells(entry, block, controllerPos, face))
                {
                    for (int slot = slots.First; slot <= slots.Last; slot++)
                    {
                        if (output) map.MapOutput(cell, face, slot);
                        else map.MapInput(cell, face, slot);
                    }
                }
            }

            return map;
        }

        public static BlockFacing ResolveFace(Block block, string code)
        {
            BlockFacing facing = MultiblockHelper.PlacementFacingFromVariant(block);
            switch (code)
            {
                case "north": return BlockFacing.NORTH;
                case "east": return BlockFacing.EAST;
                case "south": return BlockFacing.SOUTH;
                case "west": return BlockFacing.WEST;
                case "up": return BlockFacing.UP;
                case "down": return BlockFacing.DOWN;
                case "front": return facing;
                case "back": return facing.Opposite;
                case "left": return MultiblockHelper.LeftOf(facing);
                case "right": return MultiblockHelper.RightOf(facing);
                case "localNorth": return ModelLocalFace(BlockFacing.NORTH, block?.Variant?["side"]);
                case "localEast": return ModelLocalFace(BlockFacing.WEST, block?.Variant?["side"]);
                case "localSouth": return ModelLocalFace(BlockFacing.SOUTH, block?.Variant?["side"]);
                case "localWest": return ModelLocalFace(BlockFacing.EAST, block?.Variant?["side"]);
                case "inputLipNorth": return ModelLipFace(BlockFacing.NORTH, block?.Variant?["side"]);
                case "inputLipEast": return ModelLipFace(BlockFacing.EAST, block?.Variant?["side"]);
                case "inputLipSouth": return ModelLipFace(BlockFacing.SOUTH, block?.Variant?["side"]);
                case "inputLipWest": return ModelLipFace(BlockFacing.WEST, block?.Variant?["side"]);
                default: return BlockFacing.FromCode(code);
            }
        }

        private static IEnumerable<BlockPos> ResolveCells(JsonObject entry, Block block, BlockPos controllerPos, BlockFacing face)
        {
            JsonObject cellAttr = entry["cell"];
            if (cellAttr != null && cellAttr.Exists)
            {
                yield return OffsetCell(block, controllerPos, cellAttr, entry["rotateCell"].AsBool(true));
                yield break;
            }

            JsonObject cellsAttr = entry["cells"];
            if (cellsAttr != null && cellsAttr.Exists)
            {
                string cellsMode = cellsAttr.AsString(null);
                if (cellsMode == "face")
                {
                    foreach (BlockPos cell in MultiblockHelper.CellsOnFace(block, controllerPos, face))
                    {
                        yield return cell;
                    }
                    yield break;
                }
                if (cellsMode == "controller")
                {
                    yield return controllerPos;
                    yield break;
                }

                JsonObject[] cells = cellsAttr.AsArray();
                if (cells != null)
                {
                    foreach (JsonObject cell in cells)
                    {
                        if (cell == null || !cell.Exists) continue;
                        yield return OffsetCell(block, controllerPos, cell, entry["rotateCell"].AsBool(true));
                    }
                    yield break;
                }
            }

            yield return controllerPos;
        }

        private static BlockPos OffsetCell(Block block, BlockPos controllerPos, JsonObject cell, bool rotate)
        {
            Vec3i offset = new Vec3i(cell["x"].AsInt(), cell["y"].AsInt(), cell["z"].AsInt());
            if (rotate)
            {
                offset = RotateOffsetY(offset, (int)(block?.Shape?.rotateY ?? 0f));
            }

            return new BlockPos(
                controllerPos.X + offset.X,
                controllerPos.Y + offset.Y,
                controllerPos.Z + offset.Z,
                controllerPos.dimension);
        }

        private static Vec3i RotateOffsetY(Vec3i offset, int rotateYDeg)
        {
            int steps = (((rotateYDeg / 90) % 4) + 4) % 4;
            int x = offset.X;
            int z = offset.Z;
            for (int i = 0; i < steps; i++)
            {
                int nx = z;
                int nz = -x;
                x = nx;
                z = nz;
            }
            return new Vec3i(x, offset.Y, z);
        }

        private static SlotRange ResolveSlots(JsonObject entry, bool output, int inputFirst, int inputLast, int outputFirst, int outputLast)
        {
            JsonObject slotsAttr = entry["slots"];
            if (slotsAttr == null || !slotsAttr.Exists) slotsAttr = entry["slot"];

            if (slotsAttr == null || !slotsAttr.Exists)
            {
                return output ? new SlotRange(outputFirst, outputLast) : new SlotRange(inputFirst, inputLast);
            }

            string slots = slotsAttr.AsString(null);
            if (!string.IsNullOrEmpty(slots))
            {
                if (slots == "inputs" || slots == "input") return new SlotRange(inputFirst, inputLast);
                if (slots == "outputs" || slots == "output") return new SlotRange(outputFirst, outputLast);
                if (int.TryParse(slots, out int single)) return new SlotRange(single, single);

                int dash = slots.IndexOf("-", StringComparison.Ordinal);
                if (dash > 0
                    && int.TryParse(slots.Substring(0, dash), out int first)
                    && int.TryParse(slots.Substring(dash + 1), out int last))
                {
                    return new SlotRange(first, last);
                }
            }

            if (slotsAttr["first"].Exists || slotsAttr["last"].Exists)
            {
                int fallbackFirst = output ? outputFirst : inputFirst;
                int fallbackLast = output ? outputLast : inputLast;
                return new SlotRange(slotsAttr["first"].AsInt(fallbackFirst), slotsAttr["last"].AsInt(fallbackLast));
            }

            int[] slotIds = TryReadIntArray(slotsAttr);
            if (slotIds != null && slotIds.Length > 0)
            {
                int first = slotIds[0];
                int last = slotIds[0];
                for (int i = 1; i < slotIds.Length; i++)
                {
                    first = Math.Min(first, slotIds[i]);
                    last = Math.Max(last, slotIds[i]);
                }
                return new SlotRange(first, last);
            }

            return output ? new SlotRange(outputFirst, outputLast) : new SlotRange(inputFirst, inputLast);
        }

        private static int[] TryReadIntArray(JsonObject obj)
        {
            try
            {
                return obj.AsArray<int>();
            }
            catch
            {
                return null;
            }
        }

        private static BlockFacing ModelLipFace(BlockFacing modelFace, string side)
        {
            return ModelLocalFace(modelFace, side);
        }

        private static BlockFacing ModelLocalFace(BlockFacing modelFace, string side)
        {
            if (modelFace == BlockFacing.UP || modelFace == BlockFacing.DOWN) return modelFace;

            BlockFacing[] order = { BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST };
            int index = 0;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == modelFace)
                {
                    index = i;
                    break;
                }
            }

            int offset = side switch
            {
                "e" => -1,
                "s" => -2,
                "w" => -3,
                _ => 0
            };
            return order[(index + offset + 4) % 4];
        }

        private readonly struct SlotRange
        {
            public readonly int First;
            public readonly int Last;
            public bool Valid => First >= 0 && Last >= First;

            public SlotRange(int first, int last)
            {
                First = first;
                Last = last;
            }
        }
    }
}
