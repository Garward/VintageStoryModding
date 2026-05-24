using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Generic BE class for blocks whose visible motion comes entirely from
    /// <see cref="Api.BEBehaviorKineticAnimator"/> / <see cref="Api.BEBehaviorKineticPiston"/>.
    /// Static body is tesselated minus the managed elements; the behaviors render
    /// the moving parts each frame. This is the canonical pattern from the API tutorial.
    /// </summary>
    public class BEKineticAnimated : BlockEntity
    {
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded, ConfigureStaticShape);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        protected virtual void ConfigureStaticShape(Shape shape)
        {
        }
    }
}
