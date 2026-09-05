namespace VRPG.Client.Interactions;

public enum InteractionPromptPhase
{
    Hidden,
    Ready,
    Pending,
    Success,
    Rejected
}

public readonly record struct InteractionPromptState(
    InteractionPromptPhase Phase,
    string Action,
    string Target,
    string Message)
{
    public static InteractionPromptState Hidden => new(InteractionPromptPhase.Hidden, "", "", "");
}
