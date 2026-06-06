using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    internal class MachineProgressBar
    {
        private readonly ICoreClientAPI capi;
        private readonly string key;
        private readonly Func<float> getProgress;
        private readonly Func<float> getProgressMax;
        private readonly Func<bool> getCanProgress;
        private float drawnProgress = -1f;
        private float drawnProgressMax = -1f;
        private bool drawnProgressActive;
        private long lastRedrawMs;

        public MachineProgressBar(ICoreClientAPI capi, string key, Func<float> getProgress, Func<float> getProgressMax, Func<bool> getCanProgress)
        {
            this.capi = capi;
            this.key = key;
            this.getProgress = getProgress;
            this.getProgressMax = getProgressMax;
            this.getCanProgress = getCanProgress;
        }

        public GuiComposer AddToComposer(GuiComposer composer, ElementBounds bounds)
        {
            return composer.AddDynamicCustomDraw(bounds, OnDraw, key);
        }

        public void Refresh(GuiComposer composer, bool force)
        {
            if (composer == null) return;

            bool active = getCanProgress?.Invoke() == true;
            float progress = active ? getProgress?.Invoke() ?? 0f : 0f;
            float progressMax = active ? getProgressMax?.Invoke() ?? 1f : 1f;
            if (progressMax <= 0f) progressMax = 1f;

            bool changed = active != drawnProgressActive
                || Math.Abs(progress - drawnProgress) > 0.001f
                || Math.Abs(progressMax - drawnProgressMax) > 0.001f;
            if (!force && !changed) return;
            if (!force && capi.ElapsedMilliseconds - lastRedrawMs < 125) return;

            drawnProgressActive = active;
            drawnProgress = progress;
            drawnProgressMax = progressMax;
            composer.GetCustomDraw(key)?.Redraw();
            lastRedrawMs = capi.ElapsedMilliseconds;
        }

        private void OnDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            float max = drawnProgressMax > 0f ? drawnProgressMax : 1f;
            float frac = GameMath.Clamp(drawnProgress / max, 0f, 1f);
            double width = currentBounds.InnerWidth;
            double height = currentBounds.InnerHeight;
            double radius = GuiElement.scaled(2.0);

            ctx.Save();
            RoundedRectangle(ctx, 0.0, 0.0, width, height, radius);
            ctx.SetSourceRGBA(0.05, 0.04, 0.03, 0.82);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.55, 0.46, 0.34, 0.9);
            ctx.LineWidth = GuiElement.scaled(1.0);
            ctx.Stroke();

            if (drawnProgressActive && frac > 0f)
            {
                double inset = GuiElement.scaled(2.0);
                double fillWidth = Math.Max(0.0, (width - 2.0 * inset) * frac);
                RoundedRectangle(ctx, inset, inset, fillWidth, Math.Max(0.0, height - 2.0 * inset), radius);
                using LinearGradient gradient = new LinearGradient(0, 0, width, 0);
                gradient.AddColorStop(0, new Color(0.72, 0.36, 0.08, 1));
                gradient.AddColorStop(1, new Color(0.95, 0.68, 0.18, 1));
                ctx.SetSource(gradient);
                ctx.Fill();
            }

            ctx.Restore();
        }

        private static void RoundedRectangle(Context ctx, double x, double y, double width, double height, double radius)
        {
            if (width <= 0.0 || height <= 0.0) return;
            radius = Math.Min(radius, Math.Min(width, height) / 2.0);
            ctx.NewSubPath();
            ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2.0, 0.0);
            ctx.Arc(x + width - radius, y + height - radius, radius, 0.0, Math.PI / 2.0);
            ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2.0, Math.PI);
            ctx.Arc(x + radius, y + radius, radius, Math.PI, 3.0 * Math.PI / 2.0);
            ctx.ClosePath();
        }
    }
}
