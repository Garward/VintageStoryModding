using VRPG.Client.Interactions;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class CombatInteractionCandidateResolverTests
{
    [Fact]
    public void ChoosesHighestPriorityEligibleDefinition()
    {
        InteractionDefinition low = Definition("vrpg:inspect", 10);
        InteractionDefinition high = Definition("vrpg:activate", 100);

        InteractionDefinition? selected = CombatInteractionCandidateResolver.Resolve(
            new[] { low, high },
            new[] { low.Code, high.Code },
            new[] { "mechanism" },
            "adventure",
            distance: 3,
            hasLineOfSight: true);

        Assert.Same(high, selected);
    }

    [Theory]
    [InlineData("home", 3, true)]
    [InlineData("adventure", 5, true)]
    [InlineData("adventure", 3, false)]
    public void RejectsWrongContextRangeOrLineOfSight(string context, double distance, bool lineOfSight)
    {
        InteractionDefinition definition = Definition("vrpg:activate", 100);

        InteractionDefinition? selected = CombatInteractionCandidateResolver.Resolve(
            new[] { definition },
            new[] { definition.Code },
            new[] { "mechanism" },
            context,
            distance,
            lineOfSight);

        Assert.Null(selected);
    }

    [Fact]
    public void RequiresEveryDeclaredTargetTag()
    {
        InteractionDefinition definition = Definition("vrpg:activate", 100);
        definition.TargetTags = new[] { "mechanism", "ancient" };

        Assert.Null(CombatInteractionCandidateResolver.Resolve(
            new[] { definition },
            new[] { definition.Code },
            new[] { "mechanism" },
            "adventure",
            3,
            true));
    }

    private static InteractionDefinition Definition(string code, int priority)
    {
        return new InteractionDefinition
        {
            Code = code,
            Name = code,
            PromptKey = "vrpg-interaction-prompt-activate",
            HandlerCode = "vrpg:entity-behavior",
            TargetTags = new[] { "mechanism" },
            AllowedContexts = new[] { "adventure" },
            Range = 4,
            RequireLineOfSight = true,
            Priority = priority
        };
    }
}
