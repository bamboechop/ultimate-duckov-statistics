using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace UltimateDuckovStatistics.Core.Persistence;

public enum AtomicJsonLoadSource
{
    Missing,
    Primary,
    Backup,
    Temporary
}

public sealed class AtomicJsonLoadResult<T>
    where T : class
{
    public AtomicJsonLoadResult(
        T? value,
        AtomicJsonLoadSource source,
        IReadOnlyList<string> failures,
        bool primaryRepaired)
    {
        Value = value;
        Source = source;
        Failures = failures;
        PrimaryRepaired = primaryRepaired;
    }

    public T? Value { get; }

    public AtomicJsonLoadSource Source { get; }

    public IReadOnlyList<string> Failures { get; }

    public bool PrimaryRepaired { get; }

    public bool Found => Value != null;

    public bool Recovered => Source is AtomicJsonLoadSource.Backup or AtomicJsonLoadSource.Temporary;
}

public sealed class AtomicJsonStore<T>
    where T : class
{
    private readonly DataContractJsonSerializer serializer = new(
        typeof(T),
        new DataContractJsonSerializerSettings
        {
            UseSimpleDictionaryFormat = true,
            SerializeReadOnlyTypes = false
        });

    public void Save(string path, T value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A target path is required.", nameof(path));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Target path has no directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = AtomicJsonPaths.GetTemporaryPath(fullPath);
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            serializer.WriteObject(stream, value);
            stream.Flush(flushToDisk: true);
        }

        if (!File.Exists(fullPath))
        {
            File.Move(temporaryPath, fullPath);
            return;
        }

        // File.Replace is atomic on the supported Windows filesystem and puts
        // the previously valid primary at .bak in the same operation.
        File.Replace(temporaryPath, fullPath, AtomicJsonPaths.GetBackupPath(fullPath), ignoreMetadataErrors: true);
    }

    public AtomicJsonLoadResult<T> Load(string path) => Load(path, semanticValidator: null);

    public AtomicJsonLoadResult<T> Load(string path, Func<T, string?>? semanticValidator)
    {
        var fullPath = Path.GetFullPath(path);
        var failures = new List<string>();
        var candidates = new[]
        {
            (Path: fullPath, Source: AtomicJsonLoadSource.Primary),
            (Path: AtomicJsonPaths.GetBackupPath(fullPath), Source: AtomicJsonLoadSource.Backup),
            (Path: AtomicJsonPaths.GetTemporaryPath(fullPath), Source: AtomicJsonLoadSource.Temporary)
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.Path))
            {
                continue;
            }

            try
            {
                T? value;
                using (var stream = new FileStream(candidate.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    value = serializer.ReadObject(stream) as T;
                }

                if (value == null)
                {
                    throw new SerializationException("The JSON document contained no object.");
                }

                var semanticFailure = semanticValidator?.Invoke(value);
                if (!string.IsNullOrWhiteSpace(semanticFailure))
                {
                    failures.Add($"{candidate.Source}: SemanticValidation: {semanticFailure}");
                    continue;
                }

                var repaired = candidate.Source != AtomicJsonLoadSource.Primary
                    && TryRepairPrimary(candidate.Path, fullPath);
                return new AtomicJsonLoadResult<T>(value, candidate.Source, failures, repaired);
            }
            catch (Exception exception) when (exception is SerializationException
                                              or IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataContractException)
            {
                failures.Add($"{candidate.Source}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        return new AtomicJsonLoadResult<T>(null, AtomicJsonLoadSource.Missing, failures, primaryRepaired: false);
    }

    public void Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        DeleteIfPresent(fullPath);
        DeleteIfPresent(AtomicJsonPaths.GetBackupPath(fullPath));
        DeleteIfPresent(AtomicJsonPaths.GetTemporaryPath(fullPath));
    }

    private static bool TryRepairPrimary(string sourcePath, string primaryPath)
    {
        var repairPath = primaryPath + ".repair";
        try
        {
            File.Copy(sourcePath, repairPath, overwrite: true);
            if (File.Exists(primaryPath))
            {
                File.Replace(repairPath, primaryPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(repairPath, primaryPath);
            }

            return true;
        }
        catch
        {
            DeleteIfPresent(repairPath);
            return false;
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(path);
    }
}

public static class AtomicJsonPaths
{
    public static string GetBackupPath(string path) => Path.GetFullPath(path) + ".bak";

    public static string GetTemporaryPath(string path) => Path.GetFullPath(path) + ".tmp";
}
