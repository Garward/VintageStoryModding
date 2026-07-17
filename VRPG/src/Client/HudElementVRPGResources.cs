using System;
using VRPG.Client.UI;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class HudElementVRPGResources : HudElement
{
    private readonly RpgHudConfig config;
    private GuiElementVrpgResourceBars? bars;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGResources(ICoreClientAPI capi, RpgHudConfig config) : base(capi)
    {
        this.config = config;
        Compose();
    }

    public void SetSnapshot(RpgResourcePacket packet)
    {
        VrpgClientHudRuntime.HideVanillaStatbar = packet.HideVanillaStatbar;
        bars?.SetSnapshot(packet);
    }

    public override void OnOwnPlayerDataReceived()
    {
        Compose();
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents()
    {
        return false;
    }

    public override bool TryClose()
    {
        return false;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (!config.Enabled)
        {
            return;
        }

        base.OnRenderGUI(deltaTime);
    }

    private void Compose()
    {
        double width = Math.Max(120, config.Width);
        double height = CalculateHeight();
        ElementBounds dialogBounds = ElementBounds.Fixed(config.X, config.Y, width, height);
        dialogBounds.Alignment = ParseAnchor(config.Anchor);

        ElementBounds elementBounds = ElementBounds.Fixed(0, 0, width, height);
        bars = new GuiElementVrpgResourceBars(capi, elementBounds, config);

        SingleComposer = capi.Gui
            .CreateCompo("vrpg-resource-hud", dialogBounds)
            .BeginChildElements(dialogBounds)
                .AddInteractiveElement(bars, "vrpgResourceBars")
            .EndChildElements()
            .Compose();
    }

    private double CalculateHeight()
    {
        int rows = 4;
        if (config.ShowExperience)
        {
            rows++;
        }

        return rows * Math.Max(14, config.BarHeight) + Math.Max(0, rows - 1) * Math.Max(2, config.Gap);
    }

    private static EnumDialogArea ParseAnchor(string anchor)
    {
        switch ((anchor ?? "").Trim().ToLowerInvariant())
        {
            case "left-top":
            case "top-left":
                return EnumDialogArea.LeftTop;
            case "right-top":
            case "top-right":
                return EnumDialogArea.RightTop;
            case "right-bottom":
            case "bottom-right":
                return EnumDialogArea.RightBottom;
            case "center-bottom":
            case "bottom-center":
                return EnumDialogArea.CenterBottom;
            case "center-top":
            case "top-center":
                return EnumDialogArea.CenterTop;
            case "center":
                return EnumDialogArea.CenterMiddle;
            case "left-bottom":
            case "bottom-left":
            default:
                return EnumDialogArea.LeftBottom;
        }
    }
}
