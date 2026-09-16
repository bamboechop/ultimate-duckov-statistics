#if UDS_ENCOUNTER_DIAGNOSTICS
using UltimateDuckovStatistics.Encounters;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterLootQuantityEvidenceTests
{
    [Theory]
    [InlineData(20, 14, 94, 100, 6)]
    [InlineData(20, 0, 1, 21, 20)]
    [InlineData(20, 20, 100, 100, 0)]
    [InlineData(int.MaxValue, 0, 0, int.MaxValue, int.MaxValue)]
    public void RequiresMatchingSourceLossAndDestinationGain(int before, int after, int targetBefore, int targetAfter, int expected)
    {
        Assert.True(LootProbeQuantityEvidence.TryCombineDelta(before, after, targetBefore, targetAfter, false, out var moved));
        Assert.Equal(expected, moved);
    }

    [Theory]
    [InlineData(20, 10, 1, 10)]
    [InlineData(20, 21, 1, 0)]
    [InlineData(-1, 0, 0, 1)]
    [InlineData(int.MaxValue, 0, int.MaxValue, 0)]
    public void InvalidOrDivergentEvidenceCannotBecomeTakenQuantity(int before, int after, int targetBefore, int targetAfter)
    {
        Assert.False(LootProbeQuantityEvidence.TryCombineDelta(before, after, targetBefore, targetAfter, false, out var moved));
        Assert.Equal(0, moved);
    }

    [Fact]
    public void NativeExceptionKeepsOtherwiseMatchingDeltaUnproven()
    {
        Assert.False(LootProbeQuantityEvidence.TryCombineDelta(20, 14, 94, 100, true, out var moved));
        Assert.Equal(0, moved);
    }
}
#endif
