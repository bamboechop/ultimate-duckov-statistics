using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeCraftingAdapterCompositionTests : IDisposable
{
    private readonly string originalPersistentDataPath = Application.persistentDataPath;

    public NativeCraftingAdapterCompositionTests() => ResetNative();

    public void Dispose()
    {
        ResetNative();
        Application.persistentDataPath = originalPersistentDataPath;
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "ProductionComposition")]
    public void InsufficientCombinedStockDuplicateCostDoesNotPublishCanonicalDeclaredQuantity()
    {
        using var result = CompleteDuplicateCostCraft(4);

        var crafting = result.Coordinator.Current!.Statistics.Crafting;
        Assert.Equal(1, crafting.CompletionActions);
        Assert.Equal(1, crafting.ProducedQuantity);
        Assert.Empty(crafting.Resources);
        Assert.True(crafting.ResourceHistoryUnavailable);
        Assert.Equal(1, crafting.CurrencyChargeActions);
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            result.Coordinator.CurrentCraftingCapabilities.ItemResourceIdentity.State);
        var export = StatisticsExporter.Create(result.Coordinator.Current, DateTime.UtcNow);
        Assert.DoesNotContain("9001", export.CraftingResourcesCsv, StringComparison.Ordinal);
        Assert.DoesNotContain("9001", export.CraftingResourceAssociationsCsv, StringComparison.Ordinal);
        using (var json = JsonDocument.Parse(export.Json))
            Assert.False(json.RootElement.GetProperty("Crafting").GetProperty("Resources").TryGetProperty("9001", out _));
        Assert.Equal(2, ItemUtilities.ScanCount);
        Assert.Contains(result.Diagnostics, detail =>
            detail.Contains("entries totaling 6", StringComparison.Ordinal)
            && detail.Contains("proved only 4", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "ProductionComposition")]
    public void SufficientSingleStackDuplicateCostPublishesOneCanonicalSixUnitConsumption()
    {
        using var result = CompleteDuplicateCostCraft(6);

        var crafting = result.Coordinator.Current!.Statistics.Crafting;
        Assert.Equal(1, crafting.CompletionActions);
        Assert.Equal(1, crafting.ProducedQuantity);
        Assert.False(crafting.ResourceHistoryUnavailable);
        Assert.Equal(6, crafting.Resources["9001"].ConsumedQuantity);
        var recipe = crafting.Outputs["7001"].Recipes["modded-duplicate"];
        Assert.Equal(1, recipe.Resources["9001"].ConsumptionActions);
        Assert.Equal(6, recipe.Resources["9001"].ConsumedQuantity);
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Equal(
            AdapterCapabilityState.Supported,
            result.Coordinator.CurrentCraftingCapabilities.ItemResourceIdentity.State);
        var export = StatisticsExporter.Create(result.Coordinator.Current, DateTime.UtcNow);
        Assert.Contains("9001,Item 9001,6", export.CraftingResourcesCsv, StringComparison.Ordinal);
        Assert.Contains("7001,Item 7001,modded-duplicate,9001,Item 9001,1,6", export.CraftingResourceAssociationsCsv, StringComparison.Ordinal);
        Assert.Empty(ItemUtilities.OwnedItems);
        Assert.Equal(2, ItemUtilities.ScanCount);
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "ProductionComposition")]
    public void DistinctFullStacksRepeatedCostDoesNotPublishDoubleCountedDestruction()
    {
        using var result = CompleteDuplicateCostCraft(3, 3);

        var crafting = result.Coordinator.Current!.Statistics.Crafting;
        Assert.Equal(1, crafting.CompletionActions);
        Assert.Equal(1, crafting.ProducedQuantity);
        Assert.Empty(crafting.Resources);
        Assert.True(crafting.ResourceHistoryUnavailable);
        Assert.Equal(1, crafting.CurrencyChargeActions);
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            result.Coordinator.CurrentCraftingCapabilities.ItemResourceIdentity.State);
        Assert.Single(ItemUtilities.OwnedItems);
        Assert.Equal(3, ItemUtilities.OwnedItems[0].StackCount);
        Assert.Equal(2, ItemUtilities.ScanCount);
        var export = StatisticsExporter.Create(result.Coordinator.Current, DateTime.UtcNow);
        Assert.DoesNotContain("9001", export.CraftingResourcesCsv, StringComparison.Ordinal);
        Assert.DoesNotContain("9001", export.CraftingResourceAssociationsCsv, StringComparison.Ordinal);
        using (var json = JsonDocument.Parse(export.Json))
            Assert.False(json.RootElement.GetProperty("Crafting").GetProperty("Resources").TryGetProperty("9001", out _));
        Assert.Contains(result.Diagnostics, detail =>
            detail.Contains("entries totaling 6", StringComparison.Ordinal)
            && detail.Contains("proved 3 actually removed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "ProductionComposition")]
    public void DetachedSameIdChildTransferDoesNotCompleteRepeatedCostProof()
    {
        using var result = CompleteDuplicateCostCraftWithDetachedSameIdChild();

        var crafting = result.Coordinator.Current!.Statistics.Crafting;
        Assert.Equal(1, crafting.CompletionActions);
        Assert.Equal(1, crafting.ProducedQuantity);
        Assert.Empty(crafting.Resources);
        Assert.True(crafting.ResourceHistoryUnavailable);
        Assert.Equal(1, crafting.CurrencyChargeActions);
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            result.Coordinator.CurrentCraftingCapabilities.ItemResourceIdentity.State);
        Assert.Equal([3, 6], ItemUtilities.OwnedItems.Select(item => item.StackCount).OrderBy(count => count).ToArray());
        Assert.Equal(2, ItemUtilities.ScanCount);
        var export = StatisticsExporter.Create(result.Coordinator.Current, DateTime.UtcNow);
        Assert.DoesNotContain("9001", export.CraftingResourcesCsv, StringComparison.Ordinal);
        Assert.DoesNotContain("9001", export.CraftingResourceAssociationsCsv, StringComparison.Ordinal);
        using (var json = JsonDocument.Parse(export.Json))
            Assert.False(json.RootElement.GetProperty("Crafting").GetProperty("Resources").TryGetProperty("9001", out _));
        Assert.Contains(result.Diagnostics, detail =>
            detail.Contains("entries totaling 6", StringComparison.Ordinal)
            && detail.Contains("net ownership-ending stack mutations proved 3 actually removed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void CorruptExactResourceActionPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CompleteDuplicateCostCraft(6);
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = Path.Combine(
            result.Coordinator.DataRoot,
            "profiles",
            "slot-01",
            "current",
            "profile.json");
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        result.StopRuntime();

        Assert.True(File.Exists(profilePath));
        Assert.True(File.Exists(backupPath));
        Assert.Equal(1, ReadResourceActions(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        primary["Statistics"]!["Crafting"]!["Outputs"]!["7001"]!["Recipes"]!["modded-duplicate"]!
            ["Resources"]!["9001"]!["ConsumptionActions"] = 0;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.Equal(0, ReadResourceActions(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var crafting = reopened.Current!.Statistics.Crafting;
        Assert.Equal(1, crafting.CompletionActions);
        Assert.Equal(1, crafting.CurrencyChargeActions);
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Equal(6, crafting.Resources["9001"].ConsumedQuantity);
        Assert.Equal(
            1,
            crafting.Outputs["7001"].Recipes["modded-duplicate"].Resources["9001"].ConsumptionActions);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.Equal(1, ReadResourceActions(profilePath));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void DegradedResourceFanOutSubsetPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CompleteDuplicateCostCraft(6);
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = CurrentProfilePath(result);
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        DegradeCapabilities(result, capabilities =>
        {
            capabilities.ItemResourceIdentity = CraftingNativeContractPolicy.Availability(
                AdapterCapabilityState.DisabledIncompatible,
                "resource evidence degraded");
            capabilities.OutputResourceAssociation = CraftingNativeContractPolicy.Availability(
                AdapterCapabilityState.DisabledIncompatible,
                "resource evidence degraded");
        });
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            result.Coordinator.Current!.Statistics.Crafting.Capabilities.ItemResourceIdentity.State);
        result.StopRuntime();

        Assert.Equal(6, ReadResourceQuantity(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        primary["Statistics"]!["Crafting"]!["Outputs"]!["7001"]!["Recipes"]!["modded-duplicate"]!
            ["Resources"]!["9001"]!["ConsumedQuantity"] = 5;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.Equal(5, ReadResourceAssociationQuantity(profilePath));
        Assert.Equal(6, ReadResourceQuantity(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var crafting = reopened.Current!.Statistics.Crafting;
        Assert.Equal(6, crafting.Resources["9001"].ConsumedQuantity);
        Assert.Equal(
            6,
            crafting.Outputs["7001"].Recipes["modded-duplicate"].Resources["9001"].ConsumedQuantity);
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.Equal(6, ReadResourceAssociationQuantity(profilePath));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void SaturatedZeroLifetimeResourcePrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CompleteDuplicateCostCraft(6);
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = CurrentProfilePath(result);
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        result.StopRuntime();

        Assert.Equal(6, ReadResourceQuantity(backupPath));
        Assert.Equal(6, ReadResourceAssociationQuantity(backupPath));
        Assert.Equal(1, ReadResourceActions(backupPath));
        Assert.Equal(150, ReadLifetimeCurrency(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        var crafting = primary["Statistics"]!["Crafting"]!;
        crafting["ResourceQuantityArithmeticUnavailable"] = true;
        crafting["Resources"]!["9001"]!["ConsumedQuantity"] = 0;
        crafting["Outputs"]!["7001"]!["Recipes"]!["modded-duplicate"]!["Resources"]!["9001"]!
            ["ConsumedQuantity"] = 0;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.True(ReadCraftingBoolean(profilePath, "ResourceQuantityArithmeticUnavailable"));
        Assert.Equal(0, ReadResourceQuantity(profilePath));
        Assert.Equal(0, ReadResourceAssociationQuantity(profilePath));
        Assert.Equal(1, ReadResourceActions(profilePath));
        Assert.Equal(150, ReadLifetimeCurrency(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var recovered = reopened.Current!.Statistics.Crafting;
        Assert.Equal(6, recovered.Resources["9001"].ConsumedQuantity);
        var association = recovered.Outputs["7001"].Recipes["modded-duplicate"].Resources["9001"];
        Assert.Equal(6, association.ConsumedQuantity);
        Assert.Equal(1, association.ConsumptionActions);
        Assert.Equal(150, recovered.CurrencyCharged);
        Assert.False(recovered.ResourceQuantityArithmeticUnavailable);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.Equal(6, ReadResourceQuantity(profilePath));
        Assert.Equal(6, ReadResourceAssociationQuantity(profilePath));
        Assert.Equal(1, ReadResourceActions(profilePath));
        Assert.Equal(150, ReadLifetimeCurrency(profilePath));
        Assert.False(ReadCraftingBoolean(profilePath, "ResourceQuantityArithmeticUnavailable"));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void ExactResourceActionsExceedingQuantityPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CreateComposition();
        for (var action = 0; action < 2; action++)
        {
            ItemUtilities.OwnedItems.Add(Stack(9001, 6, durability: 1));
            CompleteCraft(result, DuplicateCostFormula());
        }
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = CurrentProfilePath(result);
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        result.StopRuntime();

        Assert.Equal(12, ReadResourceQuantity(backupPath));
        Assert.Equal(12, ReadResourceAssociationQuantity(backupPath));
        Assert.Equal(2, ReadResourceActions(backupPath));
        Assert.Equal(300, ReadLifetimeCurrency(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        var crafting = primary["Statistics"]!["Crafting"]!;
        crafting["Resources"]!["9001"]!["ConsumedQuantity"] = 1;
        crafting["Outputs"]!["7001"]!["Recipes"]!["modded-duplicate"]!["Resources"]!["9001"]!
            ["ConsumedQuantity"] = 1;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.False(ReadCraftingBoolean(profilePath, "ResourceQuantityArithmeticUnavailable"));
        Assert.Equal(1, ReadResourceQuantity(profilePath));
        Assert.Equal(1, ReadResourceAssociationQuantity(profilePath));
        Assert.Equal(2, ReadResourceActions(profilePath));
        Assert.Equal(300, ReadLifetimeCurrency(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var recovered = reopened.Current!.Statistics.Crafting;
        Assert.Equal(12, recovered.Resources["9001"].ConsumedQuantity);
        var association = recovered.Outputs["7001"].Recipes["modded-duplicate"].Resources["9001"];
        Assert.Equal(12, association.ConsumedQuantity);
        Assert.Equal(2, association.ConsumptionActions);
        Assert.Equal(300, recovered.CurrencyCharged);
        Assert.False(recovered.ResourceQuantityArithmeticUnavailable);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.Equal(12, ReadResourceQuantity(profilePath));
        Assert.Equal(12, ReadResourceAssociationQuantity(profilePath));
        Assert.Equal(2, ReadResourceActions(profilePath));
        Assert.Equal(300, ReadLifetimeCurrency(profilePath));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void DegradedCurrencyFanOutSubsetPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CompleteDuplicateCostCraft(6);
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = CurrentProfilePath(result);
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        DegradeCapabilities(result, capabilities =>
        {
            capabilities.CurrencyCharge = CraftingNativeContractPolicy.Availability(
                AdapterCapabilityState.DisabledIncompatible,
                "currency evidence degraded");
        });
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            result.Coordinator.Current!.Statistics.Crafting.Capabilities.CurrencyCharge.State);
        result.StopRuntime();

        Assert.Equal(150, ReadRecipeCurrency(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        primary["Statistics"]!["Crafting"]!["Outputs"]!["7001"]!["Recipes"]!["modded-duplicate"]!
            ["CurrencyCharged"] = 149;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.Equal(149, ReadRecipeCurrency(profilePath));
        Assert.Equal(150, ReadOutputCurrency(profilePath));
        Assert.Equal(150, ReadLifetimeCurrency(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var crafting = reopened.Current!.Statistics.Crafting;
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Equal(150, crafting.Outputs["7001"].CurrencyCharged);
        Assert.Equal(150, crafting.Outputs["7001"].Recipes["modded-duplicate"].CurrencyCharged);
        Assert.Equal(6, crafting.Resources["9001"].ConsumedQuantity);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.Equal(150, ReadRecipeCurrency(profilePath));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void ExactCurrencyActionsExceedingAmountPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CreateComposition();
        for (var action = 0; action < 2; action++)
        {
            ItemUtilities.OwnedItems.Add(Stack(9001, 6, durability: 1));
            CompleteCraft(result, DuplicateCostFormula());
        }
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = CurrentProfilePath(result);
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        result.StopRuntime();

        Assert.Equal(2, ReadLifetimeCurrencyActions(backupPath));
        Assert.Equal(300, ReadLifetimeCurrency(backupPath));
        Assert.Equal(300, ReadOutputCurrency(backupPath));
        Assert.Equal(300, ReadRecipeCurrency(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        var crafting = primary["Statistics"]!["Crafting"]!;
        var output = crafting["Outputs"]!["7001"]!;
        var recipe = output["Recipes"]!["modded-duplicate"]!;
        crafting["CurrencyCharged"] = 1;
        output["CurrencyCharged"] = 1;
        recipe["CurrencyCharged"] = 1;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.False(ReadCraftingBoolean(profilePath, "CurrencyAmountArithmeticUnavailable"));
        Assert.Equal(2, ReadLifetimeCurrencyActions(profilePath));
        Assert.Equal(1, ReadLifetimeCurrency(profilePath));
        Assert.Equal(1, ReadOutputCurrency(profilePath));
        Assert.Equal(1, ReadRecipeCurrency(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var recovered = reopened.Current!.Statistics.Crafting;
        Assert.Equal(2, recovered.CurrencyChargeActions);
        Assert.Equal(300, recovered.CurrencyCharged);
        Assert.Equal(2, recovered.Outputs["7001"].CurrencyChargeActions);
        Assert.Equal(300, recovered.Outputs["7001"].CurrencyCharged);
        Assert.Equal(2, recovered.Outputs["7001"].Recipes["modded-duplicate"].CurrencyChargeActions);
        Assert.Equal(300, recovered.Outputs["7001"].Recipes["modded-duplicate"].CurrencyCharged);
        Assert.Equal(12, recovered.Resources["9001"].ConsumedQuantity);
        Assert.False(recovered.CurrencyAmountArithmeticUnavailable);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.Equal(2, ReadLifetimeCurrencyActions(profilePath));
        Assert.Equal(300, ReadLifetimeCurrency(profilePath));
        Assert.Equal(300, ReadOutputCurrency(profilePath));
        Assert.Equal(300, ReadRecipeCurrency(profilePath));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void InverseCurrencyActionArithmeticPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CompleteDuplicateCostCraft(6);
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = CurrentProfilePath(result);
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        result.StopRuntime();

        Assert.Equal(1, ReadLifetimeCurrencyActions(backupPath));
        Assert.Equal(150, ReadLifetimeCurrency(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        var crafting = primary["Statistics"]!["Crafting"]!;
        var output = crafting["Outputs"]!["7001"]!;
        var recipe = output["Recipes"]!["modded-duplicate"]!;
        crafting["CurrencyActionArithmeticUnavailable"] = true;
        crafting["CurrencyCharged"] = 0;
        output["CurrencyCharged"] = 0;
        recipe["CurrencyCharged"] = 0;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.True(ReadCraftingBoolean(profilePath, "CurrencyActionArithmeticUnavailable"));
        Assert.Equal(1, ReadLifetimeCurrencyActions(profilePath));
        Assert.Equal(0, ReadLifetimeCurrency(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var recovered = reopened.Current!.Statistics.Crafting;
        Assert.Equal(1, recovered.CurrencyChargeActions);
        Assert.Equal(150, recovered.CurrencyCharged);
        Assert.Equal(1, recovered.Outputs["7001"].CurrencyChargeActions);
        Assert.Equal(150, recovered.Outputs["7001"].CurrencyCharged);
        Assert.Equal(1, recovered.Outputs["7001"].Recipes["modded-duplicate"].CurrencyChargeActions);
        Assert.Equal(150, recovered.Outputs["7001"].Recipes["modded-duplicate"].CurrencyCharged);
        Assert.False(recovered.CurrencyActionArithmeticUnavailable);
        Assert.Equal(6, recovered.Resources["9001"].ConsumedQuantity);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.False(ReadCraftingBoolean(profilePath, "CurrencyActionArithmeticUnavailable"));
        Assert.Equal(150, ReadLifetimeCurrency(profilePath));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void InverseCurrencyAmountArithmeticPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CompleteDuplicateCostCraft(6);
        var generationId = result.Coordinator.CurrentGenerationId;
        var profilePath = CurrentProfilePath(result);
        var backupPath = AtomicJsonPaths.GetBackupPath(profilePath);
        result.StopRuntime();

        Assert.Equal(1, ReadLifetimeCurrencyActions(backupPath));
        Assert.Equal(150, ReadLifetimeCurrency(backupPath));
        var backupBeforeCorruption = File.ReadAllText(backupPath);
        var primary = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
        var crafting = primary["Statistics"]!["Crafting"]!;
        var output = crafting["Outputs"]!["7001"]!;
        var recipe = output["Recipes"]!["modded-duplicate"]!;
        crafting["CurrencyAmountArithmeticUnavailable"] = true;
        crafting["CurrencyChargeActions"] = 0;
        output["CurrencyChargeActions"] = 0;
        recipe["CurrencyChargeActions"] = 0;
        File.WriteAllText(profilePath, primary.ToJsonString());
        Assert.True(ReadCraftingBoolean(profilePath, "CurrencyAmountArithmeticUnavailable"));
        Assert.Equal(0, ReadLifetimeCurrencyActions(profilePath));
        Assert.Equal(150, ReadLifetimeCurrency(profilePath));
        Assert.Equal(backupBeforeCorruption, File.ReadAllText(backupPath));

        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();

        Assert.Equal(generationId, reopened.CurrentGenerationId);
        var recovered = reopened.Current!.Statistics.Crafting;
        Assert.Equal(1, recovered.CurrencyChargeActions);
        Assert.Equal(150, recovered.CurrencyCharged);
        Assert.Equal(1, recovered.Outputs["7001"].CurrencyChargeActions);
        Assert.Equal(150, recovered.Outputs["7001"].CurrencyCharged);
        Assert.Equal(1, recovered.Outputs["7001"].Recipes["modded-duplicate"].CurrencyChargeActions);
        Assert.Equal(150, recovered.Outputs["7001"].Recipes["modded-duplicate"].CurrencyCharged);
        Assert.False(recovered.CurrencyAmountArithmeticUnavailable);
        Assert.Equal(6, recovered.Resources["9001"].ConsumedQuantity);
        Assert.Contains(reopened.DiagnosticEntries, entry =>
            entry.Message.Contains("recovered=True", StringComparison.Ordinal));
        Assert.False(ReadCraftingBoolean(profilePath, "CurrencyAmountArithmeticUnavailable"));
        Assert.Equal(1, ReadLifetimeCurrencyActions(profilePath));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "ProductionComposition")]
    public void DefaultFreeCostRetainsResourceCapabilityAndLaterItemCostRemainsExact()
    {
        using var result = CreateComposition();
        CompleteCraft(
            result,
            new CraftingFormula
            {
                id = "modded-default-free",
                result = new CraftingFormula.ItemEntry { id = 8001, amount = 1 },
                cost = default
            });

        var afterFree = result.Coordinator.Current!.Statistics.Crafting;
        Assert.Equal(1, afterFree.CompletionActions);
        Assert.Equal(0, afterFree.CurrencyChargeActions);
        Assert.Equal(0, afterFree.CurrencyCharged);
        Assert.Empty(afterFree.Resources);
        Assert.Empty(afterFree.Outputs["8001"].Recipes["modded-default-free"].Resources);
        Assert.False(afterFree.ResourceHistoryUnavailable);
        Assert.Equal(
            AdapterCapabilityState.Supported,
            result.Coordinator.CurrentCraftingCapabilities.ItemResourceIdentity.State);
        Assert.Equal(
            AdapterCapabilityState.Supported,
            result.Coordinator.CurrentCraftingCapabilities.OutputResourceAssociation.State);

        ItemUtilities.OwnedItems.Add(Stack(9002, 2, durability: 1));
        CompleteCraft(
            result,
            new CraftingFormula
            {
                id = "modded-item-cost",
                result = new CraftingFormula.ItemEntry { id = 8002, amount = 1 },
                cost = new Cost
                {
                    items = [new Cost.ItemEntry { id = 9002, amount = 2 }]
                }
            });

        var afterItemCost = result.Coordinator.Current!.Statistics.Crafting;
        Assert.Equal(2, afterItemCost.CompletionActions);
        Assert.Equal(2, afterItemCost.ProducedQuantity);
        Assert.False(afterItemCost.ResourceHistoryUnavailable);
        Assert.Equal(2, afterItemCost.Resources["9002"].ConsumedQuantity);
        var itemRecipe = afterItemCost.Outputs["8002"].Recipes["modded-item-cost"];
        Assert.Equal(1, itemRecipe.Resources["9002"].ConsumptionActions);
        Assert.Equal(2, itemRecipe.Resources["9002"].ConsumedQuantity);
        Assert.Equal(
            AdapterCapabilityState.Supported,
            result.Coordinator.CurrentCraftingCapabilities.ItemResourceIdentity.State);
        Assert.Equal(
            AdapterCapabilityState.Supported,
            result.Coordinator.CurrentCraftingCapabilities.OutputResourceAssociation.State);
        Assert.Equal(1, ItemUtilities.ScanCount);
    }

    private static CompositionResult CompleteDuplicateCostCraft(params int[] stackCounts)
    {
        var result = CreateComposition();
        for (var index = 0; index < stackCounts.Length; index++)
            ItemUtilities.OwnedItems.Add(Stack(9001, stackCounts[index], durability: index + 1));
        CompleteCraft(result, DuplicateCostFormula());
        return result;
    }

    private static CompositionResult CompleteDuplicateCostCraftWithDetachedSameIdChild()
    {
        var result = CreateComposition();
        var first = Stack(9001, 3, durability: 1);
        first.Slots.Add(new ItemStatsSystem.Items.Slot
        {
            Content = Stack(9001, 6, durability: 1)
        });
        ItemUtilities.OwnedItems.Add(first);
        ItemUtilities.OwnedItems.Add(Stack(9001, 3, durability: 2));
        CompleteCraft(result, DuplicateCostFormula());
        return result;
    }

    private static CompositionResult CreateComposition()
    {
        var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        WriteNativeSave(directory.Path);
        Saves.SavesSystem.CurrentSlot = 1;
        var coordinator = new NativeProfileCoordinator();
        coordinator.Initialize();
        var diagnostics = new List<string>();
        var adapter = new NativeCraftingAdapter(
            () => coordinator.CurrentGenerationId,
            coordinator.HandleCrafting,
            coordinator.RequestCraftingPersistence,
            coordinator.SetCraftingCapabilities,
            diagnostics.Add,
            () => true);
        ActivateValidatedCallbacks(adapter, coordinator);
        coordinator.SetCraftingBoundaryBarrier(adapter.FlushPending);
        return new CompositionResult(directory, coordinator, adapter, diagnostics);
    }

    private static void CompleteCraft(CompositionResult result, CraftingFormula formula)
    {
        var actionsBeforeDelivery = result.Coordinator.Current!.Statistics.Crafting.CompletionActions;
        var craftPrefixArguments = new object?[] { formula, null };
        CraftingHarmonyCallbacks.CraftPrefixMethod.Invoke(null, craftPrefixArguments);
        var craftScope = Assert.IsType<CraftingNativeScope>(craftPrefixArguments[1]);

        var payPrefixArguments = new object?[] { formula.cost, null };
        CraftingHarmonyCallbacks.PayPrefixMethod.Invoke(null, payPrefixArguments);
        var paymentScope = payPrefixArguments[1] as CraftingNativeScope;
        var paymentSucceeded = ExecuteFaithfulNativePayment(formula.cost);
        Assert.True(paymentSucceeded);
        CraftingHarmonyCallbacks.PayPostfixMethod.Invoke(null, [paymentScope, paymentSucceeded]);
        Assert.Null(CraftingHarmonyCallbacks.PayFinalizerMethod.Invoke(null, [null, paymentScope]));

        var deliveryCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryArguments = new object?[]
        {
            false,
            true,
            1,
            new List<Item>(),
            new UniTask(deliveryCompletion.Task)
        };
        CraftingHarmonyCallbacks.ReturnPostfixMethod.Invoke(null, deliveryArguments);
        var wrappedDelivery = Assert.IsType<UniTask>(deliveryArguments[4]);

        var craftCompletion = new TaskCompletionSource<List<Item>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var craftPostfixArguments = new object?[]
        {
            craftScope,
            new UniTask<List<Item>>(craftCompletion.Task)
        };
        CraftingHarmonyCallbacks.CraftPostfixMethod.Invoke(null, craftPostfixArguments);
        var wrappedCraft = Assert.IsType<UniTask<List<Item>>>(craftPostfixArguments[1]);
        Assert.Null(CraftingHarmonyCallbacks.CraftFinalizerMethod.Invoke(null, [null, craftScope]));
        Assert.Equal(actionsBeforeDelivery, result.Coordinator.Current!.Statistics.Crafting.CompletionActions);

        deliveryCompletion.SetResult();
        wrappedDelivery.GetAwaiter().GetResult();
        craftCompletion.SetResult(new List<Item>());
        wrappedCraft.GetAwaiter().GetResult();
        result.Coordinator.Flush();
        Assert.Equal(actionsBeforeDelivery + 1, result.Coordinator.Current.Statistics.Crafting.CompletionActions);
    }

    private static bool ExecuteFaithfulNativePayment(Cost cost)
    {
        foreach (var entry in cost.items ?? Array.Empty<Cost.ItemEntry>())
        {
            var count = ItemUtilities.GetItemCount(entry.id);
            CraftingHarmonyCallbacks.GetItemCountPostfixMethod.Invoke(null, [entry.id, count]);
            if (count < entry.amount) return false;
        }

        var removals = new List<Action>();
        var detachedItems = new List<Item>();
        foreach (var entry in cost.items ?? Array.Empty<Cost.ItemEntry>())
        {
            var matching = ItemUtilities.FindAllBelongsToPlayer(item => item.TypeID == entry.id).ToList();
            matching.Sort((left, right) =>
            {
                var durability = left.Durability.CompareTo(right.Durability);
                return durability != 0 ? durability : left.GetInstanceID().CompareTo(right.GetInstanceID());
            });
            var available = matching.Aggregate(
                0L,
                (current, item) => checked(current + (item.Stackable ? item.StackCount : 1)));
            if (available < entry.amount) return false;
            var capturedItems = matching.ToArray();
            var capturedAmount = entry.amount;
            removals.Add(() => ExecuteDeferredRemoval(capturedItems, capturedAmount, detachedItems));
        }

        foreach (var removal in removals) removal();
        foreach (var item in detachedItems) SendDetachedItemToPlayer(item);
        return true;
    }

    private static void ExecuteDeferredRemoval(
        IEnumerable<Item> capturedItems,
        long amount,
        List<Item> detachedItems)
    {
        var remaining = amount;
        foreach (var item in capturedItems)
        {
            foreach (var slot in item.Slots)
            {
                if (slot.Content == null) continue;
                detachedItems.Add(slot.Content);
                slot.Content = null;
            }
            if (item.StackCount <= remaining)
            {
                remaining -= item.StackCount;
                ItemUtilities.OwnedItems.Remove(item);
                CraftingHarmonyCallbacks.MarkDestroyedPrefixMethod.Invoke(null, [item]);
                item.MarkDestroyed();
            }
            else
            {
                SetStackCount(item, item.StackCount - checked((int)remaining));
                remaining = 0;
            }
            if (remaining <= 0) break;
        }
    }

    private static void SendDetachedItemToPlayer(Item incoming)
    {
        var destination = ItemUtilities.OwnedItems.FirstOrDefault(item =>
            item.TypeID == incoming.TypeID
            && item.Stackable
            && !item.IsBeingDestroyed
            && item.StackCount < item.MaxStackCount);
        if (destination != null)
        {
            var transferred = Math.Min(
                destination.MaxStackCount - destination.StackCount,
                incoming.StackCount);
            SetStackCount(destination, checked(destination.StackCount + transferred));
            SetStackCount(incoming, checked(incoming.StackCount - transferred));
        }
        if (incoming.StackCount > 0) ItemUtilities.OwnedItems.Add(incoming);
    }

    private static void SetStackCount(Item item, int value)
    {
        var prefixArguments = new object?[] { item, null };
        CraftingHarmonyCallbacks.StackCountPrefixMethod.Invoke(null, prefixArguments);
        item.StackCount = value;
        CraftingHarmonyCallbacks.StackCountPostfixMethod.Invoke(null, [item, prefixArguments[1]]);
    }

    private static Item Stack(int itemTypeId, int stackCount, float durability) => new()
    {
        TypeID = itemTypeId,
        StackCount = stackCount,
        Stackable = true,
        UseDurability = true,
        Durability = durability
    };

    private static CraftingFormula DuplicateCostFormula() => new()
    {
        id = "modded-duplicate",
        result = new CraftingFormula.ItemEntry { id = 7001, amount = 1 },
        cost = new Cost
        {
            money = 150,
            items =
            [
                new Cost.ItemEntry { id = 9001, amount = 3 },
                new Cost.ItemEntry { id = 9001, amount = 3 }
            ]
        }
    };

    private static void ActivateValidatedCallbacks(
        NativeCraftingAdapter adapter,
        NativeProfileCoordinator coordinator)
    {
        var capabilities = CraftingNativeContractPolicy.Supported(
            "delivery",
            "formula",
            "event resources plus repeated-entry Pay proof",
            "event currency");
        coordinator.SetCraftingCapabilities(
            CraftingNativeContractPolicy.ToRecords(capabilities, NativeCraftingAdapter.AdapterVersion),
            capabilities);
        typeof(NativeCraftingAdapter).GetField("capabilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(adapter, CraftingStatisticsReducer.CloneCapabilities(capabilities));
        typeof(NativeCraftingAdapter).GetField("accepting", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(adapter, true);
        CraftingHarmonyBridge.Attach(adapter);
    }

    private static void WriteNativeSave(string root)
    {
        var path = Path.Combine(root, Saves.SavesSystem.GetFilePath(1));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"SaveTime\":{\"value\":1}}");
    }

    private static int ReadResourceActions(string profilePath) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]!["Outputs"]!["7001"]!
            ["Recipes"]!["modded-duplicate"]!["Resources"]!["9001"]!["ConsumptionActions"]!.GetValue<int>();

    private static long ReadResourceQuantity(string profilePath) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]!["Resources"]!["9001"]!
            ["ConsumedQuantity"]!.GetValue<long>();

    private static long ReadResourceAssociationQuantity(string profilePath) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]!["Outputs"]!["7001"]!
            ["Recipes"]!["modded-duplicate"]!["Resources"]!["9001"]!["ConsumedQuantity"]!.GetValue<long>();

    private static long ReadLifetimeCurrency(string profilePath) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]!["CurrencyCharged"]!.GetValue<long>();

    private static long ReadLifetimeCurrencyActions(string profilePath) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]!["CurrencyChargeActions"]!.GetValue<long>();

    private static bool ReadCraftingBoolean(string profilePath, string propertyName) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]![propertyName]!.GetValue<bool>();

    private static long ReadOutputCurrency(string profilePath) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]!["Outputs"]!["7001"]!
            ["CurrencyCharged"]!.GetValue<long>();

    private static long ReadRecipeCurrency(string profilePath) =>
        JsonNode.Parse(File.ReadAllText(profilePath))!["Statistics"]!["Crafting"]!["Outputs"]!["7001"]!
            ["Recipes"]!["modded-duplicate"]!["CurrencyCharged"]!.GetValue<long>();

    private static string CurrentProfilePath(CompositionResult result) => Path.Combine(
        result.Coordinator.DataRoot,
        "profiles",
        "slot-01",
        "current",
        "profile.json");

    private static void DegradeCapabilities(
        CompositionResult result,
        Action<CraftingMetricCapabilities> degrade)
    {
        var capabilities = result.Coordinator.CurrentCraftingCapabilities;
        degrade(capabilities);
        result.Coordinator.SetCraftingCapabilities(
            CraftingNativeContractPolicy.ToRecords(capabilities, NativeCraftingAdapter.AdapterVersion),
            capabilities);
    }

    private static void ResetNative()
    {
        Application.version = "2.3.30";
        ItemUtilities.ResetNativeState();
        EconomyManager.ResetNativeState();
        Saves.SavesSystem.ResetNativeState();
    }

    private sealed class CompositionResult : IDisposable
    {
        private bool runtimeStopped;

        public CompositionResult(
            TemporaryDirectory directory,
            NativeProfileCoordinator coordinator,
            NativeCraftingAdapter adapter,
            IReadOnlyList<string> diagnostics)
        {
            Directory = directory;
            Coordinator = coordinator;
            Adapter = adapter;
            Diagnostics = diagnostics;
        }

        public TemporaryDirectory Directory { get; }
        public NativeProfileCoordinator Coordinator { get; }
        public NativeCraftingAdapter Adapter { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        public void StopRuntime()
        {
            if (runtimeStopped) return;
            runtimeStopped = true;
            Adapter.Dispose();
            Coordinator.Dispose();
        }

        public void Dispose()
        {
            StopRuntime();
            Directory.Dispose();
        }
    }
}
