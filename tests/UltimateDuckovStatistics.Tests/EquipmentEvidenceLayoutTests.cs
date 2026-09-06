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
        a.HistoricalUnavailable = a.HistoricalCharacterSlotStateUnavailable = a.HistoricalNestedSlotStateUnavailable = a.Composition.HistoricalUnavailable = true;
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
        p.Statistics.Runs.Add(new RunSummary { RunId = "run", SaveGenerationId = "g", EndedUtc = DateTime.UtcNow,
            EquipmentStatistics = EquipmentStatisticsReducer.Clone(p.Statistics.RunTotals.EquipmentStatistics) });
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
