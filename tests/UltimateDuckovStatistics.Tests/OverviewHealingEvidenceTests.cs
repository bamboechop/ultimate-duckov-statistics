using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class OverviewHealingEvidenceTests
{
    [Theory]
    [InlineData(0, false, "0.00", "0")]
    [InlineData(12.5, false, "12.50", "12.5")]
    [InlineData(0, true, "Unavailable", "Unavailable")]
    [InlineData(12.5, true, "12.50 (Partial)", "12.5 (Partial)")]
    public void SupportedAndRepairedEvidenceAgreeAcrossBothViews(double value, bool repaired, string overviewExpected, string itemExpected)
    {
        var profile = new Core.Persistence.ProfileDocument { GenerationId = "g" };
        profile.Statistics.SaveGenerationId = "g";
        profile.Statistics.Overall.ActualHealthRestored = value;
        profile.Statistics.RunTotals.ItemStatistics.WasRepairedFromInvalidState = repaired;
        profile.Capabilities.Add(new() { AdapterId = NativeHealingAttributionAdapter.AdapterId, State = AdapterCapabilityState.Supported });
        var projection = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
        Assert.Equal(overviewExpected, ProfileSummaryPresentationFactory.Create(projection, UiText.Get)[(int)ProfileSummaryMetric.HealthRestored].Value);
        Assert.Equal(itemExpected, ItemUsePresentationFactory.Create(projection, "g")!.Health.Text);
    }

    [Theory]
    [InlineData(0, "Unavailable", "Unavailable", ItemUseEvidence.Unavailable)]
    [InlineData(12.5, "12.50 (Partial)", "12.5 (Partial)", ItemUseEvidence.Partial)]
    public void FailedNativeActivationPublishesUnavailableHealingToBothViews(double retained,
        string overviewExpected, string itemUseExpected, object expectedEvidence)
    {
        var originalPath = Application.persistentDataPath;
        var originalSlot = Saves.SavesSystem.CurrentSlot;
        using var directory = new TemporaryDirectory();
        HarmonyLib.Harmony.ClearAll();
        try
        {
            Application.persistentDataPath = directory.Path;
            Saves.SavesSystem.CurrentSlot = 1;
            var save = Path.Combine(directory.Path, Saves.SavesSystem.GetFilePath(1));
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllText(save, "{\"SaveTime\":{\"value\":1}}");
            using var coordinator = new NativeProfileCoordinator();
            coordinator.Initialize();
            // A foreign prefix makes real adapter activation fail before any capture is enabled.
            new HarmonyLib.Harmony("foreign-healing").Patch(typeof(Health).GetMethod(nameof(Health.AddHealth))!,
                prefix: new HarmonyLib.HarmonyMethod(typeof(OverviewHealingEvidenceTests).GetMethod(nameof(ForeignPrefix), BindingFlags.NonPublic | BindingFlags.Static)!), postfix: null, transpiler: null, finalizer: null);
            using var adapter = new NativeHealingAttributionAdapter(coordinator.HandleHealing, _ => { }, new NativeBuffApplicationObservationBoundary());
            adapter.CapabilityChanged += coordinator.SetHealingCapability;
            var failed = adapter.Initialize();
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, failed.State);
            Assert.Contains("unsafe pre-existing Harmony patch set", failed.Detail);
            var profile = coordinator.Current!;
            Assert.Equal(failed.Detail, Assert.Single(profile.Capabilities, cap => cap.AdapterId == NativeHealingAttributionAdapter.AdapterId).Detail);
            // Same-format retained observations; ordinary item-use data is independently available.
            ItemUseReducer.Apply(profile.Statistics, new ItemUseRecorded { EventId = "use", SaveGenerationId = profile.GenerationId,
                GameplayContext = GameplayContext.Raid, ItemId = "medkit", DisplayName = "Medkit", Group = CanonicalItemGroup.Healing,
                ActivationCount = 1, AmountConsumed = 1, ConsumptionUnit = ConsumptionUnit.Item });
            if (retained > 0) HealingReducer.Apply(profile.Statistics, new HealingApplied { EventId = "heal", ApplicationId = "application", SourceItemUseEventId = "use", SaveGenerationId = profile.GenerationId,
                GameplayContext = GameplayContext.Raid, ItemId = "medkit", DisplayName = "Medkit", Group = CanonicalItemGroup.Healing, ActualHealthRestored = retained });
            var projection = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
            var overview = ProfileSummaryPresentationFactory.Create(projection, UiText.Get);
            var itemUse = ItemUsePresentationFactory.Create(projection, profile.GenerationId)!;
            Assert.Equal(overviewExpected, overview[(int)ProfileSummaryMetric.HealthRestored].Value);
            Assert.Equal(itemUseExpected, itemUse.Health.Text);
            Assert.Equal((ItemUseEvidence)expectedEvidence, itemUse.Health.Evidence);
        }
        finally
        {
            HarmonyLib.Harmony.ClearAll();
            Application.persistentDataPath = originalPath;
            Saves.SavesSystem.CurrentSlot = originalSlot;
        }
    }


    [Theory]
    [InlineData(true, false, 0, "Unavailable", false)]
    [InlineData(false, true, 12.5, "12.5 (partial; recorded values only)", false)]
    [InlineData(false, true, 12.5, "12.5 (partial; recorded values only)", true)]
    [InlineData(false, true, 0, "Unavailable", true)]
    [InlineData(false, false, 0, "0", false)]
    [InlineData(false, false, 12.5, "12.5", false)]
    public void CompletedRunRetainsHealingCaptureEvidenceAfterReload(bool disabledAtStart, bool loseCapture, double restored, string expected, bool lockPersistence)
    {
        var originalPath = Application.persistentDataPath;
        var originalSlot = Saves.SavesSystem.CurrentSlot;
        using var directory = new TemporaryDirectory();
        HarmonyLib.Harmony.ClearAll();
        CharacterMainControl.ResetNativeState(); LevelManager.ResetNativeState(); RaidUtilities.ResetNativeState();
        try
        {
            Application.persistentDataPath = directory.Path; Application.version = "2.3.30";
            Saves.SavesSystem.CurrentSlot = 1;
            var save = Path.Combine(directory.Path, Saves.SavesSystem.GetFilePath(1));
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllText(save, "{\"SaveTime\":{\"value\":1}}");
            using var coordinator = new NativeProfileCoordinator(); coordinator.Initialize();
            InputManager.InputActived = true; GameManager.Paused = false;
            NativeRaidContext.GameplayContext = GameplayContext.Raid;
            Duckov.Scenes.SceneLoader.IsSceneLoading = false;
            var main = new CharacterMainControl { IsMainCharacter = true, CharacterItem = new ItemStatsSystem.Item() };
            CharacterMainControl.Main = main;
            LevelManager.Instance = new LevelManagerInstance { MainCharacter = main };
            RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };
            double now = 0;
            var messages = new List<string>(); Debug.ExceptionLogged = ex => messages.Add(ex.ToString());
            using var lifecycle = new NativeRunLifecycleAdapter(() => coordinator.CurrentGenerationId,
                coordinator.HandleRunCheckpoint, coordinator.HandleRunCompleted, coordinator.SetRunCapabilities, messages.Add,
                checkpointCompletionPoller: coordinator.PollRunCheckpoint, checkpointCompletionFlusher: coordinator.FlushRunCheckpoint,
                monotonicSecondsProvider: () => now);
            void Publish(Core.Persistence.CapabilityRecord cap) => NativeHealingCapabilityPublication.Publish(cap, lifecycle, coordinator);
            void ForeignPatch() => new HarmonyLib.Harmony("foreign-healing-run").Patch(typeof(Health).GetMethod(nameof(Health.AddHealth))!,
                prefix: new HarmonyLib.HarmonyMethod(typeof(OverviewHealingEvidenceTests).GetMethod(nameof(ForeignPrefix), BindingFlags.NonPublic | BindingFlags.Static)!), postfix: null, transpiler: null, finalizer: null);
            if (disabledAtStart) ForeignPatch();
            using var adapter = new NativeHealingAttributionAdapter(coordinator.HandleHealing, _ => { }, new NativeBuffApplicationObservationBoundary());
            adapter.CapabilityChanged += Publish;
            Publish(adapter.Initialize());
            Assert.Equal(disabledAtStart ? AdapterCapabilityState.DisabledIncompatible : AdapterCapabilityState.Supported, adapter.Capability.State);
            lifecycle.Initialize(); lifecycle.Tick(); Assert.True(lifecycle.IsActive);
            var generation = coordinator.CurrentGenerationId;
            var use = new ItemUseRecorded { SegmentId = lifecycle.CurrentSegmentId, EventId = "run-use", SaveGenerationId = generation, RunId = lifecycle.CurrentRunId!, TimestampUtc = DateTime.UtcNow, MapId = lifecycle.CurrentMapId!,
                GameplayContext = GameplayContext.Raid, ItemId = "medkit", DisplayName = "Medkit", Group = CanonicalItemGroup.Healing,
                ActivationCount = 1, AmountConsumed = 1, ConsumptionUnit = ConsumptionUnit.Item };
            Assert.True(coordinator.HandleItemUse(new Core.Tracking.ItemUseCompletion(Core.Tracking.ItemUseCompletionDisposition.Counted, use)));
            Assert.True(lifecycle.RecordItemUse(use));
            if (restored > 0) { var heal = new HealingApplied { EventId = "run-heal", ApplicationId = "application",
                SourceSegmentId = lifecycle.CurrentSegmentId, SourceMapId = lifecycle.CurrentMapId, OutcomeSegmentId = lifecycle.CurrentSegmentId, OutcomeMapId = lifecycle.CurrentMapId, SourceItemUseEventId = use.EventId, SaveGenerationId = generation, RunId = lifecycle.CurrentRunId!, TimestampUtc = DateTime.UtcNow, MapId = lifecycle.CurrentMapId!,
                GameplayContext = GameplayContext.Raid, ItemId = "medkit", DisplayName = "Medkit", Group = CanonicalItemGroup.Healing, ActualHealthRestored = restored }; coordinator.HandleHealing(heal); Assert.True(lifecycle.RecordHealing(heal)); }
            if (loseCapture)
            {
                // Drain earlier writes so the failure occurs in this capability publication.
                coordinator.Flush();
                ForeignPatch();
                void Inspect() => typeof(NativeHealingAttributionAdapter).GetMethod("InspectNextPatchStamp", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(adapter, new object[] { DateTime.UtcNow.AddSeconds(10) });
                if (lockPersistence)
                {
                    using var locked = new FileStream(coordinator.CurrentProfilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    var failure = Assert.Throws<TargetInvocationException>(Inspect);
                    Assert.IsAssignableFrom<IOException>(failure.InnerException);
                }
                else Inspect();
                Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.Capability.State);
                // A later availability recovery must not erase this run's gap.
                lifecycle.SetHealingCapability(new() { State = AdapterCapabilityState.Supported });
            }
            if (disabledAtStart || loseCapture)
            {
                if (loseCapture)
                {
                    use.EventId = "missed-healing-use";
                    Assert.True(coordinator.HandleItemUse(new Core.Tracking.ItemUseCompletion(Core.Tracking.ItemUseCompletionDisposition.Counted, use)));
                    Assert.True(lifecycle.RecordItemUse(use));
                }
                var health = new Health { CurrentHealth = 20, MaxHealth = 100, IsMainCharacterHealth = true };
                health.AddHealth(10);
                Assert.Equal(30, health.CurrentHealth);
            }
            now = 2; LevelManager.RaiseEvacuated();
            Assert.True(coordinator.Current!.Statistics.Runs.Count == 1, string.Join(" | ", messages));
            var path = coordinator.CurrentProfilePath;
            coordinator.Dispose();
            var loaded = new Core.Persistence.AtomicJsonStore<Core.Persistence.ProfileDocument>().Load(path, Core.Persistence.ProfileMigrator.ValidateRecoveryCandidate);
            Assert.True(loaded.Found);
            var run = Assert.Single(loaded.Value!.Statistics.Runs);
            Assert.Equal(!disabledAtStart && !loseCapture, run.HealingCaptureComplete);
            var projection = StatisticsPanelProjectionFactory.Create(loaded.Value, new(), new(), new());
            var detail = Assert.Single(RunsPresentationFactory.Create(projection, generation)!.Runs);
            Assert.Equal(expected, detail.Summary.Single(row => row.Key == UiText.Get("ui.runs_hp")).Value);
            // Restart against the same saved run after removing the conflicting patch.
            adapter.Dispose();
            HarmonyLib.Harmony.ClearAll();
            using var reopened = new NativeProfileCoordinator(); reopened.Initialize();
            using var recoveredAdapter = new NativeHealingAttributionAdapter(reopened.HandleHealing, _ => { }, new NativeBuffApplicationObservationBoundary());
            recoveredAdapter.CapabilityChanged += reopened.SetHealingCapability;
            reopened.SetHealingCapability(recoveredAdapter.Initialize());
            Assert.Equal(AdapterCapabilityState.Supported, recoveredAdapter.Capability.State);
            var recoveredProjection = StatisticsPanelProjectionFactory.Create(reopened.Current!, new(), new(), new());
            Assert.True(ItemUsePresentationFactory.HealingSupported(reopened.Current!.Capabilities));
            var recoveredRun = Assert.Single(RunsPresentationFactory.Create(recoveredProjection, generation)!.Runs);
            Assert.Equal(expected, recoveredRun.Summary.Single(row => row.Key == UiText.Get("ui.runs_hp")).Value);
            var itemRun = Assert.Single(ItemUsePresentationFactory.Create(recoveredProjection, generation)!.RecentRuns);
            var item = Assert.Single(itemRun.Items);
            var healthText = run.HealingCaptureComplete ? restored.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : restored > 0 ? restored.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (Partial)" : "Unavailable";
            Assert.Equal(healthText, item.Health.Text);
            Assert.Equal(run.HealingCaptureComplete ? ItemUseEvidence.Supported
                : restored > 0 ? ItemUseEvidence.Partial : ItemUseEvidence.Unavailable, item.Health.Evidence);
            Assert.Contains(healthText + " HP restored", itemRun.Caption);
            Assert.Equal(ItemUseEvidence.Supported, item.Uses.Evidence);
            Assert.Equal(loseCapture ? "2" : "1", item.Uses.Text);
            void AssertLifetime(Core.Persistence.ProfileDocument profile)
            {
                var p = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
                var lifetime = ItemUsePresentationFactory.Create(p, generation)!;
                Assert.Equal(healthText, lifetime.Health.Text);
                Assert.Equal(healthText, Assert.Single(lifetime.Items).Health.Text);
                var overviewText = restored.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                if (!profile.Statistics.HealingCaptureComplete) overviewText = restored > 0 ? overviewText + " (Partial)" : "Unavailable";
                Assert.Equal(overviewText, ProfileSummaryPresentationFactory.Create(p, UiText.Get)[(int)ProfileSummaryMetric.HealthRestored].Value);
            }
            Assert.Equal(!disabledAtStart && !loseCapture, reopened.Current!.Statistics.HealingCaptureComplete);
            AssertLifetime(reopened.Current);
            var exported = reopened.ExportCurrent();
            using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(exported.Directory, "statistics.json"))))
            {
                Assert.Equal(run.HealingCaptureComplete, json.RootElement.GetProperty("HealingCaptureComplete").GetBoolean());
                Assert.Equal((int)AdapterCapabilityState.Supported, json.RootElement.GetProperty("HealingCaptureState").GetInt32());
                Assert.False(json.RootElement.GetProperty("HealingEvidenceRepaired").GetBoolean());
                Assert.Equal(restored, json.RootElement.GetProperty("Overall").GetProperty("ActualHealthRestored").GetDouble());
            }
            foreach (var file in new[] { "overview.csv", "groups.csv", "items.csv" })
            {
                var lines = File.ReadAllLines(Path.Combine(exported.Directory, file));
                var columns = lines[0].Split(',');
                foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
                {
                    // This fixture has no embedded commas or quoted fields.
                    var cells = line.Split(',');
                    string Cell(string key) => cells[Array.IndexOf(columns, key)].Trim('"');
                    Assert.Equal(columns.Length, cells.Length);
                    Assert.Equal(restored, double.Parse(Cell("actual_hp_restored"), System.Globalization.CultureInfo.InvariantCulture));
                    Assert.Equal(run.HealingCaptureComplete ? "true" : "false", Cell("healing_capture_complete"));
                    Assert.Equal("Supported", Cell("healing_capture_state"));
                    Assert.Equal("false", Cell("healing_evidence_repaired"));
                    Assert.Equal(run.HealingCaptureComplete ? "Supported" : restored > 0 ? "Partial" : "Unavailable", Cell("healing_evidence_state"));
                }
                Assert.True(lines.Length > 1);
            }

            // Simulate retained-history eviction; the lifetime flag must survive without run rows.
            var retainedPath = Path.Combine(directory.Path, "without-run-history.json");
            new Core.Persistence.AtomicJsonStore<Core.Persistence.ProfileDocument>().Save(retainedPath, reopened.Current);
            var pruned = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(retainedPath))!;
            pruned["Statistics"]!["Runs"]!.AsArray().Clear();
            File.WriteAllText(retainedPath, pruned.ToJsonString());
            var reloadedLifetime = new Core.Persistence.AtomicJsonStore<Core.Persistence.ProfileDocument>().Load(retainedPath, Core.Persistence.ProfileMigrator.ValidateRecoveryCandidate);
            Assert.True(reloadedLifetime.Found, string.Join("; ", reloadedLifetime.Failures));
            Assert.Empty(reloadedLifetime.Value!.Statistics.Runs);
            AssertLifetime(reloadedLifetime.Value);
            // An absent scalar must degrade evidence, never reject or rotate the profile.
            var missing = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            missing["Statistics"]!.AsObject().Remove("HealingCaptureComplete");
            foreach (var entry in missing["Statistics"]!["Runs"]!.AsArray()) entry!.AsObject().Remove("HealingCaptureComplete");
            var missingPath = Path.Combine(directory.Path, "missing-capture.json");
            File.WriteAllText(missingPath, missing.ToJsonString());
            var withoutCapture = new Core.Persistence.AtomicJsonStore<Core.Persistence.ProfileDocument>().Load(missingPath, Core.Persistence.ProfileMigrator.ValidateRecoveryCandidate);
            Assert.True(withoutCapture.Found);
            Assert.False(withoutCapture.Value!.Statistics.HealingCaptureComplete);
            Assert.False(Assert.Single(withoutCapture.Value.Statistics.Runs).HealingCaptureComplete);
            Assert.Equal(run.ItemStatistics.Overall.ActualHealthRestored, withoutCapture.Value.Statistics.Runs[0].ItemStatistics.Overall.ActualHealthRestored);
        }
        finally
        {
            HarmonyLib.Harmony.ClearAll(); CharacterMainControl.ResetNativeState(); LevelManager.ResetNativeState(); RaidUtilities.ResetNativeState();
            Debug.ExceptionLogged = null; Application.persistentDataPath = originalPath; Saves.SavesSystem.CurrentSlot = originalSlot;
        }
    }

    private static void ForeignPrefix() { }
}
