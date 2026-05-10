using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockGearbox : BlockAxisOriented, IKineticConnector
    {
        public override string GetPlacementVariantAxis(IWorldAccessor world, BlockSelection blockSel)
        {
            return GetPlacementVariantAxis(world, null, null, blockSel);
        }

        public override string GetPlacementVariantAxis(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel)
        {
            string heldAxis = itemStack?.Block?.Variant["axis"] ?? Variant["axis"] ?? "y";

            // gearbox-y is the regular horizontal ring: ports on north/east/south/west,
            // closed on top/bottom. Keep it horizontal no matter which face is clicked.
            if (heldAxis == "y") return "y";

            // gearbox-x/gearbox-z are the vertical ring. Hide z from creative, but still
            // redirect to it when placing the vertical item on east/west faces.
            EnumAxis face = blockSel?.Face?.Axis ?? EnumAxis.Y;
            if (face == EnumAxis.X) return "z"; // ports east/west + up/down
            if (face == EnumAxis.Z) return "x"; // ports north/south + up/down

            return heldAxis == "z" ? "z" : "x";
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            Vec3i offset = new Vec3i(toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);

            int absSum = System.Math.Abs(offset.X) + System.Math.Abs(offset.Y) + System.Math.Abs(offset.Z);
            if (absSum != 1) return null;

            EnumKineticAxis offsetAxis = EnumKineticAxisExtensions.FromVec(offset);
            if (offsetAxis == self.Axis) return null;

            // Custom-role neighbours own their own connection rules — defer entirely. Middle belt
            // segments (no inserted shaft) carry Axis=pulleyAxis as a network fiction so chain
            // siblings can connect, but no physical axle actually passes through them; without
            // this guard the gearbox would couple to the side of a middle belt whenever its
            // pulley axis lined up with the gearbox port.
            if (other.Role == EnumKineticRole.Custom) return null;

            // Gearbox-to-gearbox: both must have a port at the shared face. The other gearbox's
            // Axis is its CLOSED axis, so its port exists iff offsetAxis differs from it.
            // Direction is always -1 regardless of relative orientation: the bevel-flip on each
            // side combines so the shared port rotates consistently for both hubs.
            if (other.Role == EnumKineticRole.Gearbox)
            {
                if (offsetAxis == other.Axis) return null;
                return new KineticConnectionResult(1f, -1);
            }

            if (other.Axis != offsetAxis) return null;

            // Asymmetric direction: shafts on +X/+Y/+Z faces spin with the gearbox's
            // internal hub, those on -X/-Y/-Z spin against it. Net effect: opposite
            // collinear shafts reverse across the gearbox; perpendicular shafts on
            // matching-parity faces stay co-rotating.
            int dirSign = offset.X + offset.Y + offset.Z;
            return new KineticConnectionResult(1f, dirSign);
        }
    }
}
