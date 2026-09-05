using System;

namespace VRPG.Modules.Rpg.Onboarding;

/// <summary>Applies explicit, retry-safe transitions to persisted onboarding state.</summary>
public static class FirstRunOnboardingFlow
{
    public static bool ConfirmAppearance(FirstRunOnboardingState state)
    {
        Normalize(state);
        if (state.Stage > FirstRunOnboardingStage.Starter)
        {
            return state.AppearanceConfirmed;
        }

        state.AppearanceConfirmed = true;
        state.Stage = FirstRunOnboardingStage.Starter;
        return true;
    }

    public static bool CommitStarter(
        FirstRunOnboardingState state,
        string starterCode,
        string transactionId)
    {
        Normalize(state);
        string code = Required(starterCode, nameof(starterCode));
        string transaction = Required(transactionId, nameof(transactionId));

        if (!string.IsNullOrEmpty(state.StarterTransactionId))
        {
            return string.Equals(state.StarterTransactionId, transaction, StringComparison.Ordinal)
                && string.Equals(state.StarterCode, code, StringComparison.OrdinalIgnoreCase);
        }

        if (!state.AppearanceConfirmed || state.Stage != FirstRunOnboardingStage.Starter)
        {
            return false;
        }

        state.StarterCode = code;
        state.StarterTransactionId = transaction;
        state.Stage = FirstRunOnboardingStage.Welcome;
        return true;
    }

    public static bool SetSafeArrival(FirstRunOnboardingState state, string safeArrivalId)
    {
        Normalize(state);
        if (state.Stage == FirstRunOnboardingStage.Complete)
        {
            return false;
        }

        state.SafeArrivalId = Required(safeArrivalId, nameof(safeArrivalId));
        state.SafeReleaseValidated = false;
        return true;
    }

    public static bool AcknowledgeGuide(FirstRunOnboardingState state)
    {
        Normalize(state);
        if (state.Stage < FirstRunOnboardingStage.Welcome)
        {
            return false;
        }

        state.GuideAcknowledged = true;
        if (state.Stage != FirstRunOnboardingStage.Complete)
        {
            state.Stage = FirstRunOnboardingStage.SafeRelease;
        }
        return true;
    }

    public static bool TryCompleteSafeRelease(
        FirstRunOnboardingState state,
        bool safeReleaseReady)
    {
        Normalize(state);
        if (state.Stage == FirstRunOnboardingStage.Complete)
        {
            return true;
        }

        state.SafeReleaseValidated = safeReleaseReady;
        if (!state.GuideAcknowledged
            || !safeReleaseReady
            || string.IsNullOrEmpty(state.SafeArrivalId)
            || state.Stage != FirstRunOnboardingStage.SafeRelease)
        {
            return false;
        }

        state.Stage = FirstRunOnboardingStage.Complete;
        return true;
    }

    private static void Normalize(FirstRunOnboardingState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Normalize();
    }

    private static string Required(string value, string parameterName)
    {
        string normalized = (value ?? "").Trim();
        return normalized.Length > 0
            ? normalized
            : throw new ArgumentException("A value is required.", parameterName);
    }
}
