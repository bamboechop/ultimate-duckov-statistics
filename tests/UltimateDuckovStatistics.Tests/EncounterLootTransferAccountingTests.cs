#if UDS_ENCOUNTER_DIAGNOSTICS
using UltimateDuckovStatistics.Encounters;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterLootTransferAccountingTests
{
    [Fact]
    public void QualifiedCashSplitAndOneReturnedCashProducesTwoTakenOneReturned()
    {
        Assert.True(LootTransferAccounting.IsValidSplit(4, 2, 2, 2, true));
        var totals = new LootTransferAccounting();
        totals.Add("TakenToPlayer", 2);
        Assert.True(LootTransferAccounting.TryResidual(1, 0, false, out var returned));
        totals.Add("ReturnedFromPlayer", returned);
        Assert.True(LootTransferAccounting.TryResidual(1, 1, false, out var outerResidual));
        Assert.Equal(0, outerResidual);
        Assert.Equal(2, totals.TakenToPlayer);
        Assert.Equal(1, totals.ReturnedFromPlayer);
        Assert.Equal(1, totals.NetOutward);
    }

    [Fact]
    public void NestedPrimitiveIsSubtractedFromItsOverlappingParentEvidence()
    {
        var totals = new LootTransferAccounting();
        totals.Add("TakenToPlayer", 2);
        Assert.True(LootTransferAccounting.TryResidual(5, 2, false, out var residual));
        totals.Add("TakenToPlayer", residual);
        Assert.Equal(5, totals.TakenToPlayer);
    }

    [Theory]
    [InlineData(2, 3, false)]
    [InlineData(2, -1, false)]
    [InlineData(2, 1, true)]
    [InlineData(-1, 0, false)]
    public void ContradictoryNestingCannotProduceAResidual(int measured, int child, bool conflicting)
    {
        Assert.False(LootTransferAccounting.TryResidual(measured, child, conflicting, out var residual));
        Assert.Equal(0, residual);
    }

    [Theory]
    [InlineData(4, 4, 2, 2, true)]
    [InlineData(4, 1, 2, 2, true)]
    [InlineData(4, 2, 2, 1, true)]
    [InlineData(4, 2, 2, 2, false)]
    [InlineData(4, 0, 4, 4, true)]
    [InlineData(4, 4, 0, 0, true)]
    public void SplitMustMatchTheActualInitialDecrementAndNativeResult(int before, int after, int request, int result, bool typesMatch) =>
        Assert.False(LootTransferAccounting.IsValidSplit(before, after, request, result, typesMatch));

    [Fact]
    public void PlayerAddedItemsCanProduceNegativeNetWithoutInventingOriginalUnits()
    {
        var totals = new LootTransferAccounting();
        totals.Add("ReturnedFromPlayer", 5);
        totals.Add("TakenToPlayer", 1);
        totals.Add("TakenToPet", 2);
        totals.Add("ReturnedFromPet", 1);
        Assert.Equal(-3, totals.NetOutward);
        Assert.Equal(1, totals.TakenToPlayer);
        Assert.Equal(2, totals.TakenToPet);
        Assert.Equal(5, totals.ReturnedFromPlayer);
        Assert.Equal(1, totals.ReturnedFromPet);
    }
}
#endif
