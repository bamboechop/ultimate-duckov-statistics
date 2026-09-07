using System.Runtime.Serialization.Json;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Export;

namespace UltimateDuckovStatistics.Tests;

public sealed class ThrowableUseTests
{
    [Fact]
    public void ProductionRepositoryReopenAndExportRetainThrowableCounts()
    {
        using var directory = new TemporaryDirectory();
        var identity = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = true, SaveFileCreationUtcTicks = 100,
            ObservedWriteUtcTicks = 110, ObservedLength = 4096, GameVersion = "2.3.30", ContentSha256 = new string('a', 64),
            SaveTimeBinary = DateTime.UnixEpoch.AddTicks(100).ToBinary() };
        var repository = new ProfileRepository(directory.Path, () => DateTime.UtcNow, () => Guid.NewGuid().ToString("N")); repository.Open(identity);
        var snapshot = Snapshot(); snapshot.SaveGenerationId = repository.Current.GenerationId;
        var observation = ThrowableUseObservation.Begin(snapshot, true, true)!; observation.MarkReleased();
        var recorded = observation.Complete(snapshot.SaveGenerationId, "r", "s", 3, false, DateTime.UtcNow)!;
        repository.EnableDeferredItemPersistence();
        Assert.True(repository.RecordDeferred(recorded)); repository.CloseClean();
        var reopened = new ProfileRepository(directory.Path, () => DateTime.UtcNow, () => Guid.NewGuid().ToString("N")); reopened.Open(identity);
        var item = reopened.Current.Statistics.Items["duckov:item:67"];
        Assert.Equal(1, item.Totals.ActivationCount); Assert.Contains(ItemEffectTag.Throwable, item.EffectTags);
        var export = StatisticsExporter.Create(reopened.Current, DateTime.UtcNow);
        var exported = Assert.Single(export.Document.Items);
        Assert.Equal(1, exported.Totals.ActivationCount); Assert.Contains("Throwable", exported.EffectTags);
        reopened.CloseClean();
    }
    private static ItemUseSnapshot Snapshot() => new() { ItemId = "duckov:item:67", DisplayName = "Grenade",
        SaveGenerationId = "g", RunId = "r", MapId = "m", SegmentId = "s", GameplayContext = GameplayContext.Raid,
        Stackable = true, StackCount = 4 };
    [Theory]
    [InlineData(false, true, GameplayContext.Raid)]
    [InlineData(true, false, GameplayContext.Raid)]
    [InlineData(true, true, GameplayContext.Base)]
    [InlineData(true, true, GameplayContext.Unknown)]
    public void RejectsNpcNonThrowableAndOutsideRaid(bool player, bool throwable, GameplayContext context)
    { var snapshot = Snapshot(); snapshot.GameplayContext = context; Assert.Null(ThrowableUseObservation.Begin(snapshot, player, throwable)); }

    [Fact]
    public void CancelledReleaseAndRepeatedCompletionCannotCount()
    {
        var value = ThrowableUseObservation.Begin(Snapshot(), true, true)!;
        Assert.Null(value.Complete("g", "r", "s", 4, false, DateTime.UtcNow));
        value.MarkReleased(); Assert.Null(value.Complete("g", "r", "s", 3, false, DateTime.UtcNow));
    }
    [Fact]
    public void OneReleaseCountsOnceRegardlessOfProjectilesAndMeasuresConsumptionSeparately()
    {
        var value = ThrowableUseObservation.Begin(Snapshot(), true, true)!;
        for (var i = 0; i < 5; i++) value.MarkReleased();
        var recorded = value.Complete("g", "r", "s", 3, false, DateTime.UtcNow)!;
        Assert.Equal(1, recorded.ActivationCount); Assert.Equal(1, recorded.AmountConsumed);
        Assert.Equal(ConsumptionUnit.StackUnit, recorded.ConsumptionUnit);
        Assert.Equal(new[] { ItemEffectTag.Throwable }, recorded.EffectTags);
        Assert.Null(value.Complete("g", "r", "s", 2, false, DateTime.UtcNow));
    }
    [Theory]
    [InlineData("other", "r", "s")]
    [InlineData("g", "other", "s")]
    [InlineData("g", "r", "other")]
    [InlineData("g", null, null)]
    public void CannotCrossGenerationRunOrSegment(string generation, string? run, string? segment)
    { var value = ThrowableUseObservation.Begin(Snapshot(), true, true)!; value.MarkReleased(); Assert.Null(value.Complete(generation, run, segment, 3, false, DateTime.UtcNow)); }

    [Theory]
    [InlineData(4, false, ConsumptionUnit.StackUnit, 0)]
    [InlineData(null, false, ConsumptionUnit.UnknownAmount, 0)]
    [InlineData(null, true, ConsumptionUnit.StackUnit, 4)]
    public void RecoverableAndUnobservedConsumptionDoNotInventConsumedItems(int? after, bool destroyed, ConsumptionUnit unit, double amount)
    {
        var value = ThrowableUseObservation.Begin(Snapshot(), true, true)!; value.MarkReleased();
        var recorded = value.Complete("g", "r", "s", after, destroyed, DateTime.UtcNow)!;
        Assert.Equal(1, recorded.ActivationCount); Assert.Equal(unit, recorded.ConsumptionUnit); Assert.Equal(amount, recorded.AmountConsumed);
    }
    [Fact]
    public void ExistingItemUsePersistenceRoundTripsThrowableEvidenceAndDeduplicates()
    {
        var value = ThrowableUseObservation.Begin(Snapshot(), true, true)!; value.MarkReleased();
        var recorded = value.Complete("g", "r", "s", 3, false, DateTime.UtcNow)!;
        var profile = new ProfileStatistics { SaveGenerationId = "g" };
        Assert.True(ItemUseReducer.Apply(profile, recorded)); Assert.False(ItemUseReducer.Apply(profile, recorded));
        var serializer = new DataContractJsonSerializer(typeof(ItemAggregate));
        using var stream = new MemoryStream(); serializer.WriteObject(stream, profile.Items["duckov:item:67"]); stream.Position = 0;
        var item = (ItemAggregate)serializer.ReadObject(stream)!;
        Assert.Equal(1, item.Totals.ActivationCount); Assert.Contains(ItemEffectTag.Throwable, item.EffectTags);
        Assert.Equal(1, item.Totals.AmountsByUnit[nameof(ConsumptionUnit.StackUnit)]);
    }
}
