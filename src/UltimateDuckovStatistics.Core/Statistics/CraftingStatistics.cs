using System.Globalization;
using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class CraftingMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability CompletionActions { get; set; } = Bootstrap();
    [DataMember(Order = 2)] public MetricAvailability ProducedQuantity { get; set; } = Bootstrap();
    [DataMember(Order = 3)] public MetricAvailability OutputIdentity { get; set; } = Bootstrap();
    [DataMember(Order = 4)] public MetricAvailability RecipeIdentity { get; set; } = Bootstrap();
    [DataMember(Order = 5)] public MetricAvailability BatchMetadata { get; set; } = Bootstrap();
    [DataMember(Order = 6)] public MetricAvailability WorkstationIdentity { get; set; } = Bootstrap();
    [DataMember(Order = 7)] public MetricAvailability ContextAttribution { get; set; } = Bootstrap();
    [DataMember(Order = 8)] public MetricAvailability MultipleOutputRecipes { get; set; } = Bootstrap();
    [DataMember(Order = 9)] public MetricAvailability ItemResourceIdentity { get; set; } = Bootstrap();
    [DataMember(Order = 10)] public MetricAvailability OutputResourceAssociation { get; set; } = Bootstrap();
    [DataMember(Order = 11)] public MetricAvailability CurrencyCharge { get; set; } = Bootstrap();
    [DataMember(Order = 12)] public MetricAvailability CurrencyMoneyCashSplit { get; set; } = Bootstrap();

    private static MetricAvailability Bootstrap() => new()
    {
        State = AdapterCapabilityState.DisabledIncompatible,
        Provenance = CraftingNativeContractPolicy.BootstrapProvenance
    };
}

[DataContract]
public sealed class CraftingResourceAggregate
{
    [DataMember(Order = 1)] public string ResourceItemId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public long ConsumedQuantity { get; set; }
}

[DataContract]
public sealed class CraftingResourceAssociationAggregate
{
    [DataMember(Order = 1)] public string ResourceItemId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public long ConsumptionActions { get; set; }
    [DataMember(Order = 4)] public long ConsumedQuantity { get; set; }
}

[DataContract]
public sealed class CraftingRecipeAggregate
{
    [DataMember(Order = 1)] public string RecipeId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public long CompletionActions { get; set; }
    [DataMember(Order = 3)] public long ProducedQuantity { get; set; }
    [DataMember(Order = 4)] public Dictionary<string, long> BatchActions { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 5)] public Dictionary<string, CraftingResourceAssociationAggregate> Resources { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 6)] public long CurrencyChargeActions { get; set; }
    [DataMember(Order = 7)] public long CurrencyCharged { get; set; }
}

[DataContract]
public sealed class CraftedOutputAggregate
{
    [DataMember(Order = 1)] public string OutputItemId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public long CompletionActions { get; set; }
    [DataMember(Order = 4)] public long ProducedQuantity { get; set; }
    [DataMember(Order = 5)] public Dictionary<string, CraftingRecipeAggregate> Recipes { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 6)] public long CurrencyChargeActions { get; set; }
    [DataMember(Order = 7)] public long CurrencyCharged { get; set; }
}

[DataContract]
public sealed class CraftingStatisticsAggregate
{
    [DataMember(Order = 1)] public long CompletionActions { get; set; }
    [DataMember(Order = 2)] public long ProducedQuantity { get; set; }
    [DataMember(Order = 3)] public Dictionary<string, CraftedOutputAggregate> Outputs { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 4)] public CraftingMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 5)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 6)] public string HistoricalProvenance { get; set; } = string.Empty;
    [DataMember(Order = 7)] public bool CompletionArithmeticUnavailable { get; set; }
    [DataMember(Order = 8)] public bool QuantityArithmeticUnavailable { get; set; }
    [DataMember(Order = 9)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 10)] public Dictionary<string, CraftingResourceAggregate> Resources { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 11)] public long CurrencyChargeActions { get; set; }
    [DataMember(Order = 12)] public long CurrencyCharged { get; set; }
    [DataMember(Order = 13)] public bool ResourceHistoryUnavailable { get; set; }
    [DataMember(Order = 14)] public string ResourceHistoryProvenance { get; set; } = string.Empty;
    [DataMember(Order = 15)] public bool ResourceActionArithmeticUnavailable { get; set; }
    [DataMember(Order = 16)] public bool ResourceQuantityArithmeticUnavailable { get; set; }
    [DataMember(Order = 17)] public bool CurrencyActionArithmeticUnavailable { get; set; }
    [DataMember(Order = 18)] public bool CurrencyAmountArithmeticUnavailable { get; set; }
    [DataMember(Order = 19)] public bool CurrencyHistoryUnavailable { get; set; }
    [DataMember(Order = 20)] public string CurrencyHistoryProvenance { get; set; } = string.Empty;
}

public sealed class CraftingResourceMutation
{
    public CraftingResourceMutation(string resourceItemId, string displayName, long consumptionActions, long consumedQuantity)
    {
        ResourceItemId = resourceItemId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        ConsumptionActions = consumptionActions;
        ConsumedQuantity = consumedQuantity;
    }

    public string ResourceItemId { get; }
    public string DisplayName { get; }
    public long ConsumptionActions { get; }
    public long ConsumedQuantity { get; }
}

public sealed class CraftingMutation
{
    public static CraftingMutation Empty { get; } = new(string.Empty, default, Array.Empty<CraftingMutationRow>());

    public CraftingMutation(string saveGenerationId, DateTime timestampUtc, IReadOnlyList<CraftingMutationRow> rows)
    {
        SaveGenerationId = saveGenerationId ?? string.Empty;
        TimestampUtc = timestampUtc;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public string SaveGenerationId { get; }
    public DateTime TimestampUtc { get; }
    public IReadOnlyList<CraftingMutationRow> Rows { get; }
    public bool IsEmpty => Rows.Count == 0;
}

public sealed class CraftingMutationRow
{
    public CraftingMutationRow(
        string outputItemId,
        string outputDisplayName,
        string recipeId,
        long completionActions,
        long producedQuantity,
        Dictionary<string, long> batchActions,
        bool recipeIdentityProven = true,
        bool batchMetadataProven = true,
        IReadOnlyList<CraftingResourceMutation>? resources = null,
        long currencyChargeActions = 0,
        long currencyCharged = 0,
        bool resourceEvidenceProven = true,
        bool currencyEvidenceProven = true)
    {
        OutputItemId = outputItemId ?? string.Empty;
        OutputDisplayName = outputDisplayName ?? string.Empty;
        RecipeId = recipeId ?? string.Empty;
        CompletionActions = completionActions;
        ProducedQuantity = producedQuantity;
        BatchActions = batchActions ?? throw new ArgumentNullException(nameof(batchActions));
        RecipeIdentityProven = recipeIdentityProven;
        BatchMetadataProven = batchMetadataProven;
        Resources = resources ?? Array.Empty<CraftingResourceMutation>();
        CurrencyChargeActions = currencyChargeActions;
        CurrencyCharged = currencyCharged;
        ResourceEvidenceProven = resourceEvidenceProven;
        CurrencyEvidenceProven = currencyEvidenceProven;
    }

