using System;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client;

public readonly record struct ResourceHudOffset(double X, double Y);
public readonly record struct ResourceHudPosition(double X, double Y);

/// <summary>Pure layout rules for the variably sized, screen-anchored resource HUD.</summary>
public static class ResourceHudLayout
{
    public const int MinimumWidth = RpgHudConfig.MinimumWidth;
    public const int MaximumWidth = RpgHudConfig.MaximumWidth;
    public const int MinimumBarHeight = RpgHudConfig.MinimumBarHeight;
    public const int MaximumBarHeight = RpgHudConfig.MaximumBarHeight;
    public const int DefaultWidth = RpgHudConfig.DefaultWidth;
    public const int DefaultBarHeight = RpgHudConfig.DefaultBarHeight;

    public static int VisibleRows(RpgHudConfig config, RpgResourcePacket snapshot)
    {
        int rows = 2; // Health and mana are always present.
        if (snapshot.MaxMagicShield > 0f || config.ShowMagicShieldWhenEmpty) rows++;
        if (snapshot.BloodUnlocked || config.ShowBloodWhenUnavailable) rows++;
        if (config.ShowExperience) rows++;
        return rows;
    }

    public static double Height(RpgHudConfig config, RpgResourcePacket snapshot)
    {
        int rows = VisibleRows(config, snapshot);
        return rows * Math.Max(14, config.BarHeight)
            + Math.Max(0, rows - 1) * Math.Max(2, config.Gap);
    }

    public static ResourceHudOffset AlignmentOffset(EnumDialogArea alignment, double insetX, double insetY)
    {
        double x = alignment is EnumDialogArea.RightTop or EnumDialogArea.RightMiddle or EnumDialogArea.RightBottom
            ? -Math.Abs(insetX)
            : insetX;
        double y = alignment is EnumDialogArea.LeftBottom or EnumDialogArea.CenterBottom or EnumDialogArea.RightBottom or EnumDialogArea.FixedBottom
            ? -Math.Abs(insetY)
            : insetY;
        return new ResourceHudOffset(x, y);
    }

    public static int ClampWidth(int width) => Math.Clamp(width, MinimumWidth, MaximumWidth);

    public static int ClampBarHeight(int height) => Math.Clamp(height, MinimumBarHeight, MaximumBarHeight);

    public static ResourceHudPosition ResolveTopLeft(
        EnumDialogArea alignment,
        double screenWidth,
        double screenHeight,
        double widgetWidth,
        double widgetHeight,
        double insetX,
        double insetY)
    {
        double x = alignment switch
        {
            EnumDialogArea.RightTop or EnumDialogArea.RightMiddle or EnumDialogArea.RightBottom => screenWidth - widgetWidth - Math.Abs(insetX),
            EnumDialogArea.CenterTop or EnumDialogArea.CenterMiddle or EnumDialogArea.CenterBottom => (screenWidth - widgetWidth) / 2.0 + insetX,
            _ => insetX
        };
        double y = alignment switch
        {
            EnumDialogArea.LeftBottom or EnumDialogArea.CenterBottom or EnumDialogArea.RightBottom or EnumDialogArea.FixedBottom => screenHeight - widgetHeight - Math.Abs(insetY),
            EnumDialogArea.LeftMiddle or EnumDialogArea.CenterMiddle or EnumDialogArea.RightMiddle => (screenHeight - widgetHeight) / 2.0 + insetY,
            _ => insetY
        };

        return ClampTopLeft(x, y, screenWidth, screenHeight, widgetWidth, widgetHeight);
    }

    public static ResourceHudPosition ClampTopLeft(
        double x,
        double y,
        double screenWidth,
        double screenHeight,
        double widgetWidth,
        double widgetHeight)
    {
        return new ResourceHudPosition(
            Math.Clamp(x, 0.0, Math.Max(0.0, screenWidth - widgetWidth)),
            Math.Clamp(y, 0.0, Math.Max(0.0, screenHeight - widgetHeight)));
    }
}
