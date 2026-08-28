using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class M14LosslessAssociationTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "M14")]
    public void EventTimePairsPartialIdentitiesAndRepeatedMapFanOutRemainExact()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordShot(Shot("a-x-1", tracker, "weapon:a", "Weapon A", "ammo:x", "Ammo X")));
        Assert.True(tracker.RecordShot(Shot("a-y", tracker, "weapon:a", "Weapon A", "ammo:y", "Ammo Y")));
        Assert.False(tracker.RecordShot(Shot("a-y", tracker, "weapon:a", "Weapon A", "ammo:y", "Ammo Y")));
        Transition(tracker, 2, 4, "B");
        Assert.True(tracker.RecordShot(Shot("b-x", tracker, "weapon:b", "Weapon B", "ammo:x", "Ammo X")));
        var partial = Shot("partial-x", tracker, string.Empty, string.Empty, "ammo:x", "Ammo X");
        partial.Capabilities.WeaponIdentity = Unavailable("weapon identity missing for this callback");
        Assert.True(tracker.RecordShot(partial));
        Transition(tracker, 6, 8, "A");
        Assert.True(tracker.RecordShot(Shot("a-x-2", tracker, "weapon:a", "Renamed Weapon A", "ammo:x", "Renamed Ammo X")));

        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation" };
        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(5, run.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(2, Pair(run.WeaponStatistics, "weapon:a", "ammo:x").FiringActions);
        Assert.Equal(1, Pair(run.WeaponStatistics, "weapon:a", "ammo:y").FiringActions);
        Assert.Equal(1, Pair(run.WeaponStatistics, "weapon:b", "ammo:x").FiringActions);
        Assert.Equal("Renamed Weapon A", Pair(run.WeaponStatistics, "weapon:a", "ammo:x").WeaponDisplayName);
        Assert.Equal(1, run.WeaponStatistics.UncorrelatedFiringActions);
        Assert.Equal(1, run.WeaponStatistics.UncorrelatedAmmunitionFiringActions["ammo:x"]);
        Assert.Equal(3, run.WeaponStatistics.Weapons["weapon:a"].Totals.FiringActions);
        Assert.Equal(4, run.WeaponStatistics.AmmunitionTypes["ammo:x"].Totals.FiringActions);
        Assert.Equal(2, run.Segments[0].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(2, run.Segments[1].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(1, run.Segments[2].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(5, profile.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(5, profile.RunTotals.Maps["duckov:map:A"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(3, profile.RunTotals.RouteMaps["duckov:map:A"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(2, profile.RunTotals.RouteMaps["duckov:map:B"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(2, Pair(profile.RunTotals.RouteMaps["duckov:map:A"].WeaponStatistics, "weapon:a", "ammo:x").FiringActions);
        WeaponStatisticsReducer.ValidateAggregate(run.WeaponStatistics);

        var pairCsv = StatisticsExporter.Create(Profile(run), Now.AddMinutes(1)).WeaponAmmunitionPairsCsv;
        var pairLines = pairCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.TrimEnd('\r').Split(','))
            .ToArray();
        var headers = pairLines[0];
        var scopeIndex = Array.IndexOf(headers, "scope");
        var projectionIndex = Array.IndexOf(headers, "projection");
        var weaponIndex = Array.IndexOf(headers, "weapon_id");
        var ammunitionIndex = Array.IndexOf(headers, "ammunition_id");
        var percentageIndex = Array.IndexOf(headers, "percentage_within_observed_projection_pairs");
        var weaponProjection = Assert.Single(pairLines.Skip(1), row => row[scopeIndex] == "run"
            && row[projectionIndex] == "weapon_to_ammunition"
            && row[weaponIndex] == "weapon:b" && row[ammunitionIndex] == "ammo:x");
        var ammunitionProjection = Assert.Single(pairLines.Skip(1), row => row[scopeIndex] == "run"
            && row[projectionIndex] == "ammunition_to_weapon"
            && row[weaponIndex] == "weapon:b" && row[ammunitionIndex] == "ammo:x");
        Assert.Equal(100d, double.Parse(weaponProjection[percentageIndex], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(100d / 3d, double.Parse(ammunitionProjection[percentageIndex], System.Globalization.CultureInfo.InvariantCulture), 10);
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Performance")]
    public void MoreThanOneHundredThousandAcceptedActionsStayInOneBoundedPairAggregate()
    {
        const int actions = 100_001;
        var statistics = new WeaponStatisticsAggregate();
        for (var index = 0; index < actions; index++)
        {
            var shot = Shot($"stress:{index}", null, "weapon:modded", "Modded weapon", "ammo:modded", "Modded ammunition");
            WeaponStatisticsReducer.Apply(statistics, shot);
        }

        Assert.Equal(actions, statistics.Totals.FiringActions);
        Assert.Equal(actions, Assert.Single(statistics.WeaponAmmunitionPairs).Value.FiringActions);
        Assert.Single(statistics.Weapons);
        Assert.Single(statistics.AmmunitionTypes);
        Assert.Empty(statistics.UncorrelatedWeaponFiringActions);
        WeaponStatisticsReducer.ValidateAggregate(statistics);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void PairOverflowIsRejectedBeforeIndependentTotalsMutate()
    {
        var statistics = new WeaponStatisticsAggregate
        {
            Totals = new WeaponMetricTotals { FiringActions = long.MaxValue },
            Capabilities = WeaponNativeContractPolicy.CreateMetricCapabilities()
        };
        statistics.Weapons["weapon:a"] = new WeaponAggregate
        {
            WeaponId = "weapon:a",
            DisplayName = "A",
            Totals = new WeaponMetricTotals { FiringActions = long.MaxValue }
        };
        statistics.AmmunitionTypes["ammo:a"] = new AmmunitionAggregate
        {
            AmmunitionId = "ammo:a",
            DisplayName = "A",
            Totals = new WeaponMetricTotals { FiringActions = long.MaxValue }
        };
        var pair = new WeaponAmmunitionPairAggregate
        {
            WeaponId = "weapon:a",
            WeaponDisplayName = "A",
            AmmunitionId = "ammo:a",
            AmmunitionDisplayName = "A",
            FiringActions = long.MaxValue
        };
        statistics.WeaponAmmunitionPairs[WeaponStatisticsReducer.PairKey("weapon:a", "ammo:a")] = pair;

        Assert.Throws<OverflowException>(() => WeaponStatisticsReducer.Apply(
            statistics,
            Shot("overflow", null, "weapon:a", "A", "ammo:a", "A")));
        Assert.Equal(long.MaxValue, statistics.Totals.FiringActions);
        Assert.Equal(long.MaxValue, pair.FiringActions);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void RootAndNestedEmptyOccupiedTransitionsUseParentEquippedActiveTime()
    {
        var statistics = Equipment();
        EquipmentStatisticsReducer.Observe(statistics, EquipmentSnapshot("s0", primaryOccupied: false, scopeOccupied: false, pouchItem: "child:old", includeArmor: true), 0);
        EquipmentStatisticsReducer.Observe(statistics, EquipmentSnapshot("s1", primaryOccupied: true, scopeOccupied: true, pouchItem: string.Empty, includeArmor: true), 2);
        EquipmentStatisticsReducer.Observe(statistics, EquipmentSnapshot("s2", primaryOccupied: false, scopeOccupied: false, pouchItem: "child:modded", includeArmor: true), 5);
        EquipmentStatisticsReducer.Observe(statistics, EquipmentSnapshot("s3", primaryOccupied: false, scopeOccupied: false, pouchItem: string.Empty, includeArmor: false), 7);
        EquipmentStatisticsReducer.Observe(statistics, EquipmentSnapshot("s4", primaryOccupied: false, scopeOccupied: false, pouchItem: "child:modded", includeArmor: true), 8);
        EquipmentStatisticsReducer.Advance(statistics, 9);

        Assert.Equal(6, RootState(statistics, "slot:primary", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(3, RootState(statistics, "slot:primary", EquipmentSlotState.Occupied).ActiveDurationSeconds);
        Assert.Equal(1, RootState(statistics, "slot:armor", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(8, RootState(statistics, "slot:armor", EquipmentSlotState.Occupied).ActiveDurationSeconds);
        Assert.Equal(6, NestedState(statistics, "slot:secondary", "weapon:secondary", "5:scope", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(3, NestedState(statistics, "slot:secondary", "weapon:secondary", "5:scope", EquipmentSlotState.Occupied).ActiveDurationSeconds);
        Assert.Equal(3, NestedState(statistics, "slot:armor", "armor:modded", "5:pouch", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(2, NestedState(statistics, "slot:armor", "armor:modded", "5:pouch", EquipmentSlotState.Occupied, "child:old").ActiveDurationSeconds);
        Assert.Equal(3, NestedState(statistics, "slot:armor", "armor:modded", "5:pouch", EquipmentSlotState.Occupied, "child:modded").ActiveDurationSeconds);
        Assert.Equal(8, statistics.Items.Values.Where(value => value.Id.StartsWith("slot:armor|armor:modded|", StringComparison.Ordinal)).Sum(value => value.ActiveDurationSeconds));
        Assert.Equal(8, statistics.NestedSlotObservedDurations.Values.Where(value => value.DisplayName == "Pouch").Sum(value => value.ActiveDurationSeconds));
        EquipmentStatisticsReducer.ValidateAggregate(statistics);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void SameChildAcrossParentsSlotsAndPathsStaysDistinctAndAbsentPathIsNotEmpty()
    {
        var statistics = Equipment();
        EquipmentStatisticsReducer.Observe(statistics, Snapshot(leftPathPresent: true, rightEmpty: false, "Original child"), 0);
        EquipmentStatisticsReducer.Observe(statistics, Snapshot(leftPathPresent: false, rightEmpty: true, "Enriched child"), 2);
        EquipmentStatisticsReducer.Advance(statistics, 5);

        var left = NestedState(
            statistics,
            "slot:left",
            "gear:same",
            "5:pouch",
            EquipmentSlotState.Occupied,
            "child:same");
        Assert.Equal(2, left.ActiveDurationSeconds);
        Assert.DoesNotContain(statistics.NestedSlotStates.Values, value => value.ParentSlotId == "slot:left"
            && value.Path == "5:pouch" && value.State == EquipmentSlotState.Empty);
        Assert.Equal(2, NestedState(
            statistics,
            "slot:right",
            "gear:same",
            "7:utility",
            EquipmentSlotState.Occupied,
            "child:same").ActiveDurationSeconds);
        Assert.Equal(3, NestedState(
            statistics,
            "slot:right",
            "gear:same",
            "7:utility",
            EquipmentSlotState.Empty).ActiveDurationSeconds);
        var otherParent = NestedState(
            statistics,
            "slot:back",
            "gear:other",
            "5:pouch",
            EquipmentSlotState.Occupied,
            "child:same");
        Assert.Equal(5, otherParent.ActiveDurationSeconds);
        Assert.Equal("Enriched child", otherParent.ItemDisplayName);
        Assert.Equal(2, Assert.Single(statistics.NestedSlotObservedDurations.Values,
            value => value.Id.Contains("slot:left", StringComparison.Ordinal)).ActiveDurationSeconds);
        EquipmentStatisticsReducer.ValidateAggregate(statistics);

        static EquipmentSnapshot Snapshot(bool leftPathPresent, bool rightEmpty, string childDisplayName)
        {
            var left = Root("slot:left", "gear:same", EquipmentItemKind.Armor, "sig:left");
            if (leftPathPresent)
                left.NestedSlots.Add(Nested("5:pouch", "pouch", "Pouch", "child:same", "Original child"));
            var right = Root("slot:right", "gear:same", EquipmentItemKind.Armor, "sig:right");
            right.NestedSlots.Add(Nested(
                "7:utility",
                "utility",
                "Utility",
                rightEmpty ? string.Empty : "child:same",
                rightEmpty ? string.Empty : "Original child"));
            var back = Root("slot:back", "gear:other", EquipmentItemKind.Backpack, "sig:back");
            back.NestedSlots.Add(Nested("5:pouch", "pouch", "Pouch", "child:same", childDisplayName));
            return new EquipmentSnapshot
            {
                SnapshotId = $"snapshot:{leftPathPresent}:{rightEmpty}:{childDisplayName}",
                LoadoutId = $"loadout:{leftPathPresent}:{rightEmpty}",
                TotemSetId = "totems:none",
                Items = [left, right, back],
                CharacterSlots =
                [
                    RootState("slot:left", "Left", "gear:same", EquipmentItemKind.Armor),
                    RootState("slot:right", "Right", "gear:same", EquipmentItemKind.Armor),
                    RootState("slot:back", "Back", "gear:other", EquipmentItemKind.Backpack)
                ],
                CharacterSlotStateComplete = true,
                NestedSlotStateComplete = true
            };
        }
    }

    [Fact]
    [Trait("Category", "M14")]
    public void LoadingAndRepeatedMapTransitionsSplitSlotStateWithoutAccruingLoadingTime()
    {
        var tracker = Start("A");
        var snapshot = EquipmentSnapshot("route", false, false, string.Empty, includeArmor: true);
        Assert.True(tracker.ObserveEquipment(snapshot, Now, 0));
        tracker.Tick(Now.AddSeconds(2), 2);
        Transition(tracker, 2, 8, "B");
        Assert.True(tracker.ObserveEquipment(snapshot, Now.AddSeconds(8), 8));
        tracker.Tick(Now.AddSeconds(10), 10);
        Transition(tracker, 10, 12, "A");
        Assert.True(tracker.ObserveEquipment(snapshot, Now.AddSeconds(12), 12));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 14)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation" };
        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(6, RootState(run.EquipmentStatistics, "slot:primary", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(6, NestedState(
            run.EquipmentStatistics,
            "slot:secondary",
            "weapon:secondary",
            "5:scope",
            EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal([2d, 2d, 2d], run.Segments.Select(segment => RootState(
            segment.EquipmentStatistics,
            "slot:primary",
            EquipmentSlotState.Empty).ActiveDurationSeconds));
        Assert.Equal(6, RootState(
            profile.RunTotals.Maps["duckov:map:A"].EquipmentStatistics,
            "slot:primary",
            EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(4, RootState(
            profile.RunTotals.RouteMaps["duckov:map:A"].EquipmentStatistics,
            "slot:primary",
            EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(2, RootState(
            profile.RunTotals.RouteMaps["duckov:map:B"].EquipmentStatistics,
            "slot:primary",
            EquipmentSlotState.Empty).ActiveDurationSeconds);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void IncompleteSlotEvidenceDegradesOnlyItsOwnDimensionAndNeverCreatesEmptyTime()
    {
        var statistics = Equipment();
        var snapshot = EquipmentSnapshot("incomplete", false, false, string.Empty, includeArmor: true);
        snapshot.CharacterSlotStateComplete = false;
        snapshot.CharacterSlots.Clear();

        EquipmentStatisticsReducer.Observe(statistics, snapshot, 0);
        EquipmentStatisticsReducer.Advance(statistics, 5);

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, statistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported, statistics.Capabilities.NestedSlotState.State);
        Assert.Empty(statistics.CharacterSlotStates);
        Assert.NotEmpty(statistics.NestedSlotStates);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void IncompleteSiblingEvidenceRetainsIndividuallyProvenSlotsAndPaths()
    {
        var statistics = Equipment();
        var snapshot = EquipmentSnapshot("partial", false, false, "child:known", includeArmor: true);
        snapshot.CharacterSlotStateComplete = false;
        snapshot.CharacterSlots.RemoveAll(value => value.SlotId == "slot:armor");
        snapshot.NestedSlotStateComplete = false;
        snapshot.Items.Single(value => value.SlotId == "slot:armor").NestedSlotStateComplete = false;

        EquipmentStatisticsReducer.Observe(statistics, snapshot, 0);
        EquipmentStatisticsReducer.Advance(statistics, 3);

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, statistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, statistics.Capabilities.NestedSlotState.State);
        Assert.Equal(3, RootState(statistics, "slot:primary", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.DoesNotContain(statistics.CharacterSlotStates.Values, value => value.SlotId == "slot:armor");
        Assert.Equal(3, NestedState(
            statistics,
            "slot:secondary",
            "weapon:secondary",
            "5:scope",
            EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.DoesNotContain(statistics.NestedSlotStates.Values, value => value.ParentSlotId == "slot:armor");
        EquipmentStatisticsReducer.ValidateAggregate(statistics);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void PairingAndNestedCapabilityDegradationLeaveIndependentProvenDimensionsExact()
    {
        var weapon = new WeaponStatisticsAggregate();
        WeaponStatisticsReducer.Apply(
            weapon,
            Shot("degraded-pair", null, "weapon:known", "Known weapon", "ammo:known", "Known ammo", pairingSupported: false));

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, weapon.Capabilities.WeaponAmmunitionPairing.State);
        Assert.Equal(1, weapon.Weapons["weapon:known"].Totals.FiringActions);
        Assert.Equal(1, weapon.AmmunitionTypes["ammo:known"].Totals.FiringActions);
        Assert.Empty(weapon.WeaponAmmunitionPairs);
        Assert.Equal(1, weapon.UncorrelatedFiringActions);
        Assert.Equal(1, weapon.UncorrelatedWeaponFiringActions["weapon:known"]);
        Assert.Equal(1, weapon.UncorrelatedAmmunitionFiringActions["ammo:known"]);
        WeaponStatisticsReducer.ValidateAggregate(weapon);

        var equipment = Equipment();
        var snapshot = EquipmentSnapshot("nested-degraded", false, false, string.Empty, includeArmor: true);
        snapshot.NestedSlotStateComplete = false;
        foreach (var item in snapshot.Items)
            item.NestedSlotStateComplete = false;
        EquipmentStatisticsReducer.Observe(equipment, snapshot, 0);
        EquipmentStatisticsReducer.Advance(equipment, 2);

        Assert.Equal(AdapterCapabilityState.Supported, equipment.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, equipment.Capabilities.NestedSlotState.State);
        Assert.Equal(2, RootState(equipment, "slot:primary", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Empty(equipment.NestedSlotStates);
        EquipmentStatisticsReducer.ValidateAggregate(equipment);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void SlotStateOverflowIsRejectedBeforeClockOrAggregateMutation()
    {
        var statistics = Equipment();
        EquipmentStatisticsReducer.Observe(
            statistics,
            EquipmentSnapshot("overflow", false, false, string.Empty, includeArmor: true),
            0);
        EquipmentStatisticsReducer.Advance(statistics, 1);
        var root = RootState(statistics, "slot:primary", EquipmentSlotState.Empty);
        root.ActiveDurationSeconds = double.MaxValue;

        Assert.Throws<OverflowException>(() => EquipmentStatisticsReducer.Advance(statistics, double.MaxValue));

        Assert.Equal(1, statistics.ObservedActiveDurationSeconds);
        Assert.Equal(double.MaxValue, root.ActiveDurationSeconds);
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Persistence")]
    public void PauseAndCheckpointRoundTripPreserveOnlyActiveRaidSlotTime()
    {
        var tracker = Start("A");
        tracker.ObserveEquipment(EquipmentSnapshot("checkpoint", false, false, string.Empty, includeArmor: true));
        tracker.Tick(Now.AddSeconds(2), 2);
        tracker.Apply(Event(RunLifecycleEventKind.PauseStarted, 2));
        tracker.Tick(Now.AddSeconds(7), 7);
        tracker.Apply(Event(RunLifecycleEventKind.PauseEnded, 7));
        var checkpoint = tracker.CreateCheckpoint(Now.AddSeconds(9), 9)!;
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ActiveRunCheckpoint));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, checkpoint);
        stream.Position = 0;
        var restored = Assert.IsType<ActiveRunCheckpoint>(serializer.ReadObject(stream));
        var interrupted = restored.ToInterruptedSummary();

        Assert.Equal(4, interrupted.ActiveDurationSeconds);
        Assert.Equal(4, RootState(interrupted.EquipmentStatistics, "slot:primary", EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(4, NestedState(interrupted.EquipmentStatistics, "slot:secondary", "weapon:secondary", "5:scope", EquipmentSlotState.Empty).ActiveDurationSeconds);
        EquipmentStatisticsReducer.ValidateRecoveryCandidate(interrupted.EquipmentStatistics, 14);
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Export")]
    public void UiJsonAndDedicatedCsvAgreeAndExposeBothPairProjectionsAndSlotStates()
    {
        var tracker = Start("A");
        tracker.RecordShot(Shot("pair", tracker, "weapon:a", "Weapon A", "ammo:x", "Ammo X"));
        tracker.ObserveEquipment(EquipmentSnapshot("export", false, true, string.Empty, includeArmor: true));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 4)).Completed!;
        var profile = Profile(run);
        var export = StatisticsExporter.Create(profile, Now.AddMinutes(1));
        var view = WeaponStatisticsViewModelFactory.Create(profile);

        Assert.Equal(1, Assert.Single(view.WeaponAmmunitionPairs).Pair.FiringActions);
        Assert.Contains("weapon_to_ammunition", export.WeaponAmmunitionPairsCsv);
        Assert.Contains("ammunition_to_weapon", export.WeaponAmmunitionPairsCsv);
        Assert.Contains("percentage_within_observed_projection_pairs", export.WeaponAmmunitionPairsCsv);
        Assert.Contains("route_segment", export.WeaponAmmunitionPairsCsv);
        Assert.Contains("slot:primary", export.CharacterEquipmentSlotsCsv);
        Assert.Contains(",Empty,", export.CharacterEquipmentSlotsCsv);
        Assert.Contains("5:scope", export.EquippedItemNestedSlotsCsv);
        Assert.Contains(",Occupied,", export.EquippedItemNestedSlotsCsv);
        Assert.Contains("\"WeaponAmmunitionPairs\"", export.Json);
        Assert.Contains("\"CharacterSlotStates\"", export.Json);
        Assert.Contains("\"NestedSlotStates\"", export.Json);

        var equipmentView = EquipmentStatisticsViewModelFactory.Create(profile);
        var weapon = Assert.Single(equipmentView.Weapons, value => value.WeaponId == "weapon:secondary");
        Assert.Equal(4, weapon.TotalEquippedDurationSeconds);
        Assert.Equal(4, Assert.Single(weapon.CharacterSlots).EquippedDurationSeconds);
        var scope = Assert.Single(weapon.NestedSlotGroups, value => value.GroupKey == "scope");
        Assert.Contains(scope.Rows, value => value.State == EquipmentSlotState.Occupied);
        Assert.Contains(equipmentView.ArmorAndGearSlots, value => value.SlotId == "slot:armor");
        Assert.DoesNotContain(equipmentView.ArmorAndGearSlots, value => value.SlotId == "slot:primary");
        Assert.Equal("No Scope", UiText.FormatProvenEmpty("Scope"));
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Export")]
    public void ExportRestrictsCompletedRouteSegmentM14CapabilitiesWithoutChangingRecordedValues()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordShot(Shot("segment-a", tracker, "weapon:a", "Weapon A", "ammo:x", "Ammo X")));
        Assert.True(tracker.ObserveEquipment(EquipmentSnapshot("segment-a", false, true, string.Empty, includeArmor: true)));
        Transition(tracker, 2, 4, "B");
        Assert.True(tracker.RecordShot(Shot("segment-b", tracker, "weapon:b", "Weapon B", "ammo:y", "Ammo Y")));
        Assert.True(tracker.ObserveEquipment(EquipmentSnapshot("segment-b", false, true, string.Empty, includeArmor: true)));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        var profile = Profile(run);
        profile.Capabilities =
        [
            Disabled(WeaponCapabilityIds.WeaponAmmunitionPairing),
            Disabled(EquipmentCapabilityIds.CharacterSlotState),
            Disabled(EquipmentCapabilityIds.NestedSlotState)
        ];

        var export = StatisticsExporter.Create(profile, Now.AddMinutes(1));
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            });
        using var jsonStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(export.Json));
        var exportedDocument = Assert.IsType<StatisticsExportDocument>(serializer.ReadObject(jsonStream));

        var exportedSegments = Assert.Single(exportedDocument.Runs).Segments;
        Assert.Equal(2, exportedSegments.Count);
        foreach (var segment in exportedSegments)
        {
            Assert.Equal(
                AdapterCapabilityState.DisabledIncompatible,
                segment.WeaponStatistics.Capabilities.WeaponAmmunitionPairing.State);
            Assert.Equal(
                AdapterCapabilityState.DisabledIncompatible,
                segment.EquipmentStatistics.Capabilities.CharacterSlotState.State);
            Assert.Equal(
                AdapterCapabilityState.DisabledIncompatible,
                segment.EquipmentStatistics.Capabilities.NestedSlotState.State);
            Assert.Equal(1, Assert.Single(segment.WeaponStatistics.WeaponAmmunitionPairs).Value.FiringActions);
            Assert.Equal(2, RootState(
                segment.EquipmentStatistics,
                "slot:primary",
                EquipmentSlotState.Empty).ActiveDurationSeconds);
            Assert.Equal(2, NestedState(
                segment.EquipmentStatistics,
                "slot:secondary",
                "weapon:secondary",
                "5:scope",
                EquipmentSlotState.Occupied).ActiveDurationSeconds);
        }

        var pairRows = RouteSegmentRows(export.WeaponAmmunitionPairsCsv);
        Assert.Equal(4, pairRows.Count);
        Assert.All(pairRows, row =>
        {
            Assert.Equal("1", row["accepted_firing_actions"]);
            Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), row["pairing_state"]);
        });
        var characterRows = RouteSegmentRows(export.CharacterEquipmentSlotsCsv);
        Assert.Equal(6, characterRows.Count);
        Assert.All(characterRows, row =>
        {
            Assert.Equal("2", row["active_duration_seconds"]);
            Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), row["capability_state"]);
        });
        var nestedRows = RouteSegmentRows(export.EquippedItemNestedSlotsCsv);
        Assert.Equal(6, nestedRows.Count);
        Assert.All(nestedRows, row =>
        {
            Assert.Equal("2", row["active_duration_seconds"]);
            Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), row["capability_state"]);
        });

        Assert.All(run.Segments, segment =>
        {
            Assert.Equal(
                AdapterCapabilityState.Supported,
                segment.WeaponStatistics.Capabilities.WeaponAmmunitionPairing.State);
            Assert.Equal(
                AdapterCapabilityState.Supported,
                segment.EquipmentStatistics.Capabilities.CharacterSlotState.State);
            Assert.Equal(
                AdapterCapabilityState.Supported,
                segment.EquipmentStatistics.Capabilities.NestedSlotState.State);
        });

        static CapabilityRecord Disabled(string adapterId) => new()
        {
            AdapterId = adapterId,
            State = AdapterCapabilityState.DisabledIncompatible,
            Detail = "Current native adapter is incompatible."
        };

        static IReadOnlyList<IReadOnlyDictionary<string, string>> RouteSegmentRows(string csv)
        {
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r').Split(','))
                .ToArray();
            var headers = lines[0];
            return lines.Skip(1)
                .Where(row => row[0] == "route_segment")
                .Select(row => (IReadOnlyDictionary<string, string>)headers
                    .Select((header, index) => (header, value: row[index]))
                    .ToDictionary(value => value.header, value => value.value, StringComparer.Ordinal))
                .ToArray();
        }
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Persistence")]
    public void SchemaThirteenMigrationPreservesExactSignaturesAndMarksEveryM14ScopeHistoricallyUnavailable()
    {
        var tracker = Start("A", pairingSupported: false);
        tracker.RecordShot(Shot("old", tracker, "weapon:a", "Weapon A", "ammo:x", "Ammo X", pairingSupported: false));
        tracker.ObserveEquipment(LegacyEquipmentSnapshot("legacy-a", "signature:irreversible"));
        Transition(tracker, 2, 4, "B");
        tracker.ObserveEquipment(LegacyEquipmentSnapshot("legacy-b", "signature:other"));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        run.SchemaVersion = 13;
        var profile = Profile(run);
        profile.SchemaVersion = 13;
        profile.Statistics.SchemaVersion = 13;

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(14, profile.SchemaVersion);
        Assert.Equal(14, profile.Statistics.SchemaVersion);
        Assert.Equal(14, Assert.Single(profile.Statistics.Runs).SchemaVersion);
        foreach (var scope in M14Scopes(profile))
        {
            Assert.True(scope.Weapon.HistoricalPairingUnavailable);
            Assert.True(scope.Equipment.HistoricalCharacterSlotStateUnavailable);
            Assert.True(scope.Equipment.HistoricalNestedSlotStateUnavailable);
            Assert.Empty(scope.Weapon.WeaponAmmunitionPairs);
        }
        Assert.Contains(
            profile.Statistics.RunTotals.EquipmentStatistics.Items.Values,
            value => value.Id.Contains("signature:irreversible", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Recovery")]
    public void CurrentSchemaRecoveryRejectsPairReconciliationDamageBeforeSelection()
    {
        var tracker = Start("A");
        tracker.RecordShot(Shot("pair", tracker, "weapon:a", "Weapon A", "ammo:x", "Ammo X"));
        var profile = Profile(tracker.Apply(Event(RunLifecycleEventKind.Extracted, 2)).Completed!);
        Assert.Null(ProfileMigrator.ValidateRecoveryCandidate(profile));

        profile.Statistics.RunTotals.WeaponStatistics.WeaponAmmunitionPairs.Values.Single().FiringActions = 2;

        var failure = ProfileMigrator.ValidateRecoveryCandidate(profile);
        Assert.NotNull(failure);
        Assert.Contains("invalid M14 association state", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("character")]
    [InlineData("nested")]
    [Trait("Category", "M14")]
    [Trait("Category", "Recovery")]
    public void CurrentSchemaRecoveryRejectsSlotStateReconciliationDamageBeforeSelection(string dimension)
    {
        var tracker = Start("A");
        tracker.ObserveEquipment(EquipmentSnapshot("recovery", false, false, string.Empty, includeArmor: true));
        var profile = Profile(tracker.Apply(Event(RunLifecycleEventKind.Extracted, 2)).Completed!);
        Assert.Null(ProfileMigrator.ValidateRecoveryCandidate(profile));

        if (dimension == "character")
            profile.Statistics.RunTotals.EquipmentStatistics.CharacterSlotStates.Values.First().ActiveDurationSeconds += 1;
        else
            profile.Statistics.RunTotals.EquipmentStatistics.NestedSlotStates.Values.First().ActiveDurationSeconds += 1;

        var failure = ProfileMigrator.ValidateRecoveryCandidate(profile);
        Assert.NotNull(failure);
        Assert.Contains("invalid M14 association state", failure, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Persistence")]
    public void ProfileRestartResetNewGameAndSlotSelectionKeepM14DataInItsExactGeneration()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(
        [
            "generation", "session-one", "session-two", "reset-generation", "session-three",
            "new-game-generation", "session-four", "slot-two-generation", "session-five"
        ]);
        var repository = new ProfileRepository(directory.Path, () => Now, () => ids.Dequeue());
        repository.Open(Identity(1, 100));
        var tracker = Start("A");
        tracker.RecordShot(Shot("persisted", tracker, "weapon:a", "A", "ammo:x", "X"));
        tracker.ObserveEquipment(EquipmentSnapshot("persisted", false, false, string.Empty, includeArmor: true));
        Assert.True(repository.CompleteRun(tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!));
        repository.CloseClean();

        var restarted = new ProfileRepository(directory.Path, () => Now, () => ids.Dequeue());
        restarted.Open(Identity(1, 100));
        Assert.Equal(1, Pair(restarted.Current.Statistics.RunTotals.WeaponStatistics, "weapon:a", "ammo:x").FiringActions);
        Assert.Equal(3, RootState(
            restarted.Current.Statistics.RunTotals.EquipmentStatistics,
            "slot:primary",
            EquipmentSlotState.Empty).ActiveDurationSeconds);

        restarted.Rotate(Identity(1, 100), "UserReset");
        AssertCurrentM14Empty(restarted.Current);
        restarted.Rotate(Identity(1, 200), "NewGame");
        AssertCurrentM14Empty(restarted.Current);
        restarted.Open(Identity(2, 300), "SlotSelection");
        AssertCurrentM14Empty(restarted.Current);
        restarted.CloseClean();

        Assert.False(File.Exists(Path.Combine(
            directory.Path,
            "profiles",
            "slot-02",
            "current",
            "session.json")));
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Recovery")]
    public void ValidTemporaryCheckpointDefeatsPairDamagedPrimaryAndSlotDamagedBackup()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProfileRepository(directory.Path, () => Now, () => "generation");
        repository.Open(Identity(1, 100));
        var tracker = Start("A");
        tracker.RecordShot(Shot("checkpoint", tracker, "weapon:a", "A", "ammo:x", "X"));
        tracker.ObserveEquipment(EquipmentSnapshot("checkpoint", false, false, string.Empty, includeArmor: true));
        tracker.Tick(Now.AddSeconds(5), 5);
        repository.SaveActiveRun(tracker.CreateCheckpoint(Now.AddSeconds(5), 5)!);
        tracker.Tick(Now.AddSeconds(7), 7);
        var validTemporary = tracker.CreateCheckpoint(Now.AddSeconds(7), 7)!;
        repository.CloseClean();

        var path = Path.Combine(directory.Path, "profiles", "slot-01", "current", "active-run.json");
        var store = new AtomicJsonStore<ActiveRunCheckpoint>();
        var invalidSlotBackup = Clone(validTemporary);
        invalidSlotBackup.EquipmentStatistics.CharacterSlotStates.Values.First().ActiveDurationSeconds += 1;
        store.Save(path, invalidSlotBackup);
        var invalidPairPrimary = Clone(validTemporary);
        invalidPairPrimary.WeaponStatistics.WeaponAmmunitionPairs.Values.First().FiringActions += 1;
        store.Save(path, invalidPairPrimary);
        store.Save(AtomicJsonPaths.GetTemporaryPath(path), validTemporary);

        var diagnostics = new List<string>();
        var recovery = new ProfileRepository(
            directory.Path,
            () => Now.AddMinutes(1),
            () => "unused",
            diagnostics.Add);
        var result = recovery.Open(Identity(1, 100));

        Assert.True(result.InterruptedRunRecovered);
        Assert.Contains(diagnostics, value => value.Contains("M14 association state is invalid", StringComparison.Ordinal));
        var interrupted = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(1, Pair(interrupted.WeaponStatistics, "weapon:a", "ammo:x").FiringActions);
        Assert.Equal(7, RootState(
            interrupted.EquipmentStatistics,
            "slot:primary",
            EquipmentSlotState.Empty).ActiveDurationSeconds);
        Assert.Equal(RunOutcome.Interrupted, interrupted.Outcome);
        recovery.CloseClean();
    }

    private static RunLifecycleTracker Start(string map, bool pairingSupported = true)
    {
        var tracker = new RunLifecycleTracker(() => "run-m14");
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0));
        var weapon = WeaponNativeContractPolicy.CreateMetricCapabilities();
        if (!pairingSupported) weapon.WeaponAmmunitionPairing = Unavailable("pre-M14");
        tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 0, context: new RunStartContext
        {
            SaveGenerationId = "generation",
            NativeRaidId = "raid",
            Map = Map(map),
            IntegrityTags = IntegrityTags.Normal,
            GameVersion = "2.3.30",
            GameBuild = "24013657",
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported,
            MapCapability = AdapterCapabilityState.Supported,
            RouteCapabilities = RouteStatisticsReducer.Supported("test"),
            WeaponCapabilities = weapon,
            EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
        }));
        return tracker;
    }

    private static void Transition(RunLifecycleTracker tracker, double start, double ready, string map)
    {
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, start));
        tracker.Apply(Event(RunLifecycleEventKind.LoadingEnded, ready - 0.1));
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, ready, map: Map(map)));
    }

    private static RunLifecycleEvent Event(
        RunLifecycleEventKind kind,
        double seconds,
        RunStartContext? context = null,
        MapIdentity? map = null) => new()
        {
            Kind = kind,
            TimestampUtc = Now.AddSeconds(seconds),
            MonotonicSeconds = seconds,
            NativeRaidId = "raid",
            StartContext = context,
            Map = map
        };

    private static MapIdentity Map(string id) => new()
    {
        MapId = "duckov:map:" + id,
        DisplayName = id,
        IsKnown = true
    };

    private static ShotRecorded Shot(
        string eventId,
        RunLifecycleTracker? tracker,
        string weaponId,
        string weaponName,
        string ammunitionId,
        string ammunitionName,
        bool pairingSupported = true)
    {
        var capabilities = WeaponNativeContractPolicy.CreateMetricCapabilities();
        if (!pairingSupported) capabilities.WeaponAmmunitionPairing = Unavailable("pre-M14");
        return new ShotRecorded
        {
            EventId = eventId,
            TimestampUtc = Now,
            SaveGenerationId = "generation",
            RunId = "run-m14",
            MapId = tracker?.ActiveMapId ?? "duckov:map:A",
            SegmentId = tracker?.ActiveSegmentId,
            GameplayContext = GameplayContext.Raid,
            WeaponId = weaponId,
            WeaponDisplayName = weaponName,
            AmmunitionId = ammunitionId,
            AmmunitionDisplayName = ammunitionName,
            FiringActionCount = 1,
            Capabilities = capabilities
        };
    }

    private static EquipmentStatisticsAggregate Equipment() => new()
    {
        Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
    };

    private static EquipmentSnapshot EquipmentSnapshot(
        string id,
        bool primaryOccupied,
        bool scopeOccupied,
        string pouchItem,
        bool includeArmor)
    {
        var items = new List<EquippedItemSnapshot>();
        var roots = new List<CharacterEquipmentSlotSnapshot>();
        if (primaryOccupied)
        {
            items.Add(Root("slot:primary", "weapon:primary", EquipmentItemKind.Weapon, "sig:primary"));
            roots.Add(RootState("slot:primary", "Primary", "weapon:primary", EquipmentItemKind.Weapon));
        }
        else roots.Add(EmptyRoot("slot:primary", "Primary"));

        var secondary = Root("slot:secondary", "weapon:secondary", EquipmentItemKind.Weapon, "sig:secondary");
        secondary.NestedSlots.Add(Nested("5:scope", "scope", "Scope", scopeOccupied ? "attachment:scope" : string.Empty));
        items.Add(secondary);
        roots.Add(RootState("slot:secondary", "Secondary", "weapon:secondary", EquipmentItemKind.Weapon));

        if (includeArmor)
        {
            var armor = Root("slot:armor", "armor:modded", EquipmentItemKind.Armor, "sig:armor");
            armor.NestedSlots.Add(Nested("5:pouch", "pouch", "Pouch", pouchItem));
            armor.NestedSlots.Add(Nested("7:utility", "utility", "Utility", string.Empty));
            items.Add(armor);
            roots.Add(RootState("slot:armor", "Armor", "armor:modded", EquipmentItemKind.Armor));
        }
        else roots.Add(EmptyRoot("slot:armor", "Armor"));

        return new EquipmentSnapshot
        {
            SnapshotId = "snapshot:" + id,
            LoadoutId = "loadout:" + id,
            TotemSetId = "totems:none",
            Items = items,
            CharacterSlots = roots,
            CharacterSlotStateComplete = true,
            NestedSlotStateComplete = true
        };
    }

    private static EquipmentSnapshot LegacyEquipmentSnapshot(string id, string signature) => new()
    {
        SnapshotId = "snapshot:" + id,
        LoadoutId = "loadout:" + id,
        TotemSetId = "totems:none",
        Items = new List<EquippedItemSnapshot>
        {
            new()
            {
                SlotId = "slot:primary",
                SlotDisplayName = "Primary",
                ItemId = "weapon:a",
                ItemDisplayName = "Weapon A",
                Kind = EquipmentItemKind.Weapon,
                AttachmentSignature = signature
            }
        }
    };

    private static EquippedItemSnapshot Root(string slot, string item, EquipmentItemKind kind, string signature) => new()
    {
        SlotId = slot,
        SlotDisplayName = slot,
        ItemId = item,
        ItemDisplayName = item,
        Kind = kind,
        AttachmentSignature = signature,
        NestedSlotStateComplete = true
    };

    private static CharacterEquipmentSlotSnapshot RootState(
        string slot,
        string name,
        string item,
        EquipmentItemKind kind) => new()
        {
            SlotId = slot,
            SlotDisplayName = name,
            State = EquipmentSlotState.Occupied,
            ItemId = item,
            ItemDisplayName = item,
            ItemKind = kind
        };

    private static CharacterEquipmentSlotSnapshot EmptyRoot(string slot, string name) => new()
    {
        SlotId = slot,
        SlotDisplayName = name,
        State = EquipmentSlotState.Empty
    };

    private static NestedEquipmentSlotSnapshot Nested(
        string path,
        string key,
        string name,
        string item,
        string? itemDisplayName = null) => new()
        {
            Path = path,
            SlotKey = key,
            SlotDisplayName = name,
            State = string.IsNullOrEmpty(item) ? EquipmentSlotState.Empty : EquipmentSlotState.Occupied,
            ItemId = item,
            ItemDisplayName = string.IsNullOrEmpty(item) ? string.Empty : itemDisplayName ?? item
        };

    private static CharacterSlotStateDurationAggregate RootState(
        EquipmentStatisticsAggregate statistics,
        string slot,
        EquipmentSlotState state) => Assert.Single(statistics.CharacterSlotStates.Values, value =>
            value.SlotId == slot && value.State == state);

    private static NestedSlotStateDurationAggregate NestedState(
        EquipmentStatisticsAggregate statistics,
        string parentSlot,
        string parentItem,
        string path,
        EquipmentSlotState state,
        string item = "") => Assert.Single(statistics.NestedSlotStates.Values, value =>
            value.ParentSlotId == parentSlot && value.ParentItemId == parentItem && value.Path == path
            && value.State == state && (string.IsNullOrEmpty(item) || value.ItemId == item));

    private static WeaponAmmunitionPairAggregate Pair(
        WeaponStatisticsAggregate statistics,
        string weapon,
        string ammunition) => statistics.WeaponAmmunitionPairs[
            WeaponStatisticsReducer.PairKey(weapon, ammunition)];

    private static MetricAvailability Unavailable(string provenance) => new()
    {
        State = AdapterCapabilityState.DisabledIncompatible,
        Provenance = provenance
    };

    private static SaveIdentitySnapshot Identity(int slot, long creationTicks) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = creationTicks,
        ObservedWriteUtcTicks = creationTicks + 10,
        ObservedLength = 4096,
        GameVersion = "2.3.30",
        ContentSha256 = creationTicks.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0'),
        SaveTimeBinary = Now.AddTicks(creationTicks).ToBinary()
    };

    private static T Clone<T>(T value)
    {
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, value);
        stream.Position = 0;
        return Assert.IsType<T>(serializer.ReadObject(stream));
    }

    private static void AssertCurrentM14Empty(ProfileDocument profile)
    {
        Assert.True(WeaponStatisticsReducer.IsEmpty(profile.Statistics.RunTotals.WeaponStatistics));
        Assert.True(EquipmentStatisticsReducer.IsEmpty(profile.Statistics.RunTotals.EquipmentStatistics));
    }

    private static ProfileDocument Profile(RunSummary run)
    {
        var profile = new ProfileDocument
        {
            GenerationId = "generation",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Identity = new SaveIdentitySnapshot(),
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = "generation",
                CreatedUtc = Now,
                UpdatedUtc = Now
            },
            Capabilities = WeaponNativeContractPolicy.CreateMetricCapabilities()
                .Let(value => new List<CapabilityRecord>
                {
                    new()
                    {
                        AdapterId = WeaponCapabilityIds.WeaponAmmunitionPairing,
                        State = value.WeaponAmmunitionPairing.State,
                        Detail = value.WeaponAmmunitionPairing.Provenance
                    }
                })
        };
        RunReducer.Apply(profile.Statistics, run);
        return profile;
    }

    private static IEnumerable<(WeaponStatisticsAggregate Weapon, EquipmentStatisticsAggregate Equipment)> M14Scopes(
        ProfileDocument profile)
    {
        yield return (profile.Statistics.RunTotals.WeaponStatistics, profile.Statistics.RunTotals.EquipmentStatistics);
        foreach (var map in profile.Statistics.RunTotals.Maps.Values) yield return (map.WeaponStatistics, map.EquipmentStatistics);
        foreach (var map in profile.Statistics.RunTotals.RouteMaps.Values) yield return (map.WeaponStatistics, map.EquipmentStatistics);
        foreach (var run in profile.Statistics.Runs)
        {
            yield return (run.WeaponStatistics, run.EquipmentStatistics);
            foreach (var segment in run.Segments) yield return (segment.WeaponStatistics, segment.EquipmentStatistics);
        }
    }
}

internal static class M14TestExtensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> projection) => projection(value);
}