    public string OutputItemId { get; }
    public string OutputDisplayName { get; }
    public string RecipeId { get; }
    public long CompletionActions { get; }
    public long ProducedQuantity { get; }
    public IReadOnlyDictionary<string, long> BatchActions { get; }
    public bool RecipeIdentityProven { get; }
    public bool BatchMetadataProven { get; }
    public IReadOnlyList<CraftingResourceMutation> Resources { get; }
    public long CurrencyChargeActions { get; }
    public long CurrencyCharged { get; }
    public bool ResourceEvidenceProven { get; }
    public bool CurrencyEvidenceProven { get; }
}

public static class CraftingStatisticsReducer
{
    private const string ArithmeticProvenance =
        "The metric reached the Int64 arithmetic limit; prior exact totals remain available, but further capture is disabled.";

    public static bool Apply(CraftingStatisticsAggregate aggregate, CraftingMutation mutation)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (mutation == null) throw new ArgumentNullException(nameof(mutation));
        ValidateMutation(mutation);
        NormalizePersisted(aggregate);
        if (aggregate.Capabilities.RecipeIdentity.State != AdapterCapabilityState.DisabledIncompatible
            && mutation.Rows.Any(row => !row.RecipeIdentityProven))
            throw new ArgumentException("Crafting recipe identity degraded without a matching capability restriction.", nameof(mutation));
        if (aggregate.Capabilities.BatchMetadata.State != AdapterCapabilityState.DisabledIncompatible
            && mutation.Rows.Any(row => !row.BatchMetadataProven))
            throw new ArgumentException("Crafting batch metadata degraded without a matching capability restriction.", nameof(mutation));
        if (aggregate.Capabilities.ItemResourceIdentity.State != AdapterCapabilityState.DisabledIncompatible
            && mutation.Rows.Any(row => !row.ResourceEvidenceProven))
            throw new ArgumentException("Crafting resource evidence degraded without a matching capability restriction.", nameof(mutation));
        if (aggregate.Capabilities.CurrencyCharge.State != AdapterCapabilityState.DisabledIncompatible
            && mutation.Rows.Any(row => !row.CurrencyEvidenceProven))
            throw new ArgumentException("Crafting currency evidence degraded without a matching capability restriction.", nameof(mutation));
        if (mutation.IsEmpty) return false;

        var actionRows = aggregate.CompletionArithmeticUnavailable
            ? Array.Empty<CraftingMutationRow>()
            : mutation.Rows.Where(row => row.CompletionActions != 0).ToArray();
        var quantityRows = aggregate.QuantityArithmeticUnavailable
            ? Array.Empty<CraftingMutationRow>()
            : mutation.Rows.Where(row => row.ProducedQuantity != 0).ToArray();
        var resourceRows = mutation.Rows.Where(row => row.ResourceEvidenceProven && row.Resources.Count != 0).ToArray();
        var currencyRows = mutation.Rows.Where(row => row.CurrencyEvidenceProven && row.CurrencyChargeActions != 0).ToArray();

        var actionOverflow = actionRows.Length != 0 && WouldOverflowActions(aggregate, actionRows);
        var quantityOverflow = quantityRows.Length != 0 && WouldOverflowQuantity(aggregate, quantityRows);
        var resourceActionOverflow = !aggregate.ResourceActionArithmeticUnavailable
                                     && resourceRows.Length != 0
                                     && WouldOverflowResourceActions(aggregate, resourceRows);
        var resourceQuantityOverflow = !aggregate.ResourceQuantityArithmeticUnavailable
                                       && resourceRows.Length != 0
                                       && WouldOverflowResourceQuantity(aggregate, resourceRows);
        var currencyActionOverflow = !aggregate.CurrencyActionArithmeticUnavailable
                                     && currencyRows.Length != 0
                                     && WouldOverflowCurrencyActions(aggregate, currencyRows);
        var currencyAmountOverflow = !aggregate.CurrencyAmountArithmeticUnavailable
                                     && currencyRows.Length != 0
                                     && WouldOverflowCurrencyAmount(aggregate, currencyRows);
        var changed = false;
        if (mutation.Rows.Any(row => !row.ResourceEvidenceProven))
            changed |= MarkResourceHistoryUnavailable(
                aggregate,
                aggregate.Capabilities.ItemResourceIdentity.Provenance);
        if (mutation.Rows.Any(row => !row.CurrencyEvidenceProven))
            changed |= MarkCurrencyHistoryUnavailable(
                aggregate,
                aggregate.Capabilities.CurrencyCharge.Provenance);

        if (actionOverflow)
        {
            aggregate.CompletionArithmeticUnavailable = true;
            aggregate.Capabilities.CompletionActions = Unavailable(ArithmeticProvenance);
            aggregate.Capabilities.BatchMetadata = Unavailable(ArithmeticProvenance);
            if (resourceRows.Length != 0)
            {
                aggregate.ResourceActionArithmeticUnavailable = true;
                aggregate.Capabilities.OutputResourceAssociation = Unavailable(ArithmeticProvenance);
                changed |= MarkResourceHistoryUnavailable(aggregate, ArithmeticProvenance);
            }
            if (currencyRows.Length != 0)
            {
                aggregate.CurrencyActionArithmeticUnavailable = true;
                aggregate.Capabilities.CurrencyCharge = Unavailable(ArithmeticProvenance);
                changed |= MarkCurrencyHistoryUnavailable(aggregate, ArithmeticProvenance);
            }
            changed = true;
        }
        else
        {
            foreach (var row in actionRows) ApplyActions(aggregate, row);
            changed |= actionRows.Length != 0;
        }

        if (quantityOverflow)
        {
            aggregate.QuantityArithmeticUnavailable = true;
            aggregate.Capabilities.ProducedQuantity = Unavailable(ArithmeticProvenance);
            changed = true;
        }
        else
        {
            foreach (var row in quantityRows) ApplyQuantity(aggregate, row);
            changed |= quantityRows.Length != 0;
        }

        if (resourceActionOverflow)
        {
            aggregate.ResourceActionArithmeticUnavailable = true;
            aggregate.Capabilities.OutputResourceAssociation = Unavailable(ArithmeticProvenance);
            changed |= MarkResourceHistoryUnavailable(aggregate, ArithmeticProvenance);
            changed = true;
        }
        else if (!aggregate.ResourceActionArithmeticUnavailable)
        {
            foreach (var row in resourceRows) ApplyResourceActions(aggregate, row);
            changed |= resourceRows.Length != 0;
        }

        if (resourceQuantityOverflow)
        {
            aggregate.ResourceQuantityArithmeticUnavailable = true;
            aggregate.Capabilities.ItemResourceIdentity = Unavailable(ArithmeticProvenance);
            aggregate.Capabilities.OutputResourceAssociation = Unavailable(ArithmeticProvenance);
            changed |= MarkResourceHistoryUnavailable(aggregate, ArithmeticProvenance);
            changed = true;
        }
        else if (!aggregate.ResourceQuantityArithmeticUnavailable)
        {
            foreach (var row in resourceRows) ApplyResourceQuantity(aggregate, row);
            changed |= resourceRows.Length != 0;
        }

