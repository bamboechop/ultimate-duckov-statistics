using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class EquipmentEvidenceLayoutTests
{
    private static ProfileDocument ObservedProfile()
    {
        var p = new ProfileDocument { GenerationId = "g", Statistics = new ProfileStatistics { SaveGenerationId = "g" } };
        p.Capabilities = EquipmentNativeContractPolicy.ToRecords(EquipmentNativeContractPolicy.CreateSupportedCapabilities(), "test").ToList();
        var a = p.Statistics.RunTotals.EquipmentStatistics;
        a.Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities();
        var snapshot = EquipmentCompositionTests.Snapshot();
        snapshot.Items[0].NestedSlots[0].SlotDisplayName = "Magazine";
        EquipmentStatisticsReducer.Observe(a, snapshot, 0);
        EquipmentStatisticsReducer.Advance(a, 10);
        return p;
    }

    private static EquipmentPresentation Present(ProfileDocument p) => EquipmentPresentationFactory.Create(
        StatisticsPanelProjectionFactory.Create(p, new(), new(), new()), "g")!;
    private static float Measure(string text, float width, float size) => Math.Max(1, MathF.Ceiling(text.Length * size / Math.Max(1, width))) * size;

    [Fact]
    public void GenericHistoryNoticeIsOmittedWhileSpecificMissingEvidenceAndCurrentLimitationsRemain()
    {
        var p = ObservedProfile(); var a = p.Statistics.RunTotals.EquipmentStatistics;
        a.Composition.HistoricalUnavailable = true;
        var presentation = Present(p);
        Assert.All(presentation.Notices.Values, value => Assert.Empty(value));
        Assert.NotEmpty(presentation.MostUsed!.Notice); // Incomplete captured nested evidence is still qualified.
        a.Composition.Loadouts.Clear();
        presentation = Present(p);
        Assert.Equal(UiText.Get("ui.equipment_loadout_history"), presentation.MostUsed!.Notice);
        Assert.Equal(10, presentation.MostUsed.Duration);
        p.Capabilities.Single(c => c.AdapterId == EquipmentCapabilityIds.DirectTotems).State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal(UiText.Get("ui.equipment_current_unavailable"), Present(p).Notices["direct"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedEmptyAttachmentUsesRunsOverlayDashAndOccupiedTextColumn(bool recent)
    {
        var p = ObservedProfile();
        p.Statistics.Runs.Add(new RunSummary
        {
            RunId = "run",
            SaveGenerationId = "g",
            EndedUtc = DateTime.UtcNow,
            EquipmentStatistics = EquipmentStatisticsReducer.Clone(p.Statistics.RunTotals.EquipmentStatistics)
        });
        var presentation = Present(p); var selection = new EquipmentSelection(); selection.Refresh(presentation);
        var card = recent ? Assert.Single(presentation.Recent) : presentation.MostUsed!;
        var slot = Assert.Single(card.Slots, s => s.CanOpenDetails);
        Assert.True(selection.Inspect("g", EquipmentPresentation.InspectionId(card, slot)));
        var doc = new EquipmentDocument(Measure); doc.Page(selection, recent, 900);
        var empty = Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.Item && r.Caption == "Magazine");
        var occupied = Assert.Single(doc.Rows, r => r.EvidenceState == EquipmentSlotState.Occupied);
        Assert.Equal(EquipmentSlotState.Empty, empty.EvidenceState);
        Assert.Empty(empty.IconId); Assert.True(empty.HasIcon); Assert.True(empty.EmptyIcon);
        Assert.Equal("—", empty.IconFallback); Assert.False(empty.Actionable);
        Assert.Equal(occupied.TextLeft, empty.TextLeft); Assert.Equal(occupied.NameWidth, empty.NameWidth);
        Assert.Equal(occupied.TextTop, empty.TextTop);
    }

    [Fact]
    public void MissingOccupiedIconDoesNotBecomeAnEmptySlot()
    {
        var row = new EquipmentRenderRow { Kind = EquipmentRowKind.Item, EvidenceState = EquipmentSlotState.Occupied };
        Assert.True(row.HasIcon); Assert.False(row.EmptyIcon); Assert.Equal("?", row.IconFallback);
        row.EvidenceState = null;
        Assert.False(row.HasIcon); Assert.False(row.EmptyIcon);
    }

    [Fact]
    public void InvisibleUnarmedIconKeepsRecordedOccupiedIdentityAndName()
    {
        var row = new EquipmentRenderRow
        {
            Kind = EquipmentRowKind.Item,
            IconId = "duckov:weapon:356",
            Name = "Unbewaffnet",
            EvidenceState = EquipmentSlotState.Occupied
        };
        Assert.True(row.EmptyIcon); Assert.Equal("—", row.IconFallback);
        Assert.Equal(EquipmentSlotState.Occupied, row.EvidenceState);
        Assert.Equal("duckov:weapon:356", row.IconId); Assert.Equal("Unbewaffnet", row.Name);
        row.IconId = "duckov:weapon:357";
        Assert.False(row.EmptyIcon); Assert.Equal("?", row.IconFallback);
    }

    [Theory]
    [InlineData(1, 900)]
    [InlineData(2, 400)]
    public void ExpandedDirectTotemKeepsTenPixelGapBeforeNextButton(int slots, float width)
    {
        var totems = new List<EquipmentEntry>();
        foreach (var id in new[] { "first", "next" })
            totems.Add(new EquipmentEntry("direct:" + id, id, id, 20, groups: new List<EquipmentGroup> {
                new("Slots", Enumerable.Range(1, slots).Select(i => new EquipmentEntry("slot:" + i, "Totem slot " + i, "", 10, UiText.Get("ui.equipment_activation_provenactive")))) }));
        var p = new EquipmentPresentation("g", null, Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(),
            Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentGroup>(), totems, Array.Empty<EquipmentEntry>(),
            Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(), new Dictionary<string, string> { ["direct"] = "", ["empty"] = "" });
        var selection = new EquipmentSelection(); selection.Refresh(p); selection.SelectPage(EquipmentPanelSection.Totems);
        selection.Toggle("g", "direct:first");
        var doc = new EquipmentDocument(Measure); doc.Page(selection, false, width);
        var surface = Assert.Single(doc.Surfaces);
        var next = Assert.Single(doc.Rows, r => r.Id == "direct:next");
        Assert.Equal(10, next.Y - surface.Y - surface.Height);
        Assert.Equal(slots, doc.Rows.Count(r => r.Kind == EquipmentRowKind.SlotDuration));
        selection.Toggle("g", "direct:first");
        doc = new EquipmentDocument(Measure); doc.Page(selection, false, width);
        var first = Assert.Single(doc.Rows, r => r.Id == "direct:first");
        next = Assert.Single(doc.Rows, r => r.Id == "direct:next");
        Assert.Equal(10, next.Y - first.Y - first.Height);
    }

    [Theory]
    [InlineData(900)]
    [InlineData(400)]
    public void EachTotemSetSurfaceEnclosesItsMembersAndSummaryWithSeparateGaps(float width)
    {
        var sets = new List<EquipmentEntry>();
        foreach (var count in new[] { 2, 1, 0 })
        {
            var members = Enumerable.Range(0, count).Select(i => new EquipmentEntry("member:" + count + ":" + i,
                new string('T', 40), "totem:" + i, 0));
            sets.Add(new EquipmentEntry("set:" + count, "", "", count + 10, "active together · Used in 2 runs",
                count == 1 ? "No other active totem" : "", groups: new List<EquipmentGroup> { new("", members) }));
        }
        var presentation = new EquipmentPresentation("g", null, Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(),
            Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentGroup>(), Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(),
            sets, Array.Empty<EquipmentEntry>(), new Dictionary<string, string> { ["sets"] = "", ["tote"] = "" });
        var selection = new EquipmentSelection(); selection.Refresh(presentation); selection.SelectPage(EquipmentPanelSection.Totems);
        var doc = new EquipmentDocument(Measure); doc.Page(selection, true, width);
        Assert.Equal(sets.Count, doc.Surfaces.Count);
        for (var i = 0; i < sets.Count; i++)
        {
            var surface = doc.Surfaces[i];
            var rows = doc.Rows.Where(r => r.Y >= surface.Y && r.Y < surface.Y + surface.Height).ToArray();
            Assert.Equal(2 - i, rows.Count(r => r.Kind == EquipmentRowKind.Item));
            Assert.Single(rows, r => r.Name == EquipmentLayoutPolicy.Duration(sets[i].Duration) + " " + sets[i].Caption);
            Assert.All(rows, r => { Assert.True(r.Y + r.Height <= surface.Y + surface.Height); Assert.False(r.Actionable); });
            Assert.Equal(30, surface.X); Assert.Equal(width - 60, surface.Width);
            var next = doc.Rows.First(r => r.Y >= surface.Y + surface.Height);
            Assert.True(next.Y >= surface.Y + surface.Height + 10);
        }
        var toteHeading = Assert.Single(doc.Rows, r => r.Name == UiText.Get("ui.equipment_tote"));
        Assert.True(toteHeading.Y >= doc.Surfaces[^1].Y + doc.Surfaces[^1].Height + 10);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void GearOmitsOnlyRedundantSingleSlotHeading(bool repeatedName, bool multipleSlots)
    {
        var groups = new List<EquipmentGroup> { new(repeatedName ? "Blauer Würfel" : "Internal slot",
            new List<EquipmentEntry> { new("cube", "Blauer Würfel", "cube", 30) }, "Partial evidence") };
        if (multipleSlots) groups.Add(new EquipmentGroup("Other slot", new List<EquipmentEntry> { new("other", "Other item", "other", 10) }));
        var bag = new EquipmentEntry("gear:bag", "Würfelsammler", "bag", 30, groups: groups);
        var presentation = new EquipmentPresentation("g", null, Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(),
            Array.Empty<EquipmentEntry>(), new List<EquipmentGroup> { new("Backpack", new List<EquipmentEntry> { bag }) },
            Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(), Array.Empty<EquipmentEntry>(),
            new Dictionary<string, string> { ["armor"] = "" });
        var selection = new EquipmentSelection(); selection.Refresh(presentation); selection.SelectPage(EquipmentPanelSection.ArmorAndGear);
        Assert.True(selection.Toggle("g", bag.Id));
        var doc = new EquipmentDocument(Measure); doc.Page(selection, false, 900);
        Assert.Equal(!repeatedName || multipleSlots, doc.Rows.Any(r => r.Kind == EquipmentRowKind.Heading && r.Name == groups[0].Name));
        var cube = Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.Item && r.IconId == "cube");
        Assert.Equal("Blauer Würfel", cube.Name); Assert.Equal(EquipmentLayoutPolicy.Duration(30), cube.Value);
        Assert.Contains(doc.Rows, r => r.Kind == EquipmentRowKind.Notice && r.Name == "Partial evidence");
        Assert.Single(doc.Surfaces);
    }

    [Theory]
    [InlineData(900, 9, 8)]
    [InlineData(400, 80, 8)]
    [InlineData(400, 9, 40)]
    public void RecentRunButtonHasCardMarginAndSharesMeasuredMapTextCenter(float width, int mapLength, int buttonLength)
    {
        var p = ObservedProfile();
        p.Statistics.Runs.Add(new RunSummary
        {
            RunId = "run",
            SaveGenerationId = "g",
            EndedUtc = DateTime.UtcNow,
            StartingMapDisplayName = new string('M', mapLength),
            EquipmentStatistics = EquipmentStatisticsReducer.Clone(p.Statistics.RunTotals.EquipmentStatistics)
        });
        var selection = new EquipmentSelection(); selection.Refresh(Present(p));
        var doc = new EquipmentDocument(Measure, key => key == RetainedOverviewLatestRunViewRunPolicy.TextKey
            ? new string('V', buttonLength) : UiText.Get(key));
        doc.Page(selection, true, width);
        var button = Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.Route);
        var title = Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.Heading && !r.SectionHeading);
        var card = Assert.Single(doc.Surfaces);
        Assert.Equal(15f, card.X + card.Width - button.X - button.Width, .001f);
        Assert.Equal(title.Y + title.TextTop + title.NameHeight / 2, button.Y + button.Height / 2, .001f);
        Assert.True(button.Y >= card.Y + title.TextTop);
        Assert.True(title.X + title.TextLeft + title.NameWidth + 10 <= button.X);
        var caption = Assert.Single(doc.Rows, r => r.Kind == EquipmentRowKind.Notice && r.Name.Contains("Most used during this run", StringComparison.Ordinal));
        Assert.True(caption.Y >= Math.Max(title.Y + title.Height, button.Y + button.Height) + 10);
    }

    [Theory]
    [InlineData(false, 900)]
    [InlineData(true, 900)]
    [InlineData(false, 400)]
    [InlineData(true, 400)]
    public void LoadoutSectionHeadingUsesPanelPaddingOnceAndReflowsInsideIt(bool right, float width)
    {
        var selection = new EquipmentSelection(); selection.Refresh(Present(ObservedProfile()));
        var doc = new EquipmentDocument(Measure, key => key is "ui.equipment_most_used" or "ui.equipment_recent"
            ? new string('W', 80) : UiText.Get(key));
        doc.Page(selection, right, width);
        var heading = doc.Rows[0];
        Assert.True(heading.SectionHeading);
        Assert.Equal(30, heading.X + heading.TextLeft); Assert.Equal(30, heading.Y + heading.TextTop);
        Assert.Equal(width - 30, heading.X + heading.TextLeft + heading.NameWidth);
        Assert.Equal(heading.NameHeight, heading.Height);
        Assert.True(heading.Height > 40);
        Assert.True(doc.Rows[1].Y >= heading.Y + heading.Height + 10);
    }
}
