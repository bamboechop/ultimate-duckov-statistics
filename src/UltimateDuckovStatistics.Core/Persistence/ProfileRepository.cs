using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

public sealed class ProfileOpenResult
{
    public bool CreatedNew { get; internal set; }

    public bool RotatedGeneration { get; internal set; }

    public bool RecoveredSnapshot { get; internal set; }

    public bool MigratedSchema { get; internal set; }

    public bool InterruptedSessionRecovered { get; internal set; }

    public IReadOnlyList<string> LoadFailures { get; internal set; } = Array.Empty<string>();
}

public sealed class ProfileRepository
{
    private readonly string dataRoot;
    private readonly Func<DateTime> utcNow;
    private readonly Func<string> idFactory;
    private readonly Action<string> diagnostic;
    private readonly AtomicJsonStore<ProfileDocument> profileStore = new();
    private readonly AtomicJsonStore<SessionCheckpoint> sessionStore = new();
    private readonly List<CapabilityRecord> configuredCapabilities = new();
    private ProfileDocument? current;
    private string? currentDirectory;
    private bool capabilitiesConfigured;

    public ProfileRepository(
        string dataRoot,
        Func<DateTime> utcNow,
        Func<string> idFactory,
        Action<string>? diagnostic = null)
    {
        this.dataRoot = Path.GetFullPath(dataRoot ?? throw new ArgumentNullException(nameof(dataRoot)));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        this.idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));
        this.diagnostic = diagnostic ?? (_ => { });
    }

    public ProfileDocument Current => current
        ?? throw new InvalidOperationException("No profile generation is open.");

    public string CurrentGenerationId => current?.GenerationId ?? string.Empty;

    public string? CurrentProfilePath => currentDirectory == null ? null : GetProfilePath(currentDirectory);

    public ProfileOpenResult Open(SaveIdentitySnapshot identity, string creationReason = "Startup")
    {
        ValidateIdentity(identity);
        if (current != null)
        {
            CloseClean();
        }

        var result = new ProfileOpenResult();
        var slotDirectory = GetSlotDirectory(identity.Slot);
        currentDirectory = Path.Combine(slotDirectory, "current");
        Directory.CreateDirectory(currentDirectory);
        var profilePath = GetProfilePath(currentDirectory);
        var loaded = profileStore.Load(profilePath);
        result.LoadFailures = loaded.Failures;

        if (loaded.Value == null)
        {
            if (Directory.EnumerateFileSystemEntries(currentDirectory).Any())
            {
                ArchiveCurrentDirectory(slotDirectory, "UnrecoverableProfile");
                result.RotatedGeneration = true;
            }

            currentDirectory = Path.Combine(slotDirectory, "current");
            Directory.CreateDirectory(currentDirectory);
            current = CreateNewProfile(identity, creationReason);
            result.CreatedNew = true;
            SaveCurrent();
        }
        else
        {
            current = loaded.Value;
            result.RecoveredSnapshot = loaded.Recovered;
            if (!IdentityMatches(current.Identity, identity, current.Statistics.Overall.ActivationCount))
            {
                ArchiveCurrentDirectory(slotDirectory, "SaveIdentityChanged");
                currentDirectory = Path.Combine(slotDirectory, "current");
                Directory.CreateDirectory(currentDirectory);
                current = CreateNewProfile(identity, "SaveIdentityChanged");
                result.CreatedNew = true;
                result.RotatedGeneration = true;
                SaveCurrent();
            }
            else
            {
                result.MigratedSchema = ProfileMigrator.Migrate(current);
                current.Identity = identity;
                if (loaded.Recovered || result.MigratedSchema)
                {
                    SaveCurrent();
                }
            }
        }

        ApplyConfiguredCapabilities();
        result.InterruptedSessionRecovered = RecoverInterruptedSession();
        StartSession();
        return result;
    }

    public bool Record(ItemUseRecorded itemUse)
    {
        var profile = Current;
        if (!ItemUseReducer.Apply(profile.Statistics, itemUse))
        {
            return false;
        }

        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        SaveCurrent();
        return true;
    }

    public void SetCapabilities(IEnumerable<CapabilityRecord> capabilities)
    {
        if (capabilities == null)
        {
            throw new ArgumentNullException(nameof(capabilities));
        }

        configuredCapabilities.Clear();
        configuredCapabilities.AddRange(capabilities.Select(CloneCapability));
        capabilitiesConfigured = true;
        ApplyConfiguredCapabilities();
    }

    public void Rotate(SaveIdentitySnapshot identity, string reason)
    {
        ValidateIdentity(identity);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A generation rotation reason is required.", nameof(reason));
        }

        if (current == null || currentDirectory == null)
        {
            Open(identity, reason);
            return;
        }

        var slotDirectory = GetSlotDirectory(identity.Slot);
        var generationId = current.GenerationId;
        CloseClean();
        ArchiveCurrentDirectory(slotDirectory, reason, generationId);
        currentDirectory = Path.Combine(slotDirectory, "current");
        Directory.CreateDirectory(currentDirectory);
        current = CreateNewProfile(identity, reason);
        SaveCurrent();
        StartSession();
    }

    public void RefreshIdentity(SaveIdentitySnapshot identity)
    {
        ValidateIdentity(identity);
        var profile = Current;
        if (profile.Slot != identity.Slot)
        {
            throw new InvalidOperationException("Cannot refresh a different slot's identity.");
        }

        profile.Identity = identity;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        profile.Revision++;
        SaveCurrent();
    }

    public void Flush()
    {
        if (current != null)
        {
            SaveCurrent();
        }
    }

    public void CloseClean()
    {
        if (current == null || currentDirectory == null)
        {
            return;
        }

        SaveCurrent();
        sessionStore.Delete(GetSessionPath(currentDirectory));
        diagnostic($"Closed generation {current.GenerationId} cleanly.");
        current = null;
        currentDirectory = null;
    }

    private ProfileDocument CreateNewProfile(SaveIdentitySnapshot identity, string reason)
    {
        var now = EnsureUtc(utcNow());
        var generationId = idFactory();
        if (string.IsNullOrWhiteSpace(generationId))
        {
            throw new InvalidOperationException("Generation ID factory returned an empty value.");
        }

        return new ProfileDocument
        {
            GenerationId = generationId,
            Slot = identity.Slot,
            GenerationReason = reason,
            CreatedUtc = now,
            UpdatedUtc = now,
            Identity = identity,
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = generationId,
                CreatedUtc = now,
                UpdatedUtc = now
            },
            Capabilities = capabilitiesConfigured
                ? configuredCapabilities.Select(CloneCapability).ToList()
                : new List<CapabilityRecord>()
        };
    }

    private void ApplyConfiguredCapabilities()
    {
        if (!capabilitiesConfigured)
        {
            return;
        }

        var profile = Current;
        if (CapabilitiesEqual(profile.Capabilities, configuredCapabilities))
        {
            return;
        }

        profile.Capabilities = configuredCapabilities.Select(CloneCapability).ToList();
        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        SaveCurrent();
    }

    private bool RecoverInterruptedSession()
    {
        if (currentDirectory == null)
        {
            return false;
        }

        var sessionPath = GetSessionPath(currentDirectory);
        var sessionArtifactsExist = File.Exists(sessionPath)
                                    || File.Exists(AtomicJsonPaths.GetBackupPath(sessionPath))
                                    || File.Exists(AtomicJsonPaths.GetTemporaryPath(sessionPath));
        if (!sessionArtifactsExist)
        {
            return false;
        }

        var loaded = sessionStore.Load(sessionPath);
        var interruptedGeneration = loaded.Value?.GenerationId ?? "unknown";
        Current.InterruptedSessionCount++;
        Current.Revision++;
        Current.UpdatedUtc = EnsureUtc(utcNow());
        SaveCurrent();
        sessionStore.Delete(sessionPath);
        diagnostic($"Recovered interrupted UDS session for generation {interruptedGeneration}.");
        return true;
    }

    private void StartSession()
    {
        if (currentDirectory == null)
        {
            throw new InvalidOperationException("Cannot start a session without an open profile.");
        }

        var profile = Current;
        sessionStore.Save(
            GetSessionPath(currentDirectory),
            new SessionCheckpoint
            {
                SessionId = idFactory(),
                GenerationId = profile.GenerationId,
                StartedUtc = EnsureUtc(utcNow()),
                ProfileRevisionAtStart = profile.Revision
            });
    }

    private void SaveCurrent()
    {
        if (current == null || currentDirectory == null)
        {
            throw new InvalidOperationException("No current profile can be saved.");
        }

        current.SchemaVersion = ProductInfo.SchemaVersion;
        current.Statistics.SchemaVersion = ProductInfo.SchemaVersion;
        current.Statistics.SaveGenerationId = current.GenerationId;
        profileStore.Save(GetProfilePath(currentDirectory), current);
    }

    private void ArchiveCurrentDirectory(string slotDirectory, string reason, string? generationId = null)
    {
        var source = Path.Combine(slotDirectory, "current");
        if (!Directory.Exists(source) || !Directory.EnumerateFileSystemEntries(source).Any())
        {
            if (Directory.Exists(source))
            {
                Directory.Delete(source);
            }

            return;
        }

        var safeReason = string.Concat(reason.Select(character => char.IsLetterOrDigit(character) ? character : '-'));
        var timestamp = EnsureUtc(utcNow()).ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        var generation = !string.IsNullOrWhiteSpace(generationId)
            ? generationId
            : string.IsNullOrWhiteSpace(current?.GenerationId)
                ? "unknown"
                : current.GenerationId;
        var archivesDirectory = Path.Combine(slotDirectory, "archives");
        Directory.CreateDirectory(archivesDirectory);
        var destination = Path.Combine(archivesDirectory, $"{timestamp}-{generation}-{safeReason}");
        Directory.Move(source, destination);
        foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        }

        diagnostic($"Archived generation {generation} as {reason}.");
    }

    private string GetSlotDirectory(int slot) => Path.Combine(dataRoot, "profiles", $"slot-{slot:D2}");

    private static string GetProfilePath(string directory) => Path.Combine(directory, "profile.json");

    private static string GetSessionPath(string directory) => Path.Combine(directory, "session.json");

    private static bool IdentityMatches(
        SaveIdentitySnapshot stored,
        SaveIdentitySnapshot observed,
        long activationCount)
    {
        if (stored == null || stored.Slot != observed.Slot)
        {
            return false;
        }

        if (stored.SaveFilePresent != observed.SaveFilePresent)
        {
            return false;
        }

        if (stored.SaveFilePresent)
        {
            return stored.SaveFileCreationUtcTicks.HasValue
                   && observed.SaveFileCreationUtcTicks.HasValue
                   && stored.SaveFileCreationUtcTicks.Value == observed.SaveFileCreationUtcTicks.Value;
        }

        // Two missing files only identify the same harmless zero profile. Any
        // accumulated data with no stable save identity is archived instead.
        return activationCount == 0;
    }

    private static CapabilityRecord CloneCapability(CapabilityRecord source) => new()
    {
        AdapterId = source.AdapterId,
        State = source.State,
        Version = source.Version,
        Detail = source.Detail
    };

    private static bool CapabilitiesEqual(
        IReadOnlyList<CapabilityRecord>? stored,
        IReadOnlyList<CapabilityRecord> configured)
    {
        if (stored == null || stored.Count != configured.Count)
        {
            return false;
        }

        for (var index = 0; index < stored.Count; index++)
        {
            var left = stored[index];
            var right = configured[index];
            if (!string.Equals(left.AdapterId, right.AdapterId, StringComparison.Ordinal)
                || left.State != right.State
                || !string.Equals(left.Version, right.Version, StringComparison.Ordinal)
                || !string.Equals(left.Detail, right.Detail, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateIdentity(SaveIdentitySnapshot identity)
    {
        if (identity == null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        if (identity.Slot < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(identity), "Duckov save slots start at one.");
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
