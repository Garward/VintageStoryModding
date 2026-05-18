using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Renders a linked accordion/pleat chain whose strip edges are solved from shared joints.
    /// The top anchor follows a kinetic oscillator; every intermediate joint is interpolated
    /// between the fixed bottom and moving top anchor.
    /// </summary>
    public class KineticLinkedPleatRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public class Pleat
        {
            public string ElementName;
            public MeshRef Mesh;
            public Vec3f Pivot;
            public float BaseLength = 1f;
        }

        public class Chain
        {
            public List<Pleat> Pleats = new List<Pleat>();
            public string Plane;
            public KineticPistonRenderer.EnumPistonWaveform Waveform;
            public float Ratio;
            public float PhaseOffset;
            public bool Invert;
            public Vec3f Bottom;
            public Vec3f Top;
            public float TopTravelY;
            public float XA;
            public float XB;
            public float ZA;
            public float ZB;
            public bool StartAtA;
            public bool TranslateOnly;
            public float TranslateTOffset = -1f;
            public float TranslateTStep;
        }

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly Block block;
        private readonly BEBehaviorKinetic kineticBeh;
        private readonly List<Chain> chains;
        private readonly Matrixf modelMat = new Matrixf();

        public KineticLinkedPleatRenderer(ICoreClientAPI capi, BlockPos pos, Block block, BEBehaviorKinetic kineticBeh, List<Chain> chains)
        {
            this.capi = capi;
            this.pos = pos;
            this.block = block;
            this.kineticBeh = kineticBeh;
            this.chains = chains;
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
            prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;

            foreach (var chain in chains)
            {
                RenderChain(chain, blockRotRad, camPos, rpi, prog);
            }

            prog.Stop();
        }

        private void RenderChain(Chain chain, Vec3f blockRotRad, Vec3d camPos, IRenderAPI rpi, IStandardShaderProgram prog)
        {
            int count = chain.Pleats.Count;
            if (count == 0) return;

            float progress = ProgressFor(chain);
            float topY = chain.Top.Y + chain.TopTravelY * progress;

            for (int i = 0; i < count; i++)
            {
                Pleat pleat = chain.Pleats[i];
                if (pleat.Mesh == null) continue;

                Vec3f jointA = JointFor(chain, i, count, topY);
                Vec3f jointB = JointFor(chain, i + 1, count, topY);
                float dy = jointB.Y - jointA.Y;
                bool yzPlane = string.Equals(chain.Plane, "zy", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(chain.Plane, "yz", StringComparison.OrdinalIgnoreCase);

                Vec3f offset;
                if (chain.TranslateOnly)
                {
                    if (chain.TranslateTOffset >= 0f)
                    {
                        float t = chain.TranslateTOffset + chain.TranslateTStep * i;
                        offset = new Vec3f(0f, chain.TopTravelY * progress * t / 16f, 0f);
                    }
                    else
                    {
                        Vec3f baseJointA = JointFor(chain, i, count, chain.Top.Y);
                        offset = new Vec3f(
                            (jointA.X - baseJointA.X) / 16f,
                            (jointA.Y - baseJointA.Y) / 16f,
                            (jointA.Z - baseJointA.Z) / 16f
                        );
                    }
                }
                else
                {
                    offset = new Vec3f(
                        jointA.X / 16f - pleat.Pivot.X,
                        jointA.Y / 16f - pleat.Pivot.Y,
                        jointA.Z / 16f - pleat.Pivot.Z
                    );
                }

                modelMat.Identity()
                    .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y), (float)(pos.Z - camPos.Z))
                    .Translate(0.5f, 0.5f, 0.5f).Rotate(blockRotRad).Translate(-0.5f, -0.5f, -0.5f)
                    .Translate(offset.X, offset.Y, offset.Z);

                if (chain.TranslateOnly)
                {
                    prog.ModelMatrix = modelMat.Values;
                    prog.ViewMatrix = rpi.CameraMatrixOriginf;
                    prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
                    rpi.RenderMesh(pleat.Mesh);
                    continue;
                }

                modelMat.Translate(pleat.Pivot.X, pleat.Pivot.Y, pleat.Pivot.Z);

                if (yzPlane)
                {
                    float dz = jointB.Z - jointA.Z;
                    float localDz = BaseJointZ(chain, i + 1) - BaseJointZ(chain, i);
                    float baseAngle = localDz < 0f ? MathF.PI : 0f;
                    float angle = MathF.Atan2(dy, dz) - baseAngle;
                    float scale = LengthScale(pleat.BaseLength, dz, dy);
                    modelMat.RotateX(-angle);
                    modelMat.Scale(1f, 1f, scale);
                }
                else
                {
                    float dx = jointB.X - jointA.X;
                    float localDx = BaseJointX(chain, i + 1) - BaseJointX(chain, i);
                    float baseAngle = localDx < 0f ? MathF.PI : 0f;
                    float angle = MathF.Atan2(dy, dx) - baseAngle;
                    float scale = LengthScale(pleat.BaseLength, dx, dy);
                    modelMat.RotateZ(angle);
                    modelMat.Scale(scale, 1f, 1f);
                }

                modelMat.Translate(-pleat.Pivot.X, -pleat.Pivot.Y, -pleat.Pivot.Z);

                prog.ModelMatrix = modelMat.Values;
                prog.ViewMatrix = rpi.CameraMatrixOriginf;
                prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
                rpi.RenderMesh(pleat.Mesh);
            }
        }

        private float ProgressFor(Chain chain)
        {
            float rpm = kineticBeh?.ActualRPM ?? 0f;
            float t = (float)capi.World.ElapsedMilliseconds / 1000f;
            float phase = (kineticBeh?.PhaseOffset ?? 0f) + chain.PhaseOffset;
            float progress = chain.Waveform == KineticPistonRenderer.EnumPistonWaveform.Sine
                ? KineticPistonMath.OscillateSine(t, rpm, chain.Ratio, phase, 1f)
                : KineticPistonMath.OscillateTriangle(t, rpm, chain.Ratio, phase, 1f);

            if (chain.Invert) progress = 1f - progress;
            return progress;
        }

        private static Vec3f JointFor(Chain chain, int index, int count, float topY)
        {
            float t = index / (float)count;
            if (string.Equals(chain.Plane, "zy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(chain.Plane, "yz", StringComparison.OrdinalIgnoreCase))
            {
                return new Vec3f(
                    chain.Bottom.X + (chain.Top.X - chain.Bottom.X) * t,
                    chain.Bottom.Y + (topY - chain.Bottom.Y) * t,
                    BaseJointZ(chain, index)
                );
            }

            return new Vec3f(
                BaseJointX(chain, index),
                chain.Bottom.Y + (topY - chain.Bottom.Y) * t,
                chain.Bottom.Z + (chain.Top.Z - chain.Bottom.Z) * t
            );
        }

        private static float BaseJointX(Chain chain, int index)
        {
            bool useA = (index % 2 == 0) == chain.StartAtA;
            return useA ? chain.XA : chain.XB;
        }

        private static float BaseJointZ(Chain chain, int index)
        {
            bool useA = (index % 2 == 0) == chain.StartAtA;
            return useA ? chain.ZA : chain.ZB;
        }

        private static float LengthScale(float baseLength, float da, float db)
        {
            if (baseLength <= 0.0001f) return 1f;
            return MathF.Sqrt(da * da + db * db) / baseLength;
        }

        public void Dispose() { }
    }
}
