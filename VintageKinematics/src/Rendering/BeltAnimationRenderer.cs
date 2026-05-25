using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;
using VintageKinematics.Blocks;

namespace VintageKinematics.Rendering
{
    public class BeltAnimationRenderer : IRenderer
    {
        private const int FrameCount = 160;
        private const int RenderRangeBlocks = 24;
        private const int White = unchecked((int)0xffffffff);
        private static readonly int FlagsUp = VertexFlags.PackNormal(0f, 1f, 0f);
        private static readonly int FlagsDown = VertexFlags.PackNormal(0f, -1f, 0f);
        private static readonly int FlagsNorth = VertexFlags.PackNormal(0f, 0f, -1f);
        private static readonly int FlagsEast = VertexFlags.PackNormal(1f, 0f, 0f);
        private static readonly int FlagsWest = VertexFlags.PackNormal(-1f, 0f, 0f);
        private const float BlankBandU2 = 1f / 64f;
        private const float BlankBandV2 = 1f / 64f;

        private readonly ICoreClientAPI capi;
        private readonly BEBelt belt;
        private readonly BEBehaviorKinetic kinetic;
        private readonly Matrixf modelMat = new Matrixf();

        private MeshRef[] surfaceFrames;
        private MeshRef shaftMesh;
        private int surfaceTextureId;
        private int shaftTextureId;
        private EnumBeltPart builtPart;
        private bool builtHasShaft;
        private string builtDirection;

        public double RenderOrder => 0.51;
        public int RenderRange => RenderRangeBlocks;

        public BeltAnimationRenderer(ICoreClientAPI capi, BEBelt belt, BEBehaviorKinetic kinetic)
        {
            this.capi = capi;
            this.belt = belt;
            this.kinetic = kinetic;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.World.Player?.Entity == null) return;
            if (string.IsNullOrEmpty(belt.Direction)) return;

            EnsureMeshes();
            if (surfaceFrames == null) return;

            float t = (float)capi.World.ElapsedMilliseconds / 1000f;
            float rpm = kinetic?.ActualRPM ?? 0f;
            int sign = BEBelt.HeadDirSign(belt.Direction);
            float phase = PositiveMod(sign * (t * rpm / 60f + (kinetic?.PhaseOffset ?? 0f) / (2f * MathF.PI)), 1f);
            // Start segments carry an extra +180° baseRot (so their pulley faces the rest of
            // the chain), which flips shape-Z relative to world-Z. Re-invert the scroll on
            // Start only so the surface still tracks world-frame chain motion.
            if (belt.Part == EnumBeltPart.Start) phase = PositiveMod(1f - phase, 1f);
            int frame = (int)(phase * FrameCount) % FrameCount;
            if (frame < 0) frame += FrameCount;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(false);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(belt.Pos.X, belt.Pos.Y, belt.Pos.Z);
            prog.RgbaLightIn = capi.World.BlockAccessor.GetLightRGBs(belt.Pos.X, belt.Pos.Y, belt.Pos.Z);

            float rotY = BaseRotationDegrees() * MathF.PI / 180f;
            PrepareBaseMatrix(camPos, rotY);
            prog.Tex2D = surfaceTextureId;
            prog.ModelMatrix = modelMat.Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            rpi.RenderMesh(surfaceFrames[frame]);

            if (shaftMesh != null && ShouldRenderShaft())
            {
                float angle = KineticAnimatorMath.ComputeAngle(t, rpm, 1f, 0f, kinetic?.PhaseOffset ?? 0f);
                modelMat.Identity()
                    .Translate((float)(belt.Pos.X - camPos.X), (float)(belt.Pos.Y - camPos.Y), (float)(belt.Pos.Z - camPos.Z))
                    .Translate(0.5f, 0.5f, 0.5f);
                switch (kinetic?.Axis)
                {
                    case EnumKineticAxis.X: modelMat.RotateX(angle); break;
                    case EnumKineticAxis.Y: modelMat.RotateY(angle); break;
                    case EnumKineticAxis.Z: modelMat.RotateZ(angle); break;
                }
                modelMat.RotateY(rotY);
                modelMat.Translate(-0.5f, -0.5f, -0.5f);
                prog.Tex2D = shaftTextureId;
                prog.ModelMatrix = modelMat.Values;
                rpi.RenderMesh(shaftMesh);
            }

