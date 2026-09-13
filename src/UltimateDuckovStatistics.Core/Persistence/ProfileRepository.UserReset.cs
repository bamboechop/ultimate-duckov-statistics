using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;

namespace UltimateDuckovStatistics.Core.Persistence;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "The reset service must supply a preserved-generation identity and cause; general constructors cannot establish that contract.")]
public sealed class UserProfileResetFailedException : IOException
{
    public UserProfileResetFailedException(string generationId, Exception cause)
        : base("The UDS reset failed before commit; the original generation remains active.", cause)
    {
        PreservedGenerationId = generationId;
    }

    public string PreservedGenerationId { get; }
}

public sealed partial class ProfileRepository
{
    private UserResetRollback? pendingUserResetRollback;
    private UserResetActivation? pendingUserResetActivation;

    public void RefreshIdentityForUserReset(SaveIdentitySnapshot identity)
    {
        try { RefreshIdentity(identity); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UserProfileResetFailedException(CurrentGenerationId, exception);
        }
    }

    // Prepare both new-generation files before moving the active directory. A failed
    // preparation leaves the existing profile and session active; a failed promotion
    // restores that exact directory and its original file attributes.
    public void RestoreStatistics(SaveIdentitySnapshot identity, StatisticsRestorePreview preview, string expectedGeneration)
    {
        if (preview == null) throw new ArgumentNullException(nameof(preview));
        ValidateIdentity(identity);
        if (preview.Slot != identity.Slot)
            throw new InvalidOperationException("The restore destination changed.");
        RotateUserProfile(identity, preview, expectedGeneration);
    }

