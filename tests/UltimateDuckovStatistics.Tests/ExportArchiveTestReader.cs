using System.IO.Compression;
using System.Runtime.Serialization.Json;
using UltimateDuckovStatistics.Core.Export;

namespace UltimateDuckovStatistics.Tests;

internal static class ExportArchiveTestReader
{
    public static byte[] ReadJson(ProfileExportResult result)
    {
        var path = Assert.Single(result.Files);
        Assert.Equal("statistics.zip", Path.GetFileName(path));
        using var file = File.OpenRead(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("statistics.json", entry.FullName);
        using var json = entry.Open();
        using var bytes = new MemoryStream();
        json.CopyTo(bytes);
        return bytes.ToArray();
    }

    public static StatisticsExportDocument ReadDocument(ProfileExportResult result)
    {
        using var json = new MemoryStream(ReadJson(result));
        var serializer = new DataContractJsonSerializer(typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        return (StatisticsExportDocument)serializer.ReadObject(json)!;
    }
}