        if (currencyActionOverflow)
        {
            aggregate.CurrencyActionArithmeticUnavailable = true;
            aggregate.Capabilities.CurrencyCharge = Unavailable(ArithmeticProvenance);
            changed |= MarkCurrencyHistoryUnavailable(aggregate, ArithmeticProvenance);
            changed = true;
        }
        else if (!aggregate.CurrencyActionArithmeticUnavailable)
        {
            foreach (var row in currencyRows) ApplyCurrencyActions(aggregate, row);
            changed |= currencyRows.Length != 0;
        }

        if (currencyAmountOverflow)
        {
            aggregate.CurrencyAmountArithmeticUnavailable = true;
            aggregate.Capabilities.CurrencyCharge = Unavailable(ArithmeticProvenance);
            changed |= MarkCurrencyHistoryUnavailable(aggregate, ArithmeticProvenance);
            changed = true;
        }
        else if (!aggregate.CurrencyAmountArithmeticUnavailable)
        {
            foreach (var row in currencyRows) ApplyCurrencyAmount(aggregate, row);
            changed |= currencyRows.Length != 0;
        }
        return changed;
    }

    public static bool Apply(CraftingStatisticsAggregate aggregate, string saveGenerationId, CraftingMutation mutation)
    {
        if (!string.Equals(saveGenerationId, mutation.SaveGenerationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Crafting mutation belongs to a different save generation.");
        return Apply(aggregate, mutation);
    }

    public static bool NormalizePersisted(CraftingStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        var changed = false;
        if (aggregate.Capabilities == null)
        {
            aggregate.Capabilities = new CraftingMetricCapabilities();
            aggregate.WasRepairedFromInvalidState = true;
            changed = true;
        }
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.CompletionActions, value => aggregate.Capabilities.CompletionActions = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.ProducedQuantity, value => aggregate.Capabilities.ProducedQuantity = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.OutputIdentity, value => aggregate.Capabilities.OutputIdentity = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.RecipeIdentity, value => aggregate.Capabilities.RecipeIdentity = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.BatchMetadata, value => aggregate.Capabilities.BatchMetadata = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.WorkstationIdentity, value => aggregate.Capabilities.WorkstationIdentity = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.ContextAttribution, value => aggregate.Capabilities.ContextAttribution = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.MultipleOutputRecipes, value => aggregate.Capabilities.MultipleOutputRecipes = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.ItemResourceIdentity, value => aggregate.Capabilities.ItemResourceIdentity = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.OutputResourceAssociation, value => aggregate.Capabilities.OutputResourceAssociation = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.CurrencyCharge, value => aggregate.Capabilities.CurrencyCharge = value);
        changed |= EnsureAvailability(aggregate, aggregate.Capabilities.CurrencyMoneyCashSplit, value => aggregate.Capabilities.CurrencyMoneyCashSplit = value);
        if (aggregate.Outputs == null)
        {
            aggregate.Outputs = new Dictionary<string, CraftedOutputAggregate>(StringComparer.Ordinal);
            aggregate.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (aggregate.Resources == null)
        {
            aggregate.Resources = new Dictionary<string, CraftingResourceAggregate>(StringComparer.Ordinal);
            aggregate.WasRepairedFromInvalidState = true;
            changed = true;
        }
        aggregate.HistoricalProvenance ??= string.Empty;
        aggregate.ResourceHistoryProvenance ??= string.Empty;
        aggregate.CurrencyHistoryProvenance ??= string.Empty;
        foreach (var entry in aggregate.Resources.ToArray())
        {
            if (entry.Value == null)
            {
                aggregate.Resources.Remove(entry.Key);
                aggregate.WasRepairedFromInvalidState = true;
                changed = true;
                continue;
            }
            entry.Value.ResourceItemId ??= entry.Key;
            entry.Value.DisplayName ??= string.Empty;
        }
        foreach (var entry in aggregate.Outputs.ToArray())
        {
            if (entry.Value == null)
            {
                aggregate.Outputs.Remove(entry.Key);
                aggregate.WasRepairedFromInvalidState = true;
                changed = true;
                continue;
            }
            var output = entry.Value;
            output.OutputItemId ??= entry.Key;
            output.DisplayName ??= string.Empty;
            if (output.Recipes == null)
            {
                output.Recipes = new Dictionary<string, CraftingRecipeAggregate>(StringComparer.Ordinal);
                aggregate.WasRepairedFromInvalidState = true;
                changed = true;
            }
            foreach (var recipeEntry in output.Recipes.ToArray())
            {
                if (recipeEntry.Value == null)
                {
                    output.Recipes.Remove(recipeEntry.Key);
                    aggregate.WasRepairedFromInvalidState = true;
                    changed = true;
                    continue;
                }
                var recipe = recipeEntry.Value;
                recipe.RecipeId ??= recipeEntry.Key;
                recipe.BatchActions ??= new Dictionary<string, long>(StringComparer.Ordinal);
                if (recipe.Resources == null)
                {
                    recipe.Resources = new Dictionary<string, CraftingResourceAssociationAggregate>(StringComparer.Ordinal);
                    aggregate.WasRepairedFromInvalidState = true;
                    changed = true;
                }
                foreach (var resourceEntry in recipe.Resources.ToArray())
                {
                    if (resourceEntry.Value == null)
                    {
                        recipe.Resources.Remove(resourceEntry.Key);
                        aggregate.WasRepairedFromInvalidState = true;
                        changed = true;
                        continue;
                    }
                    resourceEntry.Value.ResourceItemId ??= resourceEntry.Key;
                    resourceEntry.Value.DisplayName ??= string.Empty;
                }
            }
        }
        return changed;
    }

    public static void Validate(CraftingStatisticsAggregate aggregate)
    {
        if (aggregate == null || aggregate.Capabilities == null || aggregate.Outputs == null || aggregate.Resources == null)
            throw new ArgumentException("Crafting roots are missing.", nameof(aggregate));
        foreach (var value in EnumerateCapabilities(aggregate.Capabilities)) ValidateAvailability(value);
        if ((aggregate.HistoricalUnavailable && string.IsNullOrWhiteSpace(aggregate.HistoricalProvenance))
            || (aggregate.ResourceHistoryUnavailable && string.IsNullOrWhiteSpace(aggregate.ResourceHistoryProvenance))
            || (aggregate.CurrencyHistoryUnavailable && string.IsNullOrWhiteSpace(aggregate.CurrencyHistoryProvenance)))
            throw new ArgumentException("Crafting partial-history provenance is missing.", nameof(aggregate));
        if (aggregate.CompletionActions < 0 || aggregate.ProducedQuantity < 0
            || aggregate.CurrencyChargeActions < 0 || aggregate.CurrencyCharged < 0
            || aggregate.CurrencyChargeActions > aggregate.CompletionActions
            || HasImpossibleCurrencyPair(
                aggregate,
                aggregate.CurrencyChargeActions,
                aggregate.CurrencyCharged))
            throw new ArgumentException("Crafting totals are invalid.", nameof(aggregate));

        var associationQuantityByResource = new Dictionary<string, long>(StringComparer.Ordinal);
        long outputActions = 0;
        long outputQuantity = 0;
        long outputCurrencyActions = 0;
        long outputCurrencyAmount = 0;
        foreach (var entry in aggregate.Outputs)
        {
            var output = entry.Value ?? throw new ArgumentException("Crafted output is missing.", nameof(aggregate));
            if (string.IsNullOrWhiteSpace(entry.Key) || !string.Equals(entry.Key, output.OutputItemId, StringComparison.Ordinal)
                || output.CompletionActions < 0 || output.ProducedQuantity < 0 || output.Recipes == null
                || output.CurrencyChargeActions < 0 || output.CurrencyCharged < 0
                || output.CurrencyChargeActions > output.CompletionActions
                || HasImpossibleCurrencyPair(
                    aggregate,
                    output.CurrencyChargeActions,
                    output.CurrencyCharged))
                throw new ArgumentException("Crafted output totals are invalid.", nameof(aggregate));
            outputActions = checked(outputActions + output.CompletionActions);
            outputQuantity = checked(outputQuantity + output.ProducedQuantity);
            outputCurrencyActions = checked(outputCurrencyActions + output.CurrencyChargeActions);
            outputCurrencyAmount = checked(outputCurrencyAmount + output.CurrencyCharged);
            long recipeActions = 0;
            long recipeQuantity = 0;
            long recipeCurrencyActions = 0;
            long recipeCurrencyAmount = 0;
            foreach (var recipeEntry in output.Recipes)
            {
                var recipe = recipeEntry.Value ?? throw new ArgumentException("Crafting recipe is missing.", nameof(aggregate));
                if (string.IsNullOrWhiteSpace(recipeEntry.Key) || !string.Equals(recipeEntry.Key, recipe.RecipeId, StringComparison.Ordinal)
                    || recipe.CompletionActions < 0 || recipe.ProducedQuantity < 0 || recipe.BatchActions == null
                    || recipe.Resources == null || recipe.CurrencyChargeActions < 0 || recipe.CurrencyCharged < 0
                    || recipe.CurrencyChargeActions > recipe.CompletionActions
                    || HasImpossibleCurrencyPair(
                        aggregate,
                        recipe.CurrencyChargeActions,
                        recipe.CurrencyCharged))
                    throw new ArgumentException("Crafting recipe totals are invalid.", nameof(aggregate));
                recipeActions = checked(recipeActions + recipe.CompletionActions);
                recipeQuantity = checked(recipeQuantity + recipe.ProducedQuantity);
                recipeCurrencyActions = checked(recipeCurrencyActions + recipe.CurrencyChargeActions);
                recipeCurrencyAmount = checked(recipeCurrencyAmount + recipe.CurrencyCharged);
                ValidateBatches(aggregate, recipe);
                foreach (var resourceEntry in recipe.Resources)
                {
                    var resource = resourceEntry.Value
                        ?? throw new ArgumentException("Crafting resource association is missing.", nameof(aggregate));
                    if (string.IsNullOrWhiteSpace(resourceEntry.Key)
                        || !string.Equals(resourceEntry.Key, resource.ResourceItemId, StringComparison.Ordinal)
                        || resource.ConsumptionActions < 0 || resource.ConsumedQuantity < 0
                        || (!aggregate.ResourceActionArithmeticUnavailable && resource.ConsumptionActions == 0)
                        || (!aggregate.ResourceQuantityArithmeticUnavailable && resource.ConsumedQuantity == 0)
                        || (!aggregate.ResourceQuantityArithmeticUnavailable
                            && resource.ConsumptionActions > resource.ConsumedQuantity)
                        || resource.ConsumptionActions > recipe.CompletionActions)
                        throw new ArgumentException("Crafting resource association is invalid.", nameof(aggregate));
                    associationQuantityByResource.TryGetValue(resource.ResourceItemId, out var prior);
                    associationQuantityByResource[resource.ResourceItemId] = checked(prior + resource.ConsumedQuantity);
                }
            }
            ValidateComposition(
                aggregate.Capabilities.RecipeIdentity,
                recipeActions,
                output.CompletionActions,
                recipeQuantity,
                output.ProducedQuantity,
                "Crafting recipe composition is inconsistent.");
            ValidateEqual(
                recipeCurrencyActions,
                output.CurrencyChargeActions,
                recipeCurrencyAmount,
                output.CurrencyCharged,
                "Crafting recipe currency composition is inconsistent.");
        }
        if (outputActions != aggregate.CompletionActions || outputQuantity != aggregate.ProducedQuantity)
            throw new ArgumentException("Crafting output composition is inconsistent.", nameof(aggregate));
        ValidateEqual(
            outputCurrencyActions,
            aggregate.CurrencyChargeActions,
            outputCurrencyAmount,
            aggregate.CurrencyCharged,
            "Crafting output currency composition is inconsistent.");

        foreach (var entry in aggregate.Resources)
        {
            var resource = entry.Value ?? throw new ArgumentException("Crafting resource is missing.", nameof(aggregate));
            if (string.IsNullOrWhiteSpace(entry.Key) || !string.Equals(entry.Key, resource.ResourceItemId, StringComparison.Ordinal)
                || resource.ConsumedQuantity <= 0)
                throw new ArgumentException("Crafting resource total is invalid.", nameof(aggregate));
            associationQuantityByResource.TryGetValue(entry.Key, out var associated);
            if (associated != resource.ConsumedQuantity)
                throw new ArgumentException("Crafting resource association composition is inconsistent.", nameof(aggregate));
        }
        if (associationQuantityByResource.Keys.Except(aggregate.Resources.Keys, StringComparer.Ordinal).Any())
            throw new ArgumentException("Crafting resource association has no lifetime resource total.", nameof(aggregate));
    }

    public static CraftingStatisticsAggregate Clone(CraftingStatisticsAggregate? source)
    {
        source ??= new CraftingStatisticsAggregate();
        NormalizePersisted(source);
        return new CraftingStatisticsAggregate
        {
            CompletionActions = source.CompletionActions,
            ProducedQuantity = source.ProducedQuantity,
            Outputs = source.Outputs.ToDictionary(entry => entry.Key, entry => CloneOutput(entry.Value), StringComparer.Ordinal),
            Capabilities = CloneCapabilities(source.Capabilities),
            HistoricalUnavailable = source.HistoricalUnavailable,
            HistoricalProvenance = source.HistoricalProvenance,
            CompletionArithmeticUnavailable = source.CompletionArithmeticUnavailable,
            QuantityArithmeticUnavailable = source.QuantityArithmeticUnavailable,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
            Resources = source.Resources.ToDictionary(
                entry => entry.Key,
                entry => new CraftingResourceAggregate
                {
                    ResourceItemId = entry.Value.ResourceItemId,
                    DisplayName = entry.Value.DisplayName,
                    ConsumedQuantity = entry.Value.ConsumedQuantity
                },
                StringComparer.Ordinal),
            CurrencyChargeActions = source.CurrencyChargeActions,
            CurrencyCharged = source.CurrencyCharged,
            ResourceHistoryUnavailable = source.ResourceHistoryUnavailable,
            ResourceHistoryProvenance = source.ResourceHistoryProvenance,
            ResourceActionArithmeticUnavailable = source.ResourceActionArithmeticUnavailable,
            ResourceQuantityArithmeticUnavailable = source.ResourceQuantityArithmeticUnavailable,
            CurrencyActionArithmeticUnavailable = source.CurrencyActionArithmeticUnavailable,
            CurrencyAmountArithmeticUnavailable = source.CurrencyAmountArithmeticUnavailable,
            CurrencyHistoryUnavailable = source.CurrencyHistoryUnavailable,
            CurrencyHistoryProvenance = source.CurrencyHistoryProvenance
        };
    }

    public static CraftingMetricCapabilities CloneCapabilities(CraftingMetricCapabilities source) => new()
    {
        CompletionActions = Clone(source.CompletionActions),
        ProducedQuantity = Clone(source.ProducedQuantity),
        OutputIdentity = Clone(source.OutputIdentity),
        RecipeIdentity = Clone(source.RecipeIdentity),
        BatchMetadata = Clone(source.BatchMetadata),
        WorkstationIdentity = Clone(source.WorkstationIdentity),
        ContextAttribution = Clone(source.ContextAttribution),
        MultipleOutputRecipes = Clone(source.MultipleOutputRecipes),
        ItemResourceIdentity = Clone(source.ItemResourceIdentity),
        OutputResourceAssociation = Clone(source.OutputResourceAssociation),
        CurrencyCharge = Clone(source.CurrencyCharge),
        CurrencyMoneyCashSplit = Clone(source.CurrencyMoneyCashSplit)
    };

    public static void InitializeOrRestrictCapabilities(CraftingStatisticsAggregate aggregate, CraftingMetricCapabilities current)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (current == null) throw new ArgumentNullException(nameof(current));
        NormalizePersisted(aggregate);
        aggregate.Capabilities = RestrictWithCurrent(aggregate.Capabilities, current, initializeBootstrap: true);
        if (aggregate.CompletionArithmeticUnavailable)
        {
            aggregate.Capabilities.CompletionActions = Unavailable(ArithmeticProvenance);
            aggregate.Capabilities.BatchMetadata = Unavailable(ArithmeticProvenance);
        }
        if (aggregate.QuantityArithmeticUnavailable) aggregate.Capabilities.ProducedQuantity = Unavailable(ArithmeticProvenance);
        if (aggregate.ResourceActionArithmeticUnavailable)
            aggregate.Capabilities.OutputResourceAssociation = Unavailable(ArithmeticProvenance);
        if (aggregate.ResourceQuantityArithmeticUnavailable)
        {
            aggregate.Capabilities.ItemResourceIdentity = Unavailable(ArithmeticProvenance);
            aggregate.Capabilities.OutputResourceAssociation = Unavailable(ArithmeticProvenance);
        }
        if (aggregate.CurrencyActionArithmeticUnavailable || aggregate.CurrencyAmountArithmeticUnavailable)
            aggregate.Capabilities.CurrencyCharge = Unavailable(ArithmeticProvenance);
    }

    public static CraftingMetricCapabilities RestrictWithCurrent(
        CraftingMetricCapabilities recorded,
        CraftingMetricCapabilities current) => RestrictWithCurrent(recorded, current, initializeBootstrap: false);

    private static CraftingMetricCapabilities RestrictWithCurrent(
        CraftingMetricCapabilities recorded,
        CraftingMetricCapabilities current,
        bool initializeBootstrap) => new()
        {
            CompletionActions = Restrict(recorded.CompletionActions, current.CompletionActions, initializeBootstrap),
            ProducedQuantity = Restrict(recorded.ProducedQuantity, current.ProducedQuantity, initializeBootstrap),
            OutputIdentity = Restrict(recorded.OutputIdentity, current.OutputIdentity, initializeBootstrap),
            RecipeIdentity = Restrict(recorded.RecipeIdentity, current.RecipeIdentity, initializeBootstrap),
            BatchMetadata = Restrict(recorded.BatchMetadata, current.BatchMetadata, initializeBootstrap),
            WorkstationIdentity = Restrict(recorded.WorkstationIdentity, current.WorkstationIdentity, initializeBootstrap),
            ContextAttribution = Restrict(recorded.ContextAttribution, current.ContextAttribution, initializeBootstrap),
            MultipleOutputRecipes = Restrict(recorded.MultipleOutputRecipes, current.MultipleOutputRecipes, initializeBootstrap),
            ItemResourceIdentity = Restrict(recorded.ItemResourceIdentity, current.ItemResourceIdentity, initializeBootstrap),
            OutputResourceAssociation = Restrict(recorded.OutputResourceAssociation, current.OutputResourceAssociation, initializeBootstrap),
            CurrencyCharge = Restrict(recorded.CurrencyCharge, current.CurrencyCharge, initializeBootstrap),
            CurrencyMoneyCashSplit = Restrict(recorded.CurrencyMoneyCashSplit, current.CurrencyMoneyCashSplit, initializeBootstrap)
        };

    private static void ApplyActions(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        var output = GetOutput(aggregate, row);
        aggregate.CompletionActions += row.CompletionActions;
        output.CompletionActions += row.CompletionActions;
        if (!row.RecipeIdentityProven) return;
        var recipe = GetRecipe(output, row);
        recipe.CompletionActions += row.CompletionActions;
        if (!row.BatchMetadataProven) return;
        foreach (var batch in row.BatchActions)
        {
            recipe.BatchActions.TryGetValue(batch.Key, out var current);
            recipe.BatchActions[batch.Key] = current + batch.Value;
        }
    }

    private static void ApplyQuantity(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        var output = GetOutput(aggregate, row);
        aggregate.ProducedQuantity += row.ProducedQuantity;
        output.ProducedQuantity += row.ProducedQuantity;
        if (row.RecipeIdentityProven) GetRecipe(output, row).ProducedQuantity += row.ProducedQuantity;
    }

    private static void ApplyResourceActions(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        var recipe = GetRecipe(GetOutput(aggregate, row), row);
        foreach (var resource in row.Resources)
            GetResourceAssociation(recipe, resource).ConsumptionActions += resource.ConsumptionActions;
    }

    private static void ApplyResourceQuantity(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        var recipe = GetRecipe(GetOutput(aggregate, row), row);
        foreach (var resource in row.Resources)
        {
            GetResource(aggregate, resource).ConsumedQuantity += resource.ConsumedQuantity;
            GetResourceAssociation(recipe, resource).ConsumedQuantity += resource.ConsumedQuantity;
        }
    }

    private static void ApplyCurrencyActions(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        var output = GetOutput(aggregate, row);
        var recipe = GetRecipe(output, row);
        aggregate.CurrencyChargeActions += row.CurrencyChargeActions;
        output.CurrencyChargeActions += row.CurrencyChargeActions;
        recipe.CurrencyChargeActions += row.CurrencyChargeActions;
    }

    private static void ApplyCurrencyAmount(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        var output = GetOutput(aggregate, row);
        var recipe = GetRecipe(output, row);
        aggregate.CurrencyCharged += row.CurrencyCharged;
        output.CurrencyCharged += row.CurrencyCharged;
        recipe.CurrencyCharged += row.CurrencyCharged;
    }

    private static bool WouldOverflowActions(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows)
    {
        long totalDelta = 0;
        var outputDeltas = new Dictionary<string, long>(StringComparer.Ordinal);
        var recipeDeltas = new Dictionary<(string Output, string Recipe), long>();
        var batchDeltas = new Dictionary<(string Output, string Recipe, string Batch), long>();
        foreach (var row in rows)
        {
            if (!TryAdd(ref totalDelta, row.CompletionActions)) return true;
            var output = aggregate.Outputs.TryGetValue(row.OutputItemId, out var existing) ? existing : null;
            if (!TryAccumulate(outputDeltas, row.OutputItemId, row.CompletionActions, output?.CompletionActions ?? 0)) return true;
            if (!row.RecipeIdentityProven) continue;
            var recipeKey = (row.OutputItemId, row.RecipeId);
            var recipe = output != null && output.Recipes.TryGetValue(row.RecipeId, out var existingRecipe) ? existingRecipe : null;
            if (!TryAccumulate(recipeDeltas, recipeKey, row.CompletionActions, recipe?.CompletionActions ?? 0)) return true;
            if (!row.BatchMetadataProven) continue;
            foreach (var batch in row.BatchActions)
            {
                var key = (row.OutputItemId, row.RecipeId, batch.Key);
                var current = 0L;
                if (recipe != null) recipe.BatchActions.TryGetValue(batch.Key, out current);
                if (!TryAccumulate(batchDeltas, key, batch.Value, current)) return true;
            }
        }
        return WouldAddOverflow(aggregate.CompletionActions, totalDelta);
    }

    private static bool WouldOverflowQuantity(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows)
    {
        long totalDelta = 0;
        var outputDeltas = new Dictionary<string, long>(StringComparer.Ordinal);
        var recipeDeltas = new Dictionary<(string Output, string Recipe), long>();
        foreach (var row in rows)
        {
            if (!TryAdd(ref totalDelta, row.ProducedQuantity)) return true;
            var output = aggregate.Outputs.TryGetValue(row.OutputItemId, out var existing) ? existing : null;
            if (!TryAccumulate(outputDeltas, row.OutputItemId, row.ProducedQuantity, output?.ProducedQuantity ?? 0)) return true;
            if (!row.RecipeIdentityProven) continue;
            var recipeKey = (row.OutputItemId, row.RecipeId);
            var recipe = output != null && output.Recipes.TryGetValue(row.RecipeId, out var existingRecipe) ? existingRecipe : null;
            if (!TryAccumulate(recipeDeltas, recipeKey, row.ProducedQuantity, recipe?.ProducedQuantity ?? 0)) return true;
        }
        return WouldAddOverflow(aggregate.ProducedQuantity, totalDelta);
    }

    private static bool WouldOverflowResourceActions(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows)
    {
        var deltas = new Dictionary<(string Output, string Recipe, string Resource), long>();
        foreach (var row in rows)
        {
            var recipe = TryGetRecipe(aggregate, row);
            foreach (var resource in row.Resources)
            {
                var key = (row.OutputItemId, row.RecipeId, resource.ResourceItemId);
                var current = 0L;
                if (recipe != null && recipe.Resources.TryGetValue(resource.ResourceItemId, out var association))
                    current = association.ConsumptionActions;
                if (!TryAccumulate(deltas, key, resource.ConsumptionActions, current)) return true;
            }
        }
        return false;
    }

    private static bool WouldOverflowResourceQuantity(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows)
    {
        var lifetimeDeltas = new Dictionary<string, long>(StringComparer.Ordinal);
        var associationDeltas = new Dictionary<(string Output, string Recipe, string Resource), long>();
        foreach (var row in rows)
        {
            var recipe = TryGetRecipe(aggregate, row);
            foreach (var resource in row.Resources)
            {
                aggregate.Resources.TryGetValue(resource.ResourceItemId, out var lifetime);
                if (!TryAccumulate(lifetimeDeltas, resource.ResourceItemId, resource.ConsumedQuantity, lifetime?.ConsumedQuantity ?? 0))
                    return true;
                var key = (row.OutputItemId, row.RecipeId, resource.ResourceItemId);
                var current = 0L;
                if (recipe != null && recipe.Resources.TryGetValue(resource.ResourceItemId, out var association))
                    current = association.ConsumedQuantity;
                if (!TryAccumulate(associationDeltas, key, resource.ConsumedQuantity, current)) return true;
            }
        }
        return false;
    }

    private static bool WouldOverflowCurrencyActions(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows) =>
        WouldOverflowCurrency(
            aggregate,
            rows,
            aggregate.CurrencyChargeActions,
            row => row.CurrencyChargeActions,
            output => output.CurrencyChargeActions,
            recipe => recipe.CurrencyChargeActions);

    private static bool WouldOverflowCurrencyAmount(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows) =>
        WouldOverflowCurrency(
            aggregate,
            rows,
            aggregate.CurrencyCharged,
            row => row.CurrencyCharged,
            output => output.CurrencyCharged,
            recipe => recipe.CurrencyCharged);

    private static bool WouldOverflowCurrency(
        CraftingStatisticsAggregate aggregate,
        IReadOnlyList<CraftingMutationRow> rows,
        long currentTotal,
        Func<CraftingMutationRow, long> rowValue,
        Func<CraftedOutputAggregate, long> outputValue,
        Func<CraftingRecipeAggregate, long> recipeValue)
    {
        long totalDelta = 0;
        var outputDeltas = new Dictionary<string, long>(StringComparer.Ordinal);
        var recipeDeltas = new Dictionary<(string Output, string Recipe), long>();
        foreach (var row in rows)
        {
            var delta = rowValue(row);
            if (!TryAdd(ref totalDelta, delta)) return true;
            aggregate.Outputs.TryGetValue(row.OutputItemId, out var output);
            if (!TryAccumulate(outputDeltas, row.OutputItemId, delta, output == null ? 0 : outputValue(output))) return true;
            CraftingRecipeAggregate? recipe = null;
            if (output != null) output.Recipes.TryGetValue(row.RecipeId, out recipe);
            if (!TryAccumulate(recipeDeltas, (row.OutputItemId, row.RecipeId), delta, recipe == null ? 0 : recipeValue(recipe)))
                return true;
        }
        return WouldAddOverflow(currentTotal, totalDelta);
    }

    private static CraftedOutputAggregate GetOutput(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        if (!aggregate.Outputs.TryGetValue(row.OutputItemId, out var output))
        {
            output = new CraftedOutputAggregate
            {
                OutputItemId = row.OutputItemId,
                DisplayName = FallbackName(row.OutputDisplayName, row.OutputItemId)
            };
            aggregate.Outputs.Add(row.OutputItemId, output);
        }
        else output.DisplayName = UpdatedName(output.DisplayName, row.OutputDisplayName, row.OutputItemId);
        return output;
    }

    private static CraftingRecipeAggregate GetRecipe(CraftedOutputAggregate output, CraftingMutationRow row)
    {
        if (!output.Recipes.TryGetValue(row.RecipeId, out var recipe))
        {
            recipe = new CraftingRecipeAggregate { RecipeId = row.RecipeId };
            output.Recipes.Add(row.RecipeId, recipe);
        }
        return recipe;
    }

    private static CraftingRecipeAggregate? TryGetRecipe(CraftingStatisticsAggregate aggregate, CraftingMutationRow row) =>
        aggregate.Outputs.TryGetValue(row.OutputItemId, out var output)
        && output.Recipes.TryGetValue(row.RecipeId, out var recipe)
            ? recipe
            : null;

    private static CraftingResourceAggregate GetResource(CraftingStatisticsAggregate aggregate, CraftingResourceMutation row)
    {
        if (!aggregate.Resources.TryGetValue(row.ResourceItemId, out var resource))
        {
            resource = new CraftingResourceAggregate
            {
                ResourceItemId = row.ResourceItemId,
                DisplayName = FallbackName(row.DisplayName, row.ResourceItemId)
            };
            aggregate.Resources.Add(row.ResourceItemId, resource);
        }
        else resource.DisplayName = UpdatedName(resource.DisplayName, row.DisplayName, row.ResourceItemId);
        return resource;
    }

    private static CraftingResourceAssociationAggregate GetResourceAssociation(
        CraftingRecipeAggregate recipe,
        CraftingResourceMutation row)
    {
        if (!recipe.Resources.TryGetValue(row.ResourceItemId, out var resource))
        {
            resource = new CraftingResourceAssociationAggregate
            {
                ResourceItemId = row.ResourceItemId,
                DisplayName = FallbackName(row.DisplayName, row.ResourceItemId)
            };
            recipe.Resources.Add(row.ResourceItemId, resource);
        }
        else resource.DisplayName = UpdatedName(resource.DisplayName, row.DisplayName, row.ResourceItemId);
        return resource;
    }

    private static void ValidateMutation(CraftingMutation mutation)
    {
        foreach (var row in mutation.Rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.OutputItemId) || string.IsNullOrWhiteSpace(row.RecipeId)
                || row.CompletionActions < 0 || row.ProducedQuantity < 0
                || row.CurrencyChargeActions < 0 || row.CurrencyCharged < 0)
                throw new ArgumentOutOfRangeException(nameof(mutation), "Crafting mutation rows must have identities and non-negative totals.");
            if (row.BatchMetadataProven && !row.RecipeIdentityProven)
                throw new ArgumentException("Crafting batch metadata requires proven recipe identity.", nameof(mutation));
            if (!row.BatchMetadataProven && row.BatchActions.Count != 0)
                throw new ArgumentException("Unproven crafting batch metadata cannot be retained.", nameof(mutation));
            if (!row.ResourceEvidenceProven && row.Resources.Count != 0)
                throw new ArgumentException("Unproven crafting resource evidence cannot be retained.", nameof(mutation));
            if (row.ResourceEvidenceProven && !row.RecipeIdentityProven && row.Resources.Count != 0)
                throw new ArgumentException("Crafting resource association requires proven recipe identity.", nameof(mutation));
            if (!row.CurrencyEvidenceProven && (row.CurrencyChargeActions != 0 || row.CurrencyCharged != 0))
                throw new ArgumentException("Unproven crafting currency evidence cannot be retained.", nameof(mutation));
            if (row.CurrencyEvidenceProven && !row.RecipeIdentityProven && row.CurrencyChargeActions != 0)
                throw new ArgumentException("Crafting currency association requires proven recipe identity.", nameof(mutation));
            if ((row.CurrencyChargeActions == 0) != (row.CurrencyCharged == 0)
                || row.CurrencyChargeActions > row.CompletionActions)
                throw new ArgumentException("Crafting currency evidence is inconsistent.", nameof(mutation));

            ValidateMutationBatches(row, mutation);
            var resourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resource in row.Resources)
            {
                if (resource == null || string.IsNullOrWhiteSpace(resource.ResourceItemId)
                    || resource.ConsumptionActions <= 0 || resource.ConsumedQuantity <= 0
                    || resource.ConsumptionActions > row.CompletionActions
                    || !resourceIds.Add(resource.ResourceItemId))
                    throw new ArgumentException("Crafting resource mutation is invalid or not canonicalized.", nameof(mutation));
            }
        }
    }

    private static void ValidateMutationBatches(CraftingMutationRow row, CraftingMutation mutation)
    {
        long batches = 0;
        long batchQuantity = 0;
        foreach (var batch in row.BatchActions)
        {
            if (!long.TryParse(batch.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                || quantity <= 0 || batch.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(mutation), "Crafting batch metadata is invalid.");
            batches = checked(batches + batch.Value);
            batchQuantity = checked(batchQuantity + checked(quantity * batch.Value));
        }
        if (row.BatchMetadataProven
            && (row.BatchActions.Count == 0 || batches != row.CompletionActions || batchQuantity != row.ProducedQuantity))
            throw new ArgumentException("Crafting batch composition is inconsistent.", nameof(mutation));
    }

    private static void ValidateBatches(CraftingStatisticsAggregate aggregate, CraftingRecipeAggregate recipe)
    {
        long batchActions = 0;
        long batchQuantity = 0;
        var quantityCompositionRequired = !aggregate.QuantityArithmeticUnavailable
                                          && aggregate.Capabilities.ProducedQuantity.State != AdapterCapabilityState.DisabledIncompatible;
        foreach (var batch in recipe.BatchActions)
        {
            if (!long.TryParse(batch.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                || quantity <= 0 || batch.Value < 0)
                throw new ArgumentException("Crafting batch metadata is invalid.", nameof(aggregate));
            batchActions = checked(batchActions + batch.Value);
            if (quantityCompositionRequired) batchQuantity = checked(batchQuantity + checked(quantity * batch.Value));
        }
        if (aggregate.Capabilities.BatchMetadata.State == AdapterCapabilityState.DisabledIncompatible)
        {
            if (batchActions > recipe.CompletionActions || (quantityCompositionRequired && batchQuantity > recipe.ProducedQuantity))
                throw new ArgumentException("Crafting batch composition exceeds its recipe totals.", nameof(aggregate));
        }
        else if (batchActions != recipe.CompletionActions || (quantityCompositionRequired && batchQuantity != recipe.ProducedQuantity))
            throw new ArgumentException("Crafting batch composition is inconsistent.", nameof(aggregate));
    }

    private static void ValidateComposition(
        MetricAvailability capability,
        long childActions,
        long parentActions,
        long childQuantity,
        long parentQuantity,
        string message)
    {
        if (capability.State == AdapterCapabilityState.DisabledIncompatible)
        {
            if (childActions > parentActions || childQuantity > parentQuantity) throw new ArgumentException(message);
        }
        else if (childActions != parentActions || childQuantity != parentQuantity) throw new ArgumentException(message);
    }

    private static void ValidateEqual(
        long childActions,
        long parentActions,
        long childAmount,
        long parentAmount,
        string message)
    {
        if (childActions != parentActions || childAmount != parentAmount) throw new ArgumentException(message);
    }

    private static bool HasImpossibleCurrencyPair(
        CraftingStatisticsAggregate aggregate,
        long actions,
        long amount)
    {
        if (aggregate.CurrencyActionArithmeticUnavailable
            && aggregate.CurrencyAmountArithmeticUnavailable)
            return false;
        if (aggregate.CurrencyActionArithmeticUnavailable)
            return amount < actions;
        if (aggregate.CurrencyAmountArithmeticUnavailable)
            return actions == 0 && amount > 0;
        return (actions == 0) != (amount == 0) || amount < actions;
    }

    private static CraftedOutputAggregate CloneOutput(CraftedOutputAggregate source) => new()
    {
        OutputItemId = source.OutputItemId,
        DisplayName = source.DisplayName,
        CompletionActions = source.CompletionActions,
        ProducedQuantity = source.ProducedQuantity,
        CurrencyChargeActions = source.CurrencyChargeActions,
        CurrencyCharged = source.CurrencyCharged,
        Recipes = source.Recipes.ToDictionary(
            entry => entry.Key,
            entry => new CraftingRecipeAggregate
            {
                RecipeId = entry.Value.RecipeId,
                CompletionActions = entry.Value.CompletionActions,
                ProducedQuantity = entry.Value.ProducedQuantity,
                BatchActions = new Dictionary<string, long>(entry.Value.BatchActions, StringComparer.Ordinal),
                CurrencyChargeActions = entry.Value.CurrencyChargeActions,
                CurrencyCharged = entry.Value.CurrencyCharged,
                Resources = entry.Value.Resources.ToDictionary(
                    resource => resource.Key,
                    resource => new CraftingResourceAssociationAggregate
                    {
                        ResourceItemId = resource.Value.ResourceItemId,
                        DisplayName = resource.Value.DisplayName,
                        ConsumptionActions = resource.Value.ConsumptionActions,
                        ConsumedQuantity = resource.Value.ConsumedQuantity
                    },
                    StringComparer.Ordinal)
            },
            StringComparer.Ordinal)
    };

    private static IEnumerable<MetricAvailability> EnumerateCapabilities(CraftingMetricCapabilities value)
    {
        yield return value.CompletionActions;
        yield return value.ProducedQuantity;
        yield return value.OutputIdentity;
        yield return value.RecipeIdentity;
        yield return value.BatchMetadata;
        yield return value.WorkstationIdentity;
        yield return value.ContextAttribution;
        yield return value.MultipleOutputRecipes;
        yield return value.ItemResourceIdentity;
        yield return value.OutputResourceAssociation;
        yield return value.CurrencyCharge;
        yield return value.CurrencyMoneyCashSplit;
    }

    private static bool TryAdd(ref long current, long delta)
    {
        if (WouldAddOverflow(current, delta)) return false;
        current += delta;
        return true;
    }

    private static bool TryAccumulate<TKey>(Dictionary<TKey, long> deltas, TKey key, long delta, long persisted)
        where TKey : notnull
    {
        deltas.TryGetValue(key, out var prior);
        if (WouldAddOverflow(prior, delta)) return false;
        var combined = prior + delta;
        if (WouldAddOverflow(persisted, combined)) return false;
        deltas[key] = combined;
        return true;
    }

    private static bool WouldAddOverflow(long current, long delta) => delta > long.MaxValue - current;

    private static string FallbackName(string value, string id) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown item " + id : value;

    private static string UpdatedName(string current, string incoming, string id)
    {
        if (!string.IsNullOrWhiteSpace(incoming)) return incoming;
        return string.IsNullOrWhiteSpace(current) ? "Unknown item " + id : current;
    }

    private static bool EnsureAvailability(
        CraftingStatisticsAggregate aggregate,
        MetricAvailability? availability,
        Action<MetricAvailability> replace)
    {
        if (availability != null) return false;
        replace(CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            CraftingNativeContractPolicy.BootstrapProvenance));
        aggregate.WasRepairedFromInvalidState = true;
        return true;
    }

    private static bool MarkResourceHistoryUnavailable(CraftingStatisticsAggregate aggregate, string provenance)
    {
        if (aggregate.ResourceHistoryUnavailable) return false;
        aggregate.ResourceHistoryUnavailable = true;
        aggregate.ResourceHistoryProvenance = string.IsNullOrWhiteSpace(provenance)
            ? "Crafting resource history contains an event without exact resource evidence."
            : provenance;
        return true;
    }

    private static bool MarkCurrencyHistoryUnavailable(CraftingStatisticsAggregate aggregate, string provenance)
    {
        if (aggregate.CurrencyHistoryUnavailable) return false;
        aggregate.CurrencyHistoryUnavailable = true;
        aggregate.CurrencyHistoryProvenance = string.IsNullOrWhiteSpace(provenance)
            ? "Crafting currency history contains an event without exact currency evidence."
            : provenance;
        return true;
    }

    private static MetricAvailability Restrict(MetricAvailability recorded, MetricAvailability current, bool initializeBootstrap)
    {
        if (initializeBootstrap && (IsBootstrap(recorded) || IsBlankDefault(recorded))) return Clone(current);
        return (int)recorded.State >= (int)current.State ? Clone(recorded) : Clone(current);
    }

    private static bool IsBootstrap(MetricAvailability value) =>
        value.State == AdapterCapabilityState.DisabledIncompatible
        && string.Equals(value.Provenance, CraftingNativeContractPolicy.BootstrapProvenance, StringComparison.Ordinal);

    private static bool IsBlankDefault(MetricAvailability value) =>
        value.State == AdapterCapabilityState.DisabledIncompatible && string.IsNullOrWhiteSpace(value.Provenance);

    private static MetricAvailability Unavailable(string provenance) =>
        CraftingNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, provenance);

    private static MetricAvailability Clone(MetricAvailability value) => new()
    {
        State = value.State,
        Provenance = value.Provenance ?? string.Empty
    };

    private static void ValidateAvailability(MetricAvailability? value)
    {
        if (value == null
            || !Enum.IsDefined(typeof(AdapterCapabilityState), value.State)
            || value.Provenance == null)
            throw new ArgumentException("Crafting capability availability is missing.");
    }
}