            RenderBeltItems(rpi, prog, camPos);

            prog.Stop();
        }

        private void RenderBeltItems(IRenderAPI rpi, IStandardShaderProgram prog, Vec3d camPos)
        {
            BEBelt controller = GetController();
            if (controller == null) return;
            var items = controller.Items;
            if (items.Count == 0) return;

            int chainLen = controller.ChainLength;
            int myIdx = belt.IndexInChain;

            for (int i = 0; i < items.Count; i++)
            {
                BeltItem bi = items[i];
                if (bi?.Stack == null) continue;
                int idx = (int)MathF.Floor(bi.Progress);
                if (idx < 0) idx = 0;
                else if (idx > chainLen - 1) idx = chainLen - 1;
                if (idx != myIdx) continue;

                MultiTextureMeshRef mref = GetStackMeshRef(bi.Stack);
                if (mref == null) continue;

                Vec3d wp = controller.ProgressToWorld(bi.Progress);
                modelMat.Identity()
                    .Translate((float)(wp.X - camPos.X), (float)(wp.Y - camPos.Y), (float)(wp.Z - camPos.Z))
                    .Scale(0.5f, 0.5f, 0.5f)
                    .Translate(-0.5f, 0f, -0.5f);
                prog.ModelMatrix = modelMat.Values;
                rpi.RenderMultiTextureMesh(mref, "tex");
            }
        }

        private BEBelt GetController()
        {
            if (belt.IsController) return belt;
            if (belt.ControllerPos == null) return null;
            return capi.World.BlockAccessor.GetBlockEntity(belt.ControllerPos) as BEBelt;
        }

        private MultiTextureMeshRef GetStackMeshRef(ItemStack stack)
        {
            if (stack.Class == EnumItemClass.Block)
            {
                return stack.Block != null ? capi.TesselatorManager.GetDefaultBlockMeshRef(stack.Block) : null;
            }
            return stack.Item != null ? capi.TesselatorManager.GetDefaultItemMeshRef(stack.Item) : null;
        }

        private void PrepareBaseMatrix(Vec3d camPos, float rotY)
        {
            modelMat.Identity()
                .Translate((float)(belt.Pos.X - camPos.X), (float)(belt.Pos.Y - camPos.Y), (float)(belt.Pos.Z - camPos.Z))
                .Translate(0.5f, 0.5f, 0.5f)
                .RotateY(rotY)
                .Translate(-0.5f, -0.5f, -0.5f);
        }

        private void EnsureMeshes()
        {
            if (surfaceFrames != null
                && builtPart == belt.Part
                && builtHasShaft == belt.HasShaft
                && builtDirection == belt.Direction)
            {
                return;
            }

            DisposeMeshes();

            builtPart = belt.Part;
            builtHasShaft = belt.HasShaft;
            builtDirection = belt.Direction;

            TextureAtlasPosition surfaceTex = ResolveBlockTexture("surface");
            TextureAtlasPosition woodTex = ResolveBlockTexture("wood");

            if (surfaceTex == null || woodTex == null)
            {
                builtPart = default;
                builtHasShaft = default;
                builtDirection = null;
                surfaceTextureId = 0;
                shaftTextureId = 0;
                return;
            }

            surfaceTextureId = surfaceTex.atlasTextureId;
            shaftTextureId = woodTex.atlasTextureId;
            surfaceFrames = new MeshRef[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                MeshData mesh = BuildSurfaceFrame(i / (float)FrameCount);
                mesh.SetTexPos(surfaceTex);
                surfaceFrames[i] = capi.Render.UploadMesh(mesh);
            }

            if (ShouldRenderShaft())
            {
                MeshData shaft = new MeshData(32, 48);
                AddBox(shaft, 0f, 6.25f / 16f, 6.25f / 16f, 1f, 9.75f / 16f, 9.75f / 16f);
                shaft.SetTexPos(woodTex);
                shaftMesh = capi.Render.UploadMesh(shaft);
            }
        }

