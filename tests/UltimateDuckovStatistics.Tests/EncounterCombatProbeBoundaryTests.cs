#if UDS_ENCOUNTER_DIAGNOSTICS
using UltimateDuckovStatistics.Encounters;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterCombatProbeBoundaryTests
{
    [Fact]
    public void OverkillAndNativeZeroClampReserveOneCandidateAndCountOnlyAvailableHp()
    {
        var boundary = new CombatProbeBoundary();
        Assert.True(boundary.ObserveAssignment(7, -1, 11));
        Assert.False(boundary.ObserveAssignment(-1, 0, 12));
        Assert.Equal(11, boundary.CandidateSequence);
        Assert.Equal(7, boundary.ProposedLoss);
        Assert.Equal(2, boundary.MutationCount);
    }

    [Fact]
    public void NonRaidRescueDoesNotConfirmCandidateAsDeath()
    {
        var boundary = new CombatProbeBoundary();
        Assert.True(boundary.ObserveAssignment(7, -1, 11));
        Assert.False(boundary.ObserveAssignment(-1, 1, 12));
        Assert.False(CombatProbeBoundary.IsFatal(false, false, true));
        Assert.Equal(6, CombatProbeBoundary.NetLoss(7, 1));
        Assert.Equal(7, boundary.ProposedLoss);
    }

    [Fact]
    public void NestedTransactionsKeepChildMutationOutOfParentLoss()
    {
        var parent = new CombatProbeBoundary();
        var child = new CombatProbeBoundary();
        parent.ObserveAssignment(10, 7, 1);
        child.ObserveAssignment(7, 0, 2);
        parent.ObserveAssignment(0, 0, 3);
        Assert.Equal(3, parent.ProposedLoss);
        Assert.Equal(7, child.ProposedLoss);
        Assert.False(parent.HasCandidate);
        Assert.True(child.HasCandidate);
        Assert.Equal(10, CombatProbeBoundary.NetLoss(10, 0));
    }

    [Fact]
    public void NativeExceptionAndAlreadyDeadTargetCannotConfirmNewFatality()
    {
        Assert.False(CombatProbeBoundary.IsFatal(false, true, false));
        Assert.False(CombatProbeBoundary.IsFatal(true, true, true));
        Assert.True(CombatProbeBoundary.IsFatal(false, true, true));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, -1)]
    public void NonFiniteMutationDoesNotCreateEvidence(double before, double proposed)
    {
        var boundary = new CombatProbeBoundary();
        Assert.False(boundary.ObserveAssignment(before, proposed, 1));
        Assert.False(boundary.HasCandidate);
        Assert.Equal(0, boundary.MutationCount);
        Assert.Equal(0, CombatProbeBoundary.NetLoss(before, proposed));
    }
}
#endif
