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

    private static MetricAvailability Bootstrap() => new()
    {
        State = AdapterCapabilityState.DisabledIncompatible,
        Provenance = CraftingNativeContractPolicy.BootstrapProvenance
    };
}

[DataContract]
public sealed class CraftingRecipeAggregate
{
    [DataMember(Order = 1)] public string RecipeId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public long CompletionActions { get; set; }
    [DataMember(Order = 3)] public long ProducedQuantity { get; set; }
    [DataMember(Order = 4)] public Dictionary<string, long> BatchActions { get; set; } = new(StringComparer.Ordinal);
}

[DataContract]
public sealed class CraftedOutputAggregate
{
    [DataMember(Order = 1)] public string OutputItemId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public long CompletionActions { get; set; }
    [DataMember(Order = 4)] public long ProducedQuantity { get; set; }
    [DataMember(Order = 5)] public Dictionary<string, CraftingRecipeAggregate> Recipes { get; set; } = new(StringComparer.Ordinal);
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
        bool batchMetadataProven = true)
    {
        OutputItemId = outputItemId ?? string.Empty;
        OutputDisplayName = outputDisplayName ?? string.Empty;
        RecipeId = recipeId ?? string.Empty;
        CompletionActions = completionActions;
        ProducedQuantity = producedQuantity;
        BatchActions = batchActions ?? throw new ArgumentNullException(nameof(batchActions));
        RecipeIdentityProven = recipeIdentityProven;
        BatchMetadataProven = batchMetadataProven;
    }

    public string OutputItemId { get; }
    public string OutputDisplayName { get; }
    public string RecipeId { get; }
    public long CompletionActions { get; }
    public long ProducedQuantity { get; }
    public IReadOnlyDictionary<string, long> BatchActions { get; }
    public bool RecipeIdentityProven { get; }
    public bool BatchMetadataProven { get; }
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
        if (mutation.IsEmpty) return false;

        var actionRows = aggregate.CompletionArithmeticUnavailable
            ? Array.Empty<CraftingMutationRow>()
            : mutation.Rows.Where(row => row.CompletionActions != 0).ToArray();
        var quantityRows = aggregate.QuantityArithmeticUnavailable
            ? Array.Empty<CraftingMutationRow>()
            : mutation.Rows.Where(row => row.ProducedQuantity != 0).ToArray();

        var actionOverflow = actionRows.Length != 0 && WouldOverflowActions(aggregate, actionRows);
        var quantityOverflow = quantityRows.Length != 0 && WouldOverflowQuantity(aggregate, quantityRows);
        var changed = false;
        if (actionOverflow)
        {
            aggregate.CompletionArithmeticUnavailable = true;
            aggregate.Capabilities.CompletionActions = Unavailable(ArithmeticProvenance);
            aggregate.Capabilities.BatchMetadata = Unavailable(ArithmeticProvenance);
            changed = true;
        }
        else
        {
            foreach (var row in actionRows)
            {
                ApplyActions(aggregate, row);
                changed = true;
            }
        }

        if (quantityOverflow)
        {
            aggregate.QuantityArithmeticUnavailable = true;
            aggregate.Capabilities.ProducedQuantity = Unavailable(ArithmeticProvenance);
            changed = true;
        }
        else
        {
            foreach (var row in quantityRows)
            {
                ApplyQuantity(aggregate, row);
                changed = true;
            }
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
        if (aggregate.Outputs == null)
        {
            aggregate.Outputs = new Dictionary<string, CraftedOutputAggregate>(StringComparer.Ordinal);
            aggregate.WasRepairedFromInvalidState = true;
            changed = true;
        }
        aggregate.HistoricalProvenance ??= string.Empty;
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
                recipeEntry.Value.RecipeId ??= recipeEntry.Key;
                recipeEntry.Value.BatchActions ??= new Dictionary<string, long>(StringComparer.Ordinal);
            }
        }
        return changed;
    }

    public static void Validate(CraftingStatisticsAggregate aggregate)
    {
        if (aggregate == null || aggregate.Capabilities == null || aggregate.Outputs == null)
            throw new ArgumentException("Crafting roots are missing.", nameof(aggregate));
        ValidateAvailability(aggregate.Capabilities.CompletionActions);
        ValidateAvailability(aggregate.Capabilities.ProducedQuantity);
        ValidateAvailability(aggregate.Capabilities.OutputIdentity);
        ValidateAvailability(aggregate.Capabilities.RecipeIdentity);
        ValidateAvailability(aggregate.Capabilities.BatchMetadata);
        ValidateAvailability(aggregate.Capabilities.WorkstationIdentity);
        ValidateAvailability(aggregate.Capabilities.ContextAttribution);
        ValidateAvailability(aggregate.Capabilities.MultipleOutputRecipes);
        if (aggregate.CompletionActions < 0 || aggregate.ProducedQuantity < 0)
            throw new ArgumentException("Crafting totals cannot be negative.", nameof(aggregate));

        long outputActions = 0;
        long outputQuantity = 0;
        foreach (var entry in aggregate.Outputs)
        {
            var output = entry.Value ?? throw new ArgumentException("Crafted output is missing.", nameof(aggregate));
            if (string.IsNullOrWhiteSpace(entry.Key) || !string.Equals(entry.Key, output.OutputItemId, StringComparison.Ordinal))
                throw new ArgumentException("Crafted output identity is invalid.", nameof(aggregate));
            if (output.CompletionActions < 0 || output.ProducedQuantity < 0 || output.Recipes == null)
                throw new ArgumentException("Crafted output totals are invalid.", nameof(aggregate));
            outputActions = checked(outputActions + output.CompletionActions);
            outputQuantity = checked(outputQuantity + output.ProducedQuantity);
            long recipeActions = 0;
            long recipeQuantity = 0;
            foreach (var recipeEntry in output.Recipes)
            {
                var recipe = recipeEntry.Value ?? throw new ArgumentException("Crafting recipe is missing.", nameof(aggregate));
                if (string.IsNullOrWhiteSpace(recipeEntry.Key) || !string.Equals(recipeEntry.Key, recipe.RecipeId, StringComparison.Ordinal)
                    || recipe.CompletionActions < 0 || recipe.ProducedQuantity < 0 || recipe.BatchActions == null)
                    throw new ArgumentException("Crafting recipe totals are invalid.", nameof(aggregate));
                recipeActions = checked(recipeActions + recipe.CompletionActions);
                recipeQuantity = checked(recipeQuantity + recipe.ProducedQuantity);
                long batchActions = 0;
                long batchQuantity = 0;
                var quantityCompositionRequired = !aggregate.QuantityArithmeticUnavailable
                                                  && aggregate.Capabilities.ProducedQuantity.State
                                                  != AdapterCapabilityState.DisabledIncompatible;
                foreach (var batch in recipe.BatchActions)
                {
                    if (!long.TryParse(batch.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
                        || quantity <= 0 || batch.Value < 0)
                        throw new ArgumentException("Crafting batch metadata is invalid.", nameof(aggregate));
                    batchActions = checked(batchActions + batch.Value);
                    if (quantityCompositionRequired)
                        batchQuantity = checked(batchQuantity + checked(quantity * batch.Value));
                }
                if (aggregate.Capabilities.BatchMetadata.State == AdapterCapabilityState.DisabledIncompatible)
                {
                    if (batchActions > recipe.CompletionActions
                        || (quantityCompositionRequired && batchQuantity > recipe.ProducedQuantity))
                        throw new ArgumentException("Crafting batch composition exceeds its recipe totals.", nameof(aggregate));
                }
                else if (batchActions != recipe.CompletionActions
                         || (quantityCompositionRequired && batchQuantity != recipe.ProducedQuantity))
                {
                    throw new ArgumentException("Crafting batch composition is inconsistent.", nameof(aggregate));
                }
            }
            if (aggregate.Capabilities.RecipeIdentity.State == AdapterCapabilityState.DisabledIncompatible)
            {
                if (recipeActions > output.CompletionActions || recipeQuantity > output.ProducedQuantity)
                    throw new ArgumentException("Crafting recipe composition exceeds its output total.", nameof(aggregate));
            }
            else if (recipeActions != output.CompletionActions || recipeQuantity != output.ProducedQuantity)
            {
                throw new ArgumentException("Crafting recipe composition is inconsistent.", nameof(aggregate));
            }
        }
        if (outputActions != aggregate.CompletionActions || outputQuantity != aggregate.ProducedQuantity)
            throw new ArgumentException("Crafting output composition is inconsistent.", nameof(aggregate));
    }

    public static CraftingStatisticsAggregate Clone(CraftingStatisticsAggregate? source)
    {
        source ??= new CraftingStatisticsAggregate();
        NormalizePersisted(source);
        return new CraftingStatisticsAggregate
        {
            CompletionActions = source.CompletionActions,
            ProducedQuantity = source.ProducedQuantity,
            Outputs = source.Outputs.ToDictionary(
                entry => entry.Key,
                entry => CloneOutput(entry.Value),
                StringComparer.Ordinal),
            Capabilities = CloneCapabilities(source.Capabilities),
            HistoricalUnavailable = source.HistoricalUnavailable,
            HistoricalProvenance = source.HistoricalProvenance,
            CompletionArithmeticUnavailable = source.CompletionArithmeticUnavailable,
            QuantityArithmeticUnavailable = source.QuantityArithmeticUnavailable,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
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
        MultipleOutputRecipes = Clone(source.MultipleOutputRecipes)
    };

    public static void InitializeOrRestrictCapabilities(
        CraftingStatisticsAggregate aggregate,
        CraftingMetricCapabilities current)
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
        if (aggregate.QuantityArithmeticUnavailable)
            aggregate.Capabilities.ProducedQuantity = Unavailable(ArithmeticProvenance);
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
            MultipleOutputRecipes = Restrict(recorded.MultipleOutputRecipes, current.MultipleOutputRecipes, initializeBootstrap)
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
        if (!row.RecipeIdentityProven) return;
        GetRecipe(output, row).ProducedQuantity += row.ProducedQuantity;
    }

    private static bool WouldOverflowActions(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows)
    {
        long totalDelta = 0;
        foreach (var row in rows)
        {
            if (WouldAddOverflow(totalDelta, row.CompletionActions)) return true;
            totalDelta += row.CompletionActions;
        }
        if (WouldAddOverflow(aggregate.CompletionActions, totalDelta)) return true;
        var outputDeltas = new Dictionary<string, long>(StringComparer.Ordinal);
        var recipeDeltas = new Dictionary<(string Output, string Recipe), long>();
        var batchDeltas = new Dictionary<(string Output, string Recipe, string Batch), long>();
        foreach (var row in rows)
        {
            var output = aggregate.Outputs.TryGetValue(row.OutputItemId, out var existing) ? existing : null;
            outputDeltas.TryGetValue(row.OutputItemId, out var priorOutputDelta);
            if (WouldAddOverflow(priorOutputDelta, row.CompletionActions)) return true;
            var outputDelta = priorOutputDelta + row.CompletionActions;
            if (WouldAddOverflow(output?.CompletionActions ?? 0, outputDelta)) return true;
            outputDeltas[row.OutputItemId] = outputDelta;
            if (!row.RecipeIdentityProven) continue;
            var recipeKey = (row.OutputItemId, row.RecipeId);
            recipeDeltas.TryGetValue(recipeKey, out var priorRecipeDelta);
            var recipe = output != null && output.Recipes.TryGetValue(row.RecipeId, out var existingRecipe) ? existingRecipe : null;
            if (WouldAddOverflow(priorRecipeDelta, row.CompletionActions)) return true;
            var recipeDelta = priorRecipeDelta + row.CompletionActions;
            if (WouldAddOverflow(recipe?.CompletionActions ?? 0, recipeDelta)) return true;
            recipeDeltas[recipeKey] = recipeDelta;
            if (!row.BatchMetadataProven) continue;
            foreach (var batch in row.BatchActions)
            {
                var key = (row.OutputItemId, row.RecipeId, batch.Key);
                batchDeltas.TryGetValue(key, out var priorBatchDelta);
                if (WouldAddOverflow(priorBatchDelta, batch.Value)) return true;
                var batchDelta = priorBatchDelta + batch.Value;
                var currentBatch = 0L;
                if (recipe != null) recipe.BatchActions.TryGetValue(batch.Key, out currentBatch);
                if (WouldAddOverflow(currentBatch, batchDelta)) return true;
                batchDeltas[key] = batchDelta;
            }
        }
        return false;
    }

    private static bool WouldOverflowQuantity(CraftingStatisticsAggregate aggregate, IReadOnlyList<CraftingMutationRow> rows)
    {
        long totalDelta = 0;
        foreach (var row in rows)
        {
            if (WouldAddOverflow(totalDelta, row.ProducedQuantity)) return true;
            totalDelta += row.ProducedQuantity;
        }
        if (WouldAddOverflow(aggregate.ProducedQuantity, totalDelta)) return true;
        var outputDeltas = new Dictionary<string, long>(StringComparer.Ordinal);
        var recipeDeltas = new Dictionary<(string Output, string Recipe), long>();
        foreach (var row in rows)
        {
            var output = aggregate.Outputs.TryGetValue(row.OutputItemId, out var existing) ? existing : null;
            outputDeltas.TryGetValue(row.OutputItemId, out var priorOutputDelta);
            if (WouldAddOverflow(priorOutputDelta, row.ProducedQuantity)) return true;
            var outputDelta = priorOutputDelta + row.ProducedQuantity;
            if (WouldAddOverflow(output?.ProducedQuantity ?? 0, outputDelta)) return true;
            outputDeltas[row.OutputItemId] = outputDelta;
            if (!row.RecipeIdentityProven) continue;
            var recipeKey = (row.OutputItemId, row.RecipeId);
            recipeDeltas.TryGetValue(recipeKey, out var priorRecipeDelta);
            var recipe = output != null && output.Recipes.TryGetValue(row.RecipeId, out var existingRecipe) ? existingRecipe : null;
            if (WouldAddOverflow(priorRecipeDelta, row.ProducedQuantity)) return true;
            var recipeDelta = priorRecipeDelta + row.ProducedQuantity;
            if (WouldAddOverflow(recipe?.ProducedQuantity ?? 0, recipeDelta)) return true;
            recipeDeltas[recipeKey] = recipeDelta;
        }
        return false;
    }

    private static CraftedOutputAggregate GetOutput(CraftingStatisticsAggregate aggregate, CraftingMutationRow row)
    {
        if (!aggregate.Outputs.TryGetValue(row.OutputItemId, out var output))
        {
            output = new CraftedOutputAggregate
            {
                OutputItemId = row.OutputItemId,
                DisplayName = string.IsNullOrWhiteSpace(row.OutputDisplayName)
                    ? "Unknown item " + row.OutputItemId
                    : row.OutputDisplayName
            };
            aggregate.Outputs.Add(row.OutputItemId, output);
        }
        else if (!string.IsNullOrWhiteSpace(row.OutputDisplayName))
        {
            output.DisplayName = row.OutputDisplayName;
        }
        else if (string.IsNullOrWhiteSpace(output.DisplayName))
        {
            output.DisplayName = "Unknown item " + row.OutputItemId;
        }
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

    private static void ValidateMutation(CraftingMutation mutation)
    {
        foreach (var row in mutation.Rows)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.OutputItemId) || string.IsNullOrWhiteSpace(row.RecipeId)
                || row.CompletionActions < 0 || row.ProducedQuantity < 0)
                throw new ArgumentOutOfRangeException(nameof(mutation), "Crafting mutation rows must have identities and non-negative totals.");
            if (row.BatchMetadataProven && !row.RecipeIdentityProven)
                throw new ArgumentException("Crafting batch metadata requires proven recipe identity.", nameof(mutation));
            if (!row.BatchMetadataProven && row.BatchActions.Count != 0)
                throw new ArgumentException("Unproven crafting batch metadata cannot be retained.", nameof(mutation));
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
                && (row.BatchActions.Count == 0
                    || batches != row.CompletionActions
                    || batchQuantity != row.ProducedQuantity))
                throw new ArgumentException("Crafting batch composition is inconsistent.", nameof(mutation));
        }
    }

    private static CraftedOutputAggregate CloneOutput(CraftedOutputAggregate source) => new()
    {
        OutputItemId = source.OutputItemId,
        DisplayName = source.DisplayName,
        CompletionActions = source.CompletionActions,
        ProducedQuantity = source.ProducedQuantity,
        Recipes = source.Recipes.ToDictionary(
            entry => entry.Key,
            entry => new CraftingRecipeAggregate
            {
                RecipeId = entry.Value.RecipeId,
                CompletionActions = entry.Value.CompletionActions,
                ProducedQuantity = entry.Value.ProducedQuantity,
                BatchActions = new Dictionary<string, long>(entry.Value.BatchActions, StringComparer.Ordinal)
            },
            StringComparer.Ordinal)
    };

    private static bool WouldAddOverflow(long current, long delta) => delta > long.MaxValue - current;

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
