using System;
using System.Linq;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Class selection and authoritative skill assignment state.</summary>
public sealed class SkillsPage
{
    private readonly HubPageClientService hub;
    private HubClassPacket[] classes = Array.Empty<HubClassPacket>();
    public HubClassPacket[] Classes => classes;
    public HubSkillPacket[] Skills { get; private set; } = Array.Empty<HubSkillPacket>();
    public bool Pending { get; private set; }
    public string Feedback { get; private set; } = "";
    public bool FeedbackError { get; private set; }
    public int SelectedClassIndex { get; private set; }
    public int SelectedSkillIndex { get; private set; }
    public HubClassPacket? SelectedClass => classes.ElementAtOrDefault(SelectedClassIndex);
    public HubSkillPacket? SelectedSkill => Skills.ElementAtOrDefault(SelectedSkillIndex);
    public int VisibleSlots => hub.Options.VisibleSkillSlots;
    public bool CanEquip => !Pending && !hub.Stats.Pending && !hub.Talents.Pending && !hub.Loading && SelectedSkill is { LearnedLevel: > 0, Code.Length: > 0 };
    public SkillsPage(HubPageClientService hub) => this.hub = hub;
    public void SelectClass(int index)
    {
        SelectedClassIndex = Math.Clamp(index, 0, Math.Max(0, classes.Length - 1));
        Skills = SelectedClass?.Skills.OrderBy(skill => skill.RequiredLevel).ThenBy(skill => skill.Name, StringComparer.CurrentCulture).ToArray() ?? Array.Empty<HubSkillPacket>();
        SelectSkill(0);
    }
    public void SelectSkill(int index)
    {
        SelectedSkillIndex = Math.Clamp(index, 0, Math.Max(0, Skills.Length - 1));
        hub.State = hub.State with { SelectedClass = SelectedClass?.Code ?? "", SelectedSkill = SelectedSkill?.Code ?? "" };
    }
    public void EquipSelected(int slot)
    {
        if (!CanEquip || slot < 0 || slot >= VisibleSlots) return;
        var selected = SelectedSkill!;
        Pending = true; Feedback = ""; FeedbackError = false;
        try { hub.Actions.EquipSkill(slot + 1, selected.EquippedSlot == slot + 1 ? "" : selected.Code); }
        catch (Exception error) { Pending = false; FeedbackError = true; Feedback = Shell.PlayerMenuShell.T("action-unavailable"); hub.Actions.ReportError(error); }
    }
    internal void Receive(OpenHubPacket packet)
    {
        string cls = hub.State.SelectedClass, skill = hub.State.SelectedSkill;
        int oldClass = SelectedClassIndex, oldSkill = SelectedSkillIndex;
        classes = packet.Classes;
        int classIndex = Array.FindIndex(classes, entry => entry.Code == cls);
        SelectClass(classIndex >= 0 ? classIndex : oldClass);
        int skillIndex = Array.FindIndex(Skills, entry => entry.Code == skill);
        SelectSkill(skillIndex >= 0 ? skillIndex : oldSkill);
        if (Pending) { Pending = false; Feedback = HubPageFeedback.Present(packet.FeedbackMessage, packet.FeedbackError, PlayerMenuPage.Skills); FeedbackError = packet.FeedbackError; }
    }
    internal void Reset()
    {
        classes = Array.Empty<HubClassPacket>(); Skills = Array.Empty<HubSkillPacket>();
        SelectedClassIndex = 0; SelectedSkillIndex = 0; Pending = false; Feedback = ""; FeedbackError = false;
    }
}
