using Duckov.Scenes;
using Duckov.UI;
using Saves;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeSqliteFailureBoundaryTests : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly string previousPath = Application.persistentDataPath;
    private readonly int previousSlot = SavesSystem.CurrentSlot;
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    private readonly NativeProfileCoordinator coordinator;
    private readonly ProfileRepository repository;
    private int activeOpens;
    private bool failRestoration;
    private bool blockPromotion;
    private FileStream? preparedHandle;
    private double clock;
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;

    public NativeSqliteFailureBoundaryTests()
    {
        Application.persistentDataPath = directory.Path;
        SavesSystem.ResetNativeState(); SceneLoader.ResetNativeState(); SleepView.OnAfterSleep = null;
        var native = Path.Combine(directory.Path, SavesSystem.GetFilePath(1));
        Directory.CreateDirectory(Path.GetDirectoryName(native)!);
        File.WriteAllText(native, "{\"SaveTime\":{\"value\":" + Now.ToBinary() + "}}");
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        ProfileRepository? created = null;
        coordinator = new NativeProfileCoordinator(() => clock, (root, diagnostic) => created = new ProfileRepository(
            root, () => Now, () => Guid.NewGuid().ToString("N"), diagnostic, NativeProfileJsonWriter.Write,
            CreateStorage, codec));
        coordinator.Initialize();
        repository = created!;
    }

    [Fact]
    public void RejectedRenameRestoresOwnerBeforeHandoffAndLaterPublications()
    {
        coordinator.BeginEconomyActivation("same-activation");
        Assert.True(PublishMeters(4)); Assert.True(coordinator.FlushBaseMovement());
        var path = coordinator.CurrentProfilePath;
        var generation = coordinator.CurrentGenerationId;
        coordinator.ProfileChanged += () =>
        {
            coordinator.BeginEconomyActivation("same-activation");
            Assert.True(PublishMeters(9));
            PublishCraft();
        };
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Assert.False(coordinator.ResetCurrent());
        Assert.Equal(NativeUserResetOutcome.Failure, coordinator.LastUserResetAttempt!.Outcome);
        Assert.False(coordinator.HasPendingProfileTransition);
        Assert.Equal(generation, coordinator.CurrentGenerationId);
        Assert.True(PublishMeters(12));
        Assert.True(coordinator.FlushBaseMovement());
        AssertStored(path, 12, crafted: 2);
    }

    [Fact]
    public void SuccessfulResetAfterRestorationStartsANewJournalAndPreservesArchivedValues()
    {
        Assert.True(PublishMeters(4)); Assert.True(coordinator.FlushBaseMovement());
        var path = coordinator.CurrentProfilePath;
        var generation = coordinator.CurrentGenerationId;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Assert.False(coordinator.ResetCurrent());
        Assert.True(PublishMeters(9)); Assert.True(coordinator.FlushBaseMovement());
        Assert.True(coordinator.ResetCurrent());
        Assert.NotEqual(generation, coordinator.CurrentGenerationId);
        Assert.True(PublishMeters(3)); Assert.True(coordinator.FlushBaseMovement());
        AssertStored(path, 3);
        var archives = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)!)!, "archives");
        var archived = Assert.Single(Directory.EnumerateDirectories(archives));
        using var archiveCopy = new TemporaryDirectory();
        foreach (var file in Directory.EnumerateFiles(archived))
        {
            var copy = Path.Combine(archiveCopy.Path, Path.GetFileName(file));
            File.Copy(file, copy);
            File.SetAttributes(copy, FileAttributes.Normal);
        }
        AssertStored(Path.Combine(archiveCopy.Path, "profile.sqlite"), 9);
    }

    [Fact]
    public void FailedPromotionRestoresAttributesAndExistingHistoryReaders()
    {
        Complete("first"); Complete("second"); Complete("third");
        SavesSystem.SetFile(1);
        var history = Assert.IsAssignableFrom<IIndexedRunHistory>(coordinator.Current!.Statistics.Runs);
        var retained = history.GetById("second");
        var path = coordinator.CurrentProfilePath;
        var attributes = File.GetAttributes(path);
        blockPromotion = true;
        Assert.False(coordinator.ResetCurrent());
        Assert.Equal(NativeUserResetOutcome.Failure, coordinator.LastUserResetAttempt!.Outcome);
        Assert.False(coordinator.HasPendingProfileTransition);
        Assert.Equal(attributes, File.GetAttributes(path));
        Assert.Same(history, coordinator.Current!.Statistics.Runs);
        Assert.Same(retained, history.GetById("second"));
        Assert.Equal("first", history.GetById("first").RunId); // Cold read uses the reopened owner.
        Assert.True(PublishMeters(9)); Assert.True(coordinator.FlushBaseMovement());
        Complete("fourth");
        AssertStored(path, 9, runs: 4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOwnerRestorationRemainsPendingAndRetainsAcceptedPublications(bool promotionFailure)
    {
        Complete("first"); Complete("second");
        SavesSystem.SetFile(1);
        var history = Assert.IsAssignableFrom<IIndexedRunHistory>(coordinator.Current!.Statistics.Runs);
        Assert.True(PublishMeters(4)); Assert.True(coordinator.FlushBaseMovement());
        var path = coordinator.CurrentProfilePath;
        var generation = coordinator.CurrentGenerationId;
        var handoffs = 0;
        coordinator.ProfileChanged += () => { handoffs++; PublishCraft(); };
        failRestoration = true;
        blockPromotion = promotionFailure;
        using (var held = promotionFailure ? null : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Assert.False(coordinator.ResetCurrent());
        Assert.Equal(NativeUserResetOutcome.Pending, coordinator.LastUserResetAttempt!.Outcome);
        Assert.True(coordinator.HasPendingProfileTransition);
        Assert.Equal(0, handoffs);
        Assert.True(PublishMeters(9)); // Accepted while restoration awaits a retry.
        Assert.False(coordinator.FlushBaseMovement());
        AssertStored(path, 4, runs: 2);
        failRestoration = false;
        clock += 60;
        Assert.True(coordinator.RetryPendingProfileTransition());
        Assert.Equal(NativeUserResetOutcome.Failure, coordinator.LastUserResetAttempt!.Outcome);
        Assert.Equal(generation, coordinator.CurrentGenerationId);
        Assert.Equal(1, handoffs);
        Assert.Same(history, coordinator.Current!.Statistics.Runs);
        Assert.Equal("first", history.GetById("first").RunId);
        clock += 60;
        Assert.True(coordinator.FlushBaseMovement());
        coordinator.Flush();
        AssertStored(path, 9, crafted: 2, runs: 2);
        Assert.True(coordinator.RetryPendingProfileTransition());
        Assert.Equal(1, handoffs);
    }

    [Theory]
    [InlineData("checkpoint")]
    [InlineData("identity")]
    [InlineData("busy")]
    public void FailedFinalBoundaryReleasesLeaseAndPreservesUncleanSessionForImmediateReopen(string failure)
    {
        Assert.True(PublishMeters(4)); Assert.True(coordinator.FlushBaseMovement());
        var path = coordinator.CurrentProfilePath;
        var generation = coordinator.CurrentGenerationId;
        using (var db = new SqliteStore(path, true)) Assert.Equal(1, db.ScalarLong("SELECT session_present FROM profile_state"));
        SqliteStore? busy = null;
        try
        {
            if (failure == "checkpoint") coordinator.SetActiveRunCheckpointBarrier(() => false);
            else
            {
                if (failure == "identity")
                {
                    SavesSystem.RaiseCollectSaveData();
                    Assert.NotNull(coordinator.Current!.PendingSave);
                    // RefreshIdentity must retire this lineage in a durable
                    // write even when no deferred publication remains dirty.
                }
                else Assert.True(PublishMeters(9));
                busy = new SqliteStore(path); busy.Exec("BEGIN IMMEDIATE");
            }
            coordinator.Dispose();
        }
        finally { if (busy != null) { busy.Exec("ROLLBACK"); busy.Dispose(); } }
        using (var lease = new FileStream(path + ".owner", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        using (var db = new SqliteStore(path, true)) Assert.Equal(1, db.ScalarLong("SELECT session_present FROM profile_state"));
        using var reopened = new NativeProfileCoordinator(() => clock);
        reopened.Initialize();
        Assert.Equal(generation, reopened.CurrentGenerationId);
        var recoveredMeters = reopened.Current!.Statistics.BaseMovement!.RecordedMeters;
        Assert.True(recoveredMeters >= 4);
        Assert.True(reopened.HandleBaseMovement(new() { GenerationId = generation, CaptureId = "new-activation", CapturedMeters = 12, CollectionStartedUtc = Now }));
        Assert.True(reopened.FlushBaseMovement());
        AssertStored(path, recoveredMeters + 12);
    }

    private DisposeObservation CreateStorage(string path)
    {
        var prepared = Path.GetFileName(Path.GetDirectoryName(path)!).StartsWith(".uds-reset-", StringComparison.Ordinal);
        if (!prepared && ++activeOpens > 1 && failRestoration) throw new IOException("Owner cannot be reopened yet.");
        var inner = new SqliteProfileStorage(path, codec);
        return new DisposeObservation(inner, () =>
        {
            if (prepared && blockPromotion)
                preparedHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        });
    }

    private bool PublishMeters(double value) => coordinator.HandleBaseMovement(new()
    { GenerationId = coordinator.CurrentGenerationId, CaptureId = "base", CapturedMeters = value, CollectionStartedUtc = Now });

    private void PublishCraft()
    {
        coordinator.SetCraftingCapabilities([], CraftingNativeContractPolicy.Supported("completion", "formula"));
        Assert.True(coordinator.HandleCrafting(new(coordinator.CurrentGenerationId, Now,
            [new("100", "Bandage", "recipe", 1, 2, new() { ["2"] = 1 })])));
    }

    private void Complete(string id)
    {
        var tracker = IncrementalCheckpointProtocolTests.Started(coordinator.CurrentGenerationId, runId: id);
        var run = tracker.Apply(new RunLifecycleEvent
        { Kind = RunLifecycleEventKind.Extracted, TimestampUtc = Now, MonotonicSeconds = 10 }).Completed!;
        Assert.True(coordinator.HandleRunCompleted(run));
    }

    private static void AssertStored(string path, double meters, int crafted = 0, int runs = 0)
    {
        using var db = new SqliteStore(path, true);
        Assert.Equal(meters, ProfileRecordCodec.Decode<BaseMovementStatistics>(db.Blob("SELECT payload FROM records WHERE kind=3")!).RecordedMeters);
        Assert.Equal(runs, db.ScalarLong("SELECT count(*) FROM history_index"));
        Assert.Equal(crafted, db.ScalarLong("SELECT COALESCE(SUM(quantity),0) FROM crafting WHERE kind=?", (int)ProfileRecordKind.CraftingOutput));
        Assert.False(File.Exists(path + ".recovery"));
    }

    public void Dispose()
    {
        failRestoration = false; blockPromotion = false; preparedHandle?.Dispose();
        coordinator.SetActiveRunCheckpointBarrier(() => true); coordinator.Dispose();
        try { repository.Dispose(); } catch (IOException) { }
        SavesSystem.ResetNativeState(); SceneLoader.ResetNativeState(); SleepView.OnAfterSleep = null;
        Application.persistentDataPath = previousPath; SavesSystem.CurrentSlot = previousSlot;
        directory.Dispose();
    }

    private sealed class DisposeObservation(IIncrementalProfileStorage inner, Action released) : IIncrementalProfileStorage
    {
        public string Path => inner.Path;
        public IncrementalProfileState? Load() => inner.Load();
        public void Import(ProfileDocument profile, SessionCheckpoint? session, ActiveRunCheckpoint? checkpoint, bool sessionEvidencePresent = false) => inner.Import(profile, session, checkpoint, sessionEvidencePresent);
        public Task Commit(IncrementalProfileWrite write) => inner.Commit(write);
        public Task Drain() => inner.Drain();
        public void Dispose() { inner.Dispose(); released(); }
    }
}
