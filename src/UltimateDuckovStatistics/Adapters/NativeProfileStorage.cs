using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeProfileStorage
{
    internal static ProfileRepository Create(string dataRoot, Action<string> diagnostic)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(NativeProfileStorage).Assembly.Location)
            ?? throw new IOException("The mod assembly has no dependency directory.");
        // The same-directory managed dependencies resolve through Duckov's
        // Assembly.LoadFrom context. Native loading uses the exact pinned DLL.
        // Failure is propagated before any profile is opened; no JSON fallback
        // can split authority after SQLite has been promoted.
        SqliteLibrary.Initialize(Path.Combine(assemblyDirectory, "sqlite3.dll"));
        var codec = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord);
        return new ProfileRepository(dataRoot, () => DateTime.UtcNow, () => Guid.NewGuid().ToString("N"), diagnostic,
            NativeProfileJsonWriter.Write, path => new SqliteProfileStorage(path, codec, Path.Combine(dataRoot, "export-staging")), codec);
    }
}
