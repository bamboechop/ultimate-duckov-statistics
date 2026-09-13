using System.Globalization;
using System.Diagnostics.CodeAnalysis;

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
    private void RotateUserProfile(SaveIdentitySnapshot identity)
    {
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
        var next = CreateNewProfile(identity, "UserReset");
        var suffix = Guid.NewGuid().ToString("N");
        var preparedDirectory = Path.Combine(slotDirectory, ".uds-reset-" + suffix);
        var archivesDirectory = Path.Combine(slotDirectory, "archives");
        var safeGeneration = string.Concat(previous.GenerationId.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var timestamp = EnsureUtc(utcNow()).ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture);
        var archiveDirectory = Path.Combine(archivesDirectory, $"{timestamp}-{safeGeneration}-UserReset-{suffix}");
        var attributes = new Dictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase);
        var movedPrevious = false;
        var promoted = false;
        var storageSuspended = false;
        try
        {
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
        CloseIncrementalStorage();
        current = next;
        if (UsesIncrementalStorage) EnsureIncrementalStorage();
        completionPersistencePendingRunId = null;
        LastOpenResult = new ProfileOpenResult { CreatedNew = true, RotatedGeneration = true, LoadSource = AtomicJsonLoadSource.Missing };
        System.Threading.Volatile.Write(ref lastSaveReceipt,
            new ProfileSaveReceipt(next.GenerationId, next.Revision, EnsureUtc(utcNow())));
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
