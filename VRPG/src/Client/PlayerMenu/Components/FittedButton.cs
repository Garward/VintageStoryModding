namespace VRPG.Client.PlayerMenu.Components;

public enum MenuControlState { Default, Hover, Focused, Pressed, Selected, Disabled, Locked, Pending, Success, Rejected }

public readonly record struct FittedButton(string Intent, string LabelKey, MenuControlState State, string ReasonKey = "")
{
    public bool CanActivate => State is not (MenuControlState.Disabled or MenuControlState.Locked or MenuControlState.Pending);
    public double PressOffset(bool reducedMotion) => State == MenuControlState.Pressed && !reducedMotion ? 1 : 0;
}
