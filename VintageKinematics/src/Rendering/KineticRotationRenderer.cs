using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Network;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    public class KineticRotationRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly BEBehaviorKinetic kinetic;
        private MeshRef meshRef;
        private readonly Matrixf modelMat = new Matrixf();

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public KineticRotationRenderer(ICoreClientAPI capi, BlockPos pos, BEBehaviorKinetic kinetic, MeshRef meshRef)
        {
            this.capi = capi;
            this.pos = pos;
            this.kinetic = kinetic;
            this.meshRef = meshRef;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (meshRef == null) return;
            if (capi.World.Player?.Entity == null) return;

            // Time-based phase: every renderer with the same signed RPM computes the same
            // angle at any instant, keeping the entire connected line phase-locked.
            long now = capi.World.ElapsedMilliseconds;
            double angleRaw = kinetic.ActualRPM * (now / 1000.0) * Math.PI / 30.0 + kinetic.PhaseOffset;
            float angle = (float)(angleRaw % (2.0 * Math.PI));

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;

            modelMat.Identity()
                .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z))
                .Translate(0.5f, 0.5f, 0.5f);

            switch (kinetic.Axis)
            {
                case EnumKineticAxis.X: modelMat.RotateX(angle); break;
                case EnumKineticAxis.Y: modelMat.RotateY(angle); break;
                case EnumKineticAxis.Z: modelMat.RotateZ(angle); break;
            }

            modelMat.Translate(-0.5f, -0.5f, -0.5f);

            prog.ModelMatrix = modelMat.Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            rpi.RenderMesh(meshRef);
            prog.Stop();
        }

        public void Dispose()
        {
            meshRef?.Dispose();
            meshRef = null;
        }
    }
}
