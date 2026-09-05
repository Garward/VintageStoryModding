using System;
using VRPG.Config;

namespace VRPG.Client.UI;

/// <summary>Resolves uniform combat-console scaling without allowing it off-screen.</summary>
public static class CombatConsoleSizing
{
    public const double MaximumBaseWidth = 1040;
    public const double MinimumBaseWidth = 320;
    public const double WideBaseHeight = 116;
    public const double NarrowBaseHeight = 112;
    public const double ViewportMargin = 20;

    public static CombatConsoleSize Resolve(int frameWidth, double guiScale, float requestedScale)
    {
        double safeGuiScale = Math.Max(0.25, guiScale);
        double logicalViewportWidth = Math.Max(MinimumBaseWidth, frameWidth / safeGuiScale);
        double availableWidth = Math.Max(1, logicalViewportWidth - ViewportMargin);
        double configuredScale = Math.Clamp(
            requestedScale,
            RpgHudConfig.MinimumCombatConsoleScale,
            RpgHudConfig.MaximumCombatConsoleScale);

        // Preserve a usable 320-unit layout on small displays by reducing only the
        // effective local scale. The saved preference remains unchanged for a
        // later move back to a larger display.
        double fittedScale = Math.Min(configuredScale, availableWidth / MinimumBaseWidth);
        fittedScale = Math.Max(0.25, fittedScale);
        double baseWidth = Math.Clamp(availableWidth / fittedScale, MinimumBaseWidth, MaximumBaseWidth);
        double baseHeight = baseWidth < 760 ? NarrowBaseHeight : WideBaseHeight;

        return new CombatConsoleSize(
            baseWidth * fittedScale,
            baseHeight * fittedScale,
            safeGuiScale * fittedScale,
            baseWidth,
            baseHeight,
            fittedScale);
    }
}

public readonly record struct CombatConsoleSize(
    double FixedWidth,
    double FixedHeight,
    double DrawScale,
    double BaseWidth,
    double BaseHeight,
    double EffectiveLocalScale);
