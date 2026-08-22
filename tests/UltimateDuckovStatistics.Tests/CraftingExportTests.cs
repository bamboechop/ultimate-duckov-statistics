using System.Globalization;
using System.Text.Json;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class CraftingExportTests
{
    [Fact]
    public void ProfileJsonCsvAndUiUseTheSameCraftingActionsQuantityRecipeAndAvailability()
    {
        var aggregate = new CraftingStatisticsAggregate
        {
            HistoricalUnavailable = true,
            HistoricalProvenance = "pre-M13 unavailable"
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
        var totals = ParseCsv(bundle.CraftingTotalsCsv).ToList();
        var total = totals.Single(row => row["scope"] == "lifetime");
        var output = totals.Single(row => row["scope"] == "output");
        var recipe = Assert.Single(ParseCsv(bundle.CraftingRecipesCsv));

        Assert.Equal(2, jsonCrafting.GetProperty("CompletionActions").GetInt64());
        Assert.Equal(14, jsonCrafting.GetProperty("ProducedQuantity").GetInt64());
        Assert.True(jsonCrafting.GetProperty("HistoricalUnavailable").GetBoolean());
        Assert.Equal("2", total["completion_actions"]);
        Assert.Equal("14", total["produced_quantity"]);
        Assert.Equal("900001", output["output_item_id"]);
        Assert.Equal("Modded Cell", output["display_name"]);
        Assert.Equal("modded_cell", recipe["recipe_id"]);
        Assert.Equal("7", recipe["batch_quantity"]);
        Assert.Equal("2", recipe["batch_actions"]);
        Assert.Equal(nameof(AdapterCapabilityState.Supported), total["completion_capability"]);
        Assert.Equal("True", total["historical_unavailable"]);
        Assert.Equal("2", UiText.FormatCraftingCount(2, aggregate.Capabilities.CompletionActions));
        Assert.Equal("14", UiText.FormatCraftingCount(14, aggregate.Capabilities.ProducedQuantity));

        var unavailable = CraftingNativeContractPolicy.Unavailable("gap");
        Assert.Equal("Unsupported", UiText.FormatCraftingCount(0, unavailable.CompletionActions));
        Assert.Equal("2 (capture incomplete)", UiText.FormatCraftingCount(2, unavailable.CompletionActions));
        Assert.Equal(
            "2 (capture incomplete)",
            UiText.FormatCraftingCount(
                2,
                aggregate.Capabilities.CompletionActions,
                unavailable.RecipeIdentity));
    }

    [Fact]
    public void FileExportIncludesCraftingCsvFilesAlongsideJson()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var profile = new ProfileDocument
        {
            GenerationId = "generation-1",
            Slot = 1,
            Statistics = new ProfileStatistics { SaveGenerationId = "generation-1" }
        };

        var result = ProfileExportWriter.Write(
            profile,
            Path.Combine(temporaryDirectory.Path, "profile.json"),
            new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc));

        Assert.Contains(result.Files, path => string.Equals(Path.GetFileName(path), "crafting_totals.csv", StringComparison.Ordinal));
        Assert.Contains(result.Files, path => string.Equals(Path.GetFileName(path), "crafting_recipes.csv", StringComparison.Ordinal));
        Assert.Contains(result.Files, path => string.Equals(Path.GetExtension(path), ".json", StringComparison.Ordinal));
        Assert.All(result.Files, path => Assert.True(File.Exists(path)));
    }

    private static IEnumerable<Dictionary<string, string>> ParseCsv(string value)
    {
        var lines = value.Trim().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var headers = lines[0].Split(',');
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',');
            yield return headers.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
        }
    }
}
