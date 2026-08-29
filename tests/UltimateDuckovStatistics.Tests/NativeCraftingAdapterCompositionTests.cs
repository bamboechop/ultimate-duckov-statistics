using System.Reflection;
using System.Text.Json;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
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

    private static CompositionResult CompleteDuplicateCostCraft(int ownedCount)
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

        var formula = new CraftingFormula
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

        var craftPrefixArguments = new object?[] { formula, null };
        CraftingHarmonyCallbacks.CraftPrefixMethod.Invoke(null, craftPrefixArguments);
        var craftScope = Assert.IsType<CraftingNativeScope>(craftPrefixArguments[1]);

        var payPrefixArguments = new object?[] { formula.cost, null };
        CraftingHarmonyCallbacks.PayPrefixMethod.Invoke(null, payPrefixArguments);
        var paymentScope = Assert.IsType<CraftingNativeScope>(payPrefixArguments[1]);
        foreach (var entry in formula.cost.items)
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
        Assert.Equal(0, coordinator.Current!.Statistics.Crafting.CompletionActions);

        deliveryCompletion.SetResult();
        wrappedDelivery.GetAwaiter().GetResult();
        craftCompletion.SetResult(new List<Item>());
        wrappedCraft.GetAwaiter().GetResult();
        coordinator.Flush();
        return new CompositionResult(directory, coordinator, adapter, diagnostics);
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

    private static void ResetNative()
    {
        Application.version = "2.3.30";
        ItemUtilities.ResetNativeState();
        EconomyManager.ResetNativeState();
        Saves.SavesSystem.ResetNativeState();
    }

    private sealed class CompositionResult : IDisposable
    {
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

        public void Dispose()
        {
            Adapter.Dispose();
            Coordinator.Dispose();
            Directory.Dispose();
        }
    }
}
