using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

#pragma warning disable CA1861
public sealed class RetainedEquipmentTests
{
    private static ProfileDocument Profile()
    {
        var profile = new ProfileDocument { GenerationId = "g", Statistics = new ProfileStatistics { SaveGenerationId = "g" } };
        profile.Capabilities = EquipmentNativeContractPolicy.ToRecords(EquipmentNativeContractPolicy.CreateSupportedCapabilities(), "test").ToList();
        return profile;
    }
    private static StatisticsPanelProjection Projection(ProfileDocument? profile = null) => StatisticsPanelProjectionFactory.Create(profile ?? Profile(), new(), new(), new());
    private static EquipmentPresentation Present(ProfileDocument? profile = null) => EquipmentPresentationFactory.Create(Projection(profile), "g")!;
    private static EquipmentStatisticsAggregate Observe(ProfileDocument p, EquipmentSnapshot? snapshot = null, double duration = 10)
    {
        var a = p.Statistics.RunTotals.EquipmentStatistics; a.Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities();
        EquipmentStatisticsReducer.Observe(a, snapshot ?? EquipmentCompositionTests.Snapshot(), 0); EquipmentStatisticsReducer.Advance(a, duration); return a;
    }
    private static float Measure(string text, float width, float size) => Math.Max(1, MathF.Ceiling(text.Length * size * .5f / Math.Max(1, width))) * size * 1.2f;
    private static EquipmentDocument Document(EquipmentPresentation p, EquipmentPanelSection page, bool right = false, bool narrow = false, bool expand = false)
    {
        var s = new EquipmentSelection(); s.Refresh(p); s.SelectPage(page);
        if (expand) foreach (var id in p.ExpansionIds) s.Toggle("g", id);
        var d = new EquipmentDocument(Measure); d.Page(s, right, narrow ? 900 : 1400, narrow); return d;
    }
    [Fact] public void FactoryBindsCompletePublicationAndRejectsMixedOrLostGeneration()
    {
        var p = Projection(); Assert.NotNull(EquipmentPresentationFactory.Create(p, "g"));
        Assert.Null(EquipmentPresentationFactory.Create(p, "other"));
        p.Profile.Statistics.SaveGenerationId = "lost"; Assert.Null(EquipmentPresentationFactory.Create(p, "g"));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void ReplacedPublicationMemberFailsBinding(int field)
    {
        var p = Projection();
        switch (field)
        {
            case 0: p.Equipment = new(); break;
            case 1: p.RecurringLoadouts = new List<EquipmentDurationAggregate>(); break;
            case 2: p.RecentEquipmentRuns = new List<RunSummary>(); break;
            case 3: p.Equipment.Lifetime.Composition.TotemStates = new(); break;
            case 4: p.Equipment.Lifetime.Composition.Loadouts = new(); break;
            case 5: p.Equipment.Lifetime.Composition.ActiveTotemSets = new(); break;
            default: p.Runs.Runs = new List<RunSummary>(); break;
        }
        Assert.Null(EquipmentPresentationFactory.Create(p, "g"));
    }
    [Fact] public void LongerSingleRunLoadoutWinsAndDefinitionCopiesAreImmutable()
    {
        var p = Profile(); var a = Observe(p, duration: 999); var id = a.Loadouts.Keys.Single(); a.Loadouts[id].RunOccurrences = 1;
        a.Loadouts.Add("recurring", new EquipmentDurationAggregate { Id = "recurring", ActiveDurationSeconds = 10, RunOccurrences = 3 });
        var presented = Present(p); Assert.Equal(999, presented.MostUsed!.Duration); Assert.Equal("Used in 1 run", presented.MostUsed.Caption);
        Assert.Equal(2, presented.MostUsed.Slots.Count); Assert.False(presented.MostUsed.Slots[0].NestedComplete);
        a.Composition.Loadouts[id].Items[0].NestedSlots.Clear(); a.Composition.Loadouts[id].Roots.Clear();
        Assert.Equal(2, presented.MostUsed.Slots.Count); Assert.Single(presented.MostUsed.Slots[0].Attachments);
    }
    [Fact] public void MostUsedUsesOrdinalIdForEqualDurationAndKeepsActualCount()
    {
        var p = Profile(); var rows = p.Statistics.RunTotals.EquipmentStatistics.Loadouts;
        rows.Add("a", new EquipmentDurationAggregate { Id = "a", ActiveDurationSeconds = 50, RunOccurrences = 7 });
        rows.Add("Z", new EquipmentDurationAggregate { Id = "Z", ActiveDurationSeconds = 50, RunOccurrences = 1 });
        Assert.Equal("Used in 1 run", Present(p).MostUsed!.Caption);
        rows["Z"].RunOccurrences = 0;
        Assert.Equal("Used in 0 runs", Present(p).MostUsed!.Caption);
    }
    [Fact] public void SingleRunHistoricalWinnerStaysUnavailableAndRecurringExportKeepsItsFilter()
    {
        var p = Profile(); var a = Observe(p); a.Loadouts.Values.Single().RunOccurrences = 3;
        a.Loadouts.Add("long-single", new EquipmentDurationAggregate { Id = "long-single", ActiveDurationSeconds = 500, RunOccurrences = 1 });
        var projection = Projection(p);
        Assert.DoesNotContain(projection.RecurringLoadouts, r => r.Id == "long-single");
        var card = EquipmentPresentationFactory.Create(projection, "g")!.MostUsed!;
        Assert.Equal(500, card.Duration); Assert.Equal("Used in 1 run", card.Caption);
        Assert.Empty(card.Slots); Assert.Contains("unavailable", card.Notice);
        var csv = UltimateDuckovStatistics.Core.Export.StatisticsExporter.Create(p, DateTime.UnixEpoch).RecurringLoadoutsCsv;
        Assert.DoesNotContain("long-single", csv); Assert.Contains(a.Loadouts.Keys.First(), csv);
        Assert.Equal(500, a.Loadouts["long-single"].ActiveDurationSeconds);
        Assert.Equal(1, a.Loadouts["long-single"].RunOccurrences);
    }
    [Fact] public void ReplacedLifetimeLoadoutPublicationFailsBindingAndEmptyStateHasNoRunThreshold()
    {
        var projection = Projection(); projection.Equipment.Lifetime.Loadouts = new();
        Assert.Null(EquipmentPresentationFactory.Create(projection, "g"));
        var doc = Document(Present(), EquipmentPanelSection.Loadouts);
        Assert.Contains(doc.Rows, r => r.Name == "No loadout observations recorded");
        Assert.DoesNotContain(doc.Rows, r => r.Name.Contains("recurring", StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void HistoricalLoadoutKeepsDurationAndNeverParsesDescription()
    {
        var p = Profile(); p.Statistics.RunTotals.EquipmentStatistics.Loadouts.Add("old", new EquipmentDurationAggregate {
            Id = "old", DisplayName = "weapon:99; Scope", ActiveDurationSeconds = 20, RunOccurrences = 3 });
        var result = Present(p).MostUsed!; Assert.Equal(20, result.Duration); Assert.Empty(result.Slots);
        Assert.Contains("unavailable", result.Notice); Assert.DoesNotContain("Scope", result.Name);
    }
    [Fact] public void SelectedTimeGroupsSlotsAndRemainsDifferentFromEquippedDuration()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot(); s.SelectedWeaponId = s.Items[0].ItemId; s.SelectedWeaponSlotId = s.Items[0].SlotId;
        var a = Observe(p, s); a.SelectedWeapons.Values.Single().ActiveDurationSeconds = 3;
        a.SelectedWeapons.Add("other|duckov:weapon:1", new EquipmentDurationAggregate { Id = "other|duckov:weapon:1", ActiveDurationSeconds = 2 });
        var result = Present(p); Assert.Equal(5, result.SelectedWeapons.Single().Duration); Assert.Equal(10, result.Weapons.Single().Duration);
        Assert.Equal(2, result.SelectedWeapons.Single().Groups.Single().Rows.Count); Assert.Contains("selected", result.SelectedWeapons.Single().Caption);
    }
    [Fact] public void RecentRunUsesOwnMostUsedAndExactRouteNeverTerminalOrNewestFallback()
    {
        var p = Profile(); var a = Observe(p); var run = new RunSummary { RunId = "old", SaveGenerationId = "g", StartedUtc = new DateTime(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc), EndedUtc = DateTime.UtcNow,
            MapDisplayName = "Route", EquipmentStatistics = EquipmentStatisticsReducer.Clone(a) };
        run.EquipmentStatistics.Loadouts.Add("history", new EquipmentDurationAggregate { Id = "history", ActiveDurationSeconds = 99 });
        p.Statistics.Runs.Add(run); var result = Present(p);
        Assert.Equal(99, result.Recent.Single().Duration); Assert.Empty(result.Recent.Single().Slots);
        Assert.True(result.CanRoute("g", "old")); Assert.False(result.CanRoute("other", "old")); Assert.False(result.CanRoute("g", "missing"));
        Assert.Contains("Most used during this run", result.Recent[0].Caption);
        Assert.StartsWith(run.StartedUtc.ToLocalTime().ToString("dd.MM.yyyy - HH:mm", System.Globalization.CultureInfo.InvariantCulture), result.Recent[0].Caption);
    }
    [Fact] public void WeaponsOrderAndNestedStatePreserveEmptyPartialAndModdedGroups()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot(); s.Items[0].NestedSlots.Add(new NestedEquipmentSlotSnapshot {
            Path = "3:Mod/", SlotKey = "Mod", SlotDisplayName = "Modded", ItemId = "mod:1", ItemDisplayName = "Attachment" });
        Observe(p, s); var result = Present(p); var w = result.Weapons.Single();
        Assert.Contains(w.Groups, g => g.Name == "Scope" && g.Rows.Single().Name == "Nothing equipped");
        Assert.Contains(w.Groups, g => g.Name == "Modded" && g.Rows.Single().ItemId == "mod:1");
        Assert.True(w.Expandable); Assert.Equal(6, EquipmentPresentationFactory.NestedOrder("Mod"));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, new[] { "Scope", "Muzzle", "Grip", "Stock", "Tactics", "Magazine" }.Select(EquipmentPresentationFactory.NestedOrder));
    }
    [Fact] public void ArmorExcludesKnownWeaponAndTotemSlotsButRetainsUnknownEmpty()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot(); Observe(p, s);
        var result = Present(p); Assert.Single(result.Armor); Assert.Equal("Modded slot", result.Armor[0].Name);
        Assert.Equal("Nothing equipped", result.Armor[0].Rows[0].Name); Assert.False(result.Armor[0].Rows[0].Expandable);
    }
    [Fact] public void TotemStatesSeparateInactiveDirectPresenceAndUnknownToteFromActiveSets()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot();
        s.Totems[0].ActivationState = TotemActivationState.ProvenInactive;
        s.Totems.Add(new TotemSnapshot { ItemId = "duckov:totem:3", DisplayName = "Carried", CarryKind = TotemCarryKind.ToteInventory,
            ContainerId = "duckov:tote:1255", ActivationState = TotemActivationState.Unknown });
        s.TotemSetId = EquipmentIdentity.ActiveTotemSetId(s.Totems); Observe(p, s);
        var result = Present(p); Assert.Single(result.DirectTotems); Assert.Single(result.ToteTotems); Assert.Empty(result.ActiveSets);
        Assert.Contains("Proven inactive", result.DirectTotems[0].Groups[0].Rows[0].Caption); Assert.Contains("carried", result.ToteTotems[0].Caption);
        var doc = Document(result, EquipmentPanelSection.Totems, true); Assert.Contains(doc.Rows, r => r.Name == "Presence is tracked; effect activation is unknown");
    }
    [Fact] public void SingletonWordingRequiresExactDefinitionAndNeverHumanDescription()
    {
        var p = Profile(); var a = Observe(p); var exact = Present(p); Assert.Contains("No other active totem", exact.ActiveSets[0].Notice);
        a.Composition.ActiveTotemSets.Clear(); a.TotemSets.Values.Single().DisplayName = "One totem";
        Assert.DoesNotContain("No other active totem", Present(p).ActiveSets[0].Notice);
    }
    [Fact] public void EmptyDirectTimeRequiresTypedProofNotLocalizedEmptyName()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot(); s.CharacterSlots[1].SlotDisplayName = "Totem slot 1"; Observe(p, s);
        Assert.Empty(Present(p).EmptySlots);
        var doc = Document(Present(p), EquipmentPanelSection.Totems); Assert.Contains(doc.Rows, r => r.Name == "Unavailable");
    }
    [Fact] public void CurrentDegradationDoesNotHideHistoricalValuesOrValidSibling()
    {
        var p = Profile(); Observe(p); p.Capabilities.Single(c => c.AdapterId == EquipmentCapabilityIds.DirectTotems).State = AdapterCapabilityState.DisabledIncompatible;
        var result = Present(p); Assert.Single(result.DirectTotems); Assert.NotEmpty(result.Notices["direct"]); Assert.Equal("", result.Notices["tote"]);
    }
    [Fact] public void EverySelectorStatePreservesOffsetsExpansionAndResetsOnGenerationLoss()
    {
        var p = Profile(); Observe(p); var result = Present(p); var s = new EquipmentSelection(); s.Refresh(result);
        Assert.Equal(EquipmentPanelSection.Loadouts, s.Page);
        foreach (var page in Enum.GetValues<EquipmentPanelSection>()) { Assert.True(s.SelectPage(page)); s.Capture("primary", 50 + (int)page); }
        Assert.True(s.Toggle("g", result.Weapons[0].Id)); s.Refresh(Present(p)); Assert.True(s.Expanded(result.Weapons[0].Id));
        foreach (var page in Enum.GetValues<EquipmentPanelSection>()) { s.SelectPage(page); Assert.Equal(50 + (int)page, s.Offset("primary", 100, 1000)); }
        Assert.Equal(10, s.Offset("primary", 100, 110)); s.Refresh(null); Assert.False(s.Expanded(result.Weapons[0].Id)); Assert.False(s.Toggle("g", result.Weapons[0].Id));
        Assert.Equal(EquipmentPanelSection.Loadouts, s.Page);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void MeasuredDocumentRetainsAllGroupsLongNamesAndAdditionalGridSlots(bool narrow)
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot();
        for (var i = 0; i < 13; i++) s.CharacterSlots.Add(new CharacterEquipmentSlotSnapshot { SlotId = "mod:" + i, SlotDisplayName = new string('L', 200), State = EquipmentSlotState.Empty });
        var a = Observe(p, s); a.Loadouts.Values.Single().RunOccurrences = 2;
        var result = Present(p); var doc = Document(result, EquipmentPanelSection.Loadouts, narrow: narrow);
        Assert.Equal(15, doc.Rows.Count(r => r.Kind == EquipmentRowKind.Slot));
        Assert.All(doc.Rows, r => { Assert.True(r.Height > 0); Assert.True(r.Y + r.Height <= doc.Height); });
        var armorLeft = Document(result, EquipmentPanelSection.ArmorAndGear, narrow: narrow);
        var armorRight = Document(result, EquipmentPanelSection.ArmorAndGear, right: true, narrow: narrow);
        Assert.Equal(result.Armor.Count, armorLeft.Rows.Concat(armorRight.Rows).Count(r => r.Kind == EquipmentRowKind.Heading));
        Assert.Contains(armorLeft.Rows.Concat(armorRight.Rows), r => r.Kind == EquipmentRowKind.Heading && r.Height > 80);
    }
    [Fact] public void NarrowNestedGroupsStackEntireLeftColumnBeforeRightColumn()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot();
        foreach (var key in new[] { "Muzzle", "Grip", "Stock", "Tactics", "Magazine" }) s.Items[0].NestedSlots.Add(new NestedEquipmentSlotSnapshot {
            Path = key.Length + ":" + key + "/", SlotKey = key, SlotDisplayName = key, State = EquipmentSlotState.Empty });
        Observe(p, s); var result = Present(p);
        var narrow = Document(result, EquipmentPanelSection.Weapons, narrow: true, expand: true);
        var headings = narrow.Rows.Where(r => r.Kind == EquipmentRowKind.Heading).ToArray();
        Assert.Single(headings.Select(r => r.X).Distinct()); Assert.Equal(headings.OrderBy(r => r.Y), headings);
        var desktop = Document(result, EquipmentPanelSection.Weapons, expand: true);
        Assert.True(desktop.Rows.Where(r => r.Kind == EquipmentRowKind.Heading).Select(r => r.X).Distinct().Count() > 1);
    }
    [Fact] public void LargeHistoryMaterializesOnlyVisibleRowsAndReachesTrueBottom()
    {
        var d = new EquipmentDocument(Measure); float y = 30;
        for (var i = 0; i < 10000; i++) y += d.Add(new EquipmentRenderRow { Id = "row:" + i, Name = "Weapon " + i, Kind = EquipmentRowKind.Item }, 30, y, 800);
        d.Seal(); Assert.True(d.Visible(0, 500).Count < 30); var bottom = d.Height - 500;
        Assert.Contains(9999, d.Visible(bottom, 500));
        Assert.False(OverflowCuePolicy.Resolve(500, 500, 0).ShowLeading); Assert.False(OverflowCuePolicy.Resolve(500, 500, 0).ShowTrailing);
        Assert.True(OverflowCuePolicy.Resolve(500, d.Height, 0).ShowTrailing);
        Assert.True(OverflowCuePolicy.Resolve(500, d.Height, bottom / 2).ShowLeading);
        Assert.True(OverflowCuePolicy.Resolve(500, d.Height, bottom / 2).ShowTrailing);
        Assert.True(OverflowCuePolicy.Resolve(500, d.Height, bottom).ShowLeading); Assert.False(OverflowCuePolicy.Resolve(500, d.Height, bottom).ShowTrailing);
    }
    [Fact] public void ReadonlyRowsDoNotAcquireActionsAndOnlyHeadersExpand()
    {
        var p = Profile(); Observe(p); var result = Present(p);
        var doc = Document(result, EquipmentPanelSection.Weapons, expand: true);
        Assert.Single(doc.Rows, r => r.Actionable); Assert.True(doc.Rows.Single(r => r.Actionable).Expandable);
        Assert.All(Document(result, EquipmentPanelSection.Loadouts).Rows.Where(r => r.Kind != EquipmentRowKind.Slot), r => Assert.False(r.Actionable));
    }
    [Fact] public void ZeroDurationRowRemainsZeroWhileAbsentObservationHasItsOwnNotice()
    {
        var p = Profile(); var a = Observe(p); a.SelectedWeapons.Add("slot|weapon", new EquipmentDurationAggregate { Id = "slot|weapon", ActiveDurationSeconds = 0 });
        Assert.Equal(0, Present(p).SelectedWeapons.Single().Duration);
        Assert.Contains(Document(Present(), EquipmentPanelSection.Loadouts).Rows, r => r.Name == "No observations recorded");
    }
    [Fact] public void SelectedDurationOverflowIsRejectedRatherThanInvented()
    {
        var p = Profile(); var a = p.Statistics.RunTotals.EquipmentStatistics;
        a.SelectedWeapons.Add("a|w", new EquipmentDurationAggregate { Id = "a|w", ActiveDurationSeconds = decimal.MaxValue });
        a.SelectedWeapons.Add("b|w", new EquipmentDurationAggregate { Id = "b|w", ActiveDurationSeconds = decimal.MaxValue });
        Assert.Throws<OverflowException>(() => Present(p));
    }
    [Fact] public void NativeTotemSlotLabelsUseProvenStableKeysAndRetainExactAttribution()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot(); s.Totems[0].DirectSlotId = "duckov:slot:Totem2";
        s.CharacterSlots.Add(new CharacterEquipmentSlotSnapshot { SlotId = "duckov:slot:Totem1", SlotDisplayName = "Totem",
            IsDirectTotemSlot = true, State = EquipmentSlotState.Empty });
        Observe(p, s); var result = Present(p);
        Assert.Equal("Totem slot 1", result.EmptySlots.Single().Name);
        Assert.Equal("Totem slot 2", result.DirectTotems.Single().Groups[0].Rows[0].Name);
        Assert.DoesNotContain(result.Armor, g => g.Name == "Totem");
    }
    [Fact] public void BackpackExpandsOnlyObservedNestedEquipmentSlots()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot();
        s.Items[0].Kind = EquipmentItemKind.Backpack; s.Items[0].SlotId = "duckov:slot:Backpack"; s.Items[0].SlotDisplayName = "Backpack";
        s.CharacterSlots[0].SlotId = s.Items[0].SlotId; s.CharacterSlots[0].SlotDisplayName = "Backpack"; s.CharacterSlots[0].ItemKind = EquipmentItemKind.Backpack;
        s.LoadoutId = EquipmentIdentity.LoadoutId(s.Items); Observe(p, s);
        var result = Present(p); Assert.Empty(result.Weapons);
        var bag = result.Armor.Single(g => g.Name == "Backpack").Rows.Single(); Assert.True(bag.Expandable);
        Assert.Equal("Nothing equipped", bag.Groups.Single().Rows.Single().Name);
    }
    [Fact] public void MissingNamesAndIconsNeverUseStableIdsAsPrimaryWeaponLabel()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot(); s.Items[0].ItemDisplayName = s.CharacterSlots[0].ItemDisplayName = "";
        Observe(p, s); var w = Present(p).Weapons.Single(); Assert.Equal("Unknown item", w.Name);
        Assert.Equal("duckov:weapon:1", w.ItemId); Assert.Null(CombatItemIconPolicy.Resolve<object>(w.ItemId, _ => null));
    }
    [Fact] public void RecentHistoryIsCompleteAndNewestEndTimestampFirst()
    {
        var p = Profile();
        for (var i = 0; i < 100; i++) p.Statistics.Runs.Add(new RunSummary { RunId = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SaveGenerationId = "g", EndedUtc = DateTime.UnixEpoch.AddSeconds(i), MapDisplayName = "Route" });
        var result = Present(p); Assert.Equal(100, result.Recent.Count); Assert.Equal("99", result.Recent[0].RunId);
    }
    [Fact] public void CapturedSlotInspectionWorksByIdentityAndNeverAddsActionsToEmptyRoots()
    {
        var p = Profile(); var a = Observe(p); a.Loadouts.Values.Single().RunOccurrences = 2;
        var state = new EquipmentSelection(); state.Refresh(Present(p));
        var id = state.Snapshot!.InspectableSlots.Keys.Single();
        Assert.False(state.Inspect("wrong", id)); Assert.True(state.Inspect("g", id));
        var doc = new EquipmentDocument(Measure); doc.Page(state, false, 900);
        Assert.Contains(doc.Rows, r => r.Name == "Scope" || r.Caption == "Scope");
        Assert.All(doc.Rows.Where(r => r.Slot?.State == EquipmentSlotState.Empty), r => Assert.False(r.Actionable));
        state.Focus("primary", id); state.Refresh(Present());
        Assert.Null(state.InspectedId); Assert.Null(state.FocusId("primary"));
    }
    [Fact] public void LoadoutFooterShowsOneDurationAndOneSpecificHistoricalNotice()
    {
        var p = Profile(); var a = p.Statistics.RunTotals.EquipmentStatistics;
        a.Composition.HistoricalUnavailable = true;
        a.Loadouts.Add("old", new EquipmentDurationAggregate { Id = "old", ActiveDurationSeconds = 763.815m, RunOccurrences = 3 });
        var doc = Document(Present(p), EquipmentPanelSection.Loadouts);
        var footer = Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.Footer);
        Assert.Equal("12:43.815 active time", footer.Name); Assert.Equal("Used in 3 runs", footer.Value);
        Assert.Single(doc.Surfaces);
        Assert.Single(doc.Rows, r => r.Name == "Loadout composition unavailable for earlier history");
        Assert.DoesNotContain(doc.Rows.TakeWhile(r => r.Kind != EquipmentRowKind.Footer), r => r.Name.Contains("Earlier history is unavailable", StringComparison.Ordinal));
        Assert.Single(doc.Rows, r => r.Name.Contains("active time", StringComparison.Ordinal) || r.Caption.Contains("active time", StringComparison.Ordinal));
    }
    [Fact] public void SelectedRowsAreCompactAndWeaponTotalsAreOnTheSecondLine()
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot(); s.SelectedWeaponId = s.Items[0].ItemId; s.SelectedWeaponSlotId = s.Items[0].SlotId;
        Observe(p, s); var result = Present(p);
        var selected = Assert.Single(Document(result, EquipmentPanelSection.Loadouts).Rows, r => r.Id.StartsWith("selected:", StringComparison.Ordinal));
        var weapon = Assert.Single(Document(result, EquipmentPanelSection.Weapons).Rows, r => r.Actionable);
        Assert.Empty(selected.Caption); Assert.Equal("00:10.000", selected.Value);
        Assert.Empty(weapon.Value); Assert.Equal("00:10.000 total time equipped", weapon.Caption);
        Assert.True(selected.Height < weapon.Height);
        var expanded = Document(result, EquipmentPanelSection.Weapons, expand: true);
        Assert.DoesNotContain(expanded.Rows, r => r.Name == "Equipped time by slot");
        var slot = Assert.Single(expanded.Rows, r => r.Kind == EquipmentRowKind.SlotDuration);
        Assert.StartsWith("00:10.000 equipped in ", slot.Name); Assert.False(slot.Actionable);
        Assert.Equal(slot.NameHeight + 4, slot.Height);
    }
    [Fact] public void RecentRouteUsesEndpointsAndSharedLocalizedButtonLabel()
    {
        var p = Profile(); var run = new RunSummary { RunId = "route", SaveGenerationId = "g", EndedUtc = DateTime.UnixEpoch };
        run.Segments.Add(new() { SegmentIndex = 2, MapDisplayName = "End" });
        run.Segments.Add(new() { SegmentIndex = 0, MapDisplayName = "Start" });
        run.Segments.Add(new() { SegmentIndex = 1, MapDisplayName = "Intermediate" });
        p.Statistics.Runs.Add(run); var result = Present(p);
        Assert.Equal("Start - End", result.Recent.Single().Name);
        var doc = Document(result, EquipmentPanelSection.Loadouts, right: true);
        var button = Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.Route);
        Assert.Equal("View run", button.Name); Assert.Equal("route:route", button.Id); Assert.True(button.Actionable);
        Assert.True(button.Height >= RetainedOverviewLatestRunViewRunPolicy.HeightPixels);
        run.Segments.RemoveAll(s => s.SegmentIndex != 0);
        Assert.Equal("Start", Present(p).Recent.Single().Name);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ExpandedSurfaceEnclosesHeaderAndDetailsButNotTheNextItem(bool gear)
    {
        var p = Profile(); var s = EquipmentCompositionTests.Snapshot();
        if (gear)
        {
            s.Items[0].Kind = EquipmentItemKind.Backpack; s.CharacterSlots[0].ItemKind = EquipmentItemKind.Backpack;
            s.Items[0].SlotId = s.CharacterSlots[0].SlotId = "duckov:slot:Backpack";
            s.Items[0].SlotDisplayName = s.CharacterSlots[0].SlotDisplayName = "Backpack";
            s.LoadoutId = EquipmentIdentity.LoadoutId(s.Items);
        }
        Observe(p, s); var result = Present(p);
        var page = gear ? EquipmentPanelSection.ArmorAndGear : EquipmentPanelSection.Weapons;
        var closed = Document(result, page); Assert.Empty(closed.Surfaces);
        var open = Document(result, page, expand: true); var surface = Assert.Single(open.Surfaces);
        var header = Assert.Single(open.Rows, r => r.Actionable); Assert.Equal(header.Y, surface.Y);
        Assert.True(surface.Height > header.Height);
        Assert.Contains(open.Rows, r => r.Y > header.Y && r.Y + r.Height <= surface.Y + surface.Height);
        Assert.All(open.Rows.Where(r => r.Y >= surface.Y && r.Y < surface.Y + surface.Height),
            r => Assert.True(r.Y + r.Height <= surface.Y + surface.Height));
        Assert.Empty(open.VisibleSurfaces(surface.Y + surface.Height + 101, 100));
    }
    [Fact] public void TallExpandedSurfaceDoesNotExpandVisibleRowWork()
    {
        var d = new EquipmentDocument(Measure); float y = 30;
        for (var i = 0; i < 10000; i++) y += d.Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Item, Name = "Attachment" }, 30, y, 800);
        d.Surfaces.Add(new EquipmentSurface(30, 30, 800, y - 30)); d.Seal();
        Assert.Single(d.VisibleSurfaces(d.Height - 500, 500)); Assert.True(d.Visible(d.Height - 500, 500).Count < 30);
    }
    [Fact] public void ProductionCompositionWiresLifecycleNativeFeedbackRoundedClippingAndCleanup()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "UltimateDuckovStatistics.sln"))) root = root.Parent;
        Assert.NotNull(root); var ui = Path.Combine(root.FullName, "src", "UltimateDuckovStatistics", "UI");
        var view = File.ReadAllText(Path.Combine(ui, "RetainedEquipmentView.cs"));
        var shell = File.ReadAllText(Path.Combine(ui, "RetainedStatisticsShell.cs"));
        Assert.Contains("equipmentView = new EquipmentView", shell); Assert.Contains("equipmentView?.Refresh", shell);
        Assert.Contains("equipmentView?.SetVisible", shell); Assert.Contains("equipmentView?.Layout", shell); Assert.Contains("equipmentView?.Tick", shell);
        Assert.Contains("equipmentView?.Dispose", shell); Assert.Contains("equipmentView = null", shell);
        Assert.Contains("equipmentView?.Refresh(null)", File.ReadAllText(Path.Combine(ui, "RetainedRunsView.cs")));
        Assert.Contains("equipmentView?.FocusSelector", File.ReadAllText(Path.Combine(ui, "RetainedTabScroll.cs")));
        Assert.Contains("new CombatNativeTextMeasurement", view); Assert.Contains("document.Visible(", view);
        Assert.Contains("new CombatControlPool<Control>(Create)", view); Assert.Contains("controls.Dispose()", view);
        Assert.Contains("CreateOverviewLatestRunViewRun(scroll.Content", view);
        Assert.Contains("RetainedOverviewLatestRunViewRunPolicy.CornerRadiusPixels", view);
        Assert.Contains("document.VisibleSurfaces(", view); Assert.Contains("surfaceRoot.SetAsFirstSibling()", view);
        Assert.Contains("viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false", view);
        Assert.Contains("c.Rect.GetComponent<ButtonAnimation>().enabled = r.Actionable", view);
        Assert.Contains("Button.onClick.RemoveAllListeners()", view); Assert.Contains("Icon.sprite = null", view);
        Assert.Contains("selection.Snapshot.CanRoute(generation, runId)", view);
        Assert.DoesNotContain("throw ", view); Assert.DoesNotContain("ValidateSurface", view); Assert.DoesNotContain("ValidateLayout", view);
        var layout = view.Substring(view.IndexOf("public void Layout", StringComparison.Ordinal), view.IndexOf("public void Tick", StringComparison.Ordinal) - view.IndexOf("public void Layout", StringComparison.Ordinal));
        Assert.DoesNotContain("Destroy", layout); Assert.DoesNotContain("root.gameObject.SetActive(false)", layout);
    }
}
