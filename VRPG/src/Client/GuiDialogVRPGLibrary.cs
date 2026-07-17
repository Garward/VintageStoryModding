using System;
using VRPG.Client.UI;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class GuiDialogVRPGLibrary : GuiDialog
{
    private readonly LibraryEntryPacket[] entries;

    public override string? ToggleKeyCombinationCode => null;

    public GuiDialogVRPGLibrary(ICoreClientAPI capi, LibraryEntryPacket[] entries) : base(capi)
    {
        this.entries = entries ?? Array.Empty<LibraryEntryPacket>();
        Compose();
    }

    public override void OnGuiOpened()
    {
        Compose();
        base.OnGuiOpened();
    }

    private void Compose()
    {
        ElementBounds libraryBounds = ElementBounds.Fixed(0, 0, 920, 560);
        ElementBounds bgBounds = ElementBounds.Fixed(0, 0, 920, 560);
        ElementBounds searchBounds = ElementBounds.Fixed(249, 88, 317, 24);
        var libraryElement = new GuiElementVrpgLibrary(capi, libraryBounds, entries, () => TryClose());

        SingleComposer = capi.Gui
            .CreateCompo("vrpg-library", ElementStdBounds.AutosizedMainDialog)
            .BeginChildElements(bgBounds)
                .AddInteractiveElement(libraryElement, "vrpgLibrary")
                .AddTextInput(searchBounds, libraryElement.SetQuery, CairoFont.WhiteSmallishText(), "vrpgLibrarySearch")
            .EndChildElements()
            .Compose();

        SingleComposer.GetTextInput("vrpgLibrarySearch")?.SetPlaceHolderText("Search entries");
    }
}
