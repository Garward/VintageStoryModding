using VRPG.Modules.Rpg.Onboarding;
using Xunit;

namespace VRPG.Tests;

public sealed class FirstRunOnboardingFlowTests
{
    [Fact]
    public void GuideAcknowledgementCannotReleasePlayerWithoutValidatedSafeState()
    {
        FirstRunOnboardingState state = ReadyForWelcome();
        Assert.True(FirstRunOnboardingFlow.SetSafeArrival(state, "onboarding:player-one"));

        Assert.True(FirstRunOnboardingFlow.AcknowledgeGuide(state));

        Assert.Equal(FirstRunOnboardingStage.SafeRelease, state.Stage);
        Assert.True(state.GuideAcknowledged);
        Assert.False(FirstRunOnboardingFlow.TryCompleteSafeRelease(state, false));
        Assert.NotEqual(FirstRunOnboardingStage.Complete, state.Stage);
    }

    [Fact]
    public void SafeReleaseRequiresAcknowledgementAndPersistedArrival()
    {
        FirstRunOnboardingState state = ReadyForWelcome();

        Assert.False(FirstRunOnboardingFlow.TryCompleteSafeRelease(state, true));
        Assert.True(FirstRunOnboardingFlow.AcknowledgeGuide(state));
        Assert.False(FirstRunOnboardingFlow.TryCompleteSafeRelease(state, true));
        Assert.True(FirstRunOnboardingFlow.SetSafeArrival(state, "onboarding:player-one"));
        Assert.True(FirstRunOnboardingFlow.TryCompleteSafeRelease(state, true));
        Assert.Equal(FirstRunOnboardingStage.Complete, state.Stage);
    }

    [Fact]
    public void RepeatingStarterCommitCannotDuplicateOrChangeGrant()
    {
        var state = new FirstRunOnboardingState();
        Assert.True(FirstRunOnboardingFlow.ConfirmAppearance(state));

        Assert.True(FirstRunOnboardingFlow.CommitStarter(state, "vrpg:scrapper", "grant-1"));
        Assert.True(FirstRunOnboardingFlow.CommitStarter(state, "vrpg:scrapper", "grant-1"));
        Assert.False(FirstRunOnboardingFlow.CommitStarter(state, "vrpg:smith", "grant-2"));

        Assert.Equal("vrpg:scrapper", state.StarterCode);
        Assert.Equal("grant-1", state.StarterTransactionId);
        Assert.Equal(FirstRunOnboardingStage.Welcome, state.Stage);
    }

    [Fact]
    public void OnboardingProgressRoundTripsWithWorldPlayerState()
    {
        var storage = new MemoryStorage();
        var first = new VRPG.Modules.Rpg.Players.RpgPlayerStore();
        first.Load(storage);
        FirstRunOnboardingState state = first.GetOrCreate("player-one").Onboarding;
        Assert.True(FirstRunOnboardingFlow.ConfirmAppearance(state));
        Assert.True(FirstRunOnboardingFlow.CommitStarter(state, "vrpg:scrapper", "grant-1"));
        Assert.True(FirstRunOnboardingFlow.SetSafeArrival(state, "onboarding:player-one"));
        first.Save();

        var restored = new VRPG.Modules.Rpg.Players.RpgPlayerStore();
        restored.Load(storage);
        FirstRunOnboardingState loaded = restored.GetOrCreate("player-one").Onboarding;

        Assert.Equal(FirstRunOnboardingStage.Welcome, loaded.Stage);
        Assert.Equal("vrpg:scrapper", loaded.StarterCode);
        Assert.Equal("onboarding:player-one", loaded.SafeArrivalId);
    }

    private static FirstRunOnboardingState ReadyForWelcome()
    {
        var state = new FirstRunOnboardingState();
        Assert.True(FirstRunOnboardingFlow.ConfirmAppearance(state));
        Assert.True(FirstRunOnboardingFlow.CommitStarter(state, "vrpg:scrapper", "grant-1"));
        return state;
    }

    private sealed class MemoryStorage : VRPG.Modules.Rpg.Players.IRpgPlayerStateStorage
    {
        private byte[]? data;

        public byte[]? Read() => data == null ? null : (byte[])data.Clone();

        public void Write(byte[] value) => data = (byte[])value.Clone();
    }
}
