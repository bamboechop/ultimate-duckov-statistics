using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

internal sealed class CraftingProjectionBinding
{
    private readonly ProfileDocument profile;
    private readonly CraftingStatisticsAggregate crafting;
    private readonly CraftingMetricCapabilities capabilities;
    private readonly object outputs, resources, outputRows, resourceRows;
    private readonly string generation;
    public CraftingProjectionBinding(StatisticsPanelProjection p)
    {
        profile = p.Profile; crafting = p.Crafting; capabilities = p.CraftingCapabilities;
        outputs = crafting.Outputs; resources = crafting.Resources;
        outputRows = p.CraftingOutputs; resourceRows = p.CraftingResources; generation = profile.GenerationId;
    }
    public bool Matches(StatisticsPanelProjection p, string current) => generation == current
        && StatisticsPanelProjectionFactory.HasProvableGeneration(p.Profile, current)
        && ReferenceEquals(profile, p.Profile) && ReferenceEquals(crafting, p.Crafting)
        && ReferenceEquals(crafting, profile.Statistics.Crafting) && ReferenceEquals(capabilities, p.CraftingCapabilities)
        && ReferenceEquals(outputs, crafting.Outputs) && ReferenceEquals(resources, crafting.Resources)
        && ReferenceEquals(outputRows, p.CraftingOutputs) && ReferenceEquals(resourceRows, p.CraftingResources);
}

internal sealed class CraftingDetail
{
    public string ItemId { get; }
    public string Name { get; }
    public long? ProducedQuantity { get; }
    public long? ConsumedQuantity { get; }
    public CraftingDetail(string itemId, string name, long? consumedQuantity, long? producedQuantity = null)
    { ItemId = itemId; Name = name; ConsumedQuantity = consumedQuantity; ProducedQuantity = producedQuantity; }
}

internal sealed class CraftingEntry
{
    public string Id { get; }
    public string ItemId { get; }
    public string Name { get; }
    public long? Count { get; }
    public long? ProducedQuantity { get; }
    public string DetailNotice { get; }
    public IReadOnlyList<CraftingDetail> Details { get; }
    public CraftingEntry(string id, string itemId, string name, long? count, long? producedQuantity,
        IEnumerable<CraftingDetail> details, string detailNotice = "")
    {
        Id = id; ItemId = itemId; Name = name; Count = count; ProducedQuantity = producedQuantity;
        Details = Array.AsReadOnly(details.ToArray()); DetailNotice = detailNotice;
    }
}

internal sealed class CraftingPresentation
{
    public string GenerationId { get; }
    public IReadOnlyList<CraftingEntry> Outputs { get; }
    public IReadOnlyList<CraftingEntry> Resources { get; }
    public string OutputNotice { get; }
    public string ResourceNotice { get; }
    public string OutputEmpty { get; }
    public string ResourceEmpty { get; }
    public IReadOnlyCollection<string> ExpansionIds { get; }
    public CraftingPresentation(string generation, IEnumerable<CraftingEntry> outputs, IEnumerable<CraftingEntry> resources,
        string outputNotice, string resourceNotice, string outputEmpty, string resourceEmpty)
    {
        GenerationId = generation; Outputs = Array.AsReadOnly(outputs.ToArray()); Resources = Array.AsReadOnly(resources.ToArray());
        OutputNotice = outputNotice; ResourceNotice = resourceNotice; OutputEmpty = outputEmpty; ResourceEmpty = resourceEmpty;
        ExpansionIds = Array.AsReadOnly(Outputs.Concat(Resources).Select(r => r.Id).ToArray());
    }
}

