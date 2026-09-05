namespace VRPG.Interactions;

public enum InteractionRejection
{
    None,
    Unavailable,
    InvalidRequest,
    PlayerUnavailable,
    TargetMissing,
    TargetUnavailable,
    WrongContext,
    TooFar,
    Obstructed,
    NotOffered,
    PermissionDenied,
    CoolingDown,
    HandlerUnavailable
}

/// <summary>Server-resolved facts used to admit one interaction request.</summary>
public readonly record struct InteractionPolicyFacts(
    bool FeatureEnabled,
    bool RequestValid,
    bool PlayerAvailable,
    bool TargetExists,
    bool TargetAvailable,
    bool ContextAllowed,
    bool SameDimension,
    bool WithinRange,
    bool HasLineOfSight,
    bool InteractionOffered,
    bool HasPermission,
    bool CooldownReady,
    bool HandlerRegistered);

public static class InteractionPolicy
{
    public static InteractionRejection Evaluate(in InteractionPolicyFacts facts)
    {
        if (!facts.FeatureEnabled) return InteractionRejection.Unavailable;
        if (!facts.RequestValid) return InteractionRejection.InvalidRequest;
        if (!facts.PlayerAvailable) return InteractionRejection.PlayerUnavailable;
        if (!facts.TargetExists) return InteractionRejection.TargetMissing;
        if (!facts.TargetAvailable) return InteractionRejection.TargetUnavailable;
        if (!facts.ContextAllowed || !facts.SameDimension) return InteractionRejection.WrongContext;
        if (!facts.WithinRange) return InteractionRejection.TooFar;
        if (!facts.HasLineOfSight) return InteractionRejection.Obstructed;
        if (!facts.InteractionOffered) return InteractionRejection.NotOffered;
        if (!facts.HasPermission) return InteractionRejection.PermissionDenied;
        if (!facts.CooldownReady) return InteractionRejection.CoolingDown;
        if (!facts.HandlerRegistered) return InteractionRejection.HandlerUnavailable;
        return InteractionRejection.None;
    }
}
