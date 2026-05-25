using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    public class KineticStretchRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public class Stretch
        {
            public string ElementName;
            public EnumKineticAxis Axis;
            public string Mode;
            public KineticPistonRenderer.EnumPistonWaveform Waveform;
            public float MinScale;
            public float MaxScale;
            public float Ratio;
            public float PhaseOffset;
            public bool Invert;
            public Vec3f Pivot;
            public MultiTextureMeshRef Mesh;
        }

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly Block block;
        private readonly BEBehaviorKinetic kineticBeh;
        private readonly BEBehaviorKineticSource sourceBeh;
        private readonly List<Stretch> stretches;
        private readonly Matrixf modelMat = new Matrixf();

        public KineticStretchRenderer(ICoreClientAPI capi, BlockPos pos, Block block, BEBehaviorKinetic kineticBeh, BEBehaviorKineticSource sourceBeh, List<Stretch> stretches)
        {
            this.capi = capi;
            this.pos = pos;
            this.block = block;
            this.kineticBeh = kineticBeh;
            this.sourceBeh = sourceBeh;
            this.stretches = stretches;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.World.Player?.Entity == null) return;

            Vec3f blockRotRad = KineticMeshSplitter.GetBlockShapeRotationDeg(kineticBeh?.Block ?? block) * (MathF.PI / 180f);
            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);
            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            foreach (var s in stretches)
            {
                if (s.Mesh == null) continue;
                float scale = ScaleFor(s);
                float sx = s.Axis == EnumKineticAxis.X ? scale : 1f;
                float sy = s.Axis == EnumKineticAxis.Y ? scale : 1f;
                float sz = s.Axis == EnumKineticAxis.Z ? scale : 1f;

                modelMat.Identity()
                    .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z))
                    .Translate(0.5f, 0.5f, 0.5f).Rotate(blockRotRad).Translate(-0.5f, -0.5f, -0.5f)
                    .Translate(s.Pivot.X, s.Pivot.Y, s.Pivot.Z)
                    .Scale(sx, sy, sz)
                    .Translate(-s.Pivot.X, -s.Pivot.Y, -s.Pivot.Z);

                prog.ModelMatrix = modelMat.Values;
                rpi.RenderMultiTextureMesh(s.Mesh, "tex");
            }

            prog.Stop();
        }

        private float ScaleFor(Stretch s)
        {
            float progress = ProgressFor(s);
            if (s.Invert) progress = 1f - progress;
            return s.MinScale + (s.MaxScale - s.MinScale) * progress;
        }

        private float ProgressFor(Stretch s)
        {
            if (s.Mode == "sourceTimed") return sourceBeh?.TimedProgress01() ?? 0f;

            if (s.Mode == "oscillate")
            {
                float rpm = kineticBeh?.ActualRPM ?? 0f;
                float t = (float)capi.World.ElapsedMilliseconds / 1000f;
                float phase = (kineticBeh?.PhaseOffset ?? 0f) + s.PhaseOffset;
                float progress = s.Waveform == KineticPistonRenderer.EnumPistonWaveform.Sine
                    ? KineticPistonMath.OscillateSine(t, rpm, s.Ratio, phase, 1f)
                    : KineticPistonMath.OscillateTriangle(t, rpm, s.Ratio, phase, 1f);

                if (progress < 0f) return 0f;
                if (progress > 1f) return 1f;
                return progress;
            }

            return 0f;
        }

        public void Dispose() { }
    }
}
