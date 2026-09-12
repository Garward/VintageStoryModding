using System;
using System.Linq;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Base attribute allocation state retained across shell compositions.</summary>
public sealed class StatsPage
{
    private readonly HubPageClientService hub;
    private int strength, dexterity, intelligence, availablePoints;
    private bool hasResources, combatLocked;
    public bool Pending { get; private set; }
    public bool FeedbackError { get; private set; }
    public string Feedback { get; private set; } = "";
    public string FeedbackCode { get; private set; } = "";
    public StatsPage(HubPageClientService hub) => this.hub = hub;
    public static bool IsSupported(string code) => code is "strength" or "dexterity" or "intelligence";
    public static int Value(OpenHubPacket packet, string code) => Math.Max(0,
        packet.BaseStats.FirstOrDefault(stat => stat.Code == "vrpg:" + code)?.Value
        ?? packet.BaseStats.FirstOrDefault(stat => stat.Code == code)?.Value ?? 0);
    public bool CanAdjust(string code, int delta)
    {
        int value = code switch { "strength" => strength, "dexterity" => dexterity, "intelligence" => intelligence, _ => 0 };
        return !Pending && !hub.Skills.Pending && !hub.Loading && hub.HasSnapshot && hasResources && !combatLocked
            && IsSupported(code) && delta is -1 or 1 && (delta > 0 ? availablePoints > 0 : value > 0);
    }
    public void Adjust(string code, int delta)
    {
        if (!CanAdjust(code, delta)) return;
        Pending = true; FeedbackCode = code; Feedback = ""; FeedbackError = false;
        try { hub.Actions.AdjustStat(code, delta); }
        catch (Exception error) { Pending = false; FeedbackError = true; Feedback = Shell.PlayerMenuShell.T("action-unavailable"); hub.Actions.ReportError(error); }
    }
    internal void Receive(OpenHubPacket packet)
    {
        strength = Value(packet, "strength"); dexterity = Value(packet, "dexterity"); intelligence = Value(packet, "intelligence");
        UpdateResources(packet.Resources);
        if (!Pending) return;
        Pending = false; Feedback = HubPageFeedback.Present(packet.FeedbackMessage, packet.FeedbackError, PlayerMenuPage.Stats); FeedbackError = packet.FeedbackError;
    }
    internal void UpdateResources(RpgResourcePacket? resources)
    {
        hasResources = resources != null;
        availablePoints = resources?.UnspentStatPoints ?? 0;
        combatLocked = resources?.CombatLockRemainingSeconds > 0;
    }
    internal void Reset()
    {
        Pending = false; Feedback = ""; FeedbackCode = ""; FeedbackError = false;
        strength = dexterity = intelligence = 0; UpdateResources(null);
    }
}
