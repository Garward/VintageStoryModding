using VRPG.Interactions;
using Xunit;

namespace VRPG.Tests;

public sealed class InteractionPolicyTests
{
    [Fact]
    public void AcceptsOnlyWhenEveryAuthoritativeFactPasses()
    {
        Assert.Equal(InteractionRejection.None, InteractionPolicy.Evaluate(Allowed()));
    }

    [Theory]
    [InlineData(false, true, InteractionRejection.TooFar)]
    [InlineData(true, false, InteractionRejection.Obstructed)]
    public void ReportsTheFirstActionableSpatialFailure(
        bool withinRange,
        bool hasLineOfSight,
        InteractionRejection expected)
    {
        InteractionPolicyFacts facts = Allowed() with
        {
            WithinRange = withinRange,
            HasLineOfSight = hasLineOfSight
        };

        Assert.Equal(expected, InteractionPolicy.Evaluate(facts));
    }

    [Fact]
    public void RejectsAnInteractionTheEntityDoesNotOffer()
    {
        InteractionPolicyFacts facts = Allowed() with { InteractionOffered = false };

        Assert.Equal(InteractionRejection.NotOffered, InteractionPolicy.Evaluate(facts));
    }

    private static InteractionPolicyFacts Allowed()
    {
        return new InteractionPolicyFacts(
            FeatureEnabled: true,
            RequestValid: true,
            PlayerAvailable: true,
            TargetExists: true,
            TargetAvailable: true,
            ContextAllowed: true,
            SameDimension: true,
            WithinRange: true,
            HasLineOfSight: true,
            InteractionOffered: true,
            HasPermission: true,
            CooldownReady: true,
            HandlerRegistered: true);
    }
}
