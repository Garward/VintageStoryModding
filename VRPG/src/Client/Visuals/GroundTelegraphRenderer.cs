using System;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

public sealed class GroundTelegraphRenderer : IRenderer
{
    private const int TrajectorySegments = 30;
    private readonly ICoreClientAPI capi;
    private readonly GroundAreaStore store;
    private readonly VisualStyleResolver styles;
    private readonly CombatVisualsConfig config;
    private readonly SkillTargetingPreview targeting;
    private readonly MeshRef discMesh;
    private readonly MeshRef ringMesh;
    private readonly MeshRef targetingRingMesh;
    private readonly MeshRef trajectoryMesh;
    private readonly MeshData trajectoryUpdate;
    private readonly Matrixf modelMat = new Matrixf();

    // The disc/ring meshes tag every vertex with UV (0.5, 0.5) so a single solid
    // pixel is all the fragment shader ever samples; the tint color is what
    // actually paints the shape.
    private LoadedTexture whiteTexture;

    public double RenderOrder => 0.5;
    public int RenderRange => 90;

    public GroundTelegraphRenderer(
        ICoreClientAPI capi,
        GroundAreaStore store,
        VisualStyleResolver styles,
        CombatVisualsConfig config,
        SkillTargetingPreview targeting)
    {
        this.capi = capi;
        this.store = store;
        this.styles = styles;
        this.config = config;
        this.targeting = targeting;
        discMesh = capi.Render.UploadMesh(BuildDisc(48));
        ringMesh = capi.Render.UploadMesh(BuildRing(48, 0.92f));
        targetingRingMesh = capi.Render.UploadMesh(BuildTargetingRing(64));
        trajectoryUpdate = BuildTrajectory(TrajectorySegments);
        trajectoryMesh = capi.Render.UploadMesh(trajectoryUpdate);
        trajectoryUpdate.Indices = null;
        trajectoryUpdate.Rgba = null;
        whiteTexture = new LoadedTexture(capi, 0, 1, 1);
        capi.Render.LoadOrUpdateTextureFromRgba(new[] { unchecked((int)0xffffffff) }, false, 1, ref whiteTexture);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        long nowMs = capi.ElapsedMilliseconds;
        store.Prune(nowMs);
        targeting.Update();
        if ((store.All.Count == 0 && !targeting.HasTarget) || config.TelegraphOpacity <= 0.01f)
        {
            return;
        }

        IRenderAPI rpi = capi.Render;
        Vec3d cameraPos = capi.World.Player.Entity.CameraPos;
        string ownUid = capi.World.Player.PlayerUID;

        rpi.GlDisableCullFace();
        rpi.GlToggleBlend(true);

        foreach (ClientGroundArea area in store.All)
        {
            Vec3d position = ResolvePosition(area);
            IStandardShaderProgram prog = rpi.PreparedStandardShader((int)position.X, (int)position.Y, (int)position.Z);
            prog.Tex2D = whiteTexture.TextureId;
            prog.NormalShaded = 0;
            VisualStyle style = styles.Resolve(area.StyleCode, 0, area.Radius);
            Vec4f tint = Tint(style.ColorRgba, Alpha(area, ownUid, nowMs));
            prog.RgbaTint = tint;
            prog.ExtraGlow = 16;
            prog.ModelMatrix = modelMat
                .Identity()
                .Translate(position.X - cameraPos.X, position.Y - cameraPos.Y + 0.06, position.Z - cameraPos.Z)
                .Scale(area.Radius, 1f, area.Radius)
                .Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            rpi.RenderMesh(area.Shape == GroundAreaShape.Ring ? ringMesh : discMesh);

            // Wards and owned discs also get a crisp boundary ring on top.
            if (area.Shape == GroundAreaShape.Disc && area.State != GroundAreaState.Triggered)
            {
                Vec4f edge = Tint(style.ColorRgba, Math.Min(1f, tint.A * 2.2f));
                prog.RgbaTint = edge;
                rpi.RenderMesh(ringMesh);
            }

            prog.Stop();
        }

        if (targeting.HasTarget)
        {
            Vec3d position = targeting.Target;
            VisualStyle style = styles.Resolve(targeting.StyleCode, 0, targeting.Radius);
            modelMat
                .Identity()
                .Set(rpi.CameraMatrixOrigin)
                .Translate(position.X - cameraPos.X, position.Y - cameraPos.Y + 0.065, position.Z - cameraPos.Z)
                .Scale(targeting.Radius, 1f, targeting.Radius);

            // World lighting and render flags in the Standard shader can tint
            // different vertices independently. The held prediction uses the
            // unlit wireframe shader so one uniform controls the entire circle.
            IShaderProgram prog = rpi.GetEngineShader(EnumShaderProgram.Wireframe);
            prog.Use();
            rpi.LineWidth = 3f;
            rpi.GLEnableDepthTest();
            rpi.GLDepthMask(false);
            prog.Uniform("origin", new Vec3f(0f, 0f, 0f));
            prog.Uniform("colorIn", Tint(style.ColorRgba, 0.88f * config.TelegraphOpacity));
            prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);
            prog.UniformMatrix("modelViewMatrix", modelMat.Values);
            rpi.RenderMesh(targetingRingMesh);

            if (targeting.ShowsTrajectory && targeting.FlightSeconds > 0f)
            {
                UpdateTrajectoryMesh();
                modelMat
                    .Identity()
                    .Set(rpi.CameraMatrixOrigin)
                    .Translate(
                        targeting.LaunchOrigin.X - cameraPos.X,
                        targeting.LaunchOrigin.Y - cameraPos.Y,
                        targeting.LaunchOrigin.Z - cameraPos.Z);
                rpi.LineWidth = 2.2f;
                prog.Uniform("colorIn", Tint(style.ColorRgba, 0.72f * config.TelegraphOpacity));
                prog.UniformMatrix("modelViewMatrix", modelMat.Values);
                rpi.RenderMesh(trajectoryMesh);
            }

            prog.Stop();
            rpi.LineWidth = 1.6f;
            rpi.GLDepthMask(true);
        }

