using System.Text.Json;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class CraftingExportTests
{
    [Fact]
    public void ProfileJsonAndUiUseTheSameCraftingActionsQuantityRecipeAndAvailability()
    {
        var aggregate = new CraftingStatisticsAggregate
        {
        };
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.Supported("completion", "formula"));
        CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc),
                [new CraftingMutationRow("900001", "Modded Cell", "modded_cell", 2, 14, new() { ["7"] = 2 })]));
        var profile = new ProfileDocument
        {
            GenerationId = "generation-1",
            Slot = 1,
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = "generation-1",
                Crafting = aggregate
            },
            Capabilities = CraftingNativeContractPolicy.ToRecords(aggregate.Capabilities, "test").ToList()
        };

        var bundle = StatisticsExporter.Create(profile, new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc));
        using var json = JsonDocument.Parse(bundle.Json);
        var jsonCrafting = json.RootElement.GetProperty("Crafting");

        Assert.Equal(2, jsonCrafting.GetProperty("CompletionActions").GetInt64());
        Assert.Equal(14, jsonCrafting.GetProperty("ProducedQuantity").GetInt64());
        var output = jsonCrafting.GetProperty("Outputs").GetProperty("900001");
        Assert.Equal("Modded Cell", output.GetProperty("DisplayName").GetString());
        var recipe = output.GetProperty("Recipes").GetProperty("modded_cell");
        Assert.Equal("modded_cell", recipe.GetProperty("RecipeId").GetString());
        Assert.Equal(2, recipe.GetProperty("BatchActions").GetProperty("7").GetInt64());
        Assert.Equal((int)AdapterCapabilityState.Supported,
            jsonCrafting.GetProperty("Capabilities").GetProperty("CompletionActions").GetProperty("State").GetInt32());

    }
}
