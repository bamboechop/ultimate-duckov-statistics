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
    public const string ItemResourceIdentity = "native-crafting-item-resource-identity";
    public const string OutputResourceAssociation = "native-crafting-output-resource-association";
    public const string CurrencyCharge = "native-crafting-currency-charge";

    public static IReadOnlyList<string> All { get; } =
    [
        CompletionActions,
        ProducedQuantity,
        OutputIdentity,
        RecipeIdentity,
        BatchMetadata,
        ItemResourceIdentity,
        OutputResourceAssociation,
        CurrencyCharge    ];
}

public static class CraftingNativeContractPolicy
{
    public const string BootstrapProvenance = "Crafting capability has not been initialized.";

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
            ItemResourceIdentity = Availability(AdapterCapabilityState.Supported, resourceProvenance ?? formulaProvenance),
            OutputResourceAssociation = Availability(AdapterCapabilityState.Supported, resourceProvenance ?? formulaProvenance),
            CurrencyCharge = Availability(AdapterCapabilityState.Supported, currencyProvenance ?? formulaProvenance)
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
            ItemResourceIdentity = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance),
            OutputResourceAssociation = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance),
            CurrencyCharge = Availability(AdapterCapabilityState.DisabledIncompatible, metadataProvenance)
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
            ItemResourceIdentity = Clone(value),
            OutputResourceAssociation = Clone(value),
            CurrencyCharge = Clone(value)
        };
    }

    public static IReadOnlyList<CapabilityRecord> ToRecords(CraftingMetricCapabilities value, string version) =>
    [
        Record(CraftingCapabilityIds.CompletionActions, value.CompletionActions, version),
        Record(CraftingCapabilityIds.ProducedQuantity, value.ProducedQuantity, version),
        Record(CraftingCapabilityIds.OutputIdentity, value.OutputIdentity, version),
        Record(CraftingCapabilityIds.RecipeIdentity, value.RecipeIdentity, version),
        Record(CraftingCapabilityIds.BatchMetadata, value.BatchMetadata, version),
        Record(CraftingCapabilityIds.ItemResourceIdentity, value.ItemResourceIdentity, version),
        Record(CraftingCapabilityIds.OutputResourceAssociation, value.OutputResourceAssociation, version),
        Record(CraftingCapabilityIds.CurrencyCharge, value.CurrencyCharge, version)    ];

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
