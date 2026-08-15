using System.Globalization;
using UltimateDuckovStatistics.Core.Compatibility;
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

    public bool InterruptedRunRecovered { get; internal set; }

    public bool UnsupportedSchemaArchived { get; internal set; }

    public IReadOnlyList<string> LoadFailures { get; internal set; } = Array.Empty<string>();
}

public sealed class ProfilePersistenceSnapshot
{
    internal ProfilePersistenceSnapshot(string path, ProfileDocument document)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    internal string Path { get; }

    internal ProfileDocument Document { get; }

    public string GenerationId => Document.GenerationId;

    public long Revision => Document.Revision;
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
    private readonly AtomicJsonStore<ActiveRunCheckpoint> activeRunStore = new();
    private readonly List<CapabilityRecord> configuredCapabilities = new();
    private ProfileDocument? current;
    private string? currentDirectory;
    private bool capabilitiesConfigured;
    private bool deferredItemPersistenceEnabled;

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
        var loaded = profileStore.Load(profilePath, ProfileMigrator.ValidateRecoveryCandidate);
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
                current = candidate;
                if (!IdentityMatches(candidate, identity))
                {
                    result.InterruptedRunRecovered = RecoverInterruptedRun();
                    result.InterruptedSessionRecovered = RecoverInterruptedSession();
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
        result.InterruptedRunRecovered |= RecoverInterruptedRun();
        result.InterruptedSessionRecovered |= RecoverInterruptedSession();
        if (ProfileMigrator.CompactEconomyReplayEvidenceAfterRecovery(Current))
        {
            Current.Revision++;
            Current.UpdatedUtc = EnsureUtc(utcNow());
            SaveCurrent();
        }
        if (deferredItemPersistenceEnabled)
        {
            EnsureDeferredItemPersistenceEnabled();
        }
        StartSession();
        return result;
    }

    public bool Record(ItemUseRecorded itemUse)
    {
        return Record(itemUse, persistImmediately: true);
    }

    public bool RecordDeferred(ItemUseRecorded itemUse)
    {
        return Record(itemUse, persistImmediately: false);
    }

    public bool Record(HealingApplied healing)
    {
        return Record(healing, persistImmediately: true);
    }

    public bool RecordDeferred(HealingApplied healing)
    {
        return Record(healing, persistImmediately: false);
    }

    public bool Record(CurrencyFlowRecorded flow) => Record(flow, persistImmediately: true);

    public bool RecordDeferred(CurrencyFlowRecorded flow) => Record(flow, persistImmediately: false);

    public bool BeginEconomyActivation(string activationId)
    {
        var profile = Current;
        if (!EconomyStatisticsReducer.BeginReplayActivation(profile.Statistics.Economy, activationId))
            return false;
        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        return true;
    }

    public bool CanDeferItemPersistence(string? runId)
    {
        return CanDefer(Current, runId);
    }

    public void EnableDeferredItemPersistence()
    {
        if (deferredItemPersistenceEnabled)
        {
            return;
        }

        deferredItemPersistenceEnabled = true;
        EnsureDeferredItemPersistenceEnabled();
    }

    private void EnsureDeferredItemPersistenceEnabled()
    {
        var profile = Current;
        if (profile.DeferredItemPersistence != null
            && string.IsNullOrWhiteSpace(profile.DeferredItemPersistence.RunId))
        {
            return;
        }

        profile.DeferredItemPersistence = new DeferredItemPersistenceState();
        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        SaveCurrent();
    }

    public ProfilePersistenceSnapshot CapturePersistenceSnapshot()
    {
        if (current == null || currentDirectory == null)
        {
            throw new InvalidOperationException("No current profile can be captured.");
        }

        EnsureSchemaCanBeSaved(current);
        var document = CloneForDeferredPersistence(current);
        document.SchemaVersion = ProductInfo.SchemaVersion;
        document.Statistics.SchemaVersion = ProductInfo.SchemaVersion;
        document.Statistics.SaveGenerationId = document.GenerationId;
        return new ProfilePersistenceSnapshot(GetProfilePath(currentDirectory), document);
    }

    public void SaveSnapshot(ProfilePersistenceSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        EnsureSchemaCanBeSaved(snapshot.Document);
        var validationFailure = ProfileMigrator.ValidateRecoveryCandidate(snapshot.Document);
        if (!string.IsNullOrWhiteSpace(validationFailure))
        {
            throw new InvalidOperationException(
                $"Deferred profile snapshot failed semantic validation: {validationFailure}");
        }
        profileStore.Save(snapshot.Path, snapshot.Document);
    }

    private bool Record(ItemUseRecorded itemUse, bool persistImmediately)
    {
        var profile = Current;
        var defer = !persistImmediately && CanDefer(profile, itemUse.RunId);
        var deferredChanged = defer ? RecordDeferredWatermark(profile, itemUse) : (bool?)null;
        var changed = ItemUseReducer.Apply(profile.Statistics, itemUse);
        if (deferredChanged.HasValue && deferredChanged.Value != changed)
        {
            throw new InvalidOperationException("Deferred item-use watermark diverged from lifetime statistics.");
        }
        if (!changed)
        {
            return false;
        }

        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        if (!defer)
        {
            SaveCurrent();
        }

        return true;
    }

