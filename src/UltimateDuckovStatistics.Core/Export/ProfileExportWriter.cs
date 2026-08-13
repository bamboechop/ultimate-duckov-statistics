using System.Globalization;
using System.Text;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Export;

public sealed class ProfileExportResult
{
    public ProfileExportResult(string directory, IReadOnlyList<string> files)
    {
        Directory = directory;
        Files = files;
    }

    public string Directory { get; }

    public IReadOnlyList<string> Files { get; }
}

public static class ProfileExportWriter
{
    public static ProfileExportResult Write(
        ProfileDocument profile,
        string currentProfilePath,
        DateTime exportedUtc)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(currentProfilePath))
            ?? throw new ArgumentException("Profile path has no directory.", nameof(currentProfilePath));
        exportedUtc = exportedUtc.Kind == DateTimeKind.Utc ? exportedUtc : exportedUtc.ToUniversalTime();
        var exportDirectory = Path.Combine(
            currentDirectory,
            "exports",
            $"{exportedUtc.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture)}-{profile.GenerationId}");
        Directory.CreateDirectory(exportDirectory);
        var bundle = StatisticsExporter.Create(profile, exportedUtc);
        var files = new[]
        {
            WriteAtomicText(exportDirectory, "statistics.json", bundle.Json),
            WriteAtomicText(exportDirectory, "overview.csv", bundle.OverviewCsv),
            WriteAtomicText(exportDirectory, "groups.csv", bundle.GroupsCsv),
            WriteAtomicText(exportDirectory, "items.csv", bundle.ItemsCsv),
            WriteAtomicText(exportDirectory, "runs.csv", bundle.RunsCsv),
            WriteAtomicText(exportDirectory, "run_totals.csv", bundle.RunTotalsCsv),
            WriteAtomicText(exportDirectory, "map_totals.csv", bundle.MapTotalsCsv),
            WriteAtomicText(exportDirectory, "records.csv", bundle.RecordsCsv),
            WriteAtomicText(exportDirectory, "combat_totals.csv", bundle.CombatTotalsCsv),
            WriteAtomicText(exportDirectory, "combat_attribution.csv", bundle.CombatAttributionCsv),
            WriteAtomicText(exportDirectory, "weapon_totals.csv", bundle.WeaponTotalsCsv),
            WriteAtomicText(exportDirectory, "ammunition_totals.csv", bundle.AmmunitionTotalsCsv),
            WriteAtomicText(exportDirectory, "equipment_totals.csv", bundle.EquipmentTotalsCsv),
            WriteAtomicText(exportDirectory, "recurring_loadouts.csv", bundle.RecurringLoadoutsCsv),
            WriteAtomicText(exportDirectory, "equipment_combat.csv", bundle.EquipmentCombatCsv),
            WriteAtomicText(exportDirectory, "containers.csv", bundle.ContainersCsv),
            WriteAtomicText(exportDirectory, "routes.csv", bundle.RoutesCsv),
            WriteAtomicText(exportDirectory, "segments.csv", bundle.SegmentsCsv),
            WriteAtomicText(exportDirectory, "segment_events.csv", bundle.SegmentEventsCsv),
            WriteAtomicText(exportDirectory, "route_map_totals.csv", bundle.RouteMapTotalsCsv)
        };
        return new ProfileExportResult(exportDirectory, files);
    }

    private static string WriteAtomicText(string directory, string fileName, string contents)
    {
        var path = Path.Combine(directory, fileName);
        var temporaryPath = AtomicJsonPaths.GetTemporaryPath(path);
        using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(contents);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path);
        return path;
    }
}
