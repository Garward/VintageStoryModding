using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticClutch : BEKineticAnimated, IKineticConnector, IKineticActivatable
    {
        private static readonly AssetLocation ToggleSound = new AssetLocation("sounds/effect/woodswitch");
        private static readonly string[] LeverElementNames = { "lever", "lever2", "lever3" };

        public bool Engaged { get; private set; } = true;

        public void Toggle(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            Engaged = !Engaged;
            Api.World.PlaySoundAt(ToggleSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, randomizePitch: true, range: 12, volume: 0.55f);
            RebuildNetwork();
        }

        public bool OnKineticActivate(IWorldAccessor world, BlockPos targetPos, BlockFacing activatedFace, BlockPos activatorPos, float signedRPM)
        {
            Toggle(null);
            return true;
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            if (!Engaged) return null;
            return TryConnectInline(self, other, fromPos, toPos, 1);
        }

        internal static KineticConnectionResult? TryConnectInline(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos, int sideDirection)
        {
            int dx = toPos.X - fromPos.X;
            int dy = toPos.Y - fromPos.Y;
            int dz = toPos.Z - fromPos.Z;
            if (System.Math.Abs(dx) + System.Math.Abs(dy) + System.Math.Abs(dz) != 1) return null;

            Vec3i axis = EnumKineticAxisExtensions.UnitVector(self.Axis);
            bool isAxialFace = System.Math.Abs(dx) == System.Math.Abs(axis.X)
                && System.Math.Abs(dy) == System.Math.Abs(axis.Y)
                && System.Math.Abs(dz) == System.Math.Abs(axis.Z);
            if (!isAxialFace) return null;

            int dir = sideDirection;
            if (other.Role == EnumKineticRole.Gearbox)
            {
                EnumKineticAxis faceAxis = EnumKineticAxisExtensions.FromVec(new Vec3i(dx, dy, dz));
                if (faceAxis == other.Axis) return null;

                // Match BlockGearbox's port parity. Its direction is computed from gearbox -> shaft;
                // this connector sees self -> gearbox, so invert the signed face offset.
                dir *= -(dx + dy + dz);
            }
            else if (other.Axis != self.Axis)
            {
                return null;
            }

            return new KineticConnectionResult(1f, dir);
        }

        private void RebuildNetwork()
        {
            var mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
            mgr?.OnRemoved(Pos);
            mgr?.OnPlaced(Pos);
            MarkDirty(true);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("engaged", Engaged);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Engaged = tree.GetBool("engaged", true);
        }

        protected override void ConfigureStaticShape(Shape shape)
        {
            if (Engaged) return;
            RotateLever(shape, -65);
        }

        protected static void RotateLever(Shape shape, double degrees)
        {
            foreach (string name in LeverElementNames)
            {
                ShapeElement elem = shape?.GetElementByName(name);
                if (elem == null) continue;
                elem.RotationOrigin = new double[] { 14, 8, 8 };
                elem.RotationZ += degrees;
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Engaged ? "Clutch: engaged" : "Clutch: open");
        }
    }
}
