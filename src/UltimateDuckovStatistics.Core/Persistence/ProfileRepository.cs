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

    public bool UnsupportedSchemaArchived { get; internal set; }

    public IReadOnlyList<string> LoadFailures { get; internal set; } = Array.Empty<string>();
}

public sealed class ProfileRepository
{
    private static readonly TimeSpan NativeSaveIntentWindow = TimeSpan.FromSeconds(30);
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
            if (current.Slot == identity.Slot)
            {
                CloseSessionForSameSlotReopen();
            }
            else
            {
                CloseClean();
            }
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
            result.RecoveredSnapshot = loaded.Recovered;
            var candidate = loaded.Value;
            try
            {
                result.MigratedSchema = ProfileMigrator.Migrate(candidate);
            }
            catch (NotSupportedException exception)
            {
                ArchiveCurrentDirectory(slotDirectory, "UnsupportedProfileSchema", candidate.GenerationId);
                currentDirectory = Path.Combine(slotDirectory, "current");
                Directory.CreateDirectory(currentDirectory);
                current = CreateNewProfile(identity, "UnsupportedProfileSchema");
                result.CreatedNew = true;
                result.RotatedGeneration = true;
                result.UnsupportedSchemaArchived = true;
                SaveCurrent();
                diagnostic(exception.Message);
            }

