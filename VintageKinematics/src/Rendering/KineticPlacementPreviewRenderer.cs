using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Generic ghost-placement preview for any block that implements
    /// <see cref="IPlacementPreviewProvider"/>. Has no per-block knowledge — addons get a
    /// preview "for free" by implementing the interface on their Block class.
    /// </summary>
    public class KineticPlacementPreviewRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private readonly Dictionary<AssetLocation, MultiTextureMeshRef> meshCache = new Dictionary<AssetLocation, MultiTextureMeshRef>();
        private readonly Matrixf modelMat = new Matrixf();

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public KineticPlacementPreviewRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            IClientPlayer player = capi.World.Player;
            if (player?.Entity == null) return;

            ItemSlot slot = player.InventoryManager?.ActiveHotbarSlot;
            Block held = slot?.Itemstack?.Block;
            if (held is not IPlacementPreviewProvider provider) return;

            BlockSelection sel = player.CurrentBlockSelection;
            if (sel?.Position == null || sel.Face == null) return;

            if (!provider.TryResolvePlacementPreview(capi.World, player, sel, out BlockPos targetPos, out Block variant))
                return;
            if (targetPos == null || variant == null) return;

            Block existingAtTarget = capi.World.BlockAccessor.GetBlock(targetPos);
            if (existingAtTarget != null && !existingAtTarget.IsReplacableBy(held)) return;

            MultiTextureMeshRef meshRef = GetOrCreateMesh(variant);
            if (meshRef == null) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(targetPos.X, targetPos.Y, targetPos.Z);
            prog.RgbaTint = new Vec4f(1f, 1f, 1f, 0.4f);
            prog.NormalShaded = 0;
            prog.ExtraGlow = 32;

            modelMat.Identity()
                .Translate((float)(targetPos.X - camPos.X), (float)(targetPos.Y - camPos.Y), (float)(targetPos.Z - camPos.Z));

            prog.ModelMatrix = modelMat.Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            rpi.RenderMultiTextureMesh(meshRef, "tex");
            prog.Stop();

            rpi.GlToggleBlend(false);
            rpi.GlEnableCullFace();
        }

        private MultiTextureMeshRef GetOrCreateMesh(Block block)
        {
            if (block == null) return null;
            if (meshCache.TryGetValue(block.Code, out MultiTextureMeshRef cached)) return cached;

            capi.Tesselator.TesselateBlock(block, out MeshData mesh);
            if (mesh == null) return null;
            MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(mesh);
            meshCache[block.Code] = meshRef;
            return meshRef;
        }

        public void Dispose()
        {
            foreach (var mr in meshCache.Values) mr?.Dispose();
            meshCache.Clear();
        }
    }
}
