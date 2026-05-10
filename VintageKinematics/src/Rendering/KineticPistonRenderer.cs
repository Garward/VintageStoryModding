using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Per-block-entity renderer that exposes the current axis-aligned offset for each
    /// piston-driven element. Registered by <see cref="BEBehaviorKineticPiston"/> on the
    /// client only. BE authors call <see cref="ComputeOffsetFor"/> from their own
    /// tesselation/rendering code to translate the named element each frame.
    /// </summary>
    public class KineticPistonRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public enum EnumPistonMode { Oscillate, Directional }
        public enum EnumPistonWaveform { Sine, Triangle }

        public class Piston
        {
            public string ElementName;
            public EnumKineticAxis Axis;
            public EnumPistonMode Mode;
            public EnumPistonWaveform Waveform;
            public float Travel;
            public float Ratio;
            public float PhaseOffset;
            public bool Invert;
            public float CurrentPos;
            public MeshRef Mesh;
        }

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly BEBehaviorKinetic kineticBeh;
        public readonly List<Piston> Pistons;

        public KineticPistonRenderer(ICoreClientAPI capi, BlockPos pos, BEBehaviorKinetic kineticBeh, List<Piston> pistons)
        {
            this.capi = capi;
            this.pos = pos;
            this.kineticBeh = kineticBeh;
            Pistons = pistons;
        }

        private readonly Matrixf modelMat = new Matrixf();

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.World.Player?.Entity == null) return;

            Vec3f blockRotRad = KineticMeshSplitter.GetBlockShapeRotationDeg(kineticBeh?.Block) * (MathF.PI / 180f);

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);
            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;

            foreach (var p in Pistons)
            {
                if (p.Mesh == null) continue;
                Vec3f offset = ComputeOffsetFor(p.ElementName);

                // Offset is in canonical (shape-frame) axis units; the block-rotation matrix
                // applied after this translate carries it into world-frame, so a piston that
                // declares "axis: Y" in JSON correctly extends along the rotated block's Y axis.
                modelMat.Identity()
                    .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z))
                    .Translate(0.5f, 0.5f, 0.5f).Rotate(blockRotRad).Translate(-0.5f, -0.5f, -0.5f)
                    .Translate(offset.X / 16f, offset.Y / 16f, offset.Z / 16f);

                prog.ModelMatrix = modelMat.Values;
                prog.ViewMatrix = rpi.CameraMatrixOriginf;
                prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
                rpi.RenderMesh(p.Mesh);
            }

            prog.Stop();
        }

        /// <summary>Current displacement vector (1/16ths) for the named element, or zero if unknown.</summary>
        public Vec3f ComputeOffsetFor(string elementName)
        {
            foreach (var p in Pistons)
            {
                if (p.ElementName != elementName) continue;
                float scalar = ScalarFor(p);
                return p.Axis switch
                {
                    EnumKineticAxis.X => new Vec3f(scalar, 0f, 0f),
                    EnumKineticAxis.Y => new Vec3f(0f, scalar, 0f),
                    EnumKineticAxis.Z => new Vec3f(0f, 0f, scalar),
                    _ => new Vec3f()
                };
            }
            return new Vec3f();
        }

        private float ScalarFor(Piston p)
        {
            float rpm = kineticBeh?.ActualRPM ?? 0f;
            float t = (float)capi.World.ElapsedMilliseconds / 1000f;
            float phase = (kineticBeh?.PhaseOffset ?? 0f) + p.PhaseOffset;
            float scalar = p.Mode switch
            {
                EnumPistonMode.Oscillate when p.Waveform == EnumPistonWaveform.Sine
                    => KineticPistonMath.OscillateSine(t, rpm, p.Ratio, phase, p.Travel),
                EnumPistonMode.Oscillate
                    => KineticPistonMath.OscillateTriangle(t, rpm, p.Ratio, phase, p.Travel),
                EnumPistonMode.Directional => p.CurrentPos,
                _ => 0f
            };
            // Directional already accounts for invert in AdvanceDirectional; oscillate doesn't,
            // so apply the negation here to support downward-extending oscillating pistons.
            if (p.Invert && p.Mode == EnumPistonMode.Oscillate) scalar = -scalar;
            return scalar;
        }

        public void Dispose() { }
    }
}
