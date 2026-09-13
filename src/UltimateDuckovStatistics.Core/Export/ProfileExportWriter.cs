using System.Globalization;
using System.IO.Compression;
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
    public static ProfileExportResult Write(ProfileExportSnapshot snapshot, DateTime exportedUtc) =>
        WriteToRoot(snapshot, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(snapshot.ProfilePath))!, "exports"), exportedUtc);

    public static ProfileExportResult WriteToRoot(ProfileExportSnapshot snapshot, string exportRoot, DateTime exportedUtc)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        return WriteToRoot(snapshot.Document, exportRoot, exportedUtc);
    }

    public static ProfileExportResult Write(ProfilePersistenceSnapshot snapshot, DateTime exportedUtc)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        return Write(snapshot.Document, snapshot.Path, exportedUtc);
    }

    public static ProfileExportResult WriteToRoot(ProfilePersistenceSnapshot snapshot, string exportRoot, DateTime exportedUtc)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        return WriteToRoot(snapshot.Document, exportRoot, exportedUtc);
    }

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
        return WriteToRoot(profile, Path.Combine(currentDirectory, "exports"), exportedUtc);
    }

    private static ProfileExportResult WriteToRoot(ProfileDocument profile, string exportRoot, DateTime exportedUtc)
    {
        exportedUtc = exportedUtc.Kind == DateTimeKind.Utc ? exportedUtc : exportedUtc.ToUniversalTime();
        var exportDirectory = Path.Combine(
            Path.GetFullPath(exportRoot),
            $"{exportedUtc.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture)}-{profile.GenerationId}");
        Directory.CreateDirectory(exportDirectory);
        var path = Path.Combine(exportDirectory, "statistics.zip");
        var temporaryPath = AtomicJsonPaths.GetTemporaryPath(path);
        using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            // Finalize the entry and central directory before flushing and publishing.
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("statistics.json", CompressionLevel.Optimal);
                using var json = entry.Open();
                StatisticsExporter.WriteJson(profile, exportedUtc, json);
            }
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, path);
        return new ProfileExportResult(exportDirectory, new[] { path });
    }
}
