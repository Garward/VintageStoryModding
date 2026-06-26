using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticClutch : BEKineticAnimated, IKineticConnector, IKineticActivatable, IKineticAnimatorRotatorMultiplier
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

        public float GetRotatorSpeedMultiplier(string elementName)
        {
            return !Engaged && elementName == "shaftNeg" ? 0f : 1f;
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            if (!Engaged && !IsInputSide(fromPos, toPos)) return null;
            return TryConnectInline(self, other, fromPos, toPos, Block?.Variant?["side"], 1);
        }

        private bool IsInputSide(BlockPos fromPos, BlockPos toPos)
        {
            BlockFacing input = InputFacingFor(Block?.Variant?["side"]);
            return toPos.X == fromPos.X + input.Normali.X
                && toPos.Y == fromPos.Y + input.Normali.Y
                && toPos.Z == fromPos.Z + input.Normali.Z;
        }

        internal static BlockFacing ShaftNegFacingFor(string side)
        {
            return side switch
            {
                "n" => BlockFacing.NORTH,
                "e" => BlockFacing.EAST,
                "s" => BlockFacing.SOUTH,
                "w" => BlockFacing.WEST,
                "u" => BlockFacing.UP,
                "d" => BlockFacing.DOWN,
                "x" => BlockFacing.EAST,
                "z" => BlockFacing.SOUTH,
                _ => BlockFacing.NORTH
            };
        }

        internal static BlockFacing InputFacingFor(string side)
        {
            return ShaftNegFacingFor(side).Opposite;
        }

        internal static BlockFacing OutputFacingFor(string side)
        {
            return ShaftNegFacingFor(side);
        }

        internal static KineticConnectionResult? TryConnectInline(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos, string side, int sideDirection)
        {
            int dx = toPos.X - fromPos.X;
            int dy = toPos.Y - fromPos.Y;
            int dz = toPos.Z - fromPos.Z;
            if (System.Math.Abs(dx) + System.Math.Abs(dy) + System.Math.Abs(dz) != 1) return null;

            if (!IsLogicPort(side, dx, dy, dz)) return null;
            if (other.Role == EnumKineticRole.Custom) return null;

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

        private static bool IsLogicPort(string side, int dx, int dy, int dz)
        {
            BlockFacing input = InputFacingFor(side);
            BlockFacing output = OutputFacingFor(side);
            return IsFacing(input, dx, dy, dz) || IsFacing(output, dx, dy, dz);
        }

        private static bool IsFacing(BlockFacing facing, int dx, int dy, int dz)
        {
            return facing != null
                && dx == facing.Normali.X
                && dy == facing.Normali.Y
                && dz == facing.Normali.Z;
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
                elem.RotationOrigin = new double[] { 8, 14, 8 };
                elem.RotationX += degrees;
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Engaged ? "Clutch: engaged" : "Clutch: open");
        }
    }
}
