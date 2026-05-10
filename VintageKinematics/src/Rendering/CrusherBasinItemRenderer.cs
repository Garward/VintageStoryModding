using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Draws the basin's input itemstack inside the basin well. Tesselates the held block
    /// or item once on stack change and renders via the standard shader each frame.
    /// </summary>
    public class CrusherBasinItemRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly Matrixf modelMat = new Matrixf();
        private MultiTextureMeshRef meshRef;
        private ItemStack stack;

        public CrusherBasinItemRenderer(ICoreClientAPI capi, BlockPos pos)
        {
            this.capi = capi;
            this.pos = pos;
        }

        public void UpdateStack(ItemStack newStack)
        {
            meshRef?.Dispose();
            meshRef = null;
            stack = newStack?.Clone();
            if (stack == null) return;

            MeshData mesh;
            if (stack.Class == EnumItemClass.Block && stack.Block != null)
            {
                capi.Tesselator.TesselateBlock(stack.Block, out mesh);
            }
            else if (stack.Item != null)
            {
                capi.Tesselator.TesselateItem(stack.Item, out mesh);
            }
            else return;

            if (mesh != null) meshRef = capi.Render.UploadMultiTextureMesh(mesh);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (meshRef == null) return;
            if (capi.World.Player?.Entity == null) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.AlphaTest = 0.05f;

            modelMat.Identity()
                .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z))
                .Translate(0.5f, 4f / 16f, 0.5f)
                .Scale(0.5f, 0.5f, 0.5f);

            prog.ModelMatrix = modelMat.Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            rpi.RenderMultiTextureMesh(meshRef, "tex");
            prog.Stop();
        }

        public void Dispose()
        {
            meshRef?.Dispose();
            meshRef = null;
        }
    }
}
