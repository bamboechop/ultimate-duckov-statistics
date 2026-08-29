using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class CraftingCapabilityIds
{
    public const string CompletionActions = "native-crafting-completion-actions";
    public const string ProducedQuantity = "native-crafting-produced-quantity";
    public const string OutputIdentity = "native-crafting-output-identity";
    public const string RecipeIdentity = "native-crafting-recipe-identity";
    public const string BatchMetadata = "native-crafting-batch-metadata";
    public const string MultipleOutputRecipes = "native-crafting-multiple-output-recipes";
    public const string WorkstationIdentity = "native-crafting-workstation-identity";
    public const string ContextAttribution = "native-crafting-context-attribution";
    public const string ItemResourceIdentity = "native-crafting-item-resource-identity";
    public const string OutputResourceAssociation = "native-crafting-output-resource-association";
    public const string CurrencyCharge = "native-crafting-currency-charge";
    public const string CurrencyMoneyCashSplit = "native-crafting-currency-money-cash-split";

    public static IReadOnlyList<string> All { get; } =
    [
        CompletionActions,
        ProducedQuantity,
        OutputIdentity,
        RecipeIdentity,
        BatchMetadata,
        MultipleOutputRecipes,
        WorkstationIdentity,
        ContextAttribution,
        ItemResourceIdentity,
        OutputResourceAssociation,
        CurrencyCharge,
        CurrencyMoneyCashSplit
    ];
}

public static class CraftingNativeContractPolicy
{
    public const string BootstrapProvenance = "Crafting capability has not been initialized.";
    public const string WorkstationUnavailableProvenance =
        "The native completion contract does not expose the crafting workstation identity.";
    public const string ContextUnavailableProvenance =
        "The native completion contract does not expose reliable run or map attribution.";
    public const string MultipleOutputUnavailableProvenance =
        "The installed crafting formula contract exposes exactly one declared result item.";
    public const string CurrencySplitUnavailableProvenance =
        "The successful crafting boundary proves the declared total currency charge but exposes no exact Money/Cash split.";

    public static CraftingMetricCapabilities Supported(
        string completionProvenance,
        string formulaProvenance,
        string? resourceProvenance = null,
        string? currencyProvenance = null) => new()
        {
            CompletionActions = Availability(AdapterCapabilityState.Supported, completionProvenance),
            ProducedQuantity = Availability(AdapterCapabilityState.Supported, formulaProvenance),
            OutputIdentity = Availability(AdapterCapabilityState.Supported, formulaProvenance),
            RecipeIdentity = Availability(AdapterCapabilityState.Supported, formulaProvenance),
            BatchMetadata = Availability(AdapterCapabilityState.Supported, formulaProvenance),
            MultipleOutputRecipes = Availability(AdapterCapabilityState.DisabledIncompatible, MultipleOutputUnavailableProvenance),
            WorkstationIdentity = Availability(AdapterCapabilityState.DisabledIncompatible, WorkstationUnavailableProvenance),
            ContextAttribution = Availability(AdapterCapabilityState.DisabledIncompatible, ContextUnavailableProvenance),
            ItemResourceIdentity = Availability(AdapterCapabilityState.Supported, resourceProvenance ?? formulaProvenance),
            OutputResourceAssociation = Availability(AdapterCapabilityState.Supported, resourceProvenance ?? formulaProvenance),
            CurrencyCharge = Availability(AdapterCapabilityState.Supported, currencyProvenance ?? formulaProvenance),
            CurrencyMoneyCashSplit = Availability(AdapterCapabilityState.DisabledIncompatible, CurrencySplitUnavailableProvenance)
        };

    public static CraftingMetricCapabilities OutputTotalsSupportedMetadataUnavailable(
        string completionProvenance,
        string metadataProvenance) => new()
        {
            CompletionActions = Availability(AdapterCapabilityState.Supported, completionProvenance),
            ProducedQuantity = Availability(AdapterCapabilityState.Supported, completionProvenance),
            OutputIdentity = Availability(AdapterCapabilityState.Supported, completionProvenance),
            RecipeIdentity = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance),
            BatchMetadata = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance),
            MultipleOutputRecipes = Availability(AdapterCapabilityState.DisabledIncompatible, MultipleOutputUnavailableProvenance),
            WorkstationIdentity = Availability(AdapterCapabilityState.DisabledIncompatible, WorkstationUnavailableProvenance),
            ContextAttribution = Availability(AdapterCapabilityState.DisabledIncompatible, ContextUnavailableProvenance),
            ItemResourceIdentity = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance),
            OutputResourceAssociation = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance),
            CurrencyCharge = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance),
            CurrencyMoneyCashSplit = Availability(AdapterCapabilityState.DisabledIncompatible, CurrencySplitUnavailableProvenance)
        };

    public static CraftingMetricCapabilities Unavailable(string provenance)
    {
        var value = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
        return new CraftingMetricCapabilities
        {
            CompletionActions = Clone(value),
            ProducedQuantity = Clone(value),
            OutputIdentity = Clone(value),
            RecipeIdentity = Clone(value),
            BatchMetadata = Clone(value),
            MultipleOutputRecipes = Clone(value),
            WorkstationIdentity = Clone(value),
            ContextAttribution = Clone(value),
            ItemResourceIdentity = Clone(value),
            OutputResourceAssociation = Clone(value),
            CurrencyCharge = Clone(value),
            CurrencyMoneyCashSplit = Clone(value)
        };
    }

    public static IReadOnlyList<CapabilityRecord> ToRecords(CraftingMetricCapabilities value, string version) =>
    [
        Record(CraftingCapabilityIds.CompletionActions, value.CompletionActions, version),
        Record(CraftingCapabilityIds.ProducedQuantity, value.ProducedQuantity, version),
        Record(CraftingCapabilityIds.OutputIdentity, value.OutputIdentity, version),
        Record(CraftingCapabilityIds.RecipeIdentity, value.RecipeIdentity, version),
        Record(CraftingCapabilityIds.BatchMetadata, value.BatchMetadata, version),
        Record(CraftingCapabilityIds.MultipleOutputRecipes, value.MultipleOutputRecipes, version),
        Record(CraftingCapabilityIds.WorkstationIdentity, value.WorkstationIdentity, version),
        Record(CraftingCapabilityIds.ContextAttribution, value.ContextAttribution, version),
        Record(CraftingCapabilityIds.ItemResourceIdentity, value.ItemResourceIdentity, version),
        Record(CraftingCapabilityIds.OutputResourceAssociation, value.OutputResourceAssociation, version),
        Record(CraftingCapabilityIds.CurrencyCharge, value.CurrencyCharge, version),
        Record(CraftingCapabilityIds.CurrencyMoneyCashSplit, value.CurrencyMoneyCashSplit, version)
    ];

    public static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    {
        State = state,
        Provenance = provenance ?? string.Empty
    };

    private static MetricAvailability Clone(MetricAvailability source) => new()
    {
        State = source.State,
        Provenance = source.Provenance
    };

    private static CapabilityRecord Record(string id, MetricAvailability availability, string version) => new()
    {
        AdapterId = id,
        State = availability.State,
        Version = version,
        Detail = availability.Provenance
    };
}
