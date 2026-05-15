using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Reusable client renderer for showing a machine's active item stack in-world.
    /// Tesselates only when the displayed stack changes.
    /// </summary>
    public class ItemStackDisplayRenderer : IRenderer
    {
        public double RenderOrder => transform.RenderOrder;
        public int RenderRange => transform.RenderRange;

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly ItemStackDisplayTransform transform;
        private readonly Matrixf modelMat = new Matrixf();
        private MultiTextureMeshRef meshRef;
        private ItemStack stack;
        private string stackMeshKey;

        public ItemStackDisplayRenderer(ICoreClientAPI capi, BlockPos pos, ItemStackDisplayTransform transform)
        {
            this.capi = capi;
            this.pos = pos;
            this.transform = transform ?? new ItemStackDisplayTransform();
        }

        public void UpdateStack(ItemStack newStack)
        {
            string newMeshKey = MeshKey(newStack);
            if (newMeshKey == stackMeshKey)
            {
                stack = newStack?.Clone();
                return;
            }

            meshRef?.Dispose();
            meshRef = null;
            stackMeshKey = newMeshKey;
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

        private static string MeshKey(ItemStack itemStack)
        {
            if (itemStack?.Collectible == null) return null;
            return itemStack.Class + ":" + itemStack.Collectible.Code;
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
                .Translate(transform.Translation.X, transform.Translation.Y, transform.Translation.Z)
                .RotateX(transform.Rotation.X)
                .RotateY(transform.Rotation.Y)
                .RotateZ(transform.Rotation.Z)
                .Scale(transform.Scale.X, transform.Scale.Y, transform.Scale.Z);

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
            stackMeshKey = null;
        }
    }
}
