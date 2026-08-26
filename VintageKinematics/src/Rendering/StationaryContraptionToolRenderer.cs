using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    public sealed class StationaryContraptionToolRenderer : IRenderer
    {
        private readonly ICoreClientAPI capi;
        private readonly BlockPos pos;
        private readonly Block block;
        private readonly BEBehaviorKinetic kinetic;
        private readonly List<RenderedPart> parts = new List<RenderedPart>();
        private readonly Matrixf modelMatrix = new Matrixf();

        public StationaryContraptionToolRenderer(
            ICoreClientAPI capi,
            BlockPos pos,
            Block block,
            BEBehaviorKinetic kinetic,
            IEnumerable<ContraptionMovingPartDefinition> definitions)
        {
            this.capi = capi;
            this.pos = pos;
            this.block = block;
            this.kinetic = kinetic;

            foreach (ContraptionMovingPartDefinition definition in definitions)
            {
                MultiTextureMeshRef mesh = KineticMeshSplitter.TesselateElements(capi, block, definition.ElementNames);
                if (mesh != null) parts.Add(new RenderedPart(mesh, definition));
            }
        }

        public double RenderOrder => 0.5;
        public int RenderRange => 32;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (capi.World.Player?.Entity == null || parts.Count == 0) return;

            Vec3d camera = capi.World.Player.Entity.CameraPos;
            Vec3f blockRotation = KineticMeshSplitter.GetBlockShapeRotationDeg(block) * GameMath.DEG2RAD;
            float[] rotationMatrix = new Matrixf().Identity().Rotate(blockRotation).Values;
            float time = (float)capi.World.ElapsedMilliseconds / 1000f;
            float rpm = kinetic?.ActualRPM ?? 0f;

            IRenderAPI render = capi.Render;
            render.GlDisableCullFace();
            render.GlToggleBlend(true);
            IStandardShaderProgram shader = render.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            shader.ViewMatrix = render.CameraMatrixOriginf;
            shader.ProjectionMatrix = render.CurrentProjectionMatrix;

            foreach (RenderedPart part in parts)
            {
                ContraptionMovingPartDefinition definition = part.Definition;
                float angle = KineticAnimatorMath.ComputeAngle(time, rpm, definition.Ratio, definition.PhaseOffset, kinetic?.PhaseOffset ?? 0f);
                angle *= KineticAnimatorRenderer.AxisAlignmentSign(rotationMatrix, definition.Axis, kinetic?.Axis ?? definition.Axis);

                modelMatrix.Identity()
                    .Translate((float)(pos.X - camera.X), (float)(pos.InternalY - camera.Y), (float)(pos.Z - camera.Z))
                    .Translate(0.5f, 0.5f, 0.5f).Rotate(blockRotation).Translate(-0.5f, -0.5f, -0.5f)
                    .Translate(definition.Pivot.X, definition.Pivot.Y, definition.Pivot.Z);

                if (definition.Axis == EnumKineticAxis.X) modelMatrix.RotateX(angle);
                else if (definition.Axis == EnumKineticAxis.Y) modelMatrix.RotateY(angle);
                else modelMatrix.RotateZ(angle);

                modelMatrix.Translate(-definition.Pivot.X, -definition.Pivot.Y, -definition.Pivot.Z);
                shader.ModelMatrix = modelMatrix.Values;
                render.RenderMultiTextureMesh(part.Mesh, "tex");
            }

            shader.Stop();
        }

        public void Dispose()
        {
            foreach (RenderedPart part in parts) part.Mesh.Dispose();
            parts.Clear();
        }

        private sealed class RenderedPart
        {
            public RenderedPart(MultiTextureMeshRef mesh, ContraptionMovingPartDefinition definition)
            {
                Mesh = mesh;
                Definition = definition;
            }

            public MultiTextureMeshRef Mesh { get; }
            public ContraptionMovingPartDefinition Definition { get; }
        }
    }
}