        private TextureAtlasPosition ResolveBlockTexture(string textureCode)
        {
            ITexPositionSource texSource = capi.Tesselator.GetTextureSource(belt.Block, 0, true);
            TextureAtlasPosition sourcePos = texSource?[textureCode];
            TextureAtlasPosition directPos = capi.BlockTextureAtlas.GetPosition(belt.Block, textureCode, false);
            TextureAtlasPosition wildcardPos = capi.BlockTextureAtlas.GetPosition(belt.Block, textureCode, true);
            TextureAtlasPosition resolved = sourcePos
                ?? directPos
                ?? wildcardPos
                ?? capi.BlockTextureAtlas.UnknownTexturePosition;
            return resolved;
        }

        private MeshData BuildSurfaceFrame(float phase)
        {
            MeshData mesh = new MeshData(128, 192);

            const float animatedSurfaceLift = 0.001f;
            float yTop = 11f / 16f + animatedSurfaceLift;
            float yBot = 5f / 16f - animatedSurfaceLift;
            float zStart = 0f;
            float zEnd = 1f;

            if (belt.Part == EnumBeltPart.Start)
            {
                float chainPhase = PositiveMod(1f - phase, 1f);
                AddScrollingZQuadReversed(mesh, zStart, zEnd, yTop, chainPhase, isTop: true);
                AddScrollingZQuadReversed(mesh, zStart, zEnd, yBot, 1f - chainPhase, isTop: false);
            }
            else
            {
                AddScrollingZQuad(mesh, zStart, zEnd, yTop, phase, isTop: true);
                AddScrollingZQuad(mesh, zStart, zEnd, yBot, 1f - phase, isTop: false);
            }

            if (IsEndLike())
            {
                float outerWrapPhase = 1f - phase;
                AddEndWrap(mesh, outerWrapPhase);
            }

            return mesh;
        }

        private static void AddEndWrap(MeshData mesh, float outerPhase)
        {
            const float outerY1 = 5.01f / 16f;
            const float outerY2 = 10.99f / 16f;
            const float z1 = 0f;
            const float zMid = 0.1f / 16f;
            const float x1 = 0f;
            const float x2 = 1f;

            AddScrollingAxisQuad(mesh,
                x1, outerY2, z1,
                x2, outerY2, z1,
                x2, outerY1, z1,
                x1, outerY1, z1,
                outerPhase, outerY2 - outerY1, FlagsNorth);

            AddBlankAxisQuad(mesh,
                x2, outerY2, z1,
                x2, outerY2, zMid,
                x2, outerY1, zMid,
                x2, outerY1, z1,
                FlagsEast);

            AddBlankAxisQuad(mesh,
                x1, outerY2, zMid,
                x1, outerY2, z1,
                x1, outerY1, z1,
                x1, outerY1, zMid,
                FlagsWest);

            AddScrollingAxisQuad(mesh,
                x1, outerY2, zMid,
                x2, outerY2, zMid,
                x2, outerY2, z1,
                x1, outerY2, z1,
                outerPhase, zMid - z1, FlagsUp);

            AddScrollingAxisQuad(mesh,
                x1, outerY1, z1,
                x2, outerY1, z1,
                x2, outerY1, zMid,
                x1, outerY1, zMid,
                1f - outerPhase, zMid - z1, FlagsDown);

        }

        private static void AddScrollingZQuad(MeshData mesh, float z1, float z2, float y, float phase, bool isTop)
        {
            float zLen = z2 - z1;
            float vEnd = phase + zLen;

            if (vEnd <= 1f)
            {
                AddSurfaceQuad(mesh, z1, z2, y, phase, vEnd, isTop);
            }
            else
            {
                float zSplit = z1 + (1f - phase);
                AddSurfaceQuad(mesh, z1, zSplit, y, phase, 1f, isTop);
                AddSurfaceQuad(mesh, zSplit, z2, y, 0f, vEnd - 1f, isTop);
            }
        }

