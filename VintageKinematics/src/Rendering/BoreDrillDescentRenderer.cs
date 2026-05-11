using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Rendering
{
    /// <summary>
    /// Renders the bore's drill assembly at the current descent depth instead of at its baked
    /// shape position. Pulls RPM/phase from the bore's <see cref="BEBehaviorKinetic"/> so the
    /// descended drill spins in lockstep with the rest of the kinetic network. When the drill
    /// has descended, the housing notch where DrillAssembly used to sit is filled by a single
    /// spinning shaft mesh (tesselated from kineticboreshaft) instead of a duplicate drill,
    /// so the silhouette reads as housing → shaft column → drill.
    /// </summary>
    public class BoreDrillDescentRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 48;

        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly BEKineticBore bore;
        private readonly BEBehaviorKinetic kinetic;
        private readonly Matrixf modelMat = new Matrixf();
        private MeshRef drillMesh;
        private Vec3f drillPivot;
        private MeshRef shaftMesh;
        private Vec3f shaftPivot;
        // Mirrors the original DrillAssembly rotator entry from kineticbore.json (Y axis,
        // 1:1 ratio). Kept as constants so this renderer stays self-contained — the bore
        // JSON now omits this rotator entirely.
        private const float Ratio = 1f;
        private const EnumKineticAxis CanonAxis = EnumKineticAxis.Y;
        private const string DrillElementName = "DrillAssembly";
        private const string ShaftElementName = "Shaft";

        public BoreDrillDescentRenderer(ICoreClientAPI capi, BlockPos pos, BEKineticBore bore, BEBehaviorKinetic kinetic, Block shaftBlock)
        {
            this.capi = capi;
            this.pos = pos;
            this.bore = bore;
            this.kinetic = kinetic;
            var drill = KineticMeshSplitter.TesselateElement(capi, bore.Block, DrillElementName);
            drillMesh = drill.mesh;
            drillPivot = drill.pivot;
            if (shaftBlock != null)
            {
                var shaft = KineticMeshSplitter.TesselateElement(capi, shaftBlock, ShaftElementName);
                shaftMesh = shaft.mesh;
                shaftPivot = shaft.pivot;
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (drillMesh == null || capi.World.Player?.Entity == null) return;

            float t = (float)capi.World.ElapsedMilliseconds / 1000f;
            float rpm = kinetic?.ActualRPM ?? 0f;
            float kineticPhase = kinetic?.PhaseOffset ?? 0f;
            EnumKineticAxis worldAxis = kinetic?.Axis ?? EnumKineticAxis.Y;

            Vec3f blockRotRad = KineticMeshSplitter.GetBlockShapeRotationDeg(bore.Block) * (MathF.PI / 180f);
            float[] blockRotMat = new Matrixf().Identity().Rotate(blockRotRad).Values;

            float angle = KineticAnimatorMath.ComputeAngle(t, rpm, Ratio, 0f, kineticPhase);
            angle *= KineticAnimatorRenderer.AxisAlignmentSign(blockRotMat, CanonAxis, worldAxis);

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);
            IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            // When the drill is parked in the housing, render it normally. Once it has
            // descended, draw a spinning shaft in the housing notch (so the silhouette is
            // continuous: housing → shaft → drill) and the drill itself at the descended
            // depth. Both meshes share the same angle so they spin in lockstep.
            if (bore.DrillDepth > 0)
            {
                if (shaftMesh != null) DrawShaftAtCenterColumn(prog, rpi, camPos, angle, 0f);
                DrawDrill(prog, rpi, camPos, blockRotRad, angle, -bore.DrillDepth);
            }
            else
            {
                DrawDrill(prog, rpi, camPos, blockRotRad, angle, 0f);
            }

            prog.Stop();
        }

        private void DrawDrill(IStandardShaderProgram prog, IRenderAPI rpi, Vec3d camPos, Vec3f blockRotRad, float angle, float yOffset)
        {
            // Drill mesh is in the bore's canonical 3x3 frame: its vertices already sit at the
            // center column, so we translate to the controller's world position and apply the
            // bore's block rotation around the controller centre (matches the static body).
            modelMat.Identity()
                .Translate((float)(pos.X - camPos.X), (float)(pos.Y - camPos.Y) + yOffset, (float)(pos.Z - camPos.Z))
                .Translate(0.5f, 0.5f, 0.5f).Rotate(blockRotRad).Translate(-0.5f, -0.5f, -0.5f)
                .Translate(drillPivot.X, drillPivot.Y, drillPivot.Z)
                .RotateY(angle)
                .Translate(-drillPivot.X, -drillPivot.Y, -drillPivot.Z);

            prog.ModelMatrix = modelMat.Values;
            rpi.RenderMesh(drillMesh);
        }

        private void DrawShaftAtCenterColumn(IStandardShaderProgram prog, IRenderAPI rpi, Vec3d camPos, float angle, float yOffset)
        {
            // Shaft is tesselated from a standalone 1x1 block, so its mesh sits in its own
            // single-cell frame. Drop it into the bore's center column in world space rather
            // than applying the bore's block rotation — the shaft is rotationally symmetric
            // about Y, only the spin matters.
            bore.TryGetCenterColumnXZ(out int centerX, out int centerZ);
            modelMat.Identity()
                .Translate((float)(centerX - camPos.X), (float)(pos.Y - camPos.Y) + yOffset, (float)(centerZ - camPos.Z))
                .Translate(shaftPivot.X, shaftPivot.Y, shaftPivot.Z)
                .RotateY(angle)
                .Translate(-shaftPivot.X, -shaftPivot.Y, -shaftPivot.Z);

            prog.ModelMatrix = modelMat.Values;
            rpi.RenderMesh(shaftMesh);
        }

        public void Dispose()
        {
            drillMesh?.Dispose();
            drillMesh = null;
            shaftMesh?.Dispose();
            shaftMesh = null;
        }
    }
}
