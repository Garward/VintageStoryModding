namespace VRPG.Client.PlayerMenu.Components;

public readonly record struct ContextMessage(string LocalizationKey, MenuControlState State)
{
    public bool PreventRepeat => State == MenuControlState.Pending;
}
