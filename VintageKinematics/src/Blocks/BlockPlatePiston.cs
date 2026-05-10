using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Plate piston block with one stub on the bottom and a second stub on a player-chosen side.
    /// Variant key <c>side</c> takes values <c>n</c>, <c>e</c>, <c>s</c>, <c>w</c>; the second stub
    /// faces the cardinal direction of the player at placement (so they can wire a shaft into the
    /// stub from either the floor below or the side they chose). Connects only on those two faces;
    /// all other faces (top, three remaining sides) reject kinetic neighbors.
    /// </summary>
    public class BlockPlatePiston : Block, IKineticConnector
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            string desiredSide = SideForPlayer(byPlayer);
            if (Variant["side"] == desiredSide)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }
            AssetLocation variantCode = CodeWithVariant("side", desiredSide);
            Block variant = world.GetBlock(variantCode);
            if (variant == null || variant == this)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            Vec3i offset = new Vec3i(toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);
            int absSum = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
            if (absSum != 1) return null;

            // Bottom stub: -Y face, Y-axis shaft.
            if (offset.Y == -1 && other.Axis == EnumKineticAxis.Y)
            {
                return new KineticConnectionResult(1f, 1);
            }

            // Side stub depends on which variant we are. Each variant's stub points in one
            // cardinal direction; the connecting shaft must rotate around the perpendicular axis.
            string side = Variant["side"];
            EnumKineticAxis sideAxis;
            Vec3i sideOffset;
            switch (side)
            {
                case "n": sideOffset = new Vec3i(0, 0, -1); sideAxis = EnumKineticAxis.Z; break;
                case "s": sideOffset = new Vec3i(0, 0,  1); sideAxis = EnumKineticAxis.Z; break;
                case "e": sideOffset = new Vec3i(1, 0,  0); sideAxis = EnumKineticAxis.X; break;
                case "w": sideOffset = new Vec3i(-1, 0, 0); sideAxis = EnumKineticAxis.X; break;
                default: return null;
            }
            if (offset.X == sideOffset.X && offset.Y == sideOffset.Y && offset.Z == sideOffset.Z
                && other.Axis == sideAxis)
            {
                return new KineticConnectionResult(1f, 1);
            }

            return null;
        }

        /// <summary>Pick the closest cardinal direction to the player's yaw.</summary>
        private static string SideForPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return "s";
            double rad = byPlayer.Entity.Pos.Yaw % (Math.PI * 2);
            if (rad < 0) rad += Math.PI * 2;

            // VS yaw convention: 0 = south (+Z), π/2 = west (-X), π = north (-Z), 3π/2 = east (+X).
            // The stub faces "the direction the player is looking" so wiring is intuitive.
            if (rad < Math.PI / 4 || rad >= 7 * Math.PI / 4) return "s";
            if (rad < 3 * Math.PI / 4) return "w";
            if (rad < 5 * Math.PI / 4) return "n";
            return "e";
        }
    }
}
