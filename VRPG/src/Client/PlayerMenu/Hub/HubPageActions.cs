using System;
using VRPG.Client.Visuals;
using VRPG.Config;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Existing authoritative actions and local configuration destinations used by Hub pages.</summary>
public sealed record HubPageActions
{
    public Action<string, int> AdjustStat { get; init; } = (_, _) => { };
    public Action<bool, bool> UpdateOptions { get; init; } = (_, _) => { };
    public Action<bool, int> UpdateHotbar { get; init; } = (_, _) => { };
    public Action<bool> UpdateAutoThrow { get; init; } = _ => { };
    public RpgHudConfig ResourceHud { get; init; } = new();
    public Action<bool> SetResourceHudLocked { get; init; } = _ => { };
    public Action<int, int> ResizeResourceHud { get; init; } = (_, _) => { };
    public Action ResetResourceHud { get; init; } = () => { };
    public Action<int, string> EquipSkill { get; init; } = (_, _) => { };
    public Action<string[], string[]> ApplyTalentPlan { get; init; } = (_, _) => { };
    public Action OpenTalentEditor { get; init; } = () => { };
    public CombatVisualsConfig CombatVisuals { get; init; } = new();
    public Action PersistClientConfig { get; init; } = () => { };
    public Action<TalentTreeRequestPacket> RequestTalentTree { get; init; } = _ => { };
    public Action<HubOptionsPacket> ApplyClientOptions { get; init; } = _ => { };
    public Action<Exception> ReportError { get; init; } = _ => { };
}
