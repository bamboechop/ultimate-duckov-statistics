using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class CheckpointEntryCaptureTests
{
    private static readonly ProfileRecordCodec Codec = new(NativeProfileJsonWriter.WriteRecord);

    [Fact]
    public void AdvancingCurrentEquipmentCapturesItsEntriesWithoutRetainedLoadouts()
    {
        var weapon = new WeaponStatisticsAggregate(); var combat = new CombatStatisticsAggregate(); var items = new ItemStatisticsAggregate();
        var equipment = new EquipmentStatisticsAggregate { Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities() };
        var observed = EquipmentCompositionTests.Snapshot(); EquipmentStatisticsReducer.Observe(equipment, observed, 0);
        for (var index = 0; index < 1000; index++) equipment.Loadouts.Add("retained:" + index, new EquipmentDurationAggregate { Id = "retained:" + index, ActiveDurationSeconds = 10 });
        EquipmentStatisticsReducer.ValidateAggregate(equipment);
        var changes = 0;
        var collections = CheckpointEntryCollection.Bind(() => weapon, () => combat, () => items, () => equipment, () => changes++);
        foreach (var collection in collections) { var first = collection.Capture(Codec); first.Receipt.Owner.Acknowledge(first.Receipt); }
        EquipmentStatisticsReducer.Advance(equipment, 5);
        var captured = collections.Select(collection => collection.Capture(Codec)).ToArray();
        var loadouts = captured.Single(collection => collection.Kind == CheckpointEntryKind.Loadouts);
        var row = Assert.Single(loadouts.Entries);
        Assert.Equal(observed.LoadoutId, row.Key);
        Assert.Equal(5m, ProfileRecordCodec.Decode<EquipmentDurationAggregate>(row.Value!).ActiveDurationSeconds);
        Assert.DoesNotContain(captured.SelectMany(collection => collection.Entries), entry => entry.Key.StartsWith("retained:", StringComparison.Ordinal));
        Assert.True(changes > 0);
        Assert.Equal(10m, equipment.Loadouts["retained:999"].ActiveDurationSeconds);
    }

    [Fact]
    public void OlderEquipmentReceiptCannotClearANewerAcceptedDuration()
    {
        var weapon = new WeaponStatisticsAggregate(); var combat = new CombatStatisticsAggregate(); var items = new ItemStatisticsAggregate();
        var equipment = new EquipmentStatisticsAggregate { Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities() };
        var observed = EquipmentCompositionTests.Snapshot(); EquipmentStatisticsReducer.Observe(equipment, observed, 0);
        var collection = CheckpointEntryCollection.Bind(() => weapon, () => combat, () => items, () => equipment, () => { })
            .Single(value => value.Kind == CheckpointEntryKind.Loadouts);
        var initial = collection.Capture(Codec); initial.Receipt.Owner.Acknowledge(initial.Receipt);
        EquipmentStatisticsReducer.Advance(equipment, 5); var first = collection.Capture(Codec);
        EquipmentStatisticsReducer.Advance(equipment, 9);
        first.Receipt.Owner.Acknowledge(first.Receipt);
        var retry = collection.Capture(Codec);
        Assert.Equal(5m, ProfileRecordCodec.Decode<EquipmentDurationAggregate>(Assert.Single(first.Entries).Value!).ActiveDurationSeconds);
        Assert.Equal(9m, ProfileRecordCodec.Decode<EquipmentDurationAggregate>(Assert.Single(retry.Entries).Value!).ActiveDurationSeconds);
        retry.Receipt.Owner.Acknowledge(retry.Receipt);
        Assert.Empty(collection.Capture(Codec).Entries);
    }

    [Fact]
    public void NewlyReplacedDictionaryCapturesItsCompleteReplacementBeforeAcknowledgement()
    {
        var weapon = new WeaponStatisticsAggregate(); var combat = new CombatStatisticsAggregate(); var items = new ItemStatisticsAggregate();
        var equipment = new EquipmentStatisticsAggregate();
        var collection = CheckpointEntryCollection.Bind(() => weapon, () => combat, () => items, () => equipment, () => { })
            .Single(value => value.Kind == CheckpointEntryKind.Items);
        var first = collection.Capture(Codec); first.Receipt.Owner.Acknowledge(first.Receipt);
        items.Items = new Dictionary<string, ItemAggregate>(StringComparer.Ordinal) { ["item"] = new() { ItemId = "item" } };
        var replacement = collection.Capture(Codec);
        Assert.True(replacement.Receipt.ReplacesCollection); Assert.Single(replacement.Entries);
        first.Receipt.Owner.Acknowledge(first.Receipt);
        Assert.True(collection.Capture(Codec).Receipt.ReplacesCollection);
        replacement.Receipt.Owner.Acknowledge(replacement.Receipt);
        Assert.False(collection.Capture(Codec).Receipt.ReplacesCollection);
    }
}
