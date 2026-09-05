using System;

namespace VRPG.Realms.Onboarding;

public enum OnboardingActivity
{
    HostileSpawn, AmbientSpawn, Encounter, Hazard, Damage, Reward, Loot, Pickup,
    Trade, Build, Break, Liquid, Fire, Combat, PublicEntry, Travel, Creator, ExitInteraction
}

/// <summary>Single restrictive policy for the private arrival instance, including after guide acknowledgement.</summary>
public static class OnboardingInstancePolicy
{
    public static bool Allows(string ownerUid, string actorUid, OnboardingActivity activity) =>
        !string.IsNullOrWhiteSpace(ownerUid)
        && string.Equals(ownerUid, actorUid, StringComparison.Ordinal)
        && activity is OnboardingActivity.Creator or OnboardingActivity.ExitInteraction;
}
