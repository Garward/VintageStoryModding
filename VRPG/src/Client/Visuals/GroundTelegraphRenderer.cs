using System;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

public sealed class GroundTelegraphRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly GroundAreaStore store;
    private readonly VisualStyleResolver styles;
    private readonly CombatVisualsConfig config;
    private readonly MeshRef discMesh;
    private readonly MeshRef ringMesh;
    private readonly Matrixf modelMat = new Matrixf();

    // The disc/ring meshes tag every vertex with UV (0.5, 0.5) so a single solid
    // pixel is all the fragment shader ever samples; the tint color is what
    // actually paints the shape.
    private LoadedTexture whiteTexture;

    public double RenderOrder => 0.5;
    public int RenderRange => 90;

    public GroundTelegraphRenderer(ICoreClientAPI capi, GroundAreaStore store, VisualStyleResolver styles, CombatVisualsConfig config)
    {
        this.capi = capi;
        this.store = store;
        this.styles = styles;
        this.config = config;
        discMesh = capi.Render.UploadMesh(BuildDisc(48));
        ringMesh = capi.Render.UploadMesh(BuildRing(48, 0.92f));
        whiteTexture = new LoadedTexture(capi);
        capi.Render.LoadOrUpdateTextureFromRgba(new[] { unchecked((int)0xffffffff) }, false, 0, ref whiteTexture);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        long nowMs = capi.ElapsedMilliseconds;
        store.Prune(nowMs);
        if (store.All.Count == 0 || config.TelegraphOpacity <= 0.01f)
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
            VisualStyle style = styles.Resolve(area.StyleCode, 0, area.Radius);
            Vec4f tint = Tint(style.ColorRgba, Alpha(area, ownUid, nowMs));
            prog.RgbaTint = tint;
            // These generated meshes intentionally have no normals. Disabling
            // normal shading prevents the standard shader from interpreting
            // missing normal data as the severe RGB quadrant gradient seen on
            // large ground discs.
            prog.NormalShaded = 0;
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
        var mesh = new MeshData(segments + 2, segments * 3, false, true, true, false);
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
        var mesh = new MeshData((segments + 1) * 2, segments * 6, false, true, true, false);
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

    private static void AddVertex(MeshData mesh, float x, float y, float z)
    {
        mesh.AddVertexWithFlags(x, y, z, 0.5f, 0.5f, unchecked((int)0xffffffff), 0);
    }

    public void Dispose()
    {
        discMesh.Dispose();
        ringMesh.Dispose();
        whiteTexture.Dispose();
    }
}
