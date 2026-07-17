using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VRPG.Client.UI;
using VRPG.Network;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class GuiDialogVRPGTalentEditor : GuiDialog
{
    private TalentEditorSnapshotPacket packet;
    private readonly Action<string> openSavedTree;
    private readonly Action<string> selectTemplate;
    private readonly Action<string, string, string, float> addModifier;
    private readonly Action<string, string> renameNode;
    private readonly Action<string> renameTree;
    private readonly Action save;
    private readonly Action<string> saveAs;
    private readonly Action<string> deleteSavedTree;
    private GuiElementVrpgTalentEditor? editorElement;
    private string query = "";
    private string selectedStat = "";
    private string operation = "add";
    private string amount = "1";
    private string selectedNodeCode = "";
    private string nodeName = "";
    private string treeName = "";

    public override string? ToggleKeyCombinationCode => null;

    public GuiDialogVRPGTalentEditor(
        ICoreClientAPI capi,
        TalentEditorSnapshotPacket packet,
        Action<string> openSavedTree,
        Action<string> selectTemplate,
        Action<string, string, string, float> addModifier,
        Action<string, string> renameNode,
        Action<string> renameTree,
        Action save,
        Action<string> saveAs,
        Action<string> deleteSavedTree) : base(capi)
    {
        this.packet = packet;
        this.openSavedTree = openSavedTree;
        this.selectTemplate = selectTemplate;
        this.addModifier = addModifier;
        this.renameNode = renameNode;
        this.renameTree = renameTree;
        this.saveAs = saveAs;
        this.deleteSavedTree = deleteSavedTree;
        treeName = packet.Tree.TreeName;
        this.save = save;
        selectedStat = packet.Stats.FirstOrDefault()?.Code ?? "";
        Compose();
    }

    public void UpdatePacket(TalentEditorSnapshotPacket next)
    {
        bool documentChanged = !string.Equals(packet.SelectedSavedTreeCode, next.SelectedSavedTreeCode, StringComparison.OrdinalIgnoreCase)
            || packet.GraphResetRevision != next.GraphResetRevision;
        if (documentChanged)
            selectedNodeCode = "";
        packet = next;
        treeName = next.Tree.TreeName;
        if (!packet.Stats.Any(stat => string.Equals(stat.Code, selectedStat, StringComparison.OrdinalIgnoreCase)))
            selectedStat = packet.Stats.FirstOrDefault()?.Code ?? "";
        if (!documentChanged && editorElement != null)
        {
            editorElement.SetTree(next.Tree, next.Feedback, next.FeedbackError);
            SingleComposer?.GetTextInput("talentTreeName")?.SetValue(treeName, false);
            GuiElementDropDown? savedTrees = SingleComposer?.GetDropDown("talentSavedTree");
            savedTrees?.SetList(next.SavedTreeCodes, next.SavedTreeNames);
            int savedTreeIndex = Math.Max(0, Array.FindIndex(next.SavedTreeCodes, code => string.Equals(code, next.SelectedSavedTreeCode, StringComparison.OrdinalIgnoreCase)));
            savedTrees?.SetSelectedIndex(savedTreeIndex);
            return;
        }
        Compose();
    }

    private void Compose()
    {
        const double width = 1040;
        const double height = 680;
        ElementBounds editorBounds = ElementBounds.Fixed(0, 0, width, height);
        ElementBounds containerBounds = ElementBounds.Fixed(0, 0, width, height);
        editorElement = new GuiElementVrpgTalentEditor(capi, editorBounds, packet.Tree, selectedNodeCode, packet.Feedback, packet.FeedbackError, OnNodeSelected);

        TalentEditorStatPacket[] filtered = packet.Stats
            .Where(stat => string.IsNullOrWhiteSpace(query)
                || stat.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                || stat.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || stat.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(stat => stat.Category).ThenBy(stat => stat.Name).ToArray();
        if (filtered.Length == 0) filtered = packet.Stats;
        string[] statCodes = filtered.Select(stat => stat.Code).ToArray();
        string[] statNames = filtered.Select(stat => stat.Name + "  [" + stat.Category + "]").ToArray();
        int statIndex = Math.Max(0, Array.FindIndex(statCodes, code => string.Equals(code, selectedStat, StringComparison.OrdinalIgnoreCase)));
        int savedTreeIndex = Math.Max(0, Array.FindIndex(packet.SavedTreeCodes, code => string.Equals(code, packet.SelectedSavedTreeCode, StringComparison.OrdinalIgnoreCase)));

        GuiComposer composer = capi.Gui.CreateCompo("vrpg-talent-editor", ElementStdBounds.AutosizedMainDialog)
            .BeginChildElements(containerBounds)
                .AddInteractiveElement(editorElement, "talentEditorGraph")
                .AddDropDown(packet.SavedTreeCodes, packet.SavedTreeNames, savedTreeIndex, OnSavedTreeChanged, ElementBounds.Fixed(730, 72, 292, 28), "talentSavedTree")
                .AddTextInput(ElementBounds.Fixed(730, 130, 212, 28), text => treeName = text ?? "", CairoFont.WhiteSmallishText(), "talentTreeName")
                .AddButton("Rename", RenameTree, ElementBounds.Fixed(948, 130, 74, 28), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddButton("Reset Tree", OpenTemplatePicker, ElementBounds.Fixed(730, 166, 104, 24), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddButton("Delete Saved", DeleteSavedTree, ElementBounds.Fixed(842, 166, 112, 24), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddTextInput(ElementBounds.Fixed(730, 284, 212, 28), text => nodeName = text ?? "", CairoFont.WhiteSmallishText(), "talentNodeName")
                .AddButton("Rename", RenameNode, ElementBounds.Fixed(948, 284, 74, 28), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddTextInput(ElementBounds.Fixed(730, 358, 212, 26), text => query = text ?? "", CairoFont.WhiteSmallishText(), "talentStatSearch")
                .AddButton("Filter", ApplyFilter, ElementBounds.Fixed(948, 358, 74, 26), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddDropDown(statCodes, statNames, statIndex, OnStatChanged, ElementBounds.Fixed(730, 412, 292, 28), "talentStat")
                .AddDropDown(
                    new[] { "add", "increased", "more" },
                    new[] { "Flat", "Additional %", "More %" },
                    operation == "more" ? 2 : operation == "increased" ? 1 : 0,
                    OnOperationChanged,
                    ElementBounds.Fixed(730, 468, 138, 28),
                    "talentOperation")
                .AddTextInput(ElementBounds.Fixed(884, 468, 138, 28), text => amount = text ?? "", CairoFont.WhiteSmallishText(), "talentAmount")
                .AddButton("Add / Replace", AddModifier, ElementBounds.Fixed(730, 505, 220, 30), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddButton("Save", Save, ElementBounds.Fixed(730, 644, 72, 28), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddButton("Save As New", SaveAs, ElementBounds.Fixed(808, 644, 112, 28), CenteredButtonFont(), EnumButtonStyle.Small)
                .AddButton("Close", () => TryClose(), ElementBounds.Fixed(926, 644, 96, 28), CenteredButtonFont(), EnumButtonStyle.Small)
            .EndChildElements();
        SingleComposer = composer.Compose();
        SingleComposer.GetTextInput("talentStatSearch")?.SetValue(query, false);
        SingleComposer.GetTextInput("talentStatSearch")?.SetPlaceHolderText("name, code, category");
        SingleComposer.GetTextInput("talentAmount")?.SetValue(amount, false);
        SingleComposer.GetTextInput("talentTreeName")?.SetValue(treeName, false);
        SingleComposer.GetTextInput("talentTreeName")?.SetPlaceHolderText("tree display name");
        SingleComposer.GetTextInput("talentNodeName")?.SetPlaceHolderText("blank clears the name");
    }

    private void OnSavedTreeChanged(string code, bool selected)
    {
        if (!selected || string.Equals(code, packet.SelectedSavedTreeCode, StringComparison.OrdinalIgnoreCase)) return;
        if (!packet.Dirty)
        {
            openSavedTree(code);
            return;
        }

        var confirm = new GuiDialogConfirm(capi, "Discard the unsaved draft and open the selected saved tree?", accepted =>
        {
            if (accepted) openSavedTree(code);
            else RestoreSavedTreeSelection();
        });
        confirm.TryOpen();
    }
    private void OnStatChanged(string code, bool selected) { if (selected) selectedStat = code; }
    private void OnOperationChanged(string code, bool selected) { if (selected) operation = code; }
    private bool ApplyFilter()
    {
        selectedNodeCode = editorElement?.SelectedNodeCode ?? selectedNodeCode;
        TalentEditorStatPacket[] filtered = packet.Stats
            .Where(stat => string.IsNullOrWhiteSpace(query)
                || stat.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                || stat.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || stat.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(stat => stat.Category).ThenBy(stat => stat.Name).ToArray();
        if (filtered.Length == 0) filtered = packet.Stats;
        string[] codes = filtered.Select(stat => stat.Code).ToArray();
        string[] names = filtered.Select(stat => stat.Name + "  [" + stat.Category + "]").ToArray();
        selectedStat = codes.FirstOrDefault() ?? "";
        GuiElementDropDown? dropdown = SingleComposer?.GetDropDown("talentStat");
        dropdown?.SetList(codes, names);
        dropdown?.SetSelectedIndex(0);
        return true;
    }
    private bool AddModifier()
    {
        if (editorElement != null && float.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            selectedNodeCode = editorElement.SelectedNodeCode;
            addModifier(selectedNodeCode, selectedStat, operation, value);
        }
        return true;
    }
    private bool Save() { save(); return true; }
    private bool SaveAs()
    {
        if (!string.IsNullOrWhiteSpace(treeName)) saveAs(treeName);
        return true;
    }
    private bool OpenTemplatePicker()
    {
        var picker = new GuiDialogVRPGTalentTemplatePicker(capi, packet.TemplateCodes, packet.TemplateNames, selectTemplate);
        picker.TryOpen();
        return true;
    }
    private bool DeleteSavedTree()
    {
        string code = packet.SelectedSavedTreeCode;
        if (string.IsNullOrWhiteSpace(code)) return true;
        if (string.Equals(code, packet.ActiveTreeCode, StringComparison.OrdinalIgnoreCase))
        {
            deleteSavedTree(code);
            return true;
        }

        string name = packet.Tree.TreeName;
        var confirm = new GuiDialogConfirm(capi, "Delete the saved talent tree '" + name + "'? This cannot be undone.", accepted =>
        {
            if (accepted) deleteSavedTree(code);
        });
        confirm.TryOpen();
        return true;
    }
    private bool RenameNode()
    {
        selectedNodeCode = editorElement?.SelectedNodeCode ?? selectedNodeCode;
        if (!string.IsNullOrWhiteSpace(selectedNodeCode)) renameNode(selectedNodeCode, nodeName);
        return true;
    }
    private bool RenameTree()
    {
        if (!string.IsNullOrWhiteSpace(treeName)) renameTree(treeName);
        return true;
    }

    private void OnNodeSelected(TalentTreeNodePacket? node)
    {
        selectedNodeCode = node?.Code ?? "";
        nodeName = node?.Name ?? "";
        SingleComposer?.GetTextInput("talentNodeName")?.SetValue(nodeName, false);
    }

    private void RestoreSavedTreeSelection()
    {
        int index = Math.Max(0, Array.FindIndex(packet.SavedTreeCodes, code => string.Equals(code, packet.SelectedSavedTreeCode, StringComparison.OrdinalIgnoreCase)));
        SingleComposer?.GetDropDown("talentSavedTree")?.SetSelectedIndex(index);
    }

    private static CairoFont CenteredButtonFont()
    {
        return CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);
    }
}
