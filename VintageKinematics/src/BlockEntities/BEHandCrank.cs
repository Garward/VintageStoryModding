using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public class BEHandCrank : BlockEntity, IKineticActivatable
    {
        private const float WindPulseSeconds = 0.5f;
        private KineticRotationRenderer renderer;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api is not ICoreClientAPI capi) return;

            BEBehaviorKinetic beh = GetBehavior<BEBehaviorKinetic>();
            if (beh == null) return;

            capi.Tesselator.TesselateBlock(Block, out MeshData mesh);
            MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(mesh);
            renderer = new KineticRotationRenderer(capi, Pos, beh, meshRef);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            renderer?.Dispose();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            renderer?.Dispose();
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            return true;
        }

        public bool OnKineticActivate(IWorldAccessor world, BlockPos targetPos, BlockFacing activatedFace, BlockPos activatorPos, float signedRPM)
        {
            if (world.Side != EnumAppSide.Server) return false;

            BEBehaviorKineticSource src = GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return false;

            int direction = signedRPM < 0f ? -1 : 1;
            src.Wind(seconds: WindPulseSeconds, direction: direction);
            return true;
        }
    }
}
