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
        using var result = CompleteDuplicateCostCraft(ownedCount: 4);

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
        Assert.Equal(0, ItemUtilities.ScanCount);
        Assert.Contains(result.Diagnostics, detail =>
            detail.Contains("entries totaling 6", StringComparison.Ordinal)
            && detail.Contains("proved only 4", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "ProductionComposition")]
    public void SufficientCombinedStockDuplicateCostPublishesOneCanonicalSixUnitConsumption()
    {
        using var result = CompleteDuplicateCostCraft(ownedCount: 6);

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
        Assert.Equal(0, ItemUtilities.ScanCount);
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "ProductionComposition")]
    public void CorruptExactResourceActionPrimaryRecoversProductionCraftFromBackup()
    {
        using var result = CompleteDuplicateCostCraft(ownedCount: 6);
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
            },
            ownedCount: 0);

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
            },
            ownedCount: 2);

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
        Assert.Equal(0, ItemUtilities.ScanCount);
    }

    private static CompositionResult CompleteDuplicateCostCraft(int ownedCount)
    {
        var result = CreateComposition();
        CompleteCraft(
            result,
            new CraftingFormula
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
            },
            ownedCount);
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

    private static void CompleteCraft(CompositionResult result, CraftingFormula formula, int ownedCount)
    {
        var actionsBeforeDelivery = result.Coordinator.Current!.Statistics.Crafting.CompletionActions;
        var craftPrefixArguments = new object?[] { formula, null };
        CraftingHarmonyCallbacks.CraftPrefixMethod.Invoke(null, craftPrefixArguments);
        var craftScope = Assert.IsType<CraftingNativeScope>(craftPrefixArguments[1]);

        var payPrefixArguments = new object?[] { formula.cost, null };
        CraftingHarmonyCallbacks.PayPrefixMethod.Invoke(null, payPrefixArguments);
        var paymentScope = payPrefixArguments[1] as CraftingNativeScope;
        foreach (var entry in formula.cost.items ?? Array.Empty<Cost.ItemEntry>())
            CraftingHarmonyCallbacks.GetItemCountPostfixMethod.Invoke(null, [entry.id, ownedCount]);
        CraftingHarmonyCallbacks.PayPostfixMethod.Invoke(null, [paymentScope, true]);
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
