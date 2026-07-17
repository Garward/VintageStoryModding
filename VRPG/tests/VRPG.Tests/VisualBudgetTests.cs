using VRPG.Client.Visuals;
using Xunit;

namespace VRPG.Tests;

public sealed class VisualBudgetTests
{
    private static VisualBudget Loaded(float load, bool ownFirst = true)
    {
        var budget = new VisualBudget(particlesPerSecond: 1000) { OwnFirst = ownFirst };
        budget.Record(load * 1000f, nowMs: 500);
        return budget;
    }

    [Fact]
    public void NoLoadMeansFullFidelityForEveryone()
    {
        VisualBudget budget = Loaded(0f);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Cosmetic, 500));
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Own, 500));
    }

    [Fact]
    public void OwnFirstDegradesCosmeticBeforeOthersBeforeOwn()
    {
        VisualBudget budget = Loaded(0.6f);
        Assert.Equal(0f, budget.QuantityScale(VisualPriority.Cosmetic, 500));
        Assert.Equal(0.8f, budget.QuantityScale(VisualPriority.Others, 500), 2);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Own, 500));

        VisualBudget heavier = Loaded(0.9f);
        Assert.Equal(0.5f, heavier.QuantityScale(VisualPriority.Own, 500), 2);
    }

    [Fact]
    public void CriticalNeverDegrades()
    {
        VisualBudget budget = Loaded(5f);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Critical, 500));
    }

    [Fact]
    public void UniformPolicyScalesAllNonCriticalEqually()
    {
        VisualBudget budget = Loaded(0.4f, ownFirst: false);
        Assert.Equal(0.6f, budget.QuantityScale(VisualPriority.Cosmetic, 500), 2);
        Assert.Equal(0.6f, budget.QuantityScale(VisualPriority.Others, 500), 2);
        Assert.Equal(0.6f, budget.QuantityScale(VisualPriority.Own, 500), 2);
    }

    [Fact]
    public void WindowRollsOverAndLoadResets()
    {
        var budget = new VisualBudget(1000) { OwnFirst = true };
        budget.Record(900f, nowMs: 100);
        Assert.True(budget.QuantityScale(VisualPriority.Cosmetic, 100) < 0.01f);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Cosmetic, 1300));
    }
}