        private static void AddScrollingZQuadReversed(MeshData mesh, float z1, float z2, float y, float phase, bool isTop)
        {
            float zLen = z2 - z1;
            float zSplit = z2 - (1f - phase) * zLen;

            if (zSplit > z1)
            {
                AddSurfaceQuad(mesh, z1, zSplit, y, phase, 0f, isTop);
            }
            if (zSplit < z2)
            {
                AddSurfaceQuad(mesh, zSplit, z2, y, 1f, phase, isTop);
            }
        }

        private static void AddSurfaceQuad(MeshData mesh, float z1, float z2, float y, float v1, float v2, bool isTop)
        {
            int v = mesh.VerticesCount;
            int flags = isTop ? FlagsUp : FlagsDown;
            if (isTop)
            {
                mesh.AddVertexWithFlags(0f, y, z1, 0f, v1, White, flags);
                mesh.AddVertexWithFlags(1f, y, z1, 1f, v1, White, flags);
                mesh.AddVertexWithFlags(1f, y, z2, 1f, v2, White, flags);
                mesh.AddVertexWithFlags(0f, y, z2, 0f, v2, White, flags);
            }
            else
            {
                mesh.AddVertexWithFlags(0f, y, z1, 0f, v1, White, flags);
                mesh.AddVertexWithFlags(0f, y, z2, 0f, v2, White, flags);
                mesh.AddVertexWithFlags(1f, y, z2, 1f, v2, White, flags);
                mesh.AddVertexWithFlags(1f, y, z1, 1f, v1, White, flags);
            }
            mesh.AddIndices(v, v + 1, v + 2, v, v + 2, v + 3);
        }

        private static void AddScrollingAxisQuad(MeshData mesh,
            float ax1, float ay1, float az1,
            float ax2, float ay2, float az2,
            float bx2, float by2, float bz2,
            float bx1, float by1, float bz1,
            float phase, float span, int flags)
        {
            float vEnd = phase + span;
            if (vEnd <= 1f)
            {
                AddAxisQuad(mesh,
                    ax1, ay1, az1,
                    ax2, ay2, az2,
                    bx2, by2, bz2,
                    bx1, by1, bz1,
                    phase, vEnd, flags);
                return;
            }

            float frac = (1f - phase) / span;
            float sx1 = Lerp(ax1, bx1, frac);
            float sy1 = Lerp(ay1, by1, frac);
            float sz1 = Lerp(az1, bz1, frac);
            float sx2 = Lerp(ax2, bx2, frac);
            float sy2 = Lerp(ay2, by2, frac);
            float sz2 = Lerp(az2, bz2, frac);

            AddAxisQuad(mesh,
                ax1, ay1, az1,
                ax2, ay2, az2,
                sx2, sy2, sz2,
                sx1, sy1, sz1,
                phase, 1f, flags);
            AddAxisQuad(mesh,
                sx1, sy1, sz1,
                sx2, sy2, sz2,
                bx2, by2, bz2,
                bx1, by1, bz1,
                0f, vEnd - 1f, flags);
        }

        private static void AddAxisQuad(MeshData mesh,
            float ax1, float ay1, float az1,
            float ax2, float ay2, float az2,
            float bx2, float by2, float bz2,
            float bx1, float by1, float bz1,
            float v1, float v2, int flags)
        {
            int v = mesh.VerticesCount;
            mesh.AddVertexWithFlags(ax1, ay1, az1, 0f, v1, White, flags);
            mesh.AddVertexWithFlags(ax2, ay2, az2, 1f, v1, White, flags);
            mesh.AddVertexWithFlags(bx2, by2, bz2, 1f, v2, White, flags);
            mesh.AddVertexWithFlags(bx1, by1, bz1, 0f, v2, White, flags);
            mesh.AddIndices(v, v + 1, v + 2, v, v + 2, v + 3);
        }

