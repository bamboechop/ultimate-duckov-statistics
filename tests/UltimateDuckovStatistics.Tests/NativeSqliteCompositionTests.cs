using Duckov.Scenes;
using Duckov.UI;
using Saves;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Sqlite;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeSqliteCompositionTests : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly string previousPath = Application.persistentDataPath;
    private readonly int previousSlot = SavesSystem.CurrentSlot;
    private readonly NativeProfileCoordinator coordinator;
    private double clock;
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;

    public NativeSqliteCompositionTests()
    {
        Application.persistentDataPath = directory.Path;
        SavesSystem.ResetNativeState(); SceneLoader.ResetNativeState(); SleepView.OnAfterSleep = null;
        for (var slot = 1; slot <= 2; slot++)
        {
            var path = Path.Combine(directory.Path, SavesSystem.GetFilePath(slot));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"SaveTime\":{\"value\":" + Now.AddDays(-slot).ToBinary() + "}}");
        }
        coordinator = new NativeProfileCoordinator(() => clock);
        coordinator.Initialize();
    }

    [Fact]
    public void ActualFactoryCommitsNativePublicationsAndLineageToOneDatabase()
    {
        Assert.EndsWith("profile.sqlite", coordinator.CurrentProfilePath, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(coordinator.CurrentProfilePath)!, "profile.json")));
        coordinator.SetBaseMovementBoundaryPublisher(() => PublishMeters(7));
        coordinator.SetCraftingBoundaryBarrier(() =>
        {
            coordinator.SetCraftingCapabilities([], CraftingNativeContractPolicy.Supported("completion", "formula"));
            return coordinator.HandleCrafting(new(coordinator.CurrentGenerationId, Now,
                [new("100", "Bandage", "recipe", 1, 2, new() { ["2"] = 1 })]));
        });
        SavesSystem.RaiseCollectSaveData();
        using (var db = new SqliteStore(coordinator.CurrentProfilePath, readOnly: true))
        {
            var metadata = ProfileRecordCodec.Decode<ProfileMetadataRecord>(SqliteProfileStorage.ReadRootPayload(db, 1)!);
            var distance = ProfileRecordCodec.Decode<BaseMovementStatistics>(db.Blob("SELECT payload FROM records WHERE kind=3")!);
            Assert.Equal(7, distance.RecordedMeters);
            Assert.NotNull(metadata.PendingSave);
            Assert.Equal(metadata.Identity.ContentSha256, metadata.PendingSave.ContentSha256BeforeSave);
            Assert.Equal(coordinator.Current!.Revision, metadata.Revision);
            Assert.Equal(1, db.ScalarLong("SELECT count(*) FROM receipt"));
            Assert.Equal("ok", db.ScalarText("PRAGMA integrity_check"));
        }
        Assert.Equal(2, coordinator.Current!.Statistics.Crafting.ProducedQuantity);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(coordinator.CurrentProfilePath)!, "*.recovery*"));
        Assert.False(File.Exists(coordinator.CurrentProfilePath + ".pair-owner"));
    }

    [Fact]
    public void PrimaryBusyKeepsAcceptedMovementForRetryWithoutDoubleCounting()
    {
        coordinator.SetBaseMovementBoundaryPublisher(() => PublishMeters(9));
        var previousReceipt = coordinator.Current!.Revision;
        using (var lockOwner = new SqliteStore(coordinator.CurrentProfilePath))
        {
            lockOwner.Exec("BEGIN IMMEDIATE");
            SavesSystem.RaiseCollectSaveData();
            Assert.Equal(9, coordinator.Current!.Statistics.BaseMovement!.RecordedMeters);
            Assert.Equal(previousReceipt, lockOwner.ScalarLong("SELECT revision FROM profile_state"));
            lockOwner.Exec("ROLLBACK");
        }
        clock += 5;
        SavesSystem.RaiseCollectSaveData();
        using var primary = new SqliteStore(coordinator.CurrentProfilePath, readOnly: true);
        Assert.Equal(coordinator.Current!.Revision, primary.ScalarLong("SELECT revision FROM profile_state"));
        Assert.Equal(9, ProfileRecordCodec.Decode<BaseMovementStatistics>(primary.Blob("SELECT payload FROM records WHERE kind=3")!).RecordedMeters);
    }

    [Fact]
    public void ProfileSwitchDrainsPriorGenerationAndMaintenanceHintsDoNotChangeMetrics()
    {
        Assert.True(PublishMeters(4));
        SavesSystem.RaiseCollectSaveData();
        var generation = coordinator.CurrentGenerationId;
        SceneLoader.RaiseBeforeActive();
        SleepView.OnAfterSleep?.Invoke();
        coordinator.TickProfilePersistence();
        SavesSystem.SetFile(2);
        Assert.False(coordinator.HasPendingProfileTransition);
        Assert.NotEqual(generation, coordinator.CurrentGenerationId);
        Assert.Equal(0, coordinator.Current!.Statistics.BaseMovement?.RecordedMeters ?? 0);
        SavesSystem.SetFile(1);
        Assert.Equal(generation, coordinator.CurrentGenerationId);
        Assert.Equal(4, coordinator.Current!.Statistics.BaseMovement!.RecordedMeters);
    }

    private bool PublishMeters(double meters) => coordinator.HandleBaseMovement(new()
    { GenerationId = coordinator.CurrentGenerationId, CaptureId = "base", CapturedMeters = meters, CollectionStartedUtc = Now });

    public void Dispose()
    {
        coordinator.SetActiveRunCheckpointBarrier(() => true); coordinator.Dispose();
        SavesSystem.ResetNativeState(); SceneLoader.ResetNativeState(); SleepView.OnAfterSleep = null;
        Application.persistentDataPath = previousPath; SavesSystem.CurrentSlot = previousSlot;
        directory.Dispose();
    }
}
