using System;
using VRPG.Realms.Onboarding;
using VRPG.Worlds.Generation.Onboarding;
using Xunit;

namespace VRPG.Tests;

public sealed class TavernInstancePreparationTests
{
    [Fact]
    public void PreparationWritesBoundedVolumeAndCommitsOnlyAfterReadback()
    {
        var reservation = new Reservation();
        Assert.True(TavernInstancePreparation.Prepare(reservation).Ready);
        Assert.Equal(TavernLayout.Width * TavernLayout.Height * TavernLayout.Depth, reservation.Writes);
        Assert.True(reservation.Committed);
        Assert.True(reservation.Reads > reservation.Writes);
        Assert.Equal("private/player-a/arrival", reservation.Identity.ArrivalId);
        Assert.Equal("private/player-a/recovery", reservation.Identity.RecoveryId);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void UnsafeReservationNeverWrites(bool exclusive, bool loaded, bool enforced)
    {
        var reservation = new Reservation { IsExclusiveAndEmpty = exclusive, IsLoaded = loaded, IsPolicyEnforced = enforced };
        Assert.False(TavernInstancePreparation.Prepare(reservation).Ready);
        Assert.Equal(0, reservation.Writes);
        Assert.False(reservation.Committed);
        Assert.NotEmpty(reservation.QuarantineReason);
    }

    [Fact]
    public void UnknownGeneratorVersionDoesNotRewriteExistingRealm()
    {
        var reservation = new Reservation { Identity = new("private/player-a", "player-a", 42, 999) };
        Assert.Equal("unsupported-identity", TavernInstancePreparation.Prepare(reservation).Failure);
        Assert.Equal(0, reservation.Writes);
    }

    [Fact]
    public void PartialWriteIsQuarantinedAndRetryRestoresAllAirAndBlocks()
    {
        var reservation = new Reservation { FailAtWrite = 400 };
        Assert.False(TavernInstancePreparation.Prepare(reservation).Ready);
        Assert.False(reservation.Committed);
        reservation.FailAtWrite = -1;
        reservation.Blocks[9, 2, 3] = TavernBlock.Timber;
        Assert.True(TavernInstancePreparation.Prepare(reservation).Ready);
        Assert.Equal(TavernBlock.Air, reservation.Blocks[9, 2, 3]);
    }

    [Theory]
    [InlineData("floor")]
    [InlineData("decor")]
    [InlineData("policy")]
    [InlineData("occupancy")]
    [InlineData("load")]
    public void ChangesBeforeCommitFailClosed(string change)
    {
        var reservation = new Reservation();
        reservation.AfterFlush = () =>
        {
            if (change == "floor") reservation.Blocks[9, 1, 3] = TavernBlock.Air;
            if (change == "decor") reservation.Blocks[2, 3, 11] = TavernBlock.Air;
            if (change == "policy") reservation.IsPolicyEnforced = false;
            if (change == "occupancy") reservation.IsExclusiveAndEmpty = false;
            if (change == "load") reservation.IsLoaded = false;
        };
        Assert.False(TavernInstancePreparation.Prepare(reservation).Ready);
        Assert.False(reservation.Committed);
        Assert.NotEmpty(reservation.QuarantineReason);
    }

    [Fact]
    public void OnlyOwnerCreatorAndAuthoredExitInteractionAreAllowed()
    {
        foreach (OnboardingActivity activity in Enum.GetValues<OnboardingActivity>())
        {
            Assert.Equal(activity is OnboardingActivity.Creator or OnboardingActivity.ExitInteraction,
                OnboardingInstancePolicy.Allows("owner", "owner", activity));
            Assert.False(OnboardingInstancePolicy.Allows("owner", "visitor", activity));
            Assert.False(OnboardingInstancePolicy.Allows("", "", activity));
        }
        Assert.False(OnboardingInstancePolicy.Allows("owner", "owner", (OnboardingActivity)999));
    }

    private sealed class Reservation : ITavernGenerationReservation
    {
        public TavernInstanceIdentity Identity { get; set; } = new("private/player-a", "player-a", 42);
        public bool IsExclusiveAndEmpty { get; set; } = true;
        public bool IsLoaded { get; set; } = true;
        public bool IsPolicyEnforced { get; set; } = true;
        public TavernBlock[,,] Blocks { get; } = new TavernBlock[TavernLayout.Width, TavernLayout.Height, TavernLayout.Depth];
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public int FailAtWrite { get; set; } = -1;
        public bool Committed { get; private set; }
        public string QuarantineReason { get; private set; } = "";
        public Action? AfterFlush { get; set; }

        public void Write(TavernCell cell, TavernBlock block)
        {
            if (Writes++ == FailAtWrite) throw new InvalidOperationException("Injected partial write.");
            Blocks[cell.X, cell.Y, cell.Z] = block;
        }
        public TavernBlock Read(int x, int y, int z)
        {
            Reads++;
            return Blocks[x, y, z];
        }
        public void Flush() => AfterFlush?.Invoke();
        public void CommitValidated(TavernAnchor arrival, TavernAnchor recovery)
        {
            Assert.True(Reads > 0);
            Assert.Equal("arrival", arrival.Code);
            Assert.Equal("recovery", recovery.Code);
            Committed = true;
        }
        public void Quarantine(string reason) => QuarantineReason = reason;
    }
}
