using System;
using VRPG.Config;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Hub;

public enum OptionsCategory { Notifications, ActionBar, CombatHud, CombatVisuals }

/// <summary>Existing settings actions; ownership and persistence stay with their original callbacks.</summary>
public sealed class OptionsPage
{
    private readonly HubPageClientService hub;
    private HubOptionsPacket options = new();
    private HubPageActions Actions => hub.Actions;
    public OptionsCategory Category => Enum.TryParse<OptionsCategory>(hub.State.OptionsCategory, out var category) ? category : OptionsCategory.Notifications;
    public bool CooldownNotifications => options.ShowCooldownNotifications;
    public bool ResourceNotifications => options.ShowResourceNotifications;
    public bool HotbarLocked => options.SkillHotbarLocked;
    public bool AutoThrow => options.HoldToRepeatChargedSkills;
    public int VisibleSkillSlots => Math.Clamp(options.SkillHotbarSlots, 4, 8);
    public bool OwnEffectsFirst => !string.Equals(Actions.CombatVisuals.DegradationPolicy, "uniform", StringComparison.OrdinalIgnoreCase);
    public OptionsPage(HubPageClientService hub) => this.hub = hub;
    public void SelectCategory(OptionsCategory category) { if (Enum.IsDefined(category)) hub.State = hub.State with { OptionsCategory = category.ToString() }; }
    public void ToggleCooldownNotifications() => Change(() => { options.ShowCooldownNotifications = !CooldownNotifications; Actions.UpdateOptions(CooldownNotifications, ResourceNotifications); });
    public void ToggleResourceNotifications() => Change(() => { options.ShowResourceNotifications = !ResourceNotifications; Actions.UpdateOptions(CooldownNotifications, ResourceNotifications); });
    public void ToggleHotbarLock() => Change(() => { options.SkillHotbarLocked = !HotbarLocked; Actions.UpdateHotbar(HotbarLocked, VisibleSkillSlots); });
    public void ChangeSkillSlots(int direction) => Change(() => { options.SkillHotbarSlots = Math.Clamp(VisibleSkillSlots + Math.Sign(direction), 4, 8); Actions.UpdateHotbar(HotbarLocked, VisibleSkillSlots); });
    public void ToggleAutoThrow() => Change(() => { options.HoldToRepeatChargedSkills = !AutoThrow; Actions.UpdateAutoThrow(AutoThrow); });
    public void ToggleResourceHudLock() => Change(() => Actions.SetResourceHudLocked(!Actions.ResourceHud.Locked));
    public bool CanResizeWidth(int direction) => direction < 0 ? Actions.ResourceHud.Width > RpgHudConfig.MinimumWidth : Actions.ResourceHud.Width < RpgHudConfig.MaximumWidth;
    public bool CanResizeHeight(int direction) => direction < 0 ? Actions.ResourceHud.BarHeight > RpgHudConfig.MinimumBarHeight : Actions.ResourceHud.BarHeight < RpgHudConfig.MaximumBarHeight;
    public void ChangeResourceWidth(int direction) { if (direction != 0 && CanResizeWidth(direction)) Change(() => Actions.ResizeResourceHud(Math.Sign(direction) * 25, 0)); }
    public void ChangeResourceHeight(int direction) { if (direction != 0 && CanResizeHeight(direction)) Change(() => Actions.ResizeResourceHud(0, Math.Sign(direction) * 2)); }
    public void ResetResourceHud() => Change(Actions.ResetResourceHud);
    public void ToggleCombatText() => Visual(() => Actions.CombatVisuals.CombatTextEnabled = !Actions.CombatVisuals.CombatTextEnabled);
    public void ToggleDamageNumbers() => Visual(() => Actions.CombatVisuals.DamageNumbers = !Actions.CombatVisuals.DamageNumbers);
    public void ToggleEventWords() => Visual(() => Actions.CombatVisuals.EventWords = !Actions.CombatVisuals.EventWords);
    public void ToggleMergeNumbers() => Visual(() => Actions.CombatVisuals.MergeNumbers = !Actions.CombatVisuals.MergeNumbers);
    public void ToggleStatusAuras() => Visual(() => Actions.CombatVisuals.StatusAuras = !Actions.CombatVisuals.StatusAuras);
    public void ToggleDegradationPolicy() => Visual(() => Actions.CombatVisuals.DegradationPolicy = OwnEffectsFirst ? "uniform" : "own-first");
    public void CycleTelegraphOpacity() => Visual(() => Actions.CombatVisuals.TelegraphOpacity = Actions.CombatVisuals.TelegraphOpacity > .75f ? .5f : Actions.CombatVisuals.TelegraphOpacity > .35f ? .25f : 1f);
    public void CycleIntensity() => Visual(() => Actions.CombatVisuals.Intensity = Actions.CombatVisuals.Intensity > .9f ? .75f : Actions.CombatVisuals.Intensity > .6f ? .5f : 1f);
    public string Error { get; private set; } = "";
    private void Change(Action action)
    {
        if (!hub.HasSnapshot || hub.Loading) return;
        try { action(); Error = ""; }
        catch (Exception error) { Error = Shell.PlayerMenuShell.T("action-unavailable"); Actions.ReportError(error); }
    }
    private void Visual(Action action) => Change(() => { action(); Actions.PersistClientConfig(); });
    internal void Receive(HubOptionsPacket next) { options = next; Error = ""; }
    internal void Reset() { options = new(); Error = ""; }
}