            if (!result.UnsupportedSchemaArchived)
            {
                if (!IdentityMatches(candidate, identity))
                {
                    ArchiveCurrentDirectory(slotDirectory, "SaveIdentityChanged", candidate.GenerationId);
                    currentDirectory = Path.Combine(slotDirectory, "current");
                    Directory.CreateDirectory(currentDirectory);
                    current = CreateNewProfile(identity, "SaveIdentityChanged");
                    result.CreatedNew = true;
                    result.RotatedGeneration = true;
                    SaveCurrent();
                }
                else
                {
                    var identityChanged = !IdentitiesEqual(candidate.Identity, identity);
                    var pendingSaveCleared = candidate.PendingSave != null;
                    current = candidate;
                    current.Identity = identity;
                    current.PendingSave = null;
                    if (loaded.Recovered || result.MigratedSchema || identityChanged || pendingSaveCleared)
                    {
                        SaveCurrent();
                    }
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

    public bool Record(HealingApplied healing)
    {
        var profile = Current;
        if (!HealingReducer.Apply(profile.Statistics, healing))
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

        if (identity.SaveFilePresent
            && string.IsNullOrWhiteSpace(identity.ContentSha256)
            && !string.IsNullOrWhiteSpace(profile.Identity.ContentSha256))
        {
            diagnostic("Save identity refresh skipped because a stable content fingerprint was unavailable.");
            return;
        }

        if (IdentitiesEqual(profile.Identity, identity) && profile.PendingSave == null)
        {
            return;
        }

        profile.Identity = identity;
        profile.PendingSave = null;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        profile.Revision++;
        SaveCurrent();
    }

    public void PrepareForNativeSave(SaveIdentitySnapshot identity)
    {
        ValidateIdentity(identity);
        var profile = Current;
        if (profile.Slot != identity.Slot)
        {
            throw new InvalidOperationException("Cannot prepare a different slot's save identity.");
        }

        if (!identity.SaveFilePresent
            || string.IsNullOrWhiteSpace(identity.ContentSha256)
            || !identity.SaveTimeBinary.HasValue)
        {
            diagnostic("Native save intent was not recorded because stable save identity metadata was unavailable.");
            return;
        }

        profile.Identity = identity;
        profile.PendingSave = new PendingSaveObservation
        {
            ContentSha256BeforeSave = identity.ContentSha256,
            SaveTimeBinaryBeforeSave = identity.SaveTimeBinary,
            CollectedUtc = EnsureUtc(utcNow())
        };
        profile.UpdatedUtc = profile.PendingSave.CollectedUtc;
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

        current.PendingSave = null;
        SaveCurrent();
        sessionStore.Delete(GetSessionPath(currentDirectory));
        diagnostic($"Closed generation {current.GenerationId} cleanly.");
        current = null;
        currentDirectory = null;
    }

    private void CloseSessionForSameSlotReopen()
    {
        if (current == null || currentDirectory == null)
        {
            return;
        }

        // OnSetFile can select the already-open slot immediately after
        // OnCollectSaveData. Keep that pre-save observation persisted until
        // Open compares the newly observed identity, but retire the prior UDS
        // session so the re-selection is never reported as an interruption.
        SaveCurrent();
        sessionStore.Delete(GetSessionPath(currentDirectory));
        diagnostic($"Closed generation {current.GenerationId} for same-slot re-selection.");
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

        EnsureSchemaCanBeSaved(current);
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
        var safeGeneration = string.Concat(generation.Select(character => char.IsLetterOrDigit(character) ? character : '-'));
        if (string.IsNullOrWhiteSpace(safeGeneration))
        {
            safeGeneration = "unknown";
        }
        var archivesDirectory = Path.Combine(slotDirectory, "archives");
        Directory.CreateDirectory(archivesDirectory);
        var destination = Path.Combine(archivesDirectory, $"{timestamp}-{safeGeneration}-{safeReason}");
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

    private static bool IdentityMatches(ProfileDocument candidate, SaveIdentitySnapshot observed)
    {
        var stored = candidate.Identity;
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
            if (string.IsNullOrWhiteSpace(observed.ContentSha256))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(stored.ContentSha256))
            {
                if (string.Equals(stored.ContentSha256, observed.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return ExpectedNativeSaveMatches(candidate.PendingSave, stored, observed);
            }

            // Profiles written before content fingerprints were introduced
            // cannot prove continuity once they contain statistics. A zero
            // profile is harmless to adopt only when every legacy metadata
            // observation still agrees; Open then persists the fingerprint.
            return candidate.Statistics.Overall.ActivationCount == 0
                   && stored.SaveFileCreationUtcTicks == observed.SaveFileCreationUtcTicks
                   && stored.ObservedWriteUtcTicks == observed.ObservedWriteUtcTicks
                   && stored.ObservedLength == observed.ObservedLength;
        }

        // Two missing files only identify the same harmless zero profile. Any
        // accumulated data with no stable save identity is archived instead.
        return candidate.Statistics.Overall.ActivationCount == 0;
    }

    private static bool IdentitiesEqual(SaveIdentitySnapshot left, SaveIdentitySnapshot right) =>
        left.Slot == right.Slot
        && left.SaveFilePresent == right.SaveFilePresent
        && left.SaveFileCreationUtcTicks == right.SaveFileCreationUtcTicks
        && left.ObservedWriteUtcTicks == right.ObservedWriteUtcTicks
        && left.ObservedLength == right.ObservedLength
        && string.Equals(left.GameVersion, right.GameVersion, StringComparison.Ordinal)
        && string.Equals(left.ContentSha256, right.ContentSha256, StringComparison.OrdinalIgnoreCase)
        && left.SaveTimeBinary == right.SaveTimeBinary;

    private static bool ExpectedNativeSaveMatches(
        PendingSaveObservation? pending,
        SaveIdentitySnapshot stored,
        SaveIdentitySnapshot observed)
    {
        if (pending == null
            || !stored.SaveTimeBinary.HasValue
            || !observed.SaveTimeBinary.HasValue
            || !string.Equals(
                pending.ContentSha256BeforeSave,
                stored.ContentSha256,
                StringComparison.OrdinalIgnoreCase)
            || pending.SaveTimeBinaryBeforeSave != stored.SaveTimeBinary)
        {
            return false;
        }

        try
        {
            var storedSaveTime = DateTime.FromBinary(stored.SaveTimeBinary.Value).ToUniversalTime();
            var observedSaveTime = DateTime.FromBinary(observed.SaveTimeBinary.Value).ToUniversalTime();
            var collectedUtc = EnsureUtc(pending.CollectedUtc);
            return observedSaveTime > storedSaveTime
                   && observedSaveTime >= collectedUtc
                   && observedSaveTime <= collectedUtc + NativeSaveIntentWindow;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void EnsureSchemaCanBeSaved(ProfileDocument profile)
    {
        if (profile.SchemaVersion > ProductInfo.SchemaVersion)
        {
            throw new NotSupportedException(
                $"Profile schema {profile.SchemaVersion} is newer than supported schema {ProductInfo.SchemaVersion} and will not be saved.");
        }

        if (profile.Statistics == null)
        {
            throw new InvalidOperationException("Profile statistics must be normalized before saving.");
        }

        if (profile.Statistics.SchemaVersion > ProductInfo.SchemaVersion)
        {
            throw new NotSupportedException(
                $"Statistics schema {profile.Statistics.SchemaVersion} is newer than supported schema {ProductInfo.SchemaVersion} and will not be saved.");
        }
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

        if (identity.ContentSha256 != null
            && (identity.ContentSha256.Length != 64 || identity.ContentSha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ArgumentException("Save content SHA-256 must contain exactly 64 hexadecimal characters.", nameof(identity));
        }

        if (identity.SaveTimeBinary.HasValue)
        {
            try
            {
                _ = DateTime.FromBinary(identity.SaveTimeBinary.Value);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException("Save time metadata is invalid.", nameof(identity), exception);
            }
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
