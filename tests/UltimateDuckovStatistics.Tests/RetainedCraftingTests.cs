using System.Globalization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

#pragma warning disable CA1861
public sealed class RetainedCraftingTests
{
    [Fact]
    public void CurrentLanguageResolvesOutputsResourcesAndReciprocalDetailsWithoutRewritingCrafts()
    {
        var profile = Profile(); Craft(profile, "131", "Tasse", "1026", 1, "764", 4, "Polyethylen-Folie");
        var before = System.Text.Json.JsonSerializer.Serialize(profile);
        var p = Projection(profile);
        p.Names = new EntityDisplayNames(id => id == "131" ? "Cup" : id == "764" ? "Polyethylene" : null);
        var result = CraftingPresentationFactory.Create(p, "g")!;
        Assert.Equal("Cup", result.Outputs[0].Name); Assert.Equal("Polyethylene", result.Resources[0].Name);
        Assert.Equal("Polyethylene", result.Outputs[0].Details[0].Name);
        Assert.Equal("Cup", result.Resources[0].Details[0].Name);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(profile));
    }

    private static CraftingMetricCapabilities Supported() => CraftingNativeContractPolicy.Supported("delivered", "captured formula", "exact paid items");
    private static ProfileDocument Profile()
    {
        var p = new ProfileDocument { GenerationId = "g", Statistics = new ProfileStatistics { SaveGenerationId = "g" } };
        p.Statistics.Crafting.Capabilities = Supported(); return p;
    }
    private static StatisticsPanelProjection Projection(ProfileDocument? p = null, CraftingMetricCapabilities? capabilities = null) =>
        StatisticsPanelProjectionFactory.Create(p ?? Profile(), new(), capabilities ?? Supported(), new());
    private static CraftingPresentation Present(ProfileDocument? p = null, CraftingMetricCapabilities? capabilities = null) =>
        CraftingPresentationFactory.Create(Projection(p, capabilities), "g")!;
    private static void Craft(ProfileDocument p, string output, string name, string recipe, long units,
        string? resource = null, long used = 0, string resourceName = "Resource", bool resourcesProven = true)
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(new CraftingCompletionEvidence(output, name, recipe, units,
            resource == null ? Array.Empty<CraftingResourceCostEvidence>() : new[] { new CraftingResourceCostEvidence(resource, resourceName, used) },
            resourceEvidenceProven: resourcesProven));
        Assert.True(boundary.TryComplete(token, "g", DateTime.UnixEpoch, out var mutation));
        Assert.True(CraftingStatisticsReducer.Apply(p.Statistics.Crafting, mutation));
        Assert.True(boundary.FinishPublication(token));
    }
    private static float Measure(string text, float width, float size) =>
        Math.Max(1, MathF.Ceiling(text.Length * size * .5f / Math.Max(1, width))) * size * 1.2f;
    private static EquipmentDocument Document(CraftingPresentation p, bool resources = false, float width = 1150, bool expanded = false)
    {
        var selection = new CraftingSelection(); selection.Refresh(p);
        if (expanded) foreach (var id in p.ExpansionIds) selection.Toggle(p.GenerationId, id);
        return CraftingDocument.Create(selection, resources, width, Measure);
    }

    [Fact]
    public void SuccessfulActionsRankIndependentlyOfProducedAndConsumedUnits()
    {
        var p = Profile();
        Craft(p, "1", "Small batch", "small", 30, "8", 2);
        Craft(p, "1", "Small batch", "small", 30, "8", 3);
        Craft(p, "2", "Large batch", "large", 1000, "9", 1);
        var result = Present(p);
        Assert.Equal(new[] { "1", "2" }, result.Outputs.Select(r => r.ItemId));
        Assert.Equal(2, result.Outputs[0].Count); Assert.Equal(60, result.Outputs[0].ProducedQuantity);
        Assert.Equal(5, Assert.Single(result.Outputs[0].Details).ConsumedQuantity);
        Assert.Equal(new[] { "8", "9" }, result.Resources.Select(r => r.ItemId));
        Assert.Equal(5, result.Resources[0].Count);
        Assert.Equal(60, Assert.Single(result.Resources[0].Details).ProducedQuantity);
        CraftingStatisticsReducer.Validate(p.Statistics.Crafting);
    }
    [Fact]
    public void ReciprocalViewOnlyIncludesRecipesThatRecordedThisExactResource()
    {
        var p = Profile();
        Craft(p, "1", "Output", "with-first", 30, "8", 2);
        Craft(p, "1", "Output", "with-second", 60, "9", 3);
        Craft(p, "2", "Other output", "also-first", 100, "8", 5);
        var result = Present(p);
        var resource = result.Resources.Single(r => r.ItemId == "8");
        Assert.Equal(7, resource.Count); Assert.Equal(new[] { "2", "1" }, resource.Details.Select(r => r.ItemId));
        Assert.Equal(30, resource.Details.Single(r => r.ItemId == "1").ProducedQuantity);
        Assert.Equal(90, result.Outputs.Single(r => r.ItemId == "1").ProducedQuantity);
        Assert.Equal(2, result.Outputs.Single(r => r.ItemId == "1").Details.Count);
    }
    [Fact]
    public void DisplayNameCollisionsDoNotJoinDifferentOutputOrResourceIdentities()
    {
        var p = Profile();
        Craft(p, "mod:output:a", "Same name", "a", 2, "mod:resource:a", 3, "Same resource");
        Craft(p, "mod:output:b", "Same name", "b", 7, "mod:resource:b", 4, "Same resource");
        var result = Present(p); Assert.Equal(2, result.Outputs.Count); Assert.Equal(2, result.Resources.Count);
        Assert.All(result.Resources, r => Assert.Single(r.Details));
        Assert.Equal("mod:output:a", result.Resources.Single(r => r.ItemId == "mod:resource:a").Details[0].ItemId);
        Assert.Equal(3, result.Resources.Single(r => r.ItemId == "mod:resource:a").Count);
    }
    [Fact]
    public void EqualCountsHaveDeterministicOrdinalNameAndIdentityOrdering()
    {
        var p = Profile();
        Craft(p, "b", "Same", "b", 2, "z", 1, "Same");
        Craft(p, "A", "Same", "a", 2, "B", 1, "Same");
        Craft(p, "z", "Alpha", "z", 2, "a", 1, "Alpha");
        var result = Present(p);
        Assert.Equal(new[] { "z", "A", "b" }, result.Outputs.Select(r => r.ItemId));
        Assert.Equal(new[] { "a", "B", "z" }, result.Resources.Select(r => r.ItemId));
    }
    [Fact]
    public void PresentationCopiesEvidenceAndNeverReadsCurrentRecipeOrInventory()
    {
        var p = Profile(); Craft(p, "1", "Captured output", "recipe", 30, "8", 2, "Captured resource");
        var result = Present(p);
        p.Statistics.Crafting.Outputs["1"].DisplayName = "Later name";
        p.Statistics.Crafting.Outputs["1"].Recipes.Clear(); p.Statistics.Crafting.Resources.Clear();
        Assert.Equal("Captured output", result.Outputs[0].Name);
        Assert.Equal("Captured resource", result.Outputs[0].Details[0].Name);
        Assert.Equal(30, result.Resources[0].Details[0].ProducedQuantity); Assert.Equal(2, result.Resources[0].Count);
    }
    [Fact]
    public void FactoryRequiresExactProfileAndStatisticsGeneration()
    {
        var p = Projection(); Assert.NotNull(CraftingPresentationFactory.Create(p, "g"));
        Assert.Null(CraftingPresentationFactory.Create(p, "other"));
        p.Profile.Statistics.SaveGenerationId = "other"; Assert.Null(CraftingPresentationFactory.Create(p, "g"));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ReplacedPublicationMemberCannotMixGenerations(int member)
    {
        var p = Projection();
        switch (member)
        {
            case 0: p.Crafting = new(); break;
            case 1: p.CraftingCapabilities = Supported(); break;
            case 2: p.CraftingOutputs = new List<CraftingOutputProjection>(); break;
            case 3: p.CraftingResources = new List<CraftingResourceProjection>(); break;
            case 4: p.Crafting.Outputs = new(); break;
            case 5: p.Crafting.Resources = new(); break;
            default: p.Profile = Profile(); break;
        }
        Assert.Null(CraftingPresentationFactory.Create(p, "g"));
    }
    [Fact]
    public void ResourceFailureKeepsIndependentSuccessfulOutputAndRecordedResourceValues()
    {
        var p = Profile(); Craft(p, "1", "Output", "recipe", 30, "8", 2);
        var c = Supported(); c.ItemResourceIdentity.State = AdapterCapabilityState.DisabledIncompatible;
        c.OutputResourceAssociation.State = AdapterCapabilityState.DisabledIncompatible;
        var result = Present(p, c);
        Assert.Equal(1, result.Outputs[0].Count); Assert.Equal(30, result.Outputs[0].ProducedQuantity);
        Assert.Equal(2, result.Resources[0].Count); Assert.Empty(result.OutputNotice);
        Assert.Equal(UiText.Get("ui.crafting_resource_current_unavailable"), result.ResourceNotice);
        Assert.Equal(UiText.Get("ui.crafting_association_unavailable"), result.Outputs[0].DetailNotice);
    }
    [Fact]
    public void QuantityFailureIsVisibleWithoutDisablingSuccessfulActionsOrResources()
    {
        var p = Profile(); Craft(p, "1", "Output", "recipe", 30, "8", 2);
        var c = Supported(); c.ProducedQuantity.State = AdapterCapabilityState.DisabledIncompatible;
        var result = Present(p, c);
        Assert.Equal(1, result.Outputs[0].Count); Assert.Equal(2, result.Resources[0].Count);
        Assert.Contains(UiText.Get("ui.crafting_quantity_current_unavailable"), result.Outputs[0].DetailNotice, StringComparison.Ordinal);
        Assert.Empty(result.ResourceNotice);
    }
    [Fact]
    public void SupportedEmptyUnavailableEmptyAndProvenFreeRecipeRemainDifferent()
    {
        var result = Present(); Assert.Empty(result.Outputs); Assert.Empty(result.Resources);
        Assert.Equal(UiText.Get("ui.crafting_outputs_empty"), result.OutputEmpty);
        Assert.Equal(UiText.Get("ui.crafting_resources_empty"), result.ResourceEmpty);
        var unavailable = Present(capabilities: CraftingNativeContractPolicy.Unavailable("native contract missing"));
        Assert.Equal(UiText.Get("ui.unavailable"), unavailable.OutputEmpty);
        Assert.Equal(UiText.Get("ui.unavailable"), unavailable.ResourceEmpty);
        var p = Profile(); Craft(p, "1", "Free output", "free", 3);
        Assert.Equal(UiText.Get("ui.crafting_no_resources_used"), Present(p).Outputs[0].DetailNotice);
        var missing = Profile(); Craft(missing, "1", "Output", "missing", 3, resourcesProven: false);
        var missingResult = Present(missing);
        Assert.Empty(missingResult.Resources); Assert.Equal(UiText.Get("ui.unavailable"), missingResult.ResourceEmpty);
        Assert.NotEqual(UiText.Get("ui.crafting_no_resources_used"), missingResult.Outputs[0].DetailNotice);
    }
    [Fact]
    public void SingleRecordedBatchCanProveResourceAssociatedProductionAfterLaterMissingEvidence()
    {
        var p = Profile(); Craft(p, "1", "Output", "recipe", 30, "8", 2);
        Craft(p, "1", "Output", "recipe", 30, resourcesProven: false);
        var result = Present(p); var detail = Assert.Single(result.Resources[0].Details);
        Assert.Equal(60, result.Outputs[0].ProducedQuantity); Assert.Equal(30, detail.ProducedQuantity);
        Assert.Equal(2, detail.ConsumedQuantity);
        CraftingStatisticsReducer.Validate(p.Statistics.Crafting);
    }
    [Fact]
    public void MixedRecordedBatchesDoNotGuessWhichProductionBelongsToResourceSubset()
    {
        var p = Profile(); Craft(p, "1", "Output", "recipe", 30, "8", 2);
        Craft(p, "1", "Output", "recipe", 60, resourcesProven: false);
        var result = Present(p); var detail = Assert.Single(result.Resources[0].Details);
        Assert.Equal(90, result.Outputs[0].ProducedQuantity); Assert.Null(detail.ProducedQuantity); Assert.Equal(2, detail.ConsumedQuantity);
        var doc = Document(result, resources: true, expanded: true);
        Assert.Contains(doc.Rows, r => r.Caption == UiText.Get("ui.crafting_produced_unavailable"));
        CraftingStatisticsReducer.Validate(p.Statistics.Crafting);
    }
    [Fact]
    public void FullyAssociatedMixedBatchesUseExactRecordedRecipeQuantity()
    {
        var p = Profile(); Craft(p, "1", "Output", "recipe", 30, "8", 2);
        Craft(p, "1", "Output", "recipe", 60, "8", 5);
        var result = Present(p); Assert.Equal(90, result.Resources[0].Details[0].ProducedQuantity);
        Assert.Equal(7, result.Resources[0].Details[0].ConsumedQuantity);
    }
    [Fact]
    public void CompletionArithmeticBoundaryDoesNotFabricateZeroActionsForLaterOutput()
    {
        var p = Profile();
        var row = new CraftingMutationRow("old", "Old output", "old", long.MaxValue, long.MaxValue,
            new Dictionary<string, long> { ["1"] = long.MaxValue });
        Assert.True(CraftingStatisticsReducer.Apply(p.Statistics.Crafting, new CraftingMutation("g", DateTime.UnixEpoch, new[] { row })));
        Craft(p, "later", "Later output", "later", 1, "8", 2);
        var result = Present(p); Assert.Null(result.Outputs.Single(r => r.ItemId == "later").Count);
        Assert.Equal(2, result.Resources.Single(r => r.ItemId == "8").Count);
        Assert.Null(result.Resources.Single(r => r.ItemId == "8").Details[0].ProducedQuantity);
    }
    [Fact]
    public void ResourceCaptureGapDoesNotHideProvenEmptyOutput()
    {
        var p = Profile(); p.Statistics.Crafting.ResourceHistoryUnavailable = true;
        var result = Present(p); Assert.Empty(result.Outputs); Assert.Empty(result.Resources);
        Assert.Empty(result.OutputNotice); Assert.Empty(result.ResourceNotice);
        Assert.Empty(Document(result).Surfaces); Assert.Empty(Document(result, resources: true).Surfaces);
        Assert.Equal(UiText.Get("ui.crafting_outputs_empty"), result.OutputEmpty);
    }
    [Theory]
    [InlineData("131", "duckov:item:131")]
    [InlineData("356", "duckov:item:356")]
    [InlineData("0131", "0131")]
    [InlineData("-1", "-1")]
    [InlineData("mod:131", "mod:131")]
    [InlineData("duckov:item:131", "duckov:item:131")]
    public void IconOnlyNormalizationAcceptsCanonicalNativeIds(string captured, string expected) => Assert.Equal(expected, CraftingIconPolicy.ResolveId(captured));
    [Fact]
    public void UnknownIdentityAndNativeUnarmedIconRemainDistinctFromAProvenEmptyRecipe()
    {
        var p = Profile(); Craft(p, "356", "356", "free", 1);
        var result = Present(p); Assert.Contains("356", result.Outputs[0].Name, StringComparison.Ordinal);
        var row = Assert.Single(Document(result).Rows, r => r.Actionable);
        Assert.True(row.EmptyIcon); Assert.Equal("—", row.IconFallback); Assert.Equal("output:356", row.Id);
        Assert.Equal(UiText.Get("ui.crafting_no_resources_used"), result.Outputs[0].DetailNotice);
    }
    [Fact]
    public void ExpansionAndOffsetsSurviveSameGenerationAndRejectStaleCallbacks()
    {
        var p = Profile(); Craft(p, "1", "Output", "recipe", 30, "8", 2);
        var s = new CraftingSelection(); s.Refresh(Present(p)); Assert.True(s.Toggle("g", "output:1"));
        Assert.True(s.Toggle("g", "resource:8")); s.Capture("outputs", 450); s.Capture("resources", 100); s.Focus("outputs", "output:1");
        s.Refresh(Present(p)); Assert.True(s.Expanded("output:1")); Assert.True(s.Expanded("resource:8"));
        Assert.Equal(450, s.Offset("outputs", 500, 1500)); Assert.Equal(100, s.Offset("resources", 500, 1500));
        Assert.False(s.Toggle("other", "output:1")); Assert.False(s.Toggle("g", "output:missing"));
        Assert.Equal(0, s.Offset("outputs", 500, 100));
        s.Refresh(Present()); Assert.False(s.Expanded("output:1")); Assert.Null(s.FocusId("outputs"));
        s.Refresh(new CraftingPresentation("new", Present(p).Outputs, Present(p).Resources, "", "", "", ""));
        Assert.Equal(0, s.Offset("resources", 500, 1500)); Assert.False(s.Toggle("g", "resource:8"));
        s.Refresh(null); Assert.False(s.Toggle("new", "output:1"));
    }
    [Theory]
    [InlineData(1150f)]
    [InlineData(850f)]
    [InlineData(520f)]
    public void ExpandedCardsContainWrappedDetailsAndPreserveTenPixelGaps(float width)
    {
        var p = Profile();
        Craft(p, "1", new string('W', 180), "first", 30, "8", 2, new string('M', 200));
        Craft(p, "2", "Second item", "second", 1, "8", 1);
        foreach (var resources in new[] { false, true })
        {
            var d = Document(Present(p), resources, width, expanded: true);
            Assert.Equal(resources ? 1 : 2, d.Surfaces.Count);
            Assert.All(d.Rows, row => { Assert.True(row.NameWidth > 0); Assert.True(row.X + row.Width <= width); });
            Assert.All(d.Surfaces, surface => Assert.All(d.Rows.Where(row => row.Y >= surface.Y && row.Y < surface.Y + surface.Height),
                row => Assert.True(row.Y + row.Height <= surface.Y + surface.Height)));
            Assert.All(d.Rows.Where(row => row.HasIcon && !row.Expandable), row => Assert.False(row.Actionable));
            for (var i = 1; i < d.Surfaces.Count; i++) Assert.Equal(10, d.Surfaces[i].Y - d.Surfaces[i - 1].Y - d.Surfaces[i - 1].Height);
        }
    }
    [Fact]
    public void DesktopColumnsArePeersAndNarrowColumnHeightsRemainBounded()
    {
        Assert.Equal(1155, CraftingLayoutPolicy.ColumnWidth(2350, false));
        Assert.Equal(2350, CraftingLayoutPolicy.ColumnWidth(2350, true));
        Assert.Equal(800, CraftingLayoutPolicy.ColumnHeight(800, 10000, false));
        Assert.Equal(680, CraftingLayoutPolicy.ColumnHeight(800, 10000, true));
        Assert.Equal(180, CraftingLayoutPolicy.ColumnHeight(800, 180, true));
    }
    [Fact]
    public void LargeExpandedGraphBoundsVisibleRowsAndSurfaces()
    {
        var details = Enumerable.Range(0, 10000).Select(i => new CraftingDetail(i.ToString(CultureInfo.InvariantCulture), "Resource", i + 1L)).ToArray();
        var output = new CraftingEntry("output:1", "1", "Output", 1, 1, details);
        var p = new CraftingPresentation("g", new[] { output }, Array.Empty<CraftingEntry>(), "", "", "", "");
        var d = Document(p, expanded: true); var offset = d.Height - 500;
        Assert.Single(d.VisibleSurfaces(offset, 500)); Assert.InRange(d.Visible(offset, 500).Count, 1, 20);
        Assert.All(d.Visible(offset, 500), index => Assert.False(d.Rows[index].Actionable));
    }
}
