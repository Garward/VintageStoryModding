using System;
using VRPG.Interactions;
using Xunit;

namespace VRPG.Tests;

public sealed class CombatInteractionHandlerRegistryTests
{
    [Fact]
    public void ResolvesHandlersCaseInsensitively()
    {
        var registry = new CombatInteractionHandlerRegistry();
        registry.Register(new TestHandler("vrpg:mechanism"));

        Assert.True(registry.TryGet("VRPG:MECHANISM", out ICombatInteractionHandler? handler));
        Assert.NotNull(handler);
    }

    [Fact]
    public void RejectsDuplicateHandlerOwnership()
    {
        var registry = new CombatInteractionHandlerRegistry();
        registry.Register(new TestHandler("vrpg:mechanism"));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new TestHandler("VRPG:MECHANISM")));
    }

    private sealed class TestHandler : ICombatInteractionHandler
    {
        public TestHandler(string code) => Code = code;
        public string Code { get; }
        public CombatInteractionHandlerResult Execute(in CombatInteractionHandlerContext context) =>
            CombatInteractionHandlerResult.Accepted();
    }
}
