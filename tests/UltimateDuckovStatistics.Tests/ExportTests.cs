using System.Globalization;
using System.Runtime.Serialization.Json;
using System.Text;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class ExportTests
{
    private static readonly DateTime TestTime = new(2026, 8, 9, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Export")]
    public void JsonAndCsvExportsRepresentTheSameTotals()
    {
        var profile = CreateProfile();
        ItemUseReducer.Apply(profile.Statistics, CreateUse("one", "item:one", "Medkit", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item));
        ItemUseReducer.Apply(profile.Statistics, CreateUse("two", "item:two", "Juice", CanonicalItemGroup.Drink, 2.5, ConsumptionUnit.Durability));
        HealingReducer.Apply(profile.Statistics, CreateHealing("heal-one", "one", "item:one", CanonicalItemGroup.Healing, 12.5));

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var overview = ParseCsv(bundle.OverviewCsv);
        var groups = ParseCsv(bundle.GroupsCsv);
        var items = ParseCsv(bundle.ItemsCsv);

        Assert.Equal(2, json.Overall.ActivationCount);
        Assert.Equal(json.Overall.ActivationCount, ReadLong(Assert.Single(overview), "activation_count"));
        Assert.Equal(json.Groups.Sum(group => group.Totals.ActivationCount), groups.Sum(row => ReadLong(row, "activation_count")));
        Assert.Equal(json.Items.Sum(item => item.Totals.ActivationCount), items.Sum(row => ReadLong(row, "activation_count")));
        Assert.Equal(json.Overall.ActivationCount, json.Groups.Sum(group => group.Totals.ActivationCount));
        Assert.Equal(json.Overall.ActivationCount, json.Items.Sum(item => item.Totals.ActivationCount));
        Assert.Equal(1, ReadDouble(Assert.Single(overview), "item_amount"));
        Assert.Equal(2.5, ReadDouble(Assert.Single(overview), "durability_amount"), precision: 6);
        Assert.Equal(12.5, ReadDouble(Assert.Single(overview), "actual_hp_restored"), precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            groups.Sum(row => ReadDouble(row, "actual_hp_restored")),
            precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            items.Sum(row => ReadDouble(row, "actual_hp_restored")),
            precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            json.Groups.Sum(group => group.Totals.ActualHealthRestored),
            precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            json.Items.Sum(item => item.Totals.ActualHealthRestored),
            precision: 6);
        Assert.True(Assert.Single(overview).ContainsKey("unknown_amount"));
        Assert.DoesNotContain("unknown_amount_amount", Assert.Single(overview).Keys);
    }

    [Fact]
    [Trait("Category", "Export")]
    public void CsvEscapesItemNamesWithoutChangingTheirValue()
    {
        var profile = CreateProfile();
        const string name = "Soup, \"Deluxe\"\r\nLarge";
        ItemUseReducer.Apply(profile.Statistics, CreateUse("one", "item:one", name, CanonicalItemGroup.Food, 1, ConsumptionUnit.StackUnit));

        var row = Assert.Single(ParseCsv(StatisticsExporter.Create(profile, TestTime).ItemsCsv));

        Assert.Equal(name, row["display_name"]);
    }

    [Fact]
    [Trait("Category", "Export")]
    public void ReclassifiedStableItemKeepsMatchingItemAndGroupExportRows()
    {
        var profile = CreateProfile();
        ItemUseReducer.Apply(
            profile.Statistics,
            CreateUse("one", "item:stable", "Item", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item));
        ItemUseReducer.Apply(
            profile.Statistics,
            CreateUse("two", "item:stable", "Item", CanonicalItemGroup.Drink, 2, ConsumptionUnit.Durability));

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var item = Assert.Single(ParseCsv(bundle.ItemsCsv));
        var group = Assert.Single(ParseCsv(bundle.GroupsCsv));

        Assert.Equal(nameof(CanonicalItemGroup.Healing), item["group"]);
        Assert.Equal(item["group"], group["group"]);
        Assert.Equal(ReadLong(item, "activation_count"), ReadLong(group, "activation_count"));
    }

    [Fact]
    [Trait("Category", "Export")]
    public void WriterCreatesOneCompleteGenerationScopedExportSet()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var profile = CreateProfile();
        var current = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current");
        Directory.CreateDirectory(current);
        var profilePath = System.IO.Path.Combine(current, "profile.json");

        var result = ProfileExportWriter.Write(profile, profilePath, TestTime);

        Assert.Equal(4, result.Files.Count);
        Assert.All(result.Files, path => Assert.True(File.Exists(path)));
        Assert.Empty(Directory.EnumerateFiles(result.Directory, "*.tmp"));
        Assert.Contains("generation-a", result.Directory, StringComparison.Ordinal);
    }

    private static ProfileDocument CreateProfile() => new()
    {
        GenerationId = "generation-a",
        Slot = 1,
        Revision = 4,
        CreatedUtc = TestTime,
        UpdatedUtc = TestTime,
        Identity = new SaveIdentitySnapshot { Slot = 1 },
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = "generation-a",
            CreatedUtc = TestTime,
            UpdatedUtc = TestTime
        }
    };

    private static ItemUseRecorded CreateUse(
        string eventId,
        string itemId,
        string displayName,
        CanonicalItemGroup group,
        double amount,
        ConsumptionUnit unit) => new()
    {
        EventId = eventId,
        TimestampUtc = TestTime,
        SaveGenerationId = "generation-a",
        GameplayContext = GameplayContext.Raid,
        ItemId = itemId,
        DisplayName = displayName,
        Group = group,
        EffectTags = new List<ItemEffectTag> { ItemEffectTag.Food },
        ActivationCount = 1,
        AmountConsumed = amount,
        ConsumptionUnit = unit
    };

    private static HealingApplied CreateHealing(
        string eventId,
        string sourceUseEventId,
        string itemId,
        CanonicalItemGroup group,
        double amount) => new()
    {
        EventId = eventId,
        ApplicationId = $"application-{eventId}",
        SourceItemUseEventId = sourceUseEventId,
        TimestampUtc = TestTime,
        SaveGenerationId = "generation-a",
        GameplayContext = GameplayContext.Raid,
        ItemId = itemId,
        DisplayName = itemId,
        Group = group,
        ActualHealthRestored = amount
    };

    private static StatisticsExportDocument Deserialize(string json)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return Assert.IsType<StatisticsExportDocument>(serializer.ReadObject(stream));
    }

    private static List<IReadOnlyDictionary<string, string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                field.Append(character);
            }
        }

        var headers = rows[0];
        return rows.Skip(1)
            .Where(values => values.Count > 1)
            .Select(values => (IReadOnlyDictionary<string, string>)headers
                .Select((header, index) => new KeyValuePair<string, string>(header, values[index]))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
            .ToList();
    }

    private static long ReadLong(IReadOnlyDictionary<string, string> row, string key) =>
        long.Parse(row[key], NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double ReadDouble(IReadOnlyDictionary<string, string> row, string key) =>
        double.Parse(row[key], NumberStyles.Float, CultureInfo.InvariantCulture);
}
