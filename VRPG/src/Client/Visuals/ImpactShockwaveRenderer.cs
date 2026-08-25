using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>
/// Draws short-lived unlit impact rings. One mesh is shared by every active
/// shockwave, so procedural impacts add only a bounded list and one draw call
/// per visible ring rather than spawning another gameplay entity.
/// </summary>
public sealed class ImpactShockwaveRenderer : IRenderer
{
    private const int MaximumActive = 24;
    private readonly ICoreClientAPI capi;
    private readonly List<Shockwave> active = new List<Shockwave>();
    private readonly MeshRef circleMesh;
    private readonly Matrixf modelMat = new Matrixf();

    public Action? BeforeRender { get; set; }
    public double RenderOrder => 0.51;
    public int RenderRange => 96;

    public ImpactShockwaveRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        circleMesh = capi.Render.UploadMesh(BuildCircle(72));
    }

    public void Add(Vec3d center, float radius, int colorRgba, float durationSeconds)
    {
        if (active.Count >= MaximumActive)
        {
            active.RemoveAt(0);
        }

        active.Add(new Shockwave
        {
            Center = center.Clone(),
            Radius = Math.Max(0.2f, radius),
            ColorRgba = colorRgba,
            StartedAtMs = capi.ElapsedMilliseconds,
            DurationMs = Math.Max(80, (long)(durationSeconds * 1000f))
        });
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        // Runs in OIT after the engine's Before-stage entity interpolation, so
        // listeners observe the same carrier position that is actually drawn.
        BeforeRender?.Invoke();

        long nowMs = capi.ElapsedMilliseconds;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (nowMs - active[i].StartedAtMs >= active[i].DurationMs)
            {
                active.RemoveAt(i);
            }
        }

        if (active.Count == 0)
        {
            return;
        }

        IRenderAPI rpi = capi.Render;
        Vec3d cameraPos = capi.World.Player.Entity.CameraPos;
        IShaderProgram prog = rpi.GetEngineShader(EnumShaderProgram.Wireframe);
        prog.Use();
        rpi.GLEnableDepthTest();
        rpi.GLDepthMask(false);
        rpi.GlToggleBlend(true);
        prog.Uniform("origin", new Vec3f());
        prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);

        foreach (Shockwave wave in active)
        {
            float progress = GameMath.Clamp((nowMs - wave.StartedAtMs) / (float)wave.DurationMs, 0f, 1f);
            float eased = 1f - (1f - progress) * (1f - progress);
            float radius = wave.Radius * (0.12f + 0.88f * eased);
            float alpha = (1f - progress) * (1f - progress) * 0.62f;
            modelMat
                .Identity()
                .Set(rpi.CameraMatrixOrigin)
                .Translate(
                    wave.Center.X - cameraPos.X,
                    wave.Center.Y - cameraPos.Y + 0.045,
                    wave.Center.Z - cameraPos.Z)
                .Scale(radius, 1f, radius);
            rpi.LineWidth = 1.4f + 2.2f * (1f - progress);
            prog.Uniform("colorIn", Tint(wave.ColorRgba, alpha));
            prog.UniformMatrix("modelViewMatrix", modelMat.Values);
            rpi.RenderMesh(circleMesh);
        }

        prog.Stop();
        rpi.LineWidth = 1.6f;
        rpi.GLDepthMask(true);
        rpi.GlToggleBlend(false);
    }

    public void Dispose()
    {
        active.Clear();
        circleMesh.Dispose();
    }

    private static MeshData BuildCircle(int segments)
    {
        var mesh = new MeshData();
        mesh.SetMode(EnumDrawMode.Lines);
        mesh.xyz = new float[segments * 3];
        mesh.Rgba = new byte[segments * 4];
        mesh.Indices = new int[segments * 2];
        for (int i = 0; i < segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            LineMeshUtil.AddVertex(mesh, (float)Math.Cos(angle), 0f, (float)Math.Sin(angle), ColorUtil.WhiteArgb);
            mesh.Indices[mesh.IndicesCount++] = i;
            mesh.Indices[mesh.IndicesCount++] = (i + 1) % segments;
        }

        return mesh;
    }

    private static Vec4f Tint(int colorRgba, float alpha)
    {
        float authoredAlpha = ((colorRgba >> 24) & 0xff) / 255f;
        return new Vec4f(
            ((colorRgba >> 16) & 0xff) / 255f,
            ((colorRgba >> 8) & 0xff) / 255f,
            (colorRgba & 0xff) / 255f,
            alpha * authoredAlpha);
    }

    private sealed class Shockwave
    {
        public Vec3d Center { get; init; } = new Vec3d();
        public float Radius { get; init; }
        public int ColorRgba { get; init; }
        public long StartedAtMs { get; init; }
        public long DurationMs { get; init; }
    }
}
