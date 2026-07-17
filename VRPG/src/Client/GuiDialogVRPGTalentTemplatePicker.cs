using System;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class GuiDialogVRPGTalentTemplatePicker : GuiDialog
{
    private readonly string[] templateCodes;
    private readonly string[] templateNames;
    private readonly Action<string> reset;
    private string selectedCode;

    public override string? ToggleKeyCombinationCode => null;
    public override double DrawOrder => 2;

    public GuiDialogVRPGTalentTemplatePicker(
        ICoreClientAPI capi,
        string[] templateCodes,
        string[] templateNames,
        Action<string> reset) : base(capi)
    {
        this.templateCodes = templateCodes ?? Array.Empty<string>();
        this.templateNames = templateNames ?? Array.Empty<string>();
        this.reset = reset;
        selectedCode = this.templateCodes.Length > 0 ? this.templateCodes[0] : "";
        Compose();
    }

    private void Compose()
    {
        ElementBounds background = ElementBounds.Fixed(0, 0, 430, 190);
        SingleComposer = capi.Gui
            .CreateCompo("vrpg-talent-template-picker", ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(background, true)
            .AddDialogTitleBar("Reset Talent Tree", () => TryClose())
            .BeginChildElements(background)
                .AddStaticText("Replace the private draft from a built-in template. Nothing changes for players until Save.", CairoFont.WhiteSmallText(), ElementBounds.Fixed(20, 48, 390, 42))
                .AddDropDown(templateCodes, templateNames, 0, OnTemplateChanged, ElementBounds.Fixed(20, 98, 390, 30), "vrpgResetTemplate")
                .AddButton("Cancel", () => TryClose(), ElementBounds.Fixed(196, 142, 100, 30), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddButton("Reset Draft", ResetDraft, ElementBounds.Fixed(306, 142, 104, 30), CenteredButtonFont(), EnumButtonStyle.Small)
            .EndChildElements()
            .Compose();
    }

    private void OnTemplateChanged(string code, bool selected)
    {
        if (selected) selectedCode = code;
    }

    private bool ResetDraft()
    {
        if (!string.IsNullOrWhiteSpace(selectedCode)) reset(selectedCode);
        TryClose();
        return true;
    }

    private static CairoFont CenteredButtonFont()
    {
        return CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);
    }
}
