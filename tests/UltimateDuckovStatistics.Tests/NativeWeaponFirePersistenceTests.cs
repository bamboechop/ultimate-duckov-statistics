using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativeWeaponFirePersistenceTests : IDisposable
{
    public NativeWeaponFirePersistenceTests() => ResetNativeState();

    [Theory]
    [InlineData("weapon")]
    [InlineData("ammunition")]
    [Trait("Category", "M14")]
    [Trait("Category", "Recovery")]
    public void CurrentSchemaOrphanPairPrimaryLosesToProductionGeneratedBackup(string missingMarginal)
    {
        using var directory = new TemporaryDirectory();
        var now = DateTime.UtcNow;
        var ids = new Queue<string>(["generation", "session-one"]);
        var identity = Identity();
        var repository = new ProfileRepository(directory.Path, () => now, () => ids.Dequeue());
        repository.Open(identity);
        var tracker = Start(repository.Current.GenerationId, now);
        var main = new CharacterMainControl { IsMainCharacter = true };
        CharacterMainControl.Main = main;
        using (var adapter = new NativeWeaponFireAdapter(
                   () => repository.Current.GenerationId,
                   () => tracker.ActiveRunId,
                   () => tracker.ActiveMapId,
                   tracker.RecordShot,
                   _ => { },
                   _ => { },
                   segmentIdProvider: () => tracker.ActiveSegmentId))
        {
            adapter.Initialize();
            ItemAgent_Gun.RaiseMainCharacterShoot(new ItemAgent_Gun
            {
                Holder = main,
                Item = new ItemStatsSystem.Item { TypeID = 970, DisplayName = "Production weapon" },
                GunItemSetting = new ItemSetting_Gun
                {
                    TargetBulletID = 971,
                    CurrentBulletName = "Production ammunition"
                }
            });

            var run = tracker.Apply(new RunLifecycleEvent
            {
                Kind = RunLifecycleEventKind.Extracted,
                TimestampUtc = now.AddSeconds(2),
                MonotonicSeconds = 2,
                NativeRaidId = "raid"
            }).Completed!;
            Assert.True(repository.CompleteRun(run));
        }
        repository.CloseClean();

        var profilePath = Path.Combine(
            directory.Path,
            "profiles",
            "slot-01",
            "current",
            "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var validPrimary = Assert.IsType<ProfileDocument>(store.Load(profilePath).Value);
        var validStatistics = validPrimary.Statistics.RunTotals.WeaponStatistics;
        Assert.Equal(1, Assert.Single(validStatistics.WeaponAmmunitionPairs).Value.FiringActions);
        Assert.Single(validStatistics.Weapons);
        Assert.Single(validStatistics.AmmunitionTypes);

        if (missingMarginal == "weapon") validStatistics.Weapons.Clear();
        else validStatistics.AmmunitionTypes.Clear();
        store.Save(profilePath, validPrimary);

        var diagnostics = new List<string>();
        var recovered = new ProfileRepository(
            directory.Path,
            () => now.AddMinutes(1),
            () => "session-two",
            diagnostics.Add);
        var result = recovered.Open(identity);

        Assert.True(result.RecoveredSnapshot);
        Assert.Contains(result.LoadFailures, value =>
            value.StartsWith("Primary: SemanticValidation:", StringComparison.Ordinal)
            && value.Contains("invalid M14 association state", StringComparison.Ordinal));
        var recoveredStatistics = recovered.Current.Statistics.RunTotals.WeaponStatistics;
        Assert.Equal(1, Assert.Single(recoveredStatistics.WeaponAmmunitionPairs).Value.FiringActions);
        Assert.Single(recoveredStatistics.Weapons);
        Assert.Single(recoveredStatistics.AmmunitionTypes);
        recovered.CloseClean();
    }

    public void Dispose() => ResetNativeState();

    private static void ResetNativeState()
    {
        ItemAgent_Gun.ResetNativeState();
        CharacterMainControl.ResetNativeState();
        LevelManager.ResetNativeState();
        GameManager.Paused = false;
        MultiSceneCore.Instance = null;
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
        NativeRaidContext.GameplayContext = GameplayContext.Raid;
        UnityEngine.Application.version = "2.3.30";
    }

    private static RunLifecycleTracker Start(string generationId, DateTime now)
    {
        var tracker = new RunLifecycleTracker(() => "run-orphan-pair");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = now,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = now,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = generationId,
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "24013657",
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                RouteCapabilities = RouteStatisticsReducer.Supported("test"),
                WeaponCapabilities = WeaponNativeContractPolicy.CreateMetricCapabilities()
            }
        });
        return tracker;
    }

    private static SaveIdentitySnapshot Identity() => new()
    {
        Slot = 1,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = 100,
        ObservedWriteUtcTicks = 110,
        ObservedLength = 4096,
        GameVersion = "2.3.30",
        ContentSha256 = new string('a', 64),
        SaveTimeBinary = DateTime.UnixEpoch.AddTicks(100).ToBinary()
    };
}
