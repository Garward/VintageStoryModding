using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticReverser : BEKineticAnimated, IKineticConnector, IKineticActivatable, IKineticAnimatorRotatorMultiplier
    {
        private static readonly AssetLocation ToggleSound = new AssetLocation("sounds/effect/woodswitch");

        public bool Reversed { get; private set; }

        public void Toggle(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            Reversed = !Reversed;
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
            return Reversed && elementName == "shaftNeg" ? -1f : 1f;
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            int dir = 1;
            if (Reversed && IsOutputSide(fromPos, toPos))
            {
                dir = -1;
            }
            return BEKineticClutch.TryConnectInline(self, other, fromPos, toPos, Block?.Variant?["side"], dir);
        }

        private bool IsOutputSide(BlockPos fromPos, BlockPos toPos)
        {
            BlockFacing output = ShaftNegFacing();
            return toPos.X == fromPos.X + output.Normali.X
                && toPos.Y == fromPos.Y + output.Normali.Y
                && toPos.Z == fromPos.Z + output.Normali.Z;
        }

        private BlockFacing ShaftNegFacing()
        {
            return BEKineticClutch.OutputFacingFor(Block?.Variant?["side"]);
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
            tree.SetBool("reversed", Reversed);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Reversed = tree.GetBool("reversed", false);
        }

        protected override void ConfigureStaticShape(Shape shape)
        {
            if (!Reversed) return;
            RotateLever(shape, -65);
        }

        private static void RotateLever(Shape shape, double degrees)
        {
            ShapeElement lever = shape?.GetElementByName("lever");
            if (lever == null) return;
            lever.RotationOrigin = new double[] { 8, 14, 8 };
            lever.RotationX += degrees;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Reversed ? "Reverser: flipped" : "Reverser: straight shaft");
        }
    }
}
