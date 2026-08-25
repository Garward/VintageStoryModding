using System;
using VRPG.Client.UI;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VRPG.Client;

public sealed class HudElementVRPGResources : HudElement
{
    private readonly RpgHudConfig config;
    private readonly Action persist;
    private GuiElementVrpgResourceBars? bars;
    private GuiDialogVRPGResourceHudMenu? layoutMenu;
    private RpgResourcePacket snapshot = new RpgResourcePacket
    {
        MaxHealth = 100f,
        MaxMana = 100f,
        HudEnabled = true
    };
    private int composedRows;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => true;

    public HudElementVRPGResources(ICoreClientAPI capi, RpgHudConfig config, Action persist) : base(capi)
    {
        this.config = config;
        this.persist = persist;
        Compose();
    }

    public void SetLocked(bool locked)
    {
        config.Locked = locked;
        bars?.SetLocked(locked);
        persist();
    }

    public void Resize(int widthDelta, int barHeightDelta)
    {
        ResourceHudPosition current = CurrentTopLeft();
        config.Width = ResourceHudLayout.ClampWidth(config.Width + widthDelta);
        config.BarHeight = ResourceHudLayout.ClampBarHeight(config.BarHeight + barHeightDelta);
        SetAbsolutePosition(current.X, current.Y);
        Compose();
        persist();
    }

    public void ResetLayout()
    {
        config.Anchor = "left-bottom";
        config.X = 14;
        config.Y = 112;
        config.Width = ResourceHudLayout.DefaultWidth;
        config.BarHeight = ResourceHudLayout.DefaultBarHeight;
        Compose();
        persist();
    }

    public void SetSnapshot(RpgResourcePacket packet)
    {
        if (packet == null)
        {
            return;
        }

        VrpgClientHudRuntime.HideVanillaStatbar = packet.HideVanillaStatbar;
        snapshot = packet;
        int visibleRows = ResourceHudLayout.VisibleRows(config, snapshot);
        if (visibleRows != composedRows)
        {
            Compose();
        }
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

    public override bool ShouldReceiveMouseEvents() => true;

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

    public override void OnMouseDown(MouseEvent args)
    {
        base.OnMouseDown(args);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        base.OnMouseUp(args);
    }

    public override void OnMouseMove(MouseEvent args)
    {
        base.OnMouseMove(args);
    }

    public override void Dispose()
    {
        layoutMenu?.TryClose();
        layoutMenu?.Dispose();
        layoutMenu = null;
        base.Dispose();
    }

    private void Compose()
    {
        config.Width = ResourceHudLayout.ClampWidth(config.Width);
        config.BarHeight = ResourceHudLayout.ClampBarHeight(config.BarHeight);
        double width = config.Width;
        double height = ResourceHudLayout.Height(config, snapshot);
        EnumDialogArea alignment = ParseAnchor(config.Anchor);
        ResourceHudPosition topLeft = ResolveTopLeft(alignment, width, height);
        ResourceHudOffset offset = ResourceHudLayout.AlignmentOffset(alignment, config.X, config.Y);
        ElementBounds dialogBounds = ElementBounds.Fixed(0, 0, width, height)
            .WithFixedAlignmentOffset(offset.X, offset.Y);
        dialogBounds.Alignment = alignment;

        ElementBounds elementBounds = ElementBounds.Fixed(0, 0, width, height);
        GuiElementVrpgResourceBars nextBars = new GuiElementVrpgResourceBars(
            capi,
            elementBounds,
            config,
            topLeft.X,
            topLeft.Y,
            OnMoved,
            OpenLayoutMenu);
        GuiComposer nextComposer = capi.Gui
            .CreateCompo("vrpg-resource-hud", dialogBounds)
            .AddInteractiveElement(nextBars, "vrpgResourceBars")
            .Compose();
        GuiComposer? previousComposer = SingleComposer;
        bars = nextBars;
        SingleComposer = nextComposer;
        composedRows = ResourceHudLayout.VisibleRows(config, snapshot);
        previousComposer?.Dispose();
        bars.SetSnapshot(snapshot);
    }

    private void OnMoved(int x, int y, bool finished)
    {
        SetAbsolutePosition(x, y);
        if (SingleComposer != null)
        {
            SingleComposer.Bounds.Alignment = EnumDialogArea.LeftTop;
            SingleComposer.Bounds.fixedX = config.X;
            SingleComposer.Bounds.fixedY = config.Y;
            SingleComposer.Bounds.CalcWorldBounds();
        }

        if (finished)
        {
            persist();
        }
    }

    private void OpenLayoutMenu(double mouseX, double mouseY)
    {
        layoutMenu?.TryClose();
        layoutMenu?.Dispose();
        layoutMenu = new GuiDialogVRPGResourceHudMenu(
            capi,
            config,
            mouseX,
            mouseY,
            SetLocked,
            Resize,
            ResetLayout);
        layoutMenu.TryOpen();
    }

    private ResourceHudPosition CurrentTopLeft()
    {
        return ResolveTopLeft(ParseAnchor(config.Anchor), config.Width, ResourceHudLayout.Height(config, snapshot));
    }

    private ResourceHudPosition ResolveTopLeft(EnumDialogArea alignment, double width, double height)
    {
        return ResourceHudLayout.ResolveTopLeft(
            alignment,
            capi.Render.FrameWidth / RuntimeEnv.GUIScale,
            capi.Render.FrameHeight / RuntimeEnv.GUIScale,
            width,
            height,
            config.X,
            config.Y);
    }

    private void SetAbsolutePosition(double x, double y)
    {
        ResourceHudPosition position = ResourceHudLayout.ClampTopLeft(
            x,
            y,
            capi.Render.FrameWidth / RuntimeEnv.GUIScale,
            capi.Render.FrameHeight / RuntimeEnv.GUIScale,
            config.Width,
            ResourceHudLayout.Height(config, snapshot));
        config.Anchor = "left-top";
        config.X = (int)Math.Round(position.X);
        config.Y = (int)Math.Round(position.Y);
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