    private void RotateUserProfile(SaveIdentitySnapshot identity, StatisticsRestorePreview? restore = null,
        string? expectedGeneration = null)
    {
        if (pendingUserResetActivation is { } activation)
        {
            // Promotion already committed this request. Retry only activation;
            // another preview, slot or expected generation cannot take it over.
            if (identity.Slot != activation.Profile.Slot || !ReferenceEquals(restore, activation.Restore)
                || (restore != null && expectedGeneration != activation.PreviousGenerationId)
                || (CurrentGenerationId != activation.PreviousGenerationId && !ReferenceEquals(current, activation.Profile)))
                throw new InvalidOperationException("A different profile replacement is awaiting activation.");
            ActivateUserProfile(activation);
            return;
        }
        if (restore != null && CurrentGenerationId != expectedGeneration)
            throw new InvalidOperationException("The restore destination changed.");
        var previous = Current;
        if (pendingUserResetRollback != null)
        {
            // A failed rollback is still the same transaction. Never start a
            // second rotation or create an empty active directory over its data.
            var rollback = pendingUserResetRollback;
            rollback.Restore();
            pendingUserResetRollback = null;
            throw new UserProfileResetFailedException(previous.GenerationId, rollback.Cause);
        }
        if (identity.Slot != previous.Slot)
            throw new UserProfileResetFailedException(previous.GenerationId,
                new InvalidOperationException("A UDS reset cannot rotate another save slot."));
        var activeDirectory = currentDirectory!;
        var slotDirectory = GetSlotDirectory(identity.Slot);
        var reason = restore == null ? "UserReset" : "UserRestore";
        var next = CreateNewProfile(identity, reason);
        var replacement = new UserResetActivation(previous.GenerationId, next, restore);
        var suffix = Guid.NewGuid().ToString("N");
        var preparedDirectory = Path.Combine(slotDirectory, ".uds-reset-" + suffix);
        var archivesDirectory = Path.Combine(slotDirectory, "archives");
        var safeGeneration = string.Concat(previous.GenerationId.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var timestamp = EnsureUtc(utcNow()).ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        var archiveDirectory = Path.Combine(archivesDirectory, $"{timestamp}-{safeGeneration}-{reason}-{suffix}");
        var attributes = new Dictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase);
        var movedPrevious = false;
        var promoted = false;
        var storageSuspended = false;
        try
        {
            if (restore != null) next.Statistics = restore.CreateStatistics(next.GenerationId, next.UpdatedUtc);
            SaveCurrent();
            Directory.CreateDirectory(preparedDirectory);
            PrepareResetStorage(preparedDirectory, next, new SessionCheckpoint
            {
                SessionId = idFactory(),
                GenerationId = next.GenerationId,
                StartedUtc = EnsureUtc(utcNow()),
                ProfileRevisionAtStart = next.Revision
            });
            Directory.CreateDirectory(archivesDirectory);
            storageSuspended = true;
            SuspendIncrementalStorageForReset();
            foreach (var file in Directory.EnumerateFiles(activeDirectory, "*", SearchOption.AllDirectories))
                attributes.Add(file.Substring(activeDirectory.Length + 1), File.GetAttributes(file));
            Directory.Move(activeDirectory, archiveDirectory);
            movedPrevious = true;
            foreach (var entry in attributes)
                File.SetAttributes(Path.Combine(archiveDirectory, entry.Key), entry.Value | FileAttributes.ReadOnly);
            Directory.Move(preparedDirectory, activeDirectory);
            promoted = true;
            pendingUserResetActivation = replacement;
        }
        catch (Exception exception)
        {
            if (storageSuspended && !promoted)
            {
                var rollback = new UserResetRollback(activeDirectory, archiveDirectory, attributes, movedPrevious,
                    () => { if (UsesIncrementalStorage) EnsureIncrementalStorage(); }, exception);
                pendingUserResetRollback = rollback;
                rollback.Restore();
                pendingUserResetRollback = null;
            }
            throw new UserProfileResetFailedException(previous.GenerationId, exception);
        }
        finally
        {
            // Cleanup only this invocation's exact, generated staging path. A cleanup
            // failure cannot reverse an already committed reset or hide its outcome.
            if (Directory.Exists(preparedDirectory))
            {
                try { Directory.Delete(preparedDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        ActivateUserProfile(replacement);
    }

    private void ActivateUserProfile(UserResetActivation activation)
    {
        var next = activation.Profile;
        if (!ReferenceEquals(current, next))
        {
            CloseIncrementalStorage();
            current = next;
            // Publications accepted while reopening is deferred must retain
            // their journal; retries must not replace this owner or its changes.
            if (UsesIncrementalStorage) changes = new ProfileChangeJournal(next.GenerationId, recordCodec);
        }
        if (UsesIncrementalStorage) EnsureIncrementalStorage();
        completionPersistencePendingRunId = null;
        LastOpenResult = new ProfileOpenResult { CreatedNew = true, RotatedGeneration = true, LoadSource = AtomicJsonLoadSource.Missing };
        System.Threading.Volatile.Write(ref lastSaveReceipt,
            new ProfileSaveReceipt(next.GenerationId, next.Revision, EnsureUtc(utcNow())));
        pendingUserResetActivation = null;
    }

    private sealed class UserResetActivation
    {
        public UserResetActivation(string previousGenerationId, ProfileDocument profile, StatisticsRestorePreview? restore)
        {
            PreviousGenerationId = previousGenerationId;
            Profile = profile;
            Restore = restore;
        }

        public string PreviousGenerationId { get; }
        public ProfileDocument Profile { get; }
        public StatisticsRestorePreview? Restore { get; }
    }

    private sealed class UserResetRollback
    {
        private readonly string activeDirectory;
        private readonly string archiveDirectory;
        private readonly Dictionary<string, FileAttributes> attributes;
        private readonly Action restoreStorage;
        private bool directoryRestored;
        private bool restored;

        public UserResetRollback(string activeDirectory, string archiveDirectory,
            Dictionary<string, FileAttributes> attributes, bool movedPrevious, Action restoreStorage, Exception cause)
        {
            this.activeDirectory = activeDirectory;
            this.archiveDirectory = archiveDirectory;
            this.attributes = attributes;
            this.restoreStorage = restoreStorage;
            directoryRestored = !movedPrevious;
            Cause = cause;
        }

        public Exception Cause { get; }

        public void Restore()
        {
            if (restored) return;
            if (!directoryRestored)
            {
                foreach (var entry in attributes)
                    File.SetAttributes(Path.Combine(archiveDirectory, entry.Key), entry.Value);
                Directory.Move(archiveDirectory, activeDirectory);
                directoryRestored = true;
            }
            // A restored directory alone is not a resumed generation. Failure to
            // reopen its owner keeps this transaction pending at the same step.
            restoreStorage();
            restored = true;
        }
    }
}
