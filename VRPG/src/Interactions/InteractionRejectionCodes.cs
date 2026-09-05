namespace VRPG.Interactions;

/// <summary>Stable protocol codes; localization belongs to the client presentation boundary.</summary>
public static class InteractionRejectionCodes
{
    public static string From(InteractionRejection rejection)
    {
        return rejection switch
        {
            InteractionRejection.None => "",
            InteractionRejection.Unavailable => "unavailable",
            InteractionRejection.InvalidRequest => "invalid-request",
            InteractionRejection.PlayerUnavailable => "player-unavailable",
            InteractionRejection.TargetMissing => "target-missing",
            InteractionRejection.TargetUnavailable => "target-unavailable",
            InteractionRejection.WrongContext => "wrong-context",
            InteractionRejection.TooFar => "too-far",
            InteractionRejection.Obstructed => "obstructed",
            InteractionRejection.NotOffered => "not-offered",
            InteractionRejection.PermissionDenied => "permission-denied",
            InteractionRejection.CoolingDown => "cooling-down",
            InteractionRejection.HandlerUnavailable => "handler-unavailable",
            _ => "unavailable"
        };
    }
}
