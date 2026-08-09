using UltimateDuckovStatistics.Core.Classification;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class ItemUseCorrelatorTests
{
    private static readonly DateTime StartedAt = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "ItemUse")]
    public void SuccessfulStackUseProducesOneActivationAndObservedStackAmount()
    {
        var correlator = CreateCorrelator();
        correlator.Begin(CreateSnapshot(stackable: true, stackCount: 4));

        Assert.True(correlator.MarkSuccessful(runtimeItemId: 101, durabilityAfterBehaviors: 0));
        var result = correlator.CompleteByMainPlayer(101, finalStackCount: 3, finalDurability: null, StartedAt.AddSeconds(2));

        Assert.Equal(ItemUseCompletionDisposition.Counted, result.Disposition);
        var itemUse = Assert.IsType<ItemUseRecorded>(result.NormalizedEvent);
        Assert.Equal(1, itemUse.ActivationCount);
        Assert.Equal(1, itemUse.AmountConsumed);
        Assert.Equal(ConsumptionUnit.StackUnit, itemUse.ConsumptionUnit);
        Assert.Equal("event-1", itemUse.EventId);
        Assert.Equal(0, correlator.PendingCount);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void SuccessfulDurabilityUseKeepsActionAndAmountDistinct()
    {
        var correlator = CreateCorrelator();
        correlator.Begin(CreateSnapshot(stackable: false, usesDurability: true, durability: 50));

        Assert.True(correlator.MarkSuccessful(101, durabilityAfterBehaviors: 37.5));
        var result = correlator.CompleteByMainPlayer(101, finalStackCount: null, finalDurability: 37.5, StartedAt.AddSeconds(3));

        var itemUse = Assert.IsType<ItemUseRecorded>(result.NormalizedEvent);
        Assert.Equal(1, itemUse.ActivationCount);
        Assert.Equal(12.5, itemUse.AmountConsumed, precision: 6);
        Assert.Equal(ConsumptionUnit.Durability, itemUse.ConsumptionUnit);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void NativeOneStackFallbackIsUsedWhenDestroyedItemStateIsUnavailable()
    {
        var correlator = CreateCorrelator();
        correlator.Begin(CreateSnapshot(stackable: true, stackCount: 1));
        correlator.MarkSuccessful(101, durabilityAfterBehaviors: 0);

        var result = correlator.CompleteByMainPlayer(101, finalStackCount: null, finalDurability: null, StartedAt.AddSeconds(1));

        Assert.Equal(1, result.NormalizedEvent!.AmountConsumed);
        Assert.Equal(ConsumptionUnit.StackUnit, result.NormalizedEvent.ConsumptionUnit);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void DuplicateNativeCallbackSequenceCanFinalizeOnlyOnce()
    {
        var correlator = CreateCorrelator();
        var snapshot = CreateSnapshot(stackable: true, stackCount: 2);

        correlator.Begin(snapshot);
        correlator.Begin(snapshot);
        Assert.True(correlator.MarkSuccessful(101, 0));
        Assert.True(correlator.MarkSuccessful(101, 0));

        var first = correlator.CompleteByMainPlayer(101, 1, null, StartedAt.AddSeconds(1));
        var duplicate = correlator.CompleteByMainPlayer(101, 1, null, StartedAt.AddSeconds(1));

        Assert.True(first.ShouldCount);
        Assert.NotNull(first.NormalizedEvent);
        Assert.Equal(ItemUseCompletionDisposition.MissingBegin, duplicate.Disposition);
        Assert.Null(duplicate.NormalizedEvent);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void CancelledInterruptedFailedAndNonPlayerUsesNeverProduceEvents()
    {
        var correlator = CreateCorrelator();

        correlator.Begin(CreateSnapshot());
        Assert.Equal(1, correlator.ExpireBefore(StartedAt.AddMinutes(1)));
        Assert.Equal(0, correlator.PendingCount);

        var missingBegin = correlator.CompleteByMainPlayer(101, 0, 0, StartedAt.AddMinutes(1));
        Assert.Equal(ItemUseCompletionDisposition.MissingBegin, missingBegin.Disposition);
        Assert.Null(missingBegin.NormalizedEvent);

        correlator.Begin(CreateSnapshot());
        var missingSuccess = correlator.CompleteByMainPlayer(101, 0, 0, StartedAt.AddMinutes(1));
        Assert.Equal(ItemUseCompletionDisposition.MissingSuccessfulUse, missingSuccess.Disposition);
        Assert.Null(missingSuccess.NormalizedEvent);

        correlator.Begin(CreateSnapshot());
        correlator.MarkSuccessful(101, 0);
        Assert.Equal(1, correlator.ExpireBefore(StartedAt.AddMinutes(1)));
        Assert.Equal(0, correlator.PendingCount);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void BaseUseIsNormalizedForDiagnosticsButNotCountable()
    {
        var correlator = CreateCorrelator();
        var snapshot = CreateSnapshot();
        snapshot.GameplayContext = GameplayContext.Base;
        correlator.Begin(snapshot);
        correlator.MarkSuccessful(101, 0);

        var result = correlator.CompleteByMainPlayer(101, finalStackCount: 0, finalDurability: null, StartedAt.AddSeconds(1));

        Assert.False(result.ShouldCount);
        Assert.Equal(ItemUseCompletionDisposition.IgnoredOutsideRaid, result.Disposition);
        Assert.Equal(GameplayContext.Base, result.NormalizedEvent!.GameplayContext);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void MissingStableIdentityIsRejected()
    {
        var correlator = CreateCorrelator();
        var snapshot = CreateSnapshot();
        snapshot.ItemId = "";

        Assert.Throws<ArgumentException>(() => correlator.Begin(snapshot));
    }

    private static ItemUseCorrelator CreateCorrelator()
    {
        var sequence = 0;
        return new ItemUseCorrelator(() => $"event-{++sequence}");
    }

    internal static ItemUseSnapshot CreateSnapshot(
        bool stackable = false,
        int stackCount = 1,
        bool usesDurability = false,
        double durability = 0)
    {
        return new ItemUseSnapshot
        {
            RuntimeItemId = 101,
            ItemId = "type:42",
            DisplayName = "Test consumable",
            Classification = new ItemClassificationInput { AppliesPositiveHealing = true },
            Stackable = stackable,
            StackCount = stackCount,
            UsesDurability = usesDurability,
            Durability = durability,
            TimestampUtc = StartedAt,
            SaveGenerationId = "generation-a",
            RunId = "raid-7",
            MapId = "warehouse",
            GameVersion = "2.3.30",
            GameBuild = "24013657",
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            AdapterCapability = AdapterCapabilityState.Supported,
            AdapterVersion = "native-2.3.30"
        };
    }
}
