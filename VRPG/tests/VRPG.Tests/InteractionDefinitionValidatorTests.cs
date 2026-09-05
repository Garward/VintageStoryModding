using System;
using VRPG.Data;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class InteractionDefinitionValidatorTests
{
    [Fact]
    public void AcceptsACompleteNamespacedInteraction()
    {
        InteractionDefinition definition = Valid();

        Assert.Empty(InteractionDefinitionValidator.CollectErrors(new[] { definition }));
    }

    [Fact]
    public void RejectsUnknownContextAndUnsafeRange()
    {
        InteractionDefinition definition = Valid();
        definition.AllowedContexts = new[] { "survival" };
        definition.Range = 64f;

        var errors = InteractionDefinitionValidator.CollectErrors(new[] { definition });

        Assert.Contains(errors, error => error.Contains("unknown context", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("range", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsDuplicateCodesIgnoringCase()
    {
        InteractionDefinition first = Valid();
        InteractionDefinition second = Valid();
        second.Code = "VRPG:OPEN_MECHANISM";

        var errors = InteractionDefinitionValidator.CollectErrors(new[] { first, second });

        Assert.Contains(errors, error => error.Contains("duplicate interaction code", StringComparison.Ordinal));
    }

    private static InteractionDefinition Valid()
    {
        return new InteractionDefinition
        {
            Code = "vrpg:open_mechanism",
            Name = "Open mechanism",
            PromptKey = "vrpg-interaction-prompt-open",
            HandlerCode = "vrpg:mechanism",
            AllowedContexts = new[] { "adventure" },
            Range = 4f
        };
    }
}