internal static class CraftingPresentationFactory
{
    public static CraftingPresentation? Create(StatisticsPanelProjection p, string generation, Func<string, string>? text = null)
    {
        if (p == null || string.IsNullOrWhiteSpace(generation) || p.CraftingBinding?.Matches(p, generation) != true) return null;
        var t = text ?? UiText.Get; var a = p.Crafting; var c = p.CraftingCapabilities;
        string Name(string id, string name) => string.IsNullOrWhiteSpace(name) || name == id
            ? string.Format(CultureInfo.CurrentCulture, t("ui.crafting_unknown_item"), id) : name.Trim();
        var outputSupported = Supported(c.CompletionActions) && Supported(c.OutputIdentity);
        var resourceSupported = Supported(c.ItemResourceIdentity);
        var associationSupported = Supported(c.OutputResourceAssociation);
        string DetailNotice(bool empty) => !associationSupported ? CapabilityNotice(t, "ui.crafting_association_unavailable", c.OutputResourceAssociation)
            : empty && a.ResourceHistoryUnavailable ? t("ui.unavailable") : "";
        var outputRows = a.Outputs.Values.Select(output =>
        {
            var details = output.Recipes.Values.SelectMany(recipe => recipe.Resources.Values)
                .GroupBy(r => r.ResourceItemId, StringComparer.Ordinal).Select(g => new CraftingDetail(g.Key,
                    Name(g.Key, a.Resources.TryGetValue(g.Key, out var resource) ? resource.DisplayName
                        : g.Select(r => r.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault() ?? ""),
                    a.ResourceQuantityArithmeticUnavailable ? null : ExactSum(g.Select(r => r.ConsumedQuantity))))
                .OrderBy(r => !r.ConsumedQuantity.HasValue).ThenByDescending(r => r.ConsumedQuantity)
                .ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.ItemId, StringComparer.Ordinal).ToArray();
            var emptyProven = details.Length == 0 && !a.ResourceHistoryUnavailable && associationSupported
                && output.Recipes.Count > 0 && ExactSum(output.Recipes.Values.Select(r => r.CompletionActions)) == output.CompletionActions;
            var notice = DetailNotice(details.Length == 0);
            if (details.Length == 0 && notice.Length == 0) notice = t(emptyProven ? "ui.crafting_no_resources_used" : "ui.unavailable");
            var quantityNotice = CapabilityNotice(t, "ui.crafting_quantity_current_unavailable", c.ProducedQuantity);
            if (quantityNotice.Length > 0) notice = Join(notice, quantityNotice);
            return new CraftingEntry("output:" + output.OutputItemId, output.OutputItemId, Name(output.OutputItemId, output.DisplayName),
                a.CompletionArithmeticUnavailable ? null : Observed(output.CompletionActions, c.CompletionActions),
                a.QuantityArithmeticUnavailable ? null : Observed(output.ProducedQuantity, c.ProducedQuantity), details, notice);
        }).OrderBy(r => !r.Count.HasValue).ThenByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.ItemId, StringComparer.Ordinal).ToArray();