    private bool Record(HealingApplied healing, bool persistImmediately)
    {
        var profile = Current;
        var defer = !persistImmediately && CanDefer(profile, healing.RunId);
        var deferredChanged = defer ? RecordDeferredWatermark(profile, healing) : (bool?)null;
        var changed = HealingReducer.Apply(profile.Statistics, healing);
        if (deferredChanged.HasValue && deferredChanged.Value != changed)
        {
            throw new InvalidOperationException("Deferred healing watermark diverged from lifetime statistics.");
        }
        if (!changed)
        {
            return false;
        }

        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        if (!defer)
        {
            SaveCurrent();
        }

        return true;
    }

    private bool Record(CurrencyFlowRecorded flow, bool persistImmediately)
    {
        var profile = Current;
        var deferPersistence = !persistImmediately;
        var retainRunRecoveryWatermark = deferPersistence && CanDefer(profile, flow.RunId);
        var changed = EconomyStatisticsReducer.Record(
            profile.Statistics.Economy,
            profile.GenerationId,
            flow,
            out var capabilityChanged);
        if (!changed)
        {
            if (!capabilityChanged) return false;
            profile.Revision++;
            profile.UpdatedUtc = EnsureUtc(utcNow());
            if (!deferPersistence) SaveCurrent();
            return true;
        }
        if (retainRunRecoveryWatermark && !RecordDeferredWatermark(profile, flow))
            throw new InvalidOperationException("Deferred economy watermark rejected a lifetime economy mutation.");
        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        if (!deferPersistence) SaveCurrent();
        return true;
    }

