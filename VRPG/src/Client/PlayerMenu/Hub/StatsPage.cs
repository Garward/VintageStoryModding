using System;
using System.Linq;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Base attribute allocation state retained across shell compositions.</summary>
public sealed class StatsPage
{
    private readonly HubPageClientService hub;
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
        var packet = hub.Packet;
        return !Pending && !hub.Skills.Pending && !hub.Loading && IsSupported(code) && delta is -1 or 1
            && packet?.Resources is { } resources && resources.CombatLockRemainingSeconds <= 0
            && (delta > 0 ? resources.UnspentStatPoints > 0 : Value(packet, code) > 0);
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
        if (!Pending) return;
        Pending = false; Feedback = packet.FeedbackMessage; FeedbackError = packet.FeedbackError;
    }
    internal void Reset() { Pending = false; Feedback = ""; FeedbackCode = ""; FeedbackError = false; }
}
