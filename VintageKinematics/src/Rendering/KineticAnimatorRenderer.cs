using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    public static class KineticAnimatorMath
    {
        public static float ComputeAngle(float timeSeconds, float rpm, float ratio, float phaseOffset, float kineticPhase = 0f)
        {
            return kineticPhase + phaseOffset + timeSeconds * (rpm * ratio / 60f) * 2f * MathF.PI;
        }
    }

    /// <summary>
    /// Per-block-entity renderer that rotates named shape elements at
    /// time × rpm × ratio + phaseOffset. Registered by
    /// <see cref="BEBehaviorKineticAnimator"/> on the client only.
    /// </summary>
    public class KineticAnimatorRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public enum EnumRotatorMode { Spin, Oscillate }

        public class Rotator
        {
            public string ElementName;
            public EnumKineticAxis Axis;
            public EnumRotatorMode Mode;
            public KineticPistonRenderer.EnumPistonWaveform Waveform;
            public float Ratio;
            public float PhaseOffset;
            public float MinAngle;
            public float MaxAngle;
            public bool Invert;
            public bool AlignToKineticAxis;
            public EnumKineticAxis TranslateAxis;
            public float TranslateTravel;
            public float TranslatePhaseOffset;
            public bool TranslateInvert;
            public MeshRef Mesh;
            public Vec3f Pivot;
        }

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly BEBehaviorKinetic kineticBeh;
        private readonly List<Rotator> rotators;
        private readonly Matrixf modelMat = new Matrixf();

        public KineticAnimatorRenderer(ICoreClientAPI capi, BlockPos pos, BEBehaviorKinetic kineticBeh, List<Rotator> rotators)
        {
            this.capi = capi;
            this.pos = pos;
            this.kineticBeh = kineticBeh;
            this.rotators = rotators;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.World.Player?.Entity == null) return;

            Vec3f blockRotRad = KineticMeshSplitter.GetBlockShapeRotationDeg(kineticBeh?.Block) * (MathF.PI / 180f);
            float t = (float)capi.World.ElapsedMilliseconds / 1000f;
            float rpm = kineticBeh?.ActualRPM ?? 0f;
            float kineticPhase = kineticBeh?.PhaseOffset ?? 0f;
            EnumKineticAxis kineticWorldAxis = kineticBeh?.Axis ?? EnumKineticAxis.Y;

            // Pre-compute the canonical→world rotation matrix once per frame so we can lift
            // each rotator's canonical spin axis into world frame.
            float[] blockRotMat = new Matrixf().Identity().Rotate(blockRotRad).Values;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);
            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;

            foreach (var r in rotators)
            {
                if (r.Mesh == null) continue;
                float angle = ComputeAngle(r, t, rpm, kineticPhase);
                Vec3f offset = ComputeOffset(r, t, rpm, kineticPhase);

                // The rotator declares its axis in canonical (shape-file) frame. After the block
                // is rotated by shapeByType, the canonical axis may end up anti-parallel to the
                // network's world kinetic axis — which would make the visual spin backwards.
                // Sign-flip the angle so positive RPM always rotates CCW around +(kinetic world axis).
                if (r.AlignToKineticAxis)
                {
                    angle *= AxisAlignmentSign(blockRotMat, r.Axis, kineticWorldAxis);
                }

                modelMat.Identity()
                    .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z))
                    .Translate(0.5f, 0.5f, 0.5f).Rotate(blockRotRad).Translate(-0.5f, -0.5f, -0.5f)
                    .Translate(offset.X / 16f, offset.Y / 16f, offset.Z / 16f)
                    .Translate(r.Pivot.X, r.Pivot.Y, r.Pivot.Z);

                switch (r.Axis)
                {
                    case EnumKineticAxis.X: modelMat.RotateX(angle); break;
                    case EnumKineticAxis.Y: modelMat.RotateY(angle); break;
                    case EnumKineticAxis.Z: modelMat.RotateZ(angle); break;
                }

                modelMat.Translate(-r.Pivot.X, -r.Pivot.Y, -r.Pivot.Z);

                prog.ModelMatrix = modelMat.Values;
                prog.ViewMatrix = rpi.CameraMatrixOriginf;
                prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
                rpi.RenderMesh(r.Mesh);
            }

            prog.Stop();
        }

        // Returns +1 if the canonical axis (after block rotation) points along the kinetic
        // world axis, -1 if it points opposite, and 0 if perpendicular (rotator unaligned with
        // shaft — leave it alone).
        public static float AxisAlignmentSign(float[] blockRotMat, EnumKineticAxis canonAxis, EnumKineticAxis worldAxis)
        {
            Vec3f canon = canonAxis switch
            {
                EnumKineticAxis.X => new Vec3f(1f, 0f, 0f),
                EnumKineticAxis.Y => new Vec3f(0f, 1f, 0f),
                EnumKineticAxis.Z => new Vec3f(0f, 0f, 1f),
                _ => new Vec3f()
            };
            FastVec3f rotated = Mat4f.MulWithVec3(blockRotMat, canon.X, canon.Y, canon.Z);
            float component = worldAxis switch
            {
                EnumKineticAxis.X => rotated.X,
                EnumKineticAxis.Y => rotated.Y,
                EnumKineticAxis.Z => rotated.Z,
                _ => 0f
            };
            if (MathF.Abs(component) < 0.5f) return 1f;
            return MathF.Sign(component);
        }

        public float ComputeAngleFor(string elementName)
        {
            foreach (var r in rotators)
            {
                if (r.ElementName != elementName) continue;
                return ComputeAngle(
                    r,
                    (float)capi.World.ElapsedMilliseconds / 1000f,
                    kineticBeh?.ActualRPM ?? 0f,
                    kineticBeh?.PhaseOffset ?? 0f);
            }
            return 0f;
        }

        private static float ComputeAngle(Rotator r, float timeSeconds, float rpm, float kineticPhase)
        {
            if (r.Mode == EnumRotatorMode.Oscillate)
            {
                float progress = r.Waveform == KineticPistonRenderer.EnumPistonWaveform.Sine
                    ? KineticPistonMath.OscillateSine(timeSeconds, rpm, r.Ratio, kineticPhase + r.PhaseOffset, 1f)
                    : KineticPistonMath.OscillateTriangle(timeSeconds, rpm, r.Ratio, kineticPhase + r.PhaseOffset, 1f);

                if (r.Invert) progress = 1f - progress;
                return r.MinAngle + (r.MaxAngle - r.MinAngle) * progress;
            }

            return KineticAnimatorMath.ComputeAngle(timeSeconds, rpm, r.Ratio, r.PhaseOffset, kineticPhase);
        }

        private static Vec3f ComputeOffset(Rotator r, float timeSeconds, float rpm, float kineticPhase)
        {
            if (r.TranslateTravel == 0f) return new Vec3f();

            float progress = r.Waveform == KineticPistonRenderer.EnumPistonWaveform.Sine
                ? KineticPistonMath.OscillateSine(timeSeconds, rpm, r.Ratio, kineticPhase + r.TranslatePhaseOffset, 1f)
                : KineticPistonMath.OscillateTriangle(timeSeconds, rpm, r.Ratio, kineticPhase + r.TranslatePhaseOffset, 1f);

            float scalar = r.TranslateTravel * progress;
            if (r.TranslateInvert) scalar = -scalar;

            return r.TranslateAxis switch
            {
                EnumKineticAxis.X => new Vec3f(scalar, 0f, 0f),
                EnumKineticAxis.Y => new Vec3f(0f, scalar, 0f),
                EnumKineticAxis.Z => new Vec3f(0f, 0f, scalar),
                _ => new Vec3f()
            };
        }

        public void Dispose() { }
    }
}
