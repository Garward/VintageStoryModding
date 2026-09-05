using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VRPG.Modules.Rpg.Onboarding;

public enum FirstRunOnboardingStage
{
    Appearance,
    Starter,
    Welcome,
    SafeRelease,
    Complete
}

/// <summary>Durable, world-owned progress through first character setup.</summary>
public sealed class FirstRunOnboardingState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonConverter(typeof(StringEnumConverter))]
    public FirstRunOnboardingStage Stage { get; set; } = FirstRunOnboardingStage.Appearance;

    public bool AppearanceConfirmed { get; set; }
    public string StarterCode { get; set; } = "";
    public string StarterTransactionId { get; set; } = "";
    public bool GuideAcknowledged { get; set; }
    public string SafeArrivalId { get; set; } = "";
    public bool SafeReleaseValidated { get; set; }

    public void Normalize()
    {
        if (SchemaVersion <= 0)
        {
            SchemaVersion = CurrentSchemaVersion;
        }

        if (!Enum.IsDefined(Stage))
        {
            Stage = FirstRunOnboardingStage.Appearance;
        }

        StarterCode = (StarterCode ?? "").Trim();
        StarterTransactionId = (StarterTransactionId ?? "").Trim();
        SafeArrivalId = (SafeArrivalId ?? "").Trim();

        if (Stage == FirstRunOnboardingStage.Complete)
        {
            AppearanceConfirmed = true;
            GuideAcknowledged = true;
            SafeReleaseValidated = true;
        }
    }
}
