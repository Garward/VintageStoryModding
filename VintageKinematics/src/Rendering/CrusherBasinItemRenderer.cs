using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Draws the basin's input itemstack inside the basin well.
    /// </summary>
    public class CrusherBasinItemRenderer : ItemStackDisplayRenderer
    {
        public CrusherBasinItemRenderer(ICoreClientAPI capi, BlockPos pos)
            : base(capi, pos, new ItemStackDisplayTransform
            {
                Translation = new Vec3f(0.25f, 4f / 16f, 0.25f),
                Scale = new Vec3f(0.5f, 0.5f, 0.5f)
            })
        {
        }
    }
}
