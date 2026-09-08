using System.Runtime.Serialization.Json;
using System.Text.Json.Nodes;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class EquipmentDurationPerformanceTests
{
    private static readonly DateTime Origin = DateTime.UnixEpoch;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Performance")]
    public void SteadyTrackerTicksPreserveRunAndSegmentDurationsWithoutAllocating(bool withTotems)
    {
        var snapshot = Snapshot();
        if (!withTotems) snapshot.Totems.Clear();
        snapshot.TotemSetId = EquipmentIdentity.ActiveTotemSetId(snapshot.Totems);
        var tracker = new RunLifecycleTracker(() => "run");
        tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.RaidInitialized, TimestampUtc = Origin, NativeRaidId = "raid" });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = Origin,
            StartContext = new RunStartContext
            {
                SaveGenerationId = "generation",
                NativeRaidId = "raid",
                Map = new MapIdentity { MapId = "map", DisplayName = "Map", IsKnown = true },
                LifecycleCapability = AdapterCapabilityState.Supported,
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities(),
                RouteCapabilities = RouteStatisticsReducer.Supported("native")
            }
        });
        Assert.True(tracker.ObserveEquipment(snapshot));
        for (var i = 1; i <= 1000; i++) tracker.Tick(Origin.AddTicks(i * 40000L), i / 250d);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1001; i <= 2000; i++) tracker.Tick(Origin.AddTicks(i * 40000L), i / 250d);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        var checkpoint = tracker.CreateCheckpoint(Origin.AddSeconds(8), 8)!;
        foreach (var aggregate in new[] { checkpoint.EquipmentStatistics, Assert.Single(checkpoint.Segments).EquipmentStatistics })
        {
            EquipmentStatisticsReducer.ValidateAggregate(aggregate);
            Assert.Equal(8m, Assert.Single(aggregate.Items).Value.ActiveDurationSeconds);
            Assert.Equal(8m, Assert.Single(aggregate.NestedSlotStates).Value.ActiveDurationSeconds);
            Assert.Equal(8m, Assert.Single(aggregate.Composition.EmptyDirectSlots).Value.ActiveDurationSeconds);
            Assert.Equal(withTotems ? 3 : 0, aggregate.Composition.TotemStates.Count);
            Assert.All(aggregate.Composition.TotemStates.Values, row => Assert.Equal(8m, row.DurationSeconds));
        }
    }

    [Fact]
    public void SameIdentityEnrichmentRemainsVisibleAfterFurtherAdvances()
    {
        var snapshot = Snapshot();
        var aggregate = Observed(snapshot);
        snapshot.Items[0].ItemDisplayName = snapshot.CharacterSlots[0].ItemDisplayName = "Localized rifle";
        snapshot.Items[0].NestedSlots[0].SlotDisplayName = "Localized scope";
        snapshot.Totems[0].DisplayName = "Localized totem";
        snapshot.NestedSlotStateComplete = snapshot.Items[0].NestedSlotStateComplete = true;

        Assert.True(EquipmentStatisticsReducer.Observe(aggregate, snapshot, 1));
        EquipmentStatisticsReducer.Advance(aggregate, 2);

        Assert.Equal(1, aggregate.TransitionCount);
        Assert.Equal("Localized rifle", Assert.Single(aggregate.Items).Value.DisplayName);
        Assert.Equal("Localized scope", Assert.Single(aggregate.NestedSlotStates).Value.SlotDisplayName);
        Assert.Contains(aggregate.Composition.TotemStates.Values, row => row.Totem.DisplayName == "Localized totem" && row.DurationSeconds == 2m);
        Assert.Contains("Localized rifle", aggregate.Loadouts[snapshot.LoadoutId].DisplayName);
        Assert.True(aggregate.Composition.Loadouts[snapshot.LoadoutId].NestedComplete);
        EquipmentStatisticsReducer.ValidateAggregate(aggregate);
    }

    [Fact]
    public void AttachmentAndTotemChangesPartitionTimeAcrossSuspension()
    {
        var snapshot = Snapshot();
        var aggregate = Observed(snapshot);
        EquipmentStatisticsReducer.Suspend(aggregate, 2);
        EquipmentStatisticsReducer.Advance(aggregate, 5);
        var oldLoadout = snapshot.LoadoutId;
        snapshot.SnapshotId = "changed";
        snapshot.Items[0].AttachmentSignature = "scope-equipped";
        snapshot.Items[0].NestedSlots[0].State = EquipmentSlotState.Occupied;
        snapshot.Items[0].NestedSlots[0].ItemId = "mod:scope";
        snapshot.Items[0].NestedSlots[0].ItemDisplayName = "Scope";
        snapshot.Totems.RemoveAt(2);
        snapshot.LoadoutId = EquipmentIdentity.LoadoutId(snapshot.Items);
        EquipmentStatisticsReducer.Observe(aggregate, snapshot, 5);
        EquipmentStatisticsReducer.Advance(aggregate, 8);

        Assert.Equal(2m, aggregate.Loadouts[oldLoadout].ActiveDurationSeconds);
        Assert.Equal(3m, aggregate.Loadouts[snapshot.LoadoutId].ActiveDurationSeconds);
        Assert.Collection(aggregate.Items.Values.Select(row => row.ActiveDurationSeconds).Order(),
            value => Assert.Equal(2m, value), value => Assert.Equal(3m, value));
        Assert.Collection(aggregate.Composition.TotemStates.Values
            .Where(row => row.Totem.CarryKind == TotemCarryKind.ToteInventory).Select(row => row.DurationSeconds).Order(),
            value => Assert.Equal(2m, value), value => Assert.Equal(5m, value));
        Assert.Equal(5m, Assert.Single(aggregate.Composition.EmptyDirectSlots).Value.ActiveDurationSeconds);
        EquipmentStatisticsReducer.ValidateAggregate(aggregate);
    }

    [Fact]
    public void CloneAndStoredRecoveryContinueWithTheSameExactDurations()
    {
        var aggregate = Observed(Snapshot());
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "equipment.json");
        var store = new AtomicJsonStore<EquipmentStatisticsAggregate>();
        store.Save(path, aggregate);
        var restored = store.Load(path).Value!;
        var cloned = EquipmentStatisticsReducer.Clone(aggregate);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Serialize(aggregate)), JsonNode.Parse(Serialize(restored))));
        foreach (var value in new[] { aggregate, restored, cloned })
        {
            EquipmentStatisticsReducer.Advance(value, 1.0004);
            EquipmentStatisticsReducer.Advance(value, 2.0037);
            EquipmentStatisticsReducer.ValidateAggregate(value);
        }
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Serialize(aggregate)), JsonNode.Parse(Serialize(restored))));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Serialize(aggregate)), JsonNode.Parse(Serialize(cloned))));
    }

    [Theory]
    [InlineData("item")]
    [InlineData("typed-totem")]
    [InlineData("empty-slot")]
    public void OverflowAfterSuccessfulAdvanceLeavesAllPersistedStateUnchanged(string family)
    {
        var aggregate = Observed(Snapshot());
        switch (family)
        {
            case "item": aggregate.Items.Values.Single().ActiveDurationSeconds = decimal.MaxValue; break;
            case "typed-totem": aggregate.Composition.TotemStates.Values.Last().DurationSeconds = decimal.MaxValue; break;
            case "empty-slot": aggregate.Composition.EmptyDirectSlots.Values.Single().ActiveDurationSeconds = decimal.MaxValue; break;
        }
        var before = Serialize(aggregate);

        Assert.Throws<OverflowException>(() => EquipmentStatisticsReducer.Advance(aggregate, 2));

        Assert.Equal(before, Serialize(aggregate));
    }

    [Fact]
    public void MutableSnapshotNamesAndReplacedRowsAreReadOnEveryAdvance()
    {
        var aggregate = Observed(Snapshot());
        var original = aggregate.Items.Single();
        aggregate.Items[original.Key] = new EquipmentDurationAggregate
        { Id = original.Key, ActiveDurationSeconds = original.Value.ActiveDurationSeconds, DisplayName = original.Value.DisplayName };
        aggregate.CurrentSnapshot!.Items[0].ItemDisplayName = "Enriched rifle";
        aggregate.CurrentSnapshot.Items[0].NestedSlots[0].SlotDisplayName = "Enriched scope";

        EquipmentStatisticsReducer.Advance(aggregate, 2);

        Assert.Equal(1m, original.Value.ActiveDurationSeconds);
        Assert.Equal(2m, aggregate.Items[original.Key].ActiveDurationSeconds);
        Assert.Equal("Enriched rifle", aggregate.Items[original.Key].DisplayName);
        Assert.Equal("Enriched scope", aggregate.NestedSlotStates.Values.Single().SlotDisplayName);
    }

    private static EquipmentStatisticsAggregate Observed(EquipmentSnapshot snapshot)
    {
        var aggregate = new EquipmentStatisticsAggregate { Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities() };
        EquipmentStatisticsReducer.Observe(aggregate, snapshot, 0);
        EquipmentStatisticsReducer.Advance(aggregate, 1);
        return aggregate;
    }

    private static EquipmentSnapshot Snapshot()
    {
        var snapshot = EquipmentCompositionTests.Snapshot();
        snapshot.CharacterSlots.Add(new CharacterEquipmentSlotSnapshot
        { SlotId = "duckov:slot:totem-b", SlotDisplayName = "Totem B", State = EquipmentSlotState.Empty, IsDirectTotemSlot = true });
        for (var i = 0; i < 2; i++) snapshot.Totems.Add(new TotemSnapshot
        {
            ItemId = "duckov:totem:3",
            DisplayName = "Carried totem",
            CarryKind = TotemCarryKind.ToteInventory,
            ContainerId = "duckov:tote:1255",
            ActivationState = TotemActivationState.Unknown
        });
        return snapshot;
    }

    private static byte[] Serialize(EquipmentStatisticsAggregate aggregate)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(EquipmentStatisticsAggregate),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(stream, aggregate);
        return stream.ToArray();
    }
}