    public void SaveActiveRun(ActiveRunCheckpoint checkpoint)
    {
        if (checkpoint == null)
        {
            throw new ArgumentNullException(nameof(checkpoint));
        }

        if (currentDirectory == null)
        {
            throw new InvalidOperationException("No profile generation is open.");
        }

        if (checkpoint.SchemaVersion > ProductInfo.SchemaVersion
            || string.IsNullOrWhiteSpace(checkpoint.RunId)
            || !string.Equals(checkpoint.SaveGenerationId, Current.GenerationId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Active-run checkpoint does not match the current generation.", nameof(checkpoint));
        }

        ValidateAndNormalizeRouteCheckpoint(checkpoint, requireCurrentSchemaRoots: checkpoint.SchemaVersion >= 8);

        checkpoint.WeaponStatistics ??= new WeaponStatisticsAggregate();
        var normalization = WeaponStatisticsReducer.NormalizePersisted(checkpoint.WeaponStatistics);
        if (normalization.InvalidCounters)
        {
            throw new ArgumentException("Active-run checkpoint contains invalid weapon counters.", nameof(checkpoint));
        }

        WeaponStatisticsReducer.ValidateAggregate(checkpoint.WeaponStatistics);
        try
        {
            CombatStatisticsReducer.ValidateRecoveryCandidate(checkpoint.CombatStatistics);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"Active-run checkpoint contains invalid combat state: {exception.Message}",
                nameof(checkpoint),
                exception);
        }

        checkpoint.CombatStatistics ??= new CombatStatisticsAggregate();
        CombatStatisticsReducer.NormalizePersisted(checkpoint.CombatStatistics);
        CombatStatisticsReducer.ValidateAggregate(checkpoint.CombatStatistics);
        try
        {
            EquipmentStatisticsReducer.ValidateRecoveryCandidate(checkpoint.EquipmentStatistics, checkpoint.SchemaVersion);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"Active-run checkpoint contains invalid equipment state: {exception.Message}",
                nameof(checkpoint),
                exception);
        }
        checkpoint.EquipmentStatistics ??= new EquipmentStatisticsAggregate();
        EquipmentStatisticsReducer.NormalizePersisted(checkpoint.EquipmentStatistics);
        EquipmentStatisticsReducer.ValidateAggregate(checkpoint.EquipmentStatistics);
        try
        {
            ContainerStatisticsReducer.ValidateRecoveryCandidate(
                checkpoint.ContainerState,
                ProductInfo.SchemaVersion);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"Active-run checkpoint contains invalid container state: {exception.Message}",
                nameof(checkpoint),
                exception);
        }
        checkpoint.ContainerState ??= new ContainerRunCheckpointState();
        ContainerStatisticsReducer.NormalizeCheckpoint(checkpoint.ContainerState);
        ContainerStatisticsReducer.ValidateAggregate(checkpoint.ContainerState.Statistics);
        if (checkpoint.SchemaVersion >= 9 && checkpoint.Economy == null)
            throw new ArgumentException("Current-schema active-run economy root is missing.", nameof(checkpoint));
        checkpoint.Economy ??= new EconomyStatisticsAggregate
        {
            HistoricalUnavailable = true,
            Capabilities = HistoricalEconomyCapabilities("Historical active-run checkpoint predates M9; economy was not recorded.")
        };
        EconomyStatisticsReducer.ValidateRecoveryCandidate(checkpoint.Economy);
        EconomyStatisticsReducer.NormalizePersisted(checkpoint.Economy);
        EconomyStatisticsReducer.Validate(checkpoint.Economy);
        checkpoint.SchemaVersion = ProductInfo.SchemaVersion;
        activeRunStore.Save(GetActiveRunPath(currentDirectory), checkpoint);
    }

    public bool CompleteRun(RunSummary summary)
    {
        var profile = Current;
        var applied = RunReducer.Apply(profile.Statistics, summary);
        if (applied) EconomyStatisticsReducer.MergeTerminalOutcomes(profile.Statistics.Economy, summary.Economy);
        var clearedDeferredWatermark = ClearDeferredWatermark(profile, summary.RunId);
        if (applied || clearedDeferredWatermark)
        {
            profile.Revision++;
            profile.UpdatedUtc = EnsureUtc(summary.EndedUtc);
            SaveCurrent();
        }

        if (currentDirectory != null)
        {
            activeRunStore.Delete(GetActiveRunPath(currentDirectory));
        }

        return applied;
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

    public void SetEconomyCapabilities(EconomyMetricCapabilities capabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        var profile = Current;
        profile.Statistics.Economy ??= new EconomyStatisticsAggregate();
        EconomyStatisticsReducer.SetCapabilities(profile.Statistics.Economy, capabilities);
        profile.Revision++;
        profile.UpdatedUtc = EnsureUtc(utcNow());
        SaveCurrent();
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
                : new List<CapabilityRecord>(),
            DeferredItemPersistence = deferredItemPersistenceEnabled
                ? new DeferredItemPersistenceState()
                : null
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

    private bool RecoverInterruptedRun()
    {
        if (currentDirectory == null)
        {
            return false;
        }

        var path = GetActiveRunPath(currentDirectory);
        var artifactsExist = File.Exists(path)
                             || File.Exists(AtomicJsonPaths.GetBackupPath(path))
                             || File.Exists(AtomicJsonPaths.GetTemporaryPath(path));
        if (!artifactsExist)
        {
            return false;
        }

        var loaded = activeRunStore.Load(path, ValidateActiveRunCheckpointForRecovery);
        var checkpoint = loaded.Value;
        if (checkpoint == null)
        {
            ArchiveActiveRunArtifacts("UnrecoverableActiveRun");
            diagnostic($"Active-run checkpoint was unreadable and was preserved for diagnostics: {string.Join(" | ", loaded.Failures)}");
            return false;
        }

        if (loaded.Recovered)
        {
            diagnostic(
                $"Rejected an earlier active-run candidate and recovered the semantically valid {loaded.Source} snapshot: "
                + string.Join(" | ", loaded.Failures));
        }

        var summary = checkpoint.ToInterruptedSummary();
        var alreadyFinalized = Current.Statistics.Runs.Any(
            run => string.Equals(run.RunId, summary.RunId, StringComparison.Ordinal));
        var recoveredLifetimeItems = !alreadyFinalized && RecoverDeferredLifetimeItems(checkpoint);
        var recoveredLifetimeEconomy = !alreadyFinalized && RecoverDeferredLifetimeEconomy(checkpoint);
        var applied = RunReducer.Apply(Current.Statistics, summary);
        if (applied) EconomyStatisticsReducer.MergeTerminalOutcomes(Current.Statistics.Economy, summary.Economy);
        var clearedDeferredWatermark = ClearDeferredWatermark(Current, summary.RunId);

        if (applied || recoveredLifetimeItems || recoveredLifetimeEconomy || clearedDeferredWatermark)
        {
            Current.Revision++;
            Current.UpdatedUtc = summary.EndedUtc;
            SaveCurrent();
        }

        activeRunStore.Delete(path);
        diagnostic(
            applied
                ? $"Recovered interrupted run {summary.RunId} for generation {summary.SaveGenerationId}."
                : $"Cleared already-finalized active-run checkpoint {summary.RunId} without duplicating it.");
        return applied;
    }

    private string? ValidateActiveRunCheckpointForRecovery(ActiveRunCheckpoint checkpoint)
    {
        try
        {
            ValidateAndNormalizeRouteCheckpoint(checkpoint, requireCurrentSchemaRoots: checkpoint.SchemaVersion >= 8);
        }
        catch (ArgumentException exception)
        {
            return $"Active-run checkpoint contains invalid route state: {exception.Message}";
        }

        checkpoint.WeaponStatistics ??= new WeaponStatisticsAggregate();
        var weaponNormalization = WeaponStatisticsReducer.NormalizePersisted(checkpoint.WeaponStatistics);
        if (weaponNormalization.InvalidCounters)
        {
            return "Active-run checkpoint contains negative weapon counters.";
        }

        try
        {
            CombatStatisticsReducer.ValidateRecoveryCandidate(checkpoint.CombatStatistics);
        }
        catch (ArgumentException exception)
        {
            return $"Active-run checkpoint contains invalid combat state: {exception.Message}";
        }

        checkpoint.CombatStatistics ??= new CombatStatisticsAggregate();
        CombatStatisticsReducer.NormalizePersisted(checkpoint.CombatStatistics);
        if (checkpoint.SchemaVersion < 5)
        {
            checkpoint.CombatStatistics.Capabilities = CombatNativeContractPolicy.CreateUnavailableCapabilities(
                "Historical active-run checkpoint predates M5; combat attribution was not recorded.");
        }

        try
        {
            EquipmentStatisticsReducer.ValidateRecoveryCandidate(checkpoint.EquipmentStatistics, checkpoint.SchemaVersion);
        }
        catch (ArgumentException exception)
        {
            return $"Active-run checkpoint contains invalid equipment state: {exception.Message}";
        }
        checkpoint.EquipmentStatistics ??= new EquipmentStatisticsAggregate();
        EquipmentStatisticsReducer.NormalizePersisted(checkpoint.EquipmentStatistics);
        if (checkpoint.SchemaVersion < 6)
        {
            checkpoint.EquipmentStatistics.Capabilities = EquipmentNativeContractPolicy.CreateUnavailableCapabilities(
                "Historical active-run checkpoint predates M6; equipment and totem state was not recorded.");
            checkpoint.EquipmentStatistics.HistoricalUnavailable = true;
        }

        try
        {
            ContainerStatisticsReducer.ValidateRecoveryCandidate(
                checkpoint.ContainerState,
                checkpoint.SchemaVersion);
        }
        catch (ArgumentException exception)
        {
            return $"Active-run checkpoint contains invalid container state: {exception.Message}";
        }
        if (checkpoint.ContainerState == null)
        {
            checkpoint.ContainerState = new ContainerRunCheckpointState
            {
                Statistics = new ContainerStatisticsAggregate
                {
                    Capabilities = ContainerNativeContractPolicy.Unavailable(
                        "Historical active-run checkpoint predates M7; successful unique-container access was not recorded."),
                    HistoricalUnavailable = true
                }
            };
        }
        try
        {
            ContainerStatisticsReducer.NormalizeCheckpoint(checkpoint.ContainerState);
        }
        catch (ArgumentException exception)
        {
            return $"Active-run checkpoint contains invalid container state: {exception.Message}";
        }
        if (checkpoint.SchemaVersion < 7)
        {
            checkpoint.ContainerState.Statistics.Capabilities = ContainerNativeContractPolicy.Unavailable(
                "Historical active-run checkpoint predates M7; successful unique-container access was not recorded.");
            checkpoint.ContainerState.Statistics.HistoricalUnavailable = true;
        }

        if (checkpoint.SchemaVersion >= 9 && checkpoint.Economy == null)
            return "Current-schema active-run economy root is missing.";
        checkpoint.Economy ??= new EconomyStatisticsAggregate
        {
            HistoricalUnavailable = true,
            Capabilities = HistoricalEconomyCapabilities("Historical active-run checkpoint predates M9; economy was not recorded.")
        };
        try
        {
            EconomyStatisticsReducer.ValidateRecoveryCandidate(checkpoint.Economy);
        }
        catch (ArgumentException exception)
        {
            return $"Active-run checkpoint contains invalid economy state: {exception.Message}";
        }
        EconomyStatisticsReducer.NormalizePersisted(checkpoint.Economy);

        if (checkpoint.SchemaVersion > ProductInfo.SchemaVersion)
        {
            return $"Active-run checkpoint schema {checkpoint.SchemaVersion} is newer than supported schema {ProductInfo.SchemaVersion}.";
        }

        if (string.IsNullOrWhiteSpace(checkpoint.RunId))
        {
            return "Active-run checkpoint has no run identity.";
        }

        if (!string.Equals(checkpoint.SaveGenerationId, Current.GenerationId, StringComparison.Ordinal))
        {
            return "Active-run checkpoint does not match the current save generation.";
        }

        try
        {
            WeaponStatisticsReducer.ValidateAggregate(checkpoint.WeaponStatistics);
            CombatStatisticsReducer.ValidateAggregate(checkpoint.CombatStatistics);
            EquipmentStatisticsReducer.ValidateAggregate(checkpoint.EquipmentStatistics);
            ContainerStatisticsReducer.ValidateAggregate(checkpoint.ContainerState.Statistics);
            EconomyStatisticsReducer.Validate(checkpoint.Economy);
            RunReducer.Validate(checkpoint.ToInterruptedSummary());
            return null;
        }
        catch (ArgumentException exception)
        {
            return $"Active-run checkpoint is structurally invalid: {exception.Message}";
        }
    }

    private static void ValidateAndNormalizeRouteCheckpoint(
        ActiveRunCheckpoint checkpoint,
        bool requireCurrentSchemaRoots)
    {
        if (requireCurrentSchemaRoots
            && (checkpoint.RouteCapabilities == null
                || checkpoint.Segments == null
                || checkpoint.SegmentEventAssociations == null
                || checkpoint.ItemStatistics == null
                || checkpoint.MovementBaseline == null
                || checkpoint.RouteCapabilities.OrderedRoute == null
                || checkpoint.RouteCapabilities.Segments == null
                || checkpoint.RouteCapabilities.EventAttribution == null
                || checkpoint.RouteCapabilities.RouteAwareMapTotals == null
                || string.IsNullOrWhiteSpace(checkpoint.StartingMapId)))
            throw new ArgumentException("Current-schema route roots are incomplete.", nameof(checkpoint));

        if (checkpoint.SchemaVersion < 8)
        {
            checkpoint.StartingMapId = checkpoint.MapId;
            checkpoint.StartingMapDisplayName = checkpoint.MapDisplayName;
            checkpoint.StartingMapKnown = checkpoint.MapKnown;
            checkpoint.Segments = new List<MapSegmentSummary>();
            checkpoint.SegmentEventAssociations = new List<SegmentEventAssociation>();
            checkpoint.RouteCapabilities = RouteStatisticsReducer.Unavailable(
                "Historical active-run checkpoint predates M8; route recovery is unavailable.");
            checkpoint.HistoricalRouteUnavailable = true;
            checkpoint.ItemStatistics = new ItemStatisticsAggregate { HistoricalUnavailable = true };
            checkpoint.TransitionExcludedDistance = 0;
            checkpoint.TransitionPending = false;
            checkpoint.CurrentSegmentId = null;
            checkpoint.MovementBaseline ??= new MovementBaselineState();
            return;
        }

        checkpoint.Segments ??= new List<MapSegmentSummary>();
        checkpoint.SegmentEventAssociations ??= new List<SegmentEventAssociation>();
        checkpoint.RouteCapabilities ??= RouteStatisticsReducer.Unavailable("Route capability record was missing.");
        checkpoint.ItemStatistics ??= new ItemStatisticsAggregate();
        checkpoint.MovementBaseline ??= new MovementBaselineState();
        RouteStatisticsReducer.ValidateCapabilities(checkpoint.RouteCapabilities);
        if (requireCurrentSchemaRoots)
        {
            ItemStatisticsAggregateReducer.Validate(checkpoint.ItemStatistics);
            if (!ItemStatisticsAggregateReducer.IsCompositionConsistent(checkpoint.ItemStatistics))
            {
                throw new ArgumentException(
                    "Current-schema run item statistics are compositionally inconsistent.",
                    nameof(checkpoint));
            }
        }
        if (checkpoint.Segments.Count > RouteStatisticsReducer.MaximumSegmentsPerRun
            || checkpoint.SegmentEventAssociations.Count > RouteStatisticsReducer.MaximumEventAssociationsPerRun)
            throw new ArgumentException("Current-schema route state exceeds its defensive bound.", nameof(checkpoint));

        var segmentsSupported = checkpoint.RouteCapabilities.Segments?.State == AdapterCapabilityState.Supported;
        var hasRetainedSegments = checkpoint.Segments.Count > 0;
        if (segmentsSupported && !hasRetainedSegments)
            throw new ArgumentException("Supported route checkpoint has no segment.", nameof(checkpoint));
        if (hasRetainedSegments)
        {
            RouteStatisticsReducer.Validate(checkpoint.Segments, allowOpenLast: true);
            if (!checkpoint.HistoricalRouteUnavailable
                && !string.Equals(checkpoint.StartingMapId, checkpoint.Segments[0].MapId, StringComparison.Ordinal))
                throw new ArgumentException("Active route checkpoint starting map does not match its first segment.", nameof(checkpoint));
        }
        RouteStatisticsReducer.ValidateAssociations(checkpoint.Segments, checkpoint.SegmentEventAssociations);
        if (segmentsSupported)
        {
            var lastSegment = checkpoint.Segments[^1];
            if (checkpoint.TransitionPending)
            {
                if (!string.IsNullOrWhiteSpace(checkpoint.CurrentSegmentId)
                    || lastSegment.ExitReason != MapSegmentExitReason.Transition
                    || !lastSegment.ExitedUtc.HasValue)
                    throw new ArgumentException("Pending transition checkpoint has inconsistent current-segment state.", nameof(checkpoint));
            }
            else if (lastSegment.ExitReason != MapSegmentExitReason.None
                     || lastSegment.ExitedUtc.HasValue
                     || !string.Equals(checkpoint.CurrentSegmentId, lastSegment.SegmentId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Active route checkpoint does not identify its open final segment.", nameof(checkpoint));
            }
        }
        else if (!string.IsNullOrWhiteSpace(checkpoint.CurrentSegmentId)
                 || checkpoint.Segments.Any(segment => segment.ExitReason == MapSegmentExitReason.None || !segment.ExitedUtc.HasValue))
        {
            throw new ArgumentException("Unavailable route checkpoint retained an active segment pointer or open segment.", nameof(checkpoint));
        }
        RouteStatisticsReducer.NormalizePersisted(checkpoint.Segments);
        ItemStatisticsAggregateReducer.NormalizePersisted(checkpoint.ItemStatistics);
        ItemStatisticsAggregateReducer.Validate(checkpoint.ItemStatistics);
        if (checkpoint.MovementBaseline.HasBaseline
            && (!Finite(checkpoint.MovementBaseline.X)
                || !Finite(checkpoint.MovementBaseline.Y)
                || !Finite(checkpoint.MovementBaseline.Z)
                || !Finite(checkpoint.MovementBaseline.MonotonicSeconds)
                || checkpoint.MovementBaseline.MonotonicSeconds < 0))
            throw new ArgumentException("Movement baseline is invalid.", nameof(checkpoint));
        if (!string.IsNullOrWhiteSpace(checkpoint.CurrentSegmentId)
            && !checkpoint.Segments.Any(segment => string.Equals(segment.SegmentId, checkpoint.CurrentSegmentId, StringComparison.Ordinal)))
            throw new ArgumentException("Current segment identity is not present in the ordered route.", nameof(checkpoint));
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private void ArchiveActiveRunArtifacts(string reason)
    {
        if (currentDirectory == null)
        {
            return;
        }

        var path = GetActiveRunPath(currentDirectory);
        var paths = new[]
        {
            path,
            AtomicJsonPaths.GetBackupPath(path),
            AtomicJsonPaths.GetTemporaryPath(path)
        };
        var recoveryDirectory = Path.Combine(currentDirectory, "checkpoint-recovery");
        Directory.CreateDirectory(recoveryDirectory);
        var timestamp = EnsureUtc(utcNow()).ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        foreach (var source in paths.Where(File.Exists))
        {
            var destination = Path.Combine(
                recoveryDirectory,
                $"{timestamp}-{reason}-{Path.GetFileName(source)}");
            File.Move(source, destination);
            File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
        }
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

    private static ProfileDocument CloneForDeferredPersistence(ProfileDocument source)
    {
        var statistics = source.Statistics;
        return new ProfileDocument
        {
            SchemaVersion = source.SchemaVersion,
            GenerationId = source.GenerationId,
            Slot = source.Slot,
            GenerationReason = source.GenerationReason,
            CreatedUtc = source.CreatedUtc,
            UpdatedUtc = source.UpdatedUtc,
            Revision = source.Revision,
            InterruptedSessionCount = source.InterruptedSessionCount,
            Identity = CloneIdentity(source.Identity),
            Statistics = new ProfileStatistics
            {
                SchemaVersion = statistics.SchemaVersion,
                SaveGenerationId = statistics.SaveGenerationId,
                CreatedUtc = statistics.CreatedUtc,
                UpdatedUtc = statistics.UpdatedUtc,
                Overall = CloneTotals(statistics.Overall),
                Items = statistics.Items.ToDictionary(
                    entry => entry.Key,
                    entry => new ItemAggregate
                    {
                        ItemId = entry.Value.ItemId,
                        DisplayName = entry.Value.DisplayName,
                        Group = entry.Value.Group,
                        EffectTags = new List<ItemEffectTag>(entry.Value.EffectTags),
                        Totals = CloneTotals(entry.Value.Totals)
                    },
                    StringComparer.Ordinal),
                Groups = statistics.Groups.ToDictionary(
                    entry => entry.Key,
                    entry => CloneTotals(entry.Value),
                    StringComparer.Ordinal),
                RecentEventIds = new List<string>(statistics.RecentEventIds),
                // Completed summaries and their aggregates are immutable during an active run.
                // Lifecycle boundaries drain the deferred writer before they can append or merge.
                Runs = new List<RunSummary>(statistics.Runs),
                RunTotals = statistics.RunTotals,
                RunRecords = statistics.RunRecords,
                Economy = EconomyStatisticsReducer.Clone(statistics.Economy)
            },
            Capabilities = source.Capabilities.Select(CloneCapability).ToList(),
            PendingSave = source.PendingSave == null
                ? null
                : new PendingSaveObservation
                {
                    ContentSha256BeforeSave = source.PendingSave.ContentSha256BeforeSave,
                    SaveTimeBinaryBeforeSave = source.PendingSave.SaveTimeBinaryBeforeSave,
                    CollectedUtc = source.PendingSave.CollectedUtc
                },
            DeferredItemPersistence = source.DeferredItemPersistence == null
                ? null
                : new DeferredItemPersistenceState
                {
                    RunId = source.DeferredItemPersistence.RunId,
                    AppliedLifetimeStatistics = ItemStatisticsAggregateReducer.Clone(
                        source.DeferredItemPersistence.AppliedLifetimeStatistics),
                    AppliedLifetimeEconomy = EconomyStatisticsReducer.Clone(
                        source.DeferredItemPersistence.AppliedLifetimeEconomy)
                }
        };
    }

    private static bool RecordDeferredWatermark(ProfileDocument profile, ItemUseRecorded itemUse)
    {
        var state = GetDeferredWatermark(profile, itemUse.RunId);
        return ItemStatisticsAggregateReducer.Record(
            state.AppliedLifetimeStatistics,
            profile.GenerationId,
            itemUse);
    }

    private static bool CanDefer(ProfileDocument profile, string? runId)
    {
        if (profile.DeferredItemPersistence == null)
        {
            throw new InvalidOperationException("Deferred item persistence must be enabled before recording events.");
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            return false;
        }

        if (string.Equals(profile.DeferredItemPersistence.RunId, runId, StringComparison.Ordinal))
        {
            return true;
        }

        return !profile.Statistics.Runs.Any(run => string.Equals(run.RunId, runId, StringComparison.Ordinal));
    }

    private static bool RecordDeferredWatermark(ProfileDocument profile, HealingApplied healing)
    {
        var state = GetDeferredWatermark(profile, healing.RunId);
        return ItemStatisticsAggregateReducer.Record(
            state.AppliedLifetimeStatistics,
            profile.GenerationId,
            healing);
    }

    private static bool RecordDeferredWatermark(ProfileDocument profile, CurrencyFlowRecorded flow)
    {
        var state = GetDeferredWatermark(profile, flow.RunId);
        return EconomyStatisticsReducer.Record(state.AppliedLifetimeEconomy, profile.GenerationId, flow);
    }

    private static DeferredItemPersistenceState GetDeferredWatermark(ProfileDocument profile, string? runId)
    {
        if (profile.DeferredItemPersistence == null)
        {
            throw new InvalidOperationException("Deferred item persistence must be enabled before recording events.");
        }
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("An active run identity is required for deferred item persistence.");
        }

        var state = profile.DeferredItemPersistence;
        if (string.IsNullOrWhiteSpace(state.RunId))
        {
            state.RunId = runId;
            state.AppliedLifetimeStatistics = new ItemStatisticsAggregate();
            state.AppliedLifetimeEconomy = new EconomyStatisticsAggregate();
        }
        else if (!string.Equals(state.RunId, runId, StringComparison.Ordinal))
        {
            if (!profile.Statistics.Runs.Any(run => string.Equals(run.RunId, state.RunId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Deferred item persistence cannot span two active runs.");
            }

            state.RunId = runId;
            state.AppliedLifetimeStatistics = new ItemStatisticsAggregate();
            state.AppliedLifetimeEconomy = new EconomyStatisticsAggregate();
        }

        return state;
    }

    private bool RecoverDeferredLifetimeItems(ActiveRunCheckpoint checkpoint)
    {
        var state = Current.DeferredItemPersistence;
        if (state == null)
        {
            // Profiles written by builds with immediate item persistence have no
            // watermark and must retain their historical recovery behavior.
            return false;
        }

        ItemStatisticsAggregate baseline;
        if (string.IsNullOrWhiteSpace(state.RunId))
        {
            baseline = new ItemStatisticsAggregate();
        }
        else if (string.Equals(state.RunId, checkpoint.RunId, StringComparison.Ordinal))
        {
            baseline = state.AppliedLifetimeStatistics;
        }
        else
        {
            diagnostic(
                $"Deferred lifetime watermark for run {state.RunId} did not match active checkpoint {checkpoint.RunId}; "
                + "lifetime recovery was left unchanged to avoid double counting.");
            return false;
        }

        if (!ItemStatisticsAggregateReducer.TrySubtract(checkpoint.ItemStatistics, baseline, out var difference))
        {
            diagnostic(
                $"Deferred lifetime watermark for run {checkpoint.RunId} was not a subset of the active checkpoint; "
                + "lifetime recovery was left unchanged to avoid double counting.");
            return false;
        }

        if (difference.Overall.ActivationCount == 0
            && difference.Overall.ActualHealthRestored <= 0
            && difference.Overall.AmountsByUnit.Values.All(amount => amount <= 0))
        {
            return false;
        }

        ItemStatisticsAggregateReducer.ApplyRecoveryDelta(Current.Statistics, difference);
        diagnostic($"Recovered deferred lifetime item statistics from active run {checkpoint.RunId}.");
        return true;
    }

    private static bool ClearDeferredWatermark(ProfileDocument profile, string runId)
    {
        var state = profile.DeferredItemPersistence;
        if (state == null || !string.Equals(state.RunId, runId, StringComparison.Ordinal))
        {
            return false;
        }

        state.RunId = null;
        state.AppliedLifetimeStatistics = new ItemStatisticsAggregate();
        state.AppliedLifetimeEconomy = new EconomyStatisticsAggregate();
        return true;
    }

    private bool RecoverDeferredLifetimeEconomy(ActiveRunCheckpoint checkpoint)
    {
        var state = Current.DeferredItemPersistence;
        if (state == null) return false;
        EconomyStatisticsAggregate baseline;
        if (string.IsNullOrWhiteSpace(state.RunId)) baseline = new EconomyStatisticsAggregate();
        else if (string.Equals(state.RunId, checkpoint.RunId, StringComparison.Ordinal))
            baseline = state.AppliedLifetimeEconomy;
        else return false;
        if (!EconomyStatisticsReducer.TrySubtract(checkpoint.Economy, baseline, out var difference))
        {
            diagnostic($"Deferred economy watermark for run {checkpoint.RunId} was not a subset of the active checkpoint; lifetime recovery was left unchanged.");
            return false;
        }
        if (EconomyStatisticsReducer.IsEmpty(difference)) return false;
        var lifetime = Current.Statistics.Economy;
        var skippedCurrencies = new List<string>();
        var recoverableCurrency = false;
        if (difference.Currencies.ContainsKey(CurrencyKind.Money.ToString()))
        {
            if (lifetime.MoneyArithmeticSaturated) skippedCurrencies.Add(CurrencyKind.Money.ToString());
            else recoverableCurrency = true;
        }
        if (difference.Currencies.ContainsKey(CurrencyKind.Cash.ToString())
            || difference.CashRaidOutcomes.Acquired > 0
            || difference.CashRaidOutcomes.Secured > 0
            || difference.CashRaidOutcomes.Lost > 0
            || difference.CashRaidOutcomes.Unresolved > 0)
        {
            if (lifetime.CashArithmeticSaturated) skippedCurrencies.Add(CurrencyKind.Cash.ToString());
            else recoverableCurrency = true;
        }
        if (!recoverableCurrency)
        {
            diagnostic($"Deferred lifetime economy recovery for run {checkpoint.RunId} contained only arithmetic-saturated currencies; lifetime totals were left unchanged.");
            return false;
        }
        EconomyStatisticsReducer.Merge(lifetime, difference);
        diagnostic(skippedCurrencies.Count == 0
            ? $"Recovered deferred lifetime economy from active run {checkpoint.RunId}."
            : $"Recovered the representable deferred lifetime economy from active run {checkpoint.RunId}; arithmetic-saturated {string.Join(" and ", skippedCurrencies)} totals were left unchanged.");
        return true;
    }

    private static SaveIdentitySnapshot CloneIdentity(SaveIdentitySnapshot source) => new()
    {
        Slot = source.Slot,
        SaveFilePresent = source.SaveFilePresent,
        SaveFileCreationUtcTicks = source.SaveFileCreationUtcTicks,
        ObservedWriteUtcTicks = source.ObservedWriteUtcTicks,
        ObservedLength = source.ObservedLength,
        GameVersion = source.GameVersion,
        ContentSha256 = source.ContentSha256,
        SaveTimeBinary = source.SaveTimeBinary
    };

    private static AggregateTotals CloneTotals(AggregateTotals source) => new()
    {
        ActivationCount = source.ActivationCount,
        AmountsByUnit = new Dictionary<string, double>(source.AmountsByUnit, StringComparer.Ordinal),
        ActualHealthRestored = source.ActualHealthRestored
    };

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

    private static string GetActiveRunPath(string directory) => Path.Combine(directory, "active-run.json");

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
            return HasNoStatistics(candidate.Statistics)
                   && stored.SaveFileCreationUtcTicks == observed.SaveFileCreationUtcTicks
                   && stored.ObservedWriteUtcTicks == observed.ObservedWriteUtcTicks
                   && stored.ObservedLength == observed.ObservedLength;
        }

        // Two missing files only identify the same harmless zero profile. Any
        // accumulated data with no stable save identity is archived instead.
        return HasNoStatistics(candidate.Statistics);
    }

    private static bool HasNoStatistics(ProfileStatistics statistics) =>
        statistics.Overall.ActivationCount == 0
        && statistics.Overall.ActualHealthRestored == 0
        && statistics.RunTotals.TotalRuns == 0
        && statistics.RunTotals.WeaponStatistics.Totals.FiringActions == 0
        && statistics.RunTotals.WeaponStatistics.Totals.AmmunitionUnitsConsumed == 0
        && statistics.RunTotals.WeaponStatistics.Totals.Projectiles == 0
        && statistics.RunTotals.CombatStatistics.Totals.DamageCaused == 0
        && statistics.RunTotals.CombatStatistics.Totals.DamageDealt == 0
        && statistics.RunTotals.CombatStatistics.Totals.DamageReceived == 0
        && statistics.RunTotals.CombatStatistics.Totals.CompletedPlayerProjectiles == 0
        && statistics.RunTotals.CombatStatistics.Totals.MeleeSwings == 0
        && statistics.RunTotals.CombatStatistics.Totals.EnemiesKilled == 0
        && statistics.RunTotals.CombatStatistics.Totals.PlayerDeaths == 0
        && EquipmentStatisticsReducer.IsEmpty(statistics.RunTotals.EquipmentStatistics)
        && ContainerStatisticsReducer.IsEmpty(statistics.RunTotals.ContainerStatistics)
        && EconomyStatisticsReducer.IsEmpty(statistics.Economy)
        && EconomyStatisticsReducer.IsEmpty(statistics.RunTotals.Economy)
        && statistics.Runs.Count == 0;

    private static EconomyMetricCapabilities HistoricalEconomyCapabilities(string provenance) => new()
    {
        MoneyAmountDirection = Unavailable(provenance),
        MoneySourceAttribution = Unavailable(provenance),
        MoneyContextAttribution = Unavailable(provenance),
        CashAmountDirection = Unavailable(provenance),
        CashExternalAcquisition = Unavailable(provenance),
        CashContextAttribution = Unavailable(provenance),
        CashTerminalOutcomes = Unavailable(provenance),
        RouteAttribution = Unavailable(provenance)
    };

    private static MetricAvailability Unavailable(string provenance) => new()
    { State = AdapterCapabilityState.DisabledIncompatible, Provenance = provenance };

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