        private static void AddBlankAxisQuad(MeshData mesh,
            float ax1, float ay1, float az1,
            float ax2, float ay2, float az2,
            float bx2, float by2, float bz2,
            float bx1, float by1, float bz1,
            int flags)
        {
            int v = mesh.VerticesCount;
            mesh.AddVertexWithFlags(ax1, ay1, az1, 0f, 0f, White, flags);
            mesh.AddVertexWithFlags(ax2, ay2, az2, BlankBandU2, 0f, White, flags);
            mesh.AddVertexWithFlags(bx2, by2, bz2, BlankBandU2, BlankBandV2, White, flags);
            mesh.AddVertexWithFlags(bx1, by1, bz1, 0f, BlankBandV2, White, flags);
            mesh.AddIndices(v, v + 1, v + 2, v, v + 2, v + 3);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static void AddBox(MeshData mesh, float x1, float y1, float z1, float x2, float y2, float z2)
        {
            AddBoxQuad(mesh, x1, y1, z1, x2, y1, z1, x2, y2, z1, x1, y2, z1, VertexFlags.PackNormal(0f, 0f, -1f));
            AddBoxQuad(mesh, x2, y1, z1, x2, y1, z2, x2, y2, z2, x2, y2, z1, VertexFlags.PackNormal(1f, 0f, 0f));
            AddBoxQuad(mesh, x2, y1, z2, x1, y1, z2, x1, y2, z2, x2, y2, z2, VertexFlags.PackNormal(0f, 0f, 1f));
            AddBoxQuad(mesh, x1, y1, z2, x1, y1, z1, x1, y2, z1, x1, y2, z2, VertexFlags.PackNormal(-1f, 0f, 0f));
            AddBoxQuad(mesh, x1, y2, z1, x2, y2, z1, x2, y2, z2, x1, y2, z2, FlagsUp);
            AddBoxQuad(mesh, x1, y1, z2, x2, y1, z2, x2, y1, z1, x1, y1, z1, FlagsDown);
        }

        private static void AddBoxQuad(MeshData mesh,
            float x1, float y1, float z1,
            float x2, float y2, float z2,
            float x3, float y3, float z3,
            float x4, float y4, float z4,
            int flags)
        {
            int v = mesh.VerticesCount;
            mesh.AddVertexWithFlags(x1, y1, z1, 0f, 0f, White, flags);
            mesh.AddVertexWithFlags(x2, y2, z2, 1f, 0f, White, flags);
            mesh.AddVertexWithFlags(x3, y3, z3, 1f, 1f, White, flags);
            mesh.AddVertexWithFlags(x4, y4, z4, 0f, 1f, White, flags);
            mesh.AddIndices(v, v + 1, v + 2, v, v + 2, v + 3);
        }

        private bool IsEndLike()
        {
            return belt.Part == EnumBeltPart.Start || belt.Part == EnumBeltPart.End || belt.Part == EnumBeltPart.Solo;
        }

        private bool ShouldRenderShaft()
        {
            return IsEndLike() || belt.HasShaft;
        }

        private int BaseRotationDegrees()
        {
            int baseRot = belt.Direction switch
            {
                "n" => 0,
                "e" => 270,
                "s" => 180,
                "w" => 90,
                _ => 0
            };

            if (belt.Part == EnumBeltPart.Start)
            {
                baseRot = (baseRot + 180) % 360;
            }

            return baseRot;
        }

        private static float PositiveMod(float value, float modulus)
        {
            float result = value % modulus;
            if (result < 0f) result += modulus;
            return result;
        }

        public void Dispose()
        {
            DisposeMeshes();
        }

        private void DisposeMeshes()
        {
            if (surfaceFrames != null)
            {
                foreach (MeshRef mesh in surfaceFrames)
                {
                    mesh?.Dispose();
                }
                surfaceFrames = null;
            }

            shaftMesh?.Dispose();
            shaftMesh = null;
        }
    }
}
