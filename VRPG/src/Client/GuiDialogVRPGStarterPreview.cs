using System;
using VRPG.Client.UI;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class GuiDialogVRPGStarterPreview : GuiDialog
{
    private readonly OnboardingStatePacket packet;

    public override string? ToggleKeyCombinationCode => null;

    public GuiDialogVRPGStarterPreview(ICoreClientAPI capi, OnboardingStatePacket packet) : base(capi)
    {
        this.packet = packet;
        Compose();
    }

    public override void OnGuiOpened()
    {
        Compose();
        base.OnGuiOpened();
    }

    private void Compose()
    {
        ElementBounds parentBounds = ElementBounds.Fixed(0, 0, 900, 560);
        ElementBounds elementBounds = ElementBounds.Fixed(0, 0, 900, 560);
        var element = new GuiElementVrpgStarterPreview(capi, elementBounds, packet, () => TryClose());
        SingleComposer = capi.Gui
            .CreateCompo("vrpg-starter-preview", ElementStdBounds.AutosizedMainDialog)
            .BeginChildElements(parentBounds)
                .AddInteractiveElement(element, "vrpgStarterPreview")
            .EndChildElements()
            .Compose();
    }
}
