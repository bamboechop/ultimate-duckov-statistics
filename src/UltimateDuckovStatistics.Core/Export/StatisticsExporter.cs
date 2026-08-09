using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Export;

[DataContract]
public sealed class StatisticsExportDocument
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public DateTime ExportedUtc { get; set; }

    [DataMember(Order = 3)]
    public string GenerationId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int Slot { get; set; }

    [DataMember(Order = 5)]
    public long Revision { get; set; }

    [DataMember(Order = 6)]
    public AggregateTotals Overall { get; set; } = new();

    [DataMember(Order = 7)]
    public List<GroupExportRow> Groups { get; set; } = new();

    [DataMember(Order = 8)]
    public List<ItemExportRow> Items { get; set; } = new();
}

[DataContract]
public sealed class GroupExportRow
{
    [DataMember(Order = 1)]
    public string Group { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public AggregateTotals Totals { get; set; } = new();
}

[DataContract]
public sealed class ItemExportRow
{
    [DataMember(Order = 1)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Group { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> EffectTags { get; set; } = new();

    [DataMember(Order = 5)]
    public AggregateTotals Totals { get; set; } = new();
}

public sealed class StatisticsExportBundle
{
    public StatisticsExportBundle(
        StatisticsExportDocument document,
        string json,
        string overviewCsv,
        string groupsCsv,
        string itemsCsv)
    {
        Document = document;
        Json = json;
        OverviewCsv = overviewCsv;
        GroupsCsv = groupsCsv;
        ItemsCsv = itemsCsv;
    }

    public StatisticsExportDocument Document { get; }

    public string Json { get; }

    public string OverviewCsv { get; }

    public string GroupsCsv { get; }

    public string ItemsCsv { get; }
}

public static class StatisticsExporter
{
    private static readonly string[] AmountUnits =
    {
        "Item",
        "StackUnit",
        "Durability",
        "UnknownAmount"
    };
    private static readonly char[] CsvSpecialCharacters = { ',', '"', '\r', '\n' };

    public static StatisticsExportBundle Create(ProfileDocument profile, DateTime exportedUtc)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        exportedUtc = EnsureUtc(exportedUtc);
        var document = new StatisticsExportDocument
        {
            ExportedUtc = exportedUtc,
            GenerationId = profile.GenerationId,
            Slot = profile.Slot,
            Revision = profile.Revision,
            Overall = CloneTotals(profile.Statistics.Overall),
            Groups = profile.Statistics.Groups
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new GroupExportRow
                {
                    Group = entry.Key,
                    Totals = CloneTotals(entry.Value)
                })
                .ToList(),
            Items = profile.Statistics.Items.Values
                .OrderBy(item => item.ItemId, StringComparer.Ordinal)
                .Select(item => new ItemExportRow
                {
                    ItemId = item.ItemId,
                    DisplayName = item.DisplayName,
                    Group = item.Group.ToString(),
                    EffectTags = item.EffectTags.Select(tag => tag.ToString()).OrderBy(tag => tag, StringComparer.Ordinal).ToList(),
                    Totals = CloneTotals(item.Totals)
                })
                .ToList()
        };

        return new StatisticsExportBundle(
            document,
            SerializeJson(document),
            CreateOverviewCsv(document),
            CreateGroupsCsv(document),
            CreateItemsCsv(document));
    }

    private static string SerializeJson(StatisticsExportDocument document)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, document);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CreateOverviewCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "generation_id,slot,revision,exported_utc");
        builder.Append(Csv(document.GenerationId)).Append(',')
            .Append(document.Slot.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(document.Revision.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Csv(document.ExportedUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
        AppendTotals(builder, document.Overall);
        return builder.ToString();
    }

    private static string CreateGroupsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "group");
        foreach (var group in document.Groups)
        {
            builder.Append(Csv(group.Group)).Append(',');
            AppendTotals(builder, group.Totals);
        }

        return builder.ToString();
    }

    private static string CreateItemsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "item_id,display_name,group,effect_tags");
        foreach (var item in document.Items)
        {
            builder.Append(Csv(item.ItemId)).Append(',')
                .Append(Csv(item.DisplayName)).Append(',')
                .Append(Csv(item.Group)).Append(',')
                .Append(Csv(string.Join("|", item.EffectTags))).Append(',');
            AppendTotals(builder, item.Totals);
        }

        return builder.ToString();
    }

    private static void AppendTotalsHeader(StringBuilder builder, string prefix)
    {
        builder.Append(prefix).Append(",activation_count,actual_hp_restored");
        foreach (var unit in AmountUnits)
        {
            builder.Append(',').Append(GetAmountColumnName(unit));
        }

        builder.AppendLine();
    }

    private static void AppendTotals(StringBuilder builder, AggregateTotals totals)
    {
        builder.Append(totals.ActivationCount.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(totals.ActualHealthRestored.ToString("R", CultureInfo.InvariantCulture));
        foreach (var unit in AmountUnits)
        {
            builder.Append(',').Append(ReadAmount(totals, unit).ToString("R", CultureInfo.InvariantCulture));
        }

        builder.AppendLine();
    }

    private static double ReadAmount(AggregateTotals totals, string unit) =>
        totals.AmountsByUnit.TryGetValue(unit, out var value) ? value : 0;

    private static AggregateTotals CloneTotals(AggregateTotals source) => new()
    {
        ActivationCount = source.ActivationCount,
        ActualHealthRestored = source.ActualHealthRestored,
        AmountsByUnit = source.AmountsByUnit.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal)
    };

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(CsvSpecialCharacters) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string GetAmountColumnName(string unit) =>
        string.Equals(unit, "UnknownAmount", StringComparison.Ordinal)
            ? "unknown_amount"
            : $"{ToSnakeCase(unit)}_amount";

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
