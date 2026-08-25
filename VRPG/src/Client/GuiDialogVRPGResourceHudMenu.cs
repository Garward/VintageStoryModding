using System;
using VRPG.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Client;

/// <summary>Client-only context menu for positioning and sizing the resource HUD.</summary>
public sealed class GuiDialogVRPGResourceHudMenu : GuiDialog
{
    private const double MenuWidth = 320;
    private const double MenuHeight = 258;
    private const string WidthTextKey = "vrpgResourceHudWidth";
    private const string HeightTextKey = "vrpgResourceHudHeight";

    private readonly RpgHudConfig config;
    private readonly Action<bool> setLocked;
    private readonly Action<int, int> resize;
    private readonly Action reset;
    private readonly double mouseX;
    private readonly double mouseY;

    public override string? ToggleKeyCombinationCode => null;
    public override double DrawOrder => 2.5;

    public GuiDialogVRPGResourceHudMenu(
        ICoreClientAPI capi,
        RpgHudConfig config,
        double mouseX,
        double mouseY,
        Action<bool> setLocked,
        Action<int, int> resize,
        Action reset) : base(capi)
    {
        this.config = config;
        this.mouseX = mouseX;
        this.mouseY = mouseY;
        this.setLocked = setLocked;
        this.resize = resize;
        this.reset = reset;
        Compose();
    }

    private void Compose()
    {
        double screenWidth = capi.Render.FrameWidth / RuntimeEnv.GUIScale;
        double screenHeight = capi.Render.FrameHeight / RuntimeEnv.GUIScale;
        double x = Math.Clamp(mouseX / RuntimeEnv.GUIScale + 8, 0, Math.Max(0, screenWidth - MenuWidth));
        double y = Math.Clamp(mouseY / RuntimeEnv.GUIScale + 8, 0, Math.Max(0, screenHeight - MenuHeight));
        ElementBounds dialog = ElementBounds.Fixed(x, y, MenuWidth, MenuHeight);
        ElementBounds background = ElementBounds.Fixed(0, 0, MenuWidth, MenuHeight);
        CairoFont buttonFont = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);

        SingleComposer = capi.Gui
            .CreateCompo("vrpg-resource-hud-menu", dialog)
            .AddShadedDialogBG(background, true)
            .AddDialogTitleBar("Resource Bars", () => TryClose())
            .BeginChildElements(background)
                .AddButton(
                    config.Locked ? "Unlock movement" : "Lock movement",
                    ToggleLocked,
                    ElementBounds.Fixed(18, 48, 284, 30),
                    buttonFont,
                    EnumButtonStyle.Small)
                .AddDynamicText(
                    "Width  " + config.Width + " px",
                    CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(18, 94, 128, 24),
                    WidthTextKey)
                .AddButton("− 25", DecreaseWidth, ElementBounds.Fixed(158, 88, 66, 30), buttonFont, EnumButtonStyle.Small)
                .AddButton("+ 25", IncreaseWidth, ElementBounds.Fixed(236, 88, 66, 30), buttonFont, EnumButtonStyle.Small)
                .AddDynamicText(
                    "Bar height  " + config.BarHeight + " px",
                    CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(18, 138, 128, 24),
                    HeightTextKey)
                .AddButton("− 2", DecreaseHeight, ElementBounds.Fixed(158, 132, 66, 30), buttonFont, EnumButtonStyle.Small)
                .AddButton("+ 2", IncreaseHeight, ElementBounds.Fixed(236, 132, 66, 30), buttonFont, EnumButtonStyle.Small)
                .AddStaticText(
                    "Changes save on this client. Unlock, then left-drag the bars while a cursor is visible.",
                    CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(18, 174, 284, 38))
                .AddButton("Reset layout", ResetLayout, ElementBounds.Fixed(18, 218, 136, 28), buttonFont, EnumButtonStyle.Small)
                .AddButton("Close", () => TryClose(), ElementBounds.Fixed(166, 218, 136, 28), buttonFont, EnumButtonStyle.Small)
            .EndChildElements()
            .Compose();
    }

    private bool ToggleLocked()
    {
        setLocked(!config.Locked);
        TryClose();
        return true;
    }

    private bool DecreaseWidth()
    {
        resize(-25, 0);
        UpdateSizeText();
        return true;
    }

    private bool IncreaseWidth()
    {
        resize(25, 0);
        UpdateSizeText();
        return true;
    }

    private bool DecreaseHeight()
    {
        resize(0, -2);
        UpdateSizeText();
        return true;
    }

    private bool IncreaseHeight()
    {
        resize(0, 2);
        UpdateSizeText();
        return true;
    }

    private bool ResetLayout()
    {
        reset();
        TryClose();
        return true;
    }

    private void UpdateSizeText()
    {
        SingleComposer?.GetDynamicText(WidthTextKey)?.SetNewText("Width  " + config.Width + " px", autoHeight: false);
        SingleComposer?.GetDynamicText(HeightTextKey)?.SetNewText("Bar height  " + config.BarHeight + " px", autoHeight: false);
    }
}