        rpi.GlToggleBlend(false);
        rpi.GlEnableCullFace();
    }

    private Vec3d ResolvePosition(ClientGroundArea area)
    {
        if (area.FollowEntityId != 0)
        {
            Entity? followed = capi.World.GetEntityById(area.FollowEntityId);
            if (followed != null)
            {
                return new Vec3d(followed.Pos.X, followed.Pos.Y, followed.Pos.Z);
            }
        }

        return new Vec3d(area.X, area.Y, area.Z);
    }

    private float Alpha(ClientGroundArea area, string ownUid, long nowMs)
    {
        bool own = string.Equals(area.OwnerUid, ownUid, StringComparison.Ordinal);
        float sinceChange = (nowMs - area.StateChangedAtMs) / 1000f;
        float baseAlpha = area.State switch
        {
            GroundAreaState.Armed => own ? 0.16f : 0.08f,
            GroundAreaState.Triggered => Math.Max(0f, 0.46f - sinceChange * 1.05f),
            GroundAreaState.Expiring => 0.09f + 0.05f * (float)Math.Sin(nowMs / 90.0),
            _ => own ? 0.14f : 0.10f
        };
        return baseAlpha * config.TelegraphOpacity;
    }

    private static Vec4f Tint(int colorRgba, float alpha)
    {
        return new Vec4f(
            ((colorRgba >> 16) & 0xff) / 255f,
            ((colorRgba >> 8) & 0xff) / 255f,
            (colorRgba & 0xff) / 255f,
            alpha);
    }

    private static MeshData BuildDisc(int segments)
    {
        var mesh = new MeshData(segments + 2, segments * 3, true, true, true, true);
        AddVertex(mesh, 0f, 0f, 0f);
        for (int i = 0; i <= segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            AddVertex(mesh, (float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
        }

        for (int i = 1; i <= segments; i++)
        {
            mesh.AddIndices(new[] { 0, i, i + 1 });
        }

        return mesh;
    }

    private static MeshData BuildRing(int segments, float inner)
    {
        var mesh = new MeshData((segments + 1) * 2, segments * 6, true, true, true, true);
        for (int i = 0; i <= segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            AddVertex(mesh, cos * inner, 0f, sin * inner);
            AddVertex(mesh, cos, 0f, sin);
        }

        for (int i = 0; i < segments; i++)
        {
            int baseIndex = i * 2;
            mesh.AddIndices(new[] { baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 1, baseIndex + 3, baseIndex + 2 });
        }

        return mesh;
    }

    private static MeshData BuildTargetingRing(int segments)
    {
        const int tickCount = 4;
        int vertexCount = segments + tickCount * 2;
        int indexCount = segments * 2 + tickCount * 2;
        var mesh = new MeshData();
        mesh.SetMode(EnumDrawMode.Lines);
        mesh.xyz = new float[vertexCount * 3];
        mesh.Rgba = new byte[vertexCount * 4];
        mesh.Indices = new int[indexCount];

        for (int i = 0; i < segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            LineMeshUtil.AddVertex(
                mesh,
                (float)Math.Cos(angle),
                0f,
                (float)Math.Sin(angle),
                ColorUtil.WhiteArgb);
            mesh.Indices[mesh.IndicesCount++] = i;
            mesh.Indices[mesh.IndicesCount++] = (i + 1) % segments;
        }

        for (int i = 0; i < tickCount; i++)
        {
            double angle = Math.PI * 2 * i / tickCount;
            int start = mesh.VerticesCount;
            LineMeshUtil.AddVertex(mesh, (float)Math.Cos(angle) * 0.82f, 0f, (float)Math.Sin(angle) * 0.82f, ColorUtil.WhiteArgb);
            LineMeshUtil.AddVertex(mesh, (float)Math.Cos(angle), 0f, (float)Math.Sin(angle), ColorUtil.WhiteArgb);
            mesh.Indices[mesh.IndicesCount++] = start;
            mesh.Indices[mesh.IndicesCount++] = start + 1;
        }

        return mesh;
    }

    private static MeshData BuildTrajectory(int segments)
    {
        var mesh = new MeshData();
        mesh.SetMode(EnumDrawMode.Lines);
        mesh.xyz = new float[(segments + 1) * 3];
        mesh.Rgba = new byte[(segments + 1) * 4];
        mesh.Indices = new int[segments * 2];
        for (int i = 0; i <= segments; i++)
        {
            LineMeshUtil.AddVertex(mesh, 0f, 0f, 0f, ColorUtil.WhiteArgb);
        }

        // Two segments on, one off: readable as prediction rather than geometry.
        for (int i = 0; i < segments; i++)
        {
            if (i % 3 == 2) continue;
            mesh.Indices[mesh.IndicesCount++] = i;
            mesh.Indices[mesh.IndicesCount++] = i + 1;
        }

        return mesh;
    }

    private void UpdateTrajectoryMesh()
    {
        Vec3d origin = targeting.LaunchOrigin;
        for (int i = 0; i <= TrajectorySegments; i++)
        {
            Vec3d point = targeting.TrajectoryPosition(i / (float)TrajectorySegments);
            int index = i * 3;
            trajectoryUpdate.xyz[index] = (float)(point.X - origin.X);
            trajectoryUpdate.xyz[index + 1] = (float)(point.Y - origin.Y);
            trajectoryUpdate.xyz[index + 2] = (float)(point.Z - origin.Z);
        }

        capi.Render.UpdateMesh(trajectoryMesh, trajectoryUpdate);
    }

    private static void AddVertex(MeshData mesh, float x, float y, float z)
    {
        mesh.AddVertexWithFlags(
            x,
            y,
            z,
            0.5f,
            0.5f,
            unchecked((int)0xffffffff),
            VertexFlags.PackNormal(0f, 1f, 0f));
        mesh.AddNormal(0f, 1f, 0f);
    }

    public void Dispose()
    {
        discMesh.Dispose();
        ringMesh.Dispose();
        targetingRingMesh.Dispose();
        trajectoryMesh.Dispose();
        whiteTexture.Dispose();
    }
}
