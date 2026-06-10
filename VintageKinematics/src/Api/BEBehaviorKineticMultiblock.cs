using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Authoring shortcut for multiblock kinetic controllers: declare which cells of the
    /// multiblock claim accept external kinetic input, and this behavior handles variant
    /// rotation and controller-relative offset bookkeeping so the rest of the network code
    /// sees the correct cells without any per-block C#.
    ///
    /// Two ways to declare shaft cells, mixable on the same block:
    ///
    ///   "attributes": {
    ///     "kineticShaftControllerCell": true,                    // controller cell is a shaft
    ///     "kineticShaftControllerOffset": { "x": 0, "y": 1, "z": 0 },
    ///     "kineticShaftCells":   [ { "x": 1, "y": 1, "z": 2 } ],   // base-orientation claim coords
    ///     "kineticShaftElements": [ "ShaftStub" ]                   // shape element names
    ///   }
    ///
    /// Cell coords reference the unrotated (rotateY=0) shape; this behavior reads the variant's
    /// <c>shape.rotateY</c> from the block JSON and rotates declared cells in place, then subtracts
    /// the variant's <c>cposition</c> (read from the vanilla Multiblock behavior) to produce the
    /// controller-relative offsets used by <see cref="Network.WorldNodeProvider"/>.
    ///
    /// Element-name binding resolves to the cell containing the element's bounding-box centroid,
    /// which keeps connectivity in lockstep with whatever cell the visible socket actually lives in.
    /// </summary>
    public class BEBehaviorKineticMultiblock : BlockEntityBehavior, IKineticMultiblockController
    {
        // BlockBehaviorMultiblock keeps its size/cposition fields private. They've been stable
        // across VS versions for years, so reflection is the least-bad way to read them without
        // re-parsing the block's JSON behavior config from scratch.
        // ControllerPositionRel is public in the shipped DLL, the Size fields are private.
        private const BindingFlags MbFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo MbSizeX = typeof(BlockBehaviorMultiblock).GetField("SizeX", MbFlags);
        private static readonly FieldInfo MbSizeY = typeof(BlockBehaviorMultiblock).GetField("SizeY", MbFlags);
        private static readonly FieldInfo MbSizeZ = typeof(BlockBehaviorMultiblock).GetField("SizeZ", MbFlags);
        private static readonly FieldInfo MbCPos  = typeof(BlockBehaviorMultiblock).GetField("ControllerPositionRel", MbFlags);

        private HashSet<long> shaftCellOffsets;

        public BEBehaviorKineticMultiblock(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            ResolveShaftCells(api);
        }

        public bool IsKineticShaftCell(BlockPos fillerPos)
        {
            if (shaftCellOffsets == null || shaftCellOffsets.Count == 0) return false;
            int ox = fillerPos.X - Blockentity.Pos.X;
            int oy = fillerPos.Y - Blockentity.Pos.Y;
            int oz = fillerPos.Z - Blockentity.Pos.Z;
            return shaftCellOffsets.Contains(PackOffset(ox, oy, oz));
        }

        private void ResolveShaftCells(ICoreAPI api)
        {
            shaftCellOffsets = new HashSet<long>();

            Block block = Blockentity.Block;
            if (block == null) return;

            BlockBehaviorMultiblock mb = block.GetBehavior<BlockBehaviorMultiblock>();
            if (mb == null)
            {
                api.Logger.Warning("[VK] {0} has KineticMultiblock behavior but no Multiblock behavior; can't resolve shaft cells.", block.Code);
                return;
            }

            int rotateY = (int)(block.Shape?.rotateY ?? 0f);
            Vec3i cposition = (MbCPos?.GetValue(mb) as Vec3i) ?? new Vec3i(0, 0, 0);
            int sx = (int?)MbSizeX?.GetValue(mb) ?? 1;
            int sy = (int?)MbSizeY?.GetValue(mb) ?? 1;
            int sz = (int?)MbSizeZ?.GetValue(mb) ?? 1;
            Vec3i baseSize = BaseClaimSizeForRotation(new Vec3i(sx, sy, sz), rotateY);

            var baseCells = new List<Vec3i>();

            if (block.Attributes?["kineticShaftControllerCell"].AsBool(false) == true)
            {
                shaftCellOffsets.Add(PackOffset(0, 0, 0));
            }

            JsonObject controllerOffsetAttr = block.Attributes?["kineticShaftControllerOffset"];
            if (controllerOffsetAttr != null && controllerOffsetAttr.Exists)
            {
                shaftCellOffsets.Add(PackOffset(
                    controllerOffsetAttr["x"].AsInt(),
                    controllerOffsetAttr["y"].AsInt(),
                    controllerOffsetAttr["z"].AsInt()
                ));
            }

            JsonObject cellsAttr = block.Attributes?["kineticShaftCells"];
            if (cellsAttr != null && cellsAttr.Exists)
            {
                foreach (var entry in cellsAttr.AsArray())
                {
                    baseCells.Add(new Vec3i(entry["x"].AsInt(), entry["y"].AsInt(), entry["z"].AsInt()));
                }
            }

            JsonObject elementsAttr = block.Attributes?["kineticShaftElements"];
            if (elementsAttr != null && elementsAttr.Exists)
            {
                string[] names = elementsAttr.AsArray<string>();
                Shape shape = LoadBaseShape(api, block);
                if (shape == null && names != null && names.Length > 0)
                {
                    api.Logger.Warning("[VK] {0}: kineticShaftElements declared but base shape failed to load.", block.Code);
                }
                else if (names != null)
                {
                    foreach (string name in names)
                    {
                        Vec3i cell = ResolveElementToCell(api, block, shape, name, baseSize.X, baseSize.Y, baseSize.Z);
                        if (cell != null && !baseCells.Contains(cell)) baseCells.Add(cell);
                    }
                }
            }

            foreach (var baseCell in baseCells)
            {
                Vec3i rotated = RotateCellY(baseCell, rotateY, baseSize.X, baseSize.Z);
                int ox = rotated.X - cposition.X;
                int oy = rotated.Y - cposition.Y;
                int oz = rotated.Z - cposition.Z;
                shaftCellOffsets.Add(PackOffset(ox, oy, oz));
            }
        }

        private static Shape LoadBaseShape(ICoreAPI api, Block block)
        {
            if (block?.Shape?.Base == null) return null;
            AssetLocation loc = block.Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");
            return Shape.TryGet(api, loc);
        }

        private static Vec3i ResolveElementToCell(ICoreAPI api, Block block, Shape shape, string name, int sx, int sy, int sz)
        {
            if (shape == null) return null;
            ShapeElement elt = shape.GetElementByName(name, StringComparison.InvariantCultureIgnoreCase);
            if (elt == null)
            {
                api.Logger.Warning("[VK] {0}: kineticShaftElements references '{1}' but no such element in shape {2}.", block.Code, name, block.Shape?.Base);
                return null;
            }
            // Centroid in shape units (16 per cell). Clamp into the claim so elements that protrude
            // past the cell boundary (e.g. a stub sticking out beyond the body) still resolve to the
            // cell they're anchored in.
            double cx = (elt.From[0] + elt.To[0]) * 0.5;
            double cy = (elt.From[1] + elt.To[1]) * 0.5;
            double cz = (elt.From[2] + elt.To[2]) * 0.5;
            int ix = (int)Math.Clamp(Math.Floor(cx / 16.0), 0, sx - 1);
            int iy = (int)Math.Clamp(Math.Floor(cy / 16.0), 0, sy - 1);
            int iz = (int)Math.Clamp(Math.Floor(cz / 16.0), 0, sz - 1);
            return new Vec3i(ix, iy, iz);
        }

        /// <summary>
        /// Rotates a base-orientation cell coordinate by <paramref name="rotateYDeg"/> around the
        /// claim center, in 90° steps. Matches the rotation applied by the renderer for variants.
        /// </summary>
        public static Vec3i RotateCellY(Vec3i baseCell, int rotateYDeg, int claimSizeX, int claimSizeZ)
        {
            int steps = (((rotateYDeg / 90) % 4) + 4) % 4;
            int cx = baseCell.X, cy = baseCell.Y, cz = baseCell.Z;
            int sx = claimSizeX, sz = claimSizeZ;
            for (int i = 0; i < steps; i++)
            {
                int ncx = cz;
                int ncz = sx - 1 - cx;
                cx = ncx;
                cz = ncz;
                (sx, sz) = (sz, sx);
            }
            return new Vec3i(cx, cy, cz);
        }

        public static Vec3i BaseClaimSizeForRotation(Vec3i rotatedSize, int rotateYDeg)
        {
            int steps = (((rotateYDeg / 90) % 4) + 4) % 4;
            return (steps & 1) == 0
                ? new Vec3i(rotatedSize.X, rotatedSize.Y, rotatedSize.Z)
                : new Vec3i(rotatedSize.Z, rotatedSize.Y, rotatedSize.X);
        }

        private static long PackOffset(int x, int y, int z)
        {
            // 21 bits per axis, biased into unsigned range — way more than any practical multiblock.
            const int Bias = 1 << 20;
            return ((long)(x + Bias) & 0x1FFFFF)
                 | (((long)(y + Bias) & 0x1FFFFF) << 21)
                 | (((long)(z + Bias) & 0x1FFFFF) << 42);
        }
    }
}
