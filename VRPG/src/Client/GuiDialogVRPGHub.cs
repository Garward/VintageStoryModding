using System;
using VRPG.Client.UI;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Client;

public sealed class GuiDialogVRPGHub : GuiDialog
{
    private OpenHubPacket packet;
    private TalentTreeSnapshotPacket talentTree;
    private readonly Action<LibraryEntryPacket[]> openLibrary;
    private readonly Action<string, int> adjustStat;
    private readonly Action<bool, bool> updateOptions;
    private readonly Action<bool, int> updateHotbar;
    private readonly Action<int, string> equipSkill;
    private readonly Action<string[], string[]> applyTalentPlan;
    private readonly Action openTalentEditor;
    private GuiElementVrpgHub? hubElement;

    public override string? ToggleKeyCombinationCode => null;

    public GuiDialogVRPGHub(
        ICoreClientAPI capi,
        OpenHubPacket packet,
        TalentTreeSnapshotPacket talentTree,
        Action<LibraryEntryPacket[]> openLibrary,
        Action<string, int> adjustStat,
        Action<bool, bool> updateOptions,
        Action<bool, int> updateHotbar,
        Action<int, string> equipSkill,
        Action<string[], string[]> applyTalentPlan,
        Action openTalentEditor) : base(capi)
    {
        this.packet = packet;
        this.talentTree = talentTree;
        this.openLibrary = openLibrary;
        this.adjustStat = adjustStat;
        this.updateOptions = updateOptions;
        this.updateHotbar = updateHotbar;
        this.equipSkill = equipSkill;
        this.applyTalentPlan = applyTalentPlan;
        this.openTalentEditor = openTalentEditor;
        Compose();
    }

    public void UpdatePacket(OpenHubPacket nextPacket)
    {
        packet = nextPacket;
        hubElement?.UpdatePacket(nextPacket);
    }

    public void UpdateResources(RpgResourcePacket resources)
    {
        packet.Resources = resources;
        hubElement?.UpdateResources(resources);
    }

    public void UpdateTalentTree(TalentTreeSnapshotPacket nextTalentTree)
    {
        talentTree = nextTalentTree;
        hubElement?.UpdateTalentTree(nextTalentTree);
    }

    public void UpdateTalentProgress(string[] talents)
    {
        packet.Talents = talents ?? Array.Empty<string>();
        hubElement?.UpdateTalentProgress(packet.Talents);
    }

    public override void OnGuiOpened()
    {
        Compose();
        base.OnGuiOpened();
    }

    private void Compose()
    {
        double logicalScreenWidth = capi.Render.FrameWidth / RuntimeEnv.GUIScale;
        double logicalScreenHeight = capi.Render.FrameHeight / RuntimeEnv.GUIScale;
        double hubWidth = Math.Min(1040.0, Math.Max(920.0, logicalScreenWidth - 40.0));
        double hubHeight = Math.Min(620.0, Math.Max(560.0, logicalScreenHeight - 40.0));
        ElementBounds hubBounds = ElementBounds.Fixed(0, 0, hubWidth, hubHeight);
        ElementBounds bgBounds = ElementBounds.Fixed(0, 0, hubWidth, hubHeight);
        hubElement = new GuiElementVrpgHub(capi, hubBounds, packet, talentTree, () => TryClose(), entries =>
        {
            TryClose();
            openLibrary(entries);
        }, adjustStat, updateOptions, updateHotbar, equipSkill, applyTalentPlan, openTalentEditor);

        SingleComposer = capi.Gui
            .CreateCompo("vrpg-hub", ElementStdBounds.AutosizedMainDialog)
            .BeginChildElements(bgBounds)
                .AddInteractiveElement(hubElement, "vrpgHub")
            .EndChildElements()
            .Compose();
    }
}