        // Build the reverse view from the recorded graph, without an inventory/recipe lookup.
        // Per-recipe output totals alone cannot prove output quantity for a subset of its actions.
        var reverse = new Dictionary<string, List<CraftingDetail>>(StringComparer.Ordinal);
        foreach (var output in a.Outputs.Values)
            foreach (var group in output.Recipes.Values.SelectMany(recipe => recipe.Resources.Values.Select(resource => (Recipe: recipe, Resource: resource)))
                         .GroupBy(pair => pair.Resource.ResourceItemId, StringComparer.Ordinal))
            {
                if (!reverse.TryGetValue(group.Key, out var list)) reverse.Add(group.Key, list = new List<CraftingDetail>());
                var quantity = a.QuantityArithmeticUnavailable || a.ResourceActionArithmeticUnavailable ? null
                    : ExactSum(group.Select(pair => AssociatedProduction(pair.Recipe, pair.Resource)));
                list.Add(new CraftingDetail(output.OutputItemId, Name(output.OutputItemId, output.DisplayName),
                    a.ResourceQuantityArithmeticUnavailable ? null : ExactSum(group.Select(pair => pair.Resource.ConsumedQuantity)), quantity));
            }
        var resourceRows = a.Resources.Values.Select(resource =>
        {
            var details = reverse.TryGetValue(resource.ResourceItemId, out var rows) ? rows : new List<CraftingDetail>();
            var ordered = details.OrderBy(r => !r.ConsumedQuantity.HasValue).ThenByDescending(r => r.ConsumedQuantity)
                .ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.ItemId, StringComparer.Ordinal);
            var notice = DetailNotice(details.Count == 0);
            if (details.Count == 0 && notice.Length == 0) notice = t("ui.unavailable");
            return new CraftingEntry("resource:" + resource.ResourceItemId, resource.ResourceItemId, Name(resource.ResourceItemId, resource.DisplayName),
                a.ResourceQuantityArithmeticUnavailable ? null : Observed(resource.ConsumedQuantity, c.ItemResourceIdentity), null, ordered, notice);
        }).OrderBy(r => !r.Count.HasValue).ThenByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.ItemId, StringComparer.Ordinal).ToArray();
        var outputNotice = CapabilityNotice(t, "ui.crafting_current_unavailable", c.CompletionActions, c.OutputIdentity);
        var resourceNotice = CapabilityNotice(t, "ui.crafting_resource_current_unavailable", c.ItemResourceIdentity);
        if (a.WasRepairedFromInvalidState)
        { outputNotice = Join(outputNotice, t("ui.crafting_recorded_partial")); resourceNotice = Join(resourceNotice, t("ui.crafting_recorded_partial")); }
        return new CraftingPresentation(generation, outputRows, resourceRows, outputNotice, resourceNotice,
            t(outputSupported ? "ui.crafting_outputs_empty" : "ui.unavailable"),
            t(resourceSupported && !a.ResourceHistoryUnavailable ? "ui.crafting_resources_empty" : "ui.unavailable"));
    }
    private static bool Supported(MetricAvailability availability) => availability.State == AdapterCapabilityState.Supported;
    private static string CapabilityNotice(Func<string, string> text, string unavailable, params MetricAvailability[] capabilities) =>
        capabilities.Any(c => c.State == AdapterCapabilityState.DisabledIncompatible) ? text(unavailable)
        : capabilities.Any(c => c.State != AdapterCapabilityState.Supported) ? text("ui.crafting_recorded_partial") : "";
    private static long? Observed(long value, MetricAvailability availability) => value > 0 || Supported(availability) ? value : null;
    private static string Join(string first, string second) => first.Length == 0 ? second : first + "\n" + second;
    private static long? AssociatedProduction(CraftingRecipeAggregate recipe, CraftingResourceAssociationAggregate resource)
    {
        if (resource.ConsumptionActions == recipe.CompletionActions) return recipe.ProducedQuantity;
        if (recipe.BatchActions.Count != 1) return null;
        var batch = recipe.BatchActions.Single();
        if (batch.Value != recipe.CompletionActions || !long.TryParse(batch.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var units)
            || units <= 0 || recipe.CompletionActions > long.MaxValue / units || recipe.CompletionActions * units != recipe.ProducedQuantity
            || resource.ConsumptionActions > long.MaxValue / units) return null;
        return resource.ConsumptionActions * units;
    }
    private static long? ExactSum(IEnumerable<long> values) => ExactSum(values.Select(value => (long?)value));
    private static long? ExactSum(IEnumerable<long?> values)
    {
        long total = 0;
        foreach (var value in values)
        { if (!value.HasValue || value.Value < 0 || total > long.MaxValue - value.Value) return null; total += value.Value; }
        return total;
    }
}

internal static class CraftingIconPolicy
{
    // Crafting captures bare canonical native TypeIDs. Only icon lookup is normalized;
    // the captured identity used for selection, grouping and relationships is unchanged.
    public static string ResolveId(string id) => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value > 0 && value.ToString(CultureInfo.InvariantCulture) == id ? "duckov:item:" + id : id;
}
