using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class RunsDataFoundationTests
{
    [Fact]
    public void DelayedItemGrenadeKillSurvivesExtractionWithoutReassigningEarlierUnknownKills()
    {
        using var h = new NativeHarness();
        Assert.True(h.Lifecycle.RecordCombat(h.Combat("earlier") with { AttackKind = CombatAttackKind.Unknown, KillsByYou = 2, TargetIsEnemy = true }));
        // Replay the installed Skill_Grenade handoff: the thrown instance retains source and item ID.
        var player = new CharacterMainControl { IsMainCharacter = true };
        var grenade = new Grenade { damageInfo = new DamageInfo { fromCharacter = player } };
        grenade.SetWeaponIdInfo(123);
        h.Now = 2; // Detonation happens after the release scope ended, independently of the held weapon.
        object?[] prefix = { grenade, null };
        NativeGrenadeAttribution.PrefixMethod.Invoke(null, prefix);
        try
        {
            // Grenade.Explode -> ExplosionManager sets isExplosion before DamageReceiver -> Health.Hurt.
            grenade.damageInfo.isExplosion = true;
            var attack = NativeGrenadeAttribution.Classify(true, grenade.damageInfo, CombatAttackKind.Unknown);
            Assert.Equal(CombatAttackKind.Throwable, attack);
            var death = CombatObservationPolicy.ClassifyEnemyDeath(true, true, CombatOwnership.Player);
            Assert.True(h.Lifecycle.RecordCombat(h.Combat("grenade") with { AttackKind = attack, TargetIsEnemy = true,
                KillsByYou = death.KillsByYou, ObservedWorldDeaths = death.ObservedWorldDeaths,
                WeaponId = "duckov:weapon:123", ActualDamageDealt = 50, ActualDamageToTarget = 50 }));
        }
        finally { NativeGrenadeAttribution.FinalizerMethod.Invoke(null, new[] { null, prefix[1] }); }
        LevelManager.RaiseEvacuated();
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.CompleteRun(h.Completed!);
        var profile = RoundTrip(repository.Current);
        repository.CloseClean();
        profile.Capabilities = CombatNativeContractPolicy.ToRecords(CombatNativeContractPolicy.CreateSupportedCapabilities(), "test").ToList();
        Assert.Null(ProfileMigrator.ValidateRecoveryCandidate(profile));
        var totals = profile.Statistics.RunTotals.CombatStatistics.Totals;
        Assert.Equal(3, totals.KillsByYou); Assert.Equal(2, totals.PlayerKills.Unknown);
        Assert.Equal(1, totals.PlayerKills.Throwables); Assert.Equal(0, totals.ObservedWorldDeaths);
        var projection = UI.StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
        var summary = UI.CombatPresentationFactory.Create(projection, profile.GenerationId)!;
        Assert.Equal("1", Assert.Single(summary.Throwables).Value.Text);
        Assert.Equal("3", summary.Overall[2].Value.Text);
        var document = new UI.CombatDocument((_, _, size) => size, (value, size) => value.Length * size);
        document.Summary(summary, 1200, false);
        Assert.Contains(document.Rows, row => row.Kind == UI.CombatRowKind.Heading && row.Cells[0] == "Throwables");
        profile.Capabilities.Single(cap => cap.AdapterId == CombatCapabilityIds.ThrowableKills).State = AdapterCapabilityState.DisabledIncompatible;
        projection = UI.StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
        Assert.Equal(UI.CombatEvidence.Partial, UI.CombatPresentationFactory.Create(projection, profile.GenerationId)!.Throwables[0].Value.Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalCaptureUsesFreshLiveTreeAndDeathWaitsForFatalPostCall(bool death)
    {
        using var h = new NativeHarness();
        var old = h.Equipment.CaptureAssociation();
        h.Root.Content = new Item { TypeID = 991, DisplayName = "New modded item" };
        h.Root.Content.Slots.Add(new Slot { Key = "custom/nested", DisplayName = "Nested", Content = new Item { TypeID = 992 } });
        // Deliberately no tree notification: the periodic cache still refers to the old item.
        Assert.Equal(old.LoadoutId, h.Equipment.CaptureAssociation().LoadoutId);
        h.Now = 5;
        if (death)
        {
            h.Main.Health.IsDead = true;
            RaidUtilities.RaiseRaidEnd(dead: true);
            Assert.Equal(0, h.Captures);
            RaidUtilities.RaiseRaidDead();
            RaidUtilities.RaiseRaidDead();
            Assert.Equal(1, h.Captures);
            Assert.Null(h.Completed);
            Assert.Single(h.Checkpoints); // no callback disk checkpoint
            h.Root.Content = null; // installed native teardown happens here
            LevelManager.RaiseMainCharacterDead();
            LevelManager.RaiseMainCharacterDead();
            Assert.Equal(1, h.DeathObservers);
            Assert.Null(h.Completed);
            Assert.True(h.Lifecycle.RecordCombat(h.Combat("fatal") with
            {
                Ownership = CombatOwnership.OtherNpc,
                ActualDamageReceived = 30
            })); // simulated Health.Hurt post-call
            h.Lifecycle.Tick();
        }
        else
        {
            LevelManager.RaiseEvacuated();
        }
        var run = Assert.IsType<RunSummary>(h.Completed);
        Assert.Equal(death ? RunOutcome.Died : RunOutcome.Extracted, run.Outcome);
        Assert.Equal(1, h.Captures);
        Assert.Equal(TerminalLoadoutState.Complete, run.TerminalLoadout.State);
        var item = Assert.Single(run.TerminalLoadout.Snapshot!.Items);
        Assert.Equal("duckov:item:991", item.ItemId);
        Assert.Equal("duckov:item:992", Assert.Single(item.NestedSlots).ItemId);
        Assert.Contains(run.TerminalLoadout.Snapshot.CharacterSlots, slot => slot.State == EquipmentSlotState.Empty);
        Assert.Null(run.EquipmentStatistics.CurrentSnapshot);
        Assert.Equal(death ? 1 : 0, run.CombatStatistics.Totals.PlayerDeaths);
        Assert.Equal(death ? 30 : 0, run.CombatStatistics.Totals.DamageReceived);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(run.EquipmentStatistics.Items.Values).ActiveDurationSeconds);
        Assert.Equal(2, run.EquipmentStatistics.TransitionCount); // initial observation plus normal suspension
        var projection = new RunDataProjection(run);
        Assert.Equal(2, projection.TerminalSlots.Count);
        Assert.True(projection.RootSlotsComplete);
        h.Lifecycle.Tick();
        Assert.Equal(1, h.Completions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void JsonExportPreservesDetachedTerminalEvidence(bool death, bool partial)
    {
        using var h = new NativeHarness();
        h.Root.Content!.Slots.Add(new Slot { Key = "nested", Content = new Item { TypeID = 992, DisplayName = "Attachment" } });
        h.Root.Content.Slots.Add(new Slot { Key = "empty" });
        if (partial) h.Root.Content.Slots.Add(new Slot { Key = "unreadable", ThrowOnContentRead = true });
        if (death)
        {
            RaidUtilities.RaiseRaidEnd(true);
            RaidUtilities.RaiseRaidDead();
            h.Root.Content = null;
            LevelManager.RaiseMainCharacterDead();
            h.Lifecycle.Tick();
        }
        else LevelManager.RaiseEvacuated();

        var profile = new ProfileDocument { GenerationId = "generation", CreatedUtc = DateTime.UnixEpoch, UpdatedUtc = DateTime.UnixEpoch, Statistics = new ProfileStatistics { SaveGenerationId = "generation", CreatedUtc = DateTime.UnixEpoch, UpdatedUtc = DateTime.UnixEpoch } };
        Assert.True(RunReducer.Apply(profile.Statistics, h.Completed!));
        var retained = Assert.Single(profile.Statistics.Runs).TerminalLoadout;
        Assert.Equal(partial ? TerminalLoadoutState.Partial : TerminalLoadoutState.Complete, retained.State);
        Assert.Equal(death ? RunOutcome.Died : RunOutcome.Extracted, retained.CapturedOutcome);
        Assert.NotEmpty(retained.Provenance);
        Assert.True(retained.Snapshot!.CharacterSlotStateComplete);
        Assert.Equal(!partial, retained.Snapshot.NestedSlotStateComplete);
        var root = Assert.Single(retained.Snapshot.Items);
        Assert.Equal(!partial, root.NestedSlotStateComplete);
        Assert.Contains(root.NestedSlots, slot => slot.ItemId == "duckov:item:992");
        Assert.Contains(root.NestedSlots, slot => slot.State == EquipmentSlotState.Empty);
        Assert.Contains(retained.Snapshot.CharacterSlots, slot => slot.State == EquipmentSlotState.Empty);

        var export = StatisticsExporter.Create(profile, DateTime.UnixEpoch);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(export.Json));
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        var decoded = Assert.IsType<StatisticsExportDocument>(serializer.ReadObject(stream));
        var restored = Assert.Single(decoded.Runs).TerminalLoadout;
        Assert.Equal(retained.State, restored.State);
        Assert.Equal(retained.CapturedOutcome, restored.CapturedOutcome);
        Assert.Equal(retained.Provenance, restored.Provenance);
        // Compare every persisted snapshot field, including root/nested identities and completeness.
        var expected = System.Text.Json.JsonSerializer.Serialize(retained);
        Assert.Equal(expected, System.Text.Json.JsonSerializer.Serialize(restored));

        var copied = Assert.Single(export.Document.Runs).TerminalLoadout;
        Assert.NotSame(retained, copied);
        Assert.NotSame(retained.Snapshot, copied.Snapshot);
        Assert.NotSame(root, Assert.Single(copied.Snapshot!.Items));
        for (var i = 0; i < retained.Snapshot.CharacterSlots.Count; i++)
            Assert.NotSame(retained.Snapshot.CharacterSlots[i], copied.Snapshot.CharacterSlots[i]);
        for (var i = 0; i < root.NestedSlots.Count; i++)
            Assert.NotSame(root.NestedSlots[i], copied.Snapshot.Items[0].NestedSlots[i]);
        copied.Provenance = "export mutation";
        copied.Snapshot.CharacterSlots[0].ItemId = "changed root";
        copied.Snapshot.Items[0].NestedSlots[0].ItemId = "changed nested";
        copied.Snapshot.Items.Clear();
        Assert.Equal(expected, System.Text.Json.JsonSerializer.Serialize(retained));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EconomyAndCheckpointRetriesRetainFirstDetachedCandidate(bool death)
    {
        using var h = new NativeHarness();
        h.EconomyDurable = false;
        h.CheckpointDurable = false;
        h.Now = 5;
        if (death)
        {
            RaidUtilities.RaiseRaidEnd(true);
            RaidUtilities.RaiseRaidDead();
            h.Root.Content = null;
            LevelManager.RaiseMainCharacterDead();
            h.Lifecycle.Tick();
        }
        else LevelManager.RaiseEvacuated();
        Assert.Null(h.Completed);
        Assert.Equal(1, h.Captures);
        Assert.Single(h.Checkpoints);
        h.Root.Content = new Item { TypeID = 10000 };
        h.Now = 7;
        h.EconomyDurable = true;
        h.Lifecycle.Tick();
        Assert.Null(h.Completed);
        var candidate = h.Checkpoints.Last().TerminalLoadout;
        Assert.Equal("duckov:item:900", Assert.Single(candidate.Snapshot!.Items).ItemId);
        candidate.Snapshot.Items[0].ItemId = "mutated external checkpoint copy";
        h.CheckpointDurable = true;
        h.Now = 9;
        h.Lifecycle.Tick();
        Assert.Equal(1, h.Captures);
        Assert.Equal("duckov:item:900", Assert.Single(h.Completed!.TerminalLoadout.Snapshot!.Items).ItemId);
        Assert.True(h.EconomyCalls >= 3);
        Assert.Equal(1, h.Completions);
        Assert.Equal(5, h.Completed.ActiveDurationSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedFreshCaptureDoesNotSubstituteCacheOrBlockCompletion(int failure)
    {
        using var h = new NativeHarness();
        Assert.NotEqual(EquipmentEventAssociation.UnavailableId, h.Equipment.CaptureAssociation().LoadoutId);
        if (failure == 0) h.Main.CharacterItem = null;
        if (failure == 1) CharacterMainControl.Main = null;
        if (failure == 2) h.Lifecycle.SetTerminalLoadoutCapture(_ => throw new InvalidOperationException("capture failure"));
        LevelManager.RaiseEvacuated();
        Assert.Equal(TerminalLoadoutState.Unavailable, h.Completed!.TerminalLoadout.State);
        Assert.Null(h.Completed.TerminalLoadout.Snapshot);
        Assert.NotEmpty(h.Diagnostics);
        Assert.Equal(1, h.Completions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialTreeRetainsProvenUnknownModdedAndEmptySlots(bool throws)
    {
        using var h = new NativeHarness();
        if (throws) h.Root.ThrowOnContentRead = true;
        else h.Item.Slots.Add(null!);
        LevelManager.RaiseEvacuated();
        var terminal = h.Completed!.TerminalLoadout;
        Assert.Equal(TerminalLoadoutState.Partial, terminal.State);
        Assert.Contains(terminal.Snapshot!.CharacterSlots, slot => slot.SlotId == "duckov:slot:ModEmpty" && slot.State == EquipmentSlotState.Empty);
        if (!throws) Assert.Equal("duckov:slot:ModRoot", Assert.Single(terminal.Snapshot.Items).SlotId);
        Assert.NotEmpty(h.Diagnostics);
    }

    [Fact]
    public void UnreadableNestedSlotRetainsRootAndReadableSiblingWithoutClaimingCompleteness()
    {
        using var h = new NativeHarness();
        h.Root.Content!.Slots.Add(new Slot { Key = "bad", ThrowOnContentRead = true });
        h.Root.Content.Slots.Add(new Slot { Key = "good", Content = new Item { TypeID = 902 } });
        LevelManager.RaiseEvacuated();
        var terminal = h.Completed!.TerminalLoadout;
        Assert.Equal(TerminalLoadoutState.Partial, terminal.State);
        var root = Assert.Single(terminal.Snapshot!.Items);
        Assert.Equal("duckov:item:900", root.ItemId);
        Assert.Equal("duckov:item:902", Assert.Single(root.NestedSlots).ItemId);
        Assert.False(root.NestedSlotStateComplete);
        Assert.NotEmpty(h.Diagnostics);
    }

    [Fact]
    public void InterruptedRecoveryDoesNotPublishCapturedCandidateAsTerminal()
    {
        using var h = new NativeHarness();
        RaidUtilities.RaiseRaidEnd(true);
        RaidUtilities.RaiseRaidDead();
        Assert.True(h.Lifecycle.FlushCheckpoint());
        var checkpoint = h.Checkpoints.Last();
        Assert.NotNull(checkpoint.TerminalLoadout.Snapshot);
        Assert.Null(checkpoint.PendingTerminalOutcome);
        Assert.Null(checkpoint.ToRecoverySummary().TerminalLoadout.Snapshot);
        Assert.Equal(TerminalLoadoutState.Unavailable, checkpoint.ToRecoverySummary().TerminalLoadout.State);
    }

    [Theory]
    [InlineData(CombatAttackKind.Ranged)]
    [InlineData(CombatAttackKind.Melee)]
    [InlineData(CombatAttackKind.Throwable)]
    [InlineData(CombatAttackKind.Effect)]
    [InlineData(CombatAttackKind.Environmental)]
    [InlineData(CombatAttackKind.Unknown)]
    public void SameEventKillPartitionSurvivesAllScopesRecoveryAndExports(CombatAttackKind kind)
    {
        using var h = new NativeHarness();
        Assert.True(h.Lifecycle.RecordCombat(h.Combat("kill") with { AttackKind = kind, KillsByYou = 1, TargetIsEnemy = true }));
        h.CheckpointDurable = false;
        LevelManager.RaiseEvacuated();
        var checkpoint = RoundTrip(h.Checkpoints.Last());
        var run = checkpoint.ToRecoverySummary();
        Assert.Equal(TerminalLoadoutState.Complete, run.TerminalLoadout.State);
        var profile = new ProfileDocument { GenerationId = "generation", CreatedUtc = DateTime.UnixEpoch, UpdatedUtc = DateTime.UnixEpoch, Statistics = new ProfileStatistics { SaveGenerationId = "generation", CreatedUtc = DateTime.UnixEpoch, UpdatedUtc = DateTime.UnixEpoch } };
        Assert.True(RunReducer.Apply(profile.Statistics, run));
        foreach (var combat in CombatScopes(profile))
            foreach (var totals in CombatStatisticsReducer.PlayerKillScopes(combat))
            {
                totals.PlayerKills.Validate(totals.KillsByYou);
                Assert.Equal(1, totals.KillsByYou);
                Assert.Equal(kind == CombatAttackKind.Unknown, !totals.PlayerKills.ClassificationComplete);
                AssertBucket(totals.PlayerKills, kind, 1);
            }
        foreach (var equipment in EquipmentScopes(profile))
            AssertBucket(Assert.Single(equipment.CombatAssociations.Values).PlayerKills, kind, 1);
        var projection = new RunDataProjection(run);
        Assert.Equal(kind != CombatAttackKind.Unknown, projection.RangedMeleeExact);
        var export = StatisticsExporter.Create(profile, DateTime.UtcNow);
        Assert.Contains("PlayerKills", export.Json, StringComparison.Ordinal);
        Assert.Contains("TerminalLoadout", export.Json, StringComparison.Ordinal);
        foreach (var csv in new[] { export.RunsCsv, export.RunTotalsCsv, export.MapTotalsCsv, export.RouteMapTotalsCsv,
                     export.SegmentsCsv, export.CombatAttributionCsv, export.EquipmentCombatCsv })
        {
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var header = lines[0].TrimEnd('\r').Split(',');
            Assert.Contains("kill_classification_provenance", header);
            var bucket = Array.IndexOf(header, kind.ToString().ToLowerInvariant() + "_kills_by_you");
            Assert.True(bucket >= 0);
            Assert.Contains(lines.Skip(1), line => line.Split(',')[bucket] == "1");
        }
        Assert.Contains("duckov:slot:ModEmpty", StatisticsExporter.CreateTerminalLoadoutsCsv(new[] { run }), StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleVictimsAndDelayedEffectsDoNotDependOnProjectilesOrHits()
    {
        using var h = new NativeHarness();
        Assert.True(h.Lifecycle.RecordCombat(h.Combat("victim1") with { AttackKind = CombatAttackKind.Ranged, KillsByYou = 1, TargetIsEnemy = true }));
        Assert.True(h.Lifecycle.RecordCombat(h.Combat("victim2") with { AttackKind = CombatAttackKind.Ranged, KillsByYou = 1, TargetIsEnemy = true, TargetId = "second" }));
        Assert.True(h.Lifecycle.RecordCombat(h.Combat("delayed") with { AttackKind = CombatAttackKind.Effect, IsDamageOverTime = true, KillsByYou = 1, TargetIsEnemy = true }));
        Assert.True(h.Lifecycle.RecordCombat(h.Combat("nonplayer") with { Ownership = CombatOwnership.PetCompanion, ObservedWorldDeaths = 1, TargetIsEnemy = true }));
        foreach (var kind in new[] { CombatAttackKind.Ranged, CombatAttackKind.Melee })
            Assert.True(h.Lifecycle.RecordCombat(h.Combat(kind.ToString()) with { AttackKind = kind, ActualDamageDealt = 5, ActualDamageToTarget = 5, TargetIsEnemy = true }));
        LevelManager.RaiseEvacuated();
        var kills = h.Completed!.CombatStatistics.Totals.PlayerKills;
        Assert.Equal(2, kills.Ranged);
        Assert.Equal(1, kills.Effect);
        Assert.Equal(0, kills.Melee);
        kills.Validate(3);
    }

    [Fact]
    public void HistoricalAndNewRunEvidenceRemainDistinctThroughMigrationAndMerge()
    {
        using var h = new NativeHarness();
        Assert.True(h.Lifecycle.RecordCombat(h.Combat("old") with { AttackKind = CombatAttackKind.Ranged, KillsByYou = 2, TargetIsEnemy = true }));
        LevelManager.RaiseEvacuated();
        var profile = new ProfileDocument { GenerationId = "generation", CreatedUtc = DateTime.UnixEpoch, UpdatedUtc = DateTime.UnixEpoch, Statistics = new ProfileStatistics { SaveGenerationId = "generation", CreatedUtc = DateTime.UnixEpoch, UpdatedUtc = DateTime.UnixEpoch } };
        RunReducer.Apply(profile.Statistics, h.Completed!);
        profile.SchemaVersion = profile.Statistics.SchemaVersion = 16;
        foreach (var combat in CombatScopes(profile))
            foreach (var total in CombatStatisticsReducer.PlayerKillScopes(combat)) total.PlayerKills = null!;
        foreach (var equipment in EquipmentScopes(profile))
            foreach (var row in equipment.CombatAssociations.Values) row.PlayerKills = null!;
        profile.Statistics.Runs[0].TerminalLoadout = null!;
        Assert.True(ProfileMigrator.Migrate(RoundTrip(profile)));
        Assert.True(ProfileMigrator.Migrate(profile));
        Assert.False(ProfileMigrator.Migrate(profile));
        Assert.Equal(18, profile.SchemaVersion);
        Assert.Equal(TerminalLoadoutState.HistoricalUnavailable, profile.Statistics.Runs[0].TerminalLoadout.State);
        foreach (var combat in CombatScopes(profile))
            foreach (var total in CombatStatisticsReducer.PlayerKillScopes(combat))
            {
                Assert.Equal(2, total.PlayerKills.HistoricalUnclassified);
                Assert.Equal(0, total.PlayerKills.Unknown);
                Assert.False(total.PlayerKills.ClassificationComplete);
                total.PlayerKills.Validate(2);
            }
        using var next = new NativeHarness();
        next.Lifecycle.RecordCombat(next.Combat("new") with { AttackKind = CombatAttackKind.Melee, KillsByYou = 1, TargetIsEnemy = true });
        LevelManager.RaiseEvacuated();
        RunReducer.Apply(profile.Statistics, next.Completed!);
        Assert.True(new RunDataProjection(next.Completed!).RangedMeleeExact);
        Assert.False(profile.Statistics.RunTotals.CombatStatistics.Totals.PlayerKills.ClassificationComplete);
        Assert.Equal(1, profile.Statistics.RunTotals.CombatStatistics.Totals.PlayerKills.Melee);
        Assert.Equal(2, profile.Statistics.RunTotals.CombatStatistics.Totals.PlayerKills.HistoricalUnclassified);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PendingTerminalCheckpointReopenRejectsMalformedPrimaryAndKeepsExactBackup(int corruption)
    {
        using var directory = new TemporaryDirectory();
        using var h = new NativeHarness();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        h.Lifecycle.RecordCombat(h.Combat("kill") with { AttackKind = CombatAttackKind.Melee, KillsByYou = 1, TargetIsEnemy = true });
        RaidUtilities.RaiseRaidEnd(true);
        RaidUtilities.RaiseRaidDead();
        h.Root.Content = null;
        LevelManager.RaiseMainCharacterDead();
        h.Lifecycle.RecordCombat(h.Combat("fatal") with { Ownership = CombatOwnership.OtherNpc, ActualDamageReceived = 25 });
        h.CheckpointDurable = false;
        h.Lifecycle.Tick();
        var valid = RoundTrip(h.Checkpoints.Last());
        repository.SaveActiveRun(valid);
        var path = Path.Combine(Path.GetDirectoryName(repository.CurrentProfilePath!)!, "active-run.json");
        repository.CloseClean();
        var corrupt = RoundTrip(valid);
        if (corruption == 0) corrupt.CombatStatistics.Totals.PlayerKills.Melee++;
        if (corruption == 1) corrupt.CombatStatistics.Causes.Values.First().Totals.PlayerKills.Unknown++;
        if (corruption == 2) corrupt.EquipmentStatistics.CombatAssociations.Values.First().PlayerKills = null!;
        if (corruption == 3) corrupt.TerminalLoadout.Snapshot = null;
        if (corruption == 4) corrupt.CombatStatistics.Totals.PlayerKills.Ranged = long.MaxValue;
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(path, corrupt);
        var reopened = Repository(directory.Path);
        Assert.True(reopened.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(reopened.Current.Statistics.Runs);
        Assert.Equal(RunOutcome.Died, run.Outcome);
        Assert.Equal(TerminalLoadoutState.Complete, run.TerminalLoadout.State);
        Assert.Equal("duckov:item:900", Assert.Single(run.TerminalLoadout.Snapshot!.Items).ItemId);
        Assert.Equal(1, run.CombatStatistics.Totals.PlayerDeaths);
        Assert.Equal(1, run.CombatStatistics.Totals.PlayerKills.Melee);
        Assert.Equal(25, run.CombatStatistics.Totals.DamageReceived);
        reopened.CloseClean();
        var again = Repository(directory.Path);
        again.Open(Identity());
        Assert.Single(again.Current.Statistics.Runs);
        again.CloseClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentProfileCandidateSelectionRejectsMalformedPartitionBeforeNormalization(bool temporary)
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        using var h = new NativeHarness();
        h.Lifecycle.RecordCombat(h.Combat("kill") with { AttackKind = CombatAttackKind.Ranged, KillsByYou = 1, TargetIsEnemy = true });
        LevelManager.RaiseEvacuated();
        repository.CompleteRun(h.Completed!);
        var valid = RoundTrip(repository.Current);
        Assert.Null(ProfileMigrator.ValidateRecoveryCandidate(valid));
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(directory.Path, "candidate.json");
        store.Save(path, valid);
        var invalid = RoundTrip(valid);
        invalid.Statistics.RunTotals.CombatStatistics.Totals.PlayerKills.Ranged = 0;
        store.Save(path, invalid);
        if (temporary)
        {
            File.Move(AtomicJsonPaths.GetBackupPath(path), AtomicJsonPaths.GetTemporaryPath(path));
        }
        var result = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);
        Assert.Equal(temporary ? AtomicJsonLoadSource.Temporary : AtomicJsonLoadSource.Backup, result.Source);
        Assert.Equal(1, result.Value!.Statistics.RunTotals.CombatStatistics.Totals.PlayerKills.Ranged);
        repository.CloseClean();
    }

    [Fact]
    public void Schema16CheckpointMigrationDoesNotInferAttackKindOrTerminalTree()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        using var h = new NativeHarness();
        h.Lifecycle.RecordCombat(h.Combat("kill") with { AttackKind = CombatAttackKind.Ranged, KillsByYou = 3, TargetIsEnemy = true });
        h.CheckpointDurable = false;
        LevelManager.RaiseEvacuated();
        var checkpoint = RoundTrip(h.Checkpoints.Last());
        checkpoint.SchemaVersion = 16;
        checkpoint.TerminalLoadout = null!;
        checkpoint.CombatStatistics.Totals.PlayerKills = null!;
        var path = Path.Combine(Path.GetDirectoryName(repository.CurrentProfilePath!)!, "active-run.json");
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(path, checkpoint);
        repository.CloseClean();
        var reopened = Repository(directory.Path);
        Assert.True(reopened.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(reopened.Current.Statistics.Runs);
        Assert.Equal(3, run.CombatStatistics.Totals.KillsByYou);
        Assert.Equal(3, run.CombatStatistics.Totals.PlayerKills.HistoricalUnclassified);
        Assert.Equal(0, run.CombatStatistics.Totals.PlayerKills.Ranged);
        Assert.Equal(TerminalLoadoutState.HistoricalUnavailable, run.TerminalLoadout.State);
        Assert.Null(run.TerminalLoadout.Snapshot);
        reopened.CloseClean();
    }

    [Fact]
    public void CheckedKillOverflowRejectsAllFanOutBeforeMutationAndRunMergeBeforePublication()
    {
        using var h = new NativeHarness();
        h.Lifecycle.RecordCombat(h.Combat("maximum") with { AttackKind = CombatAttackKind.Ranged, KillsByYou = long.MaxValue, TargetIsEnemy = true });
        Assert.Throws<OverflowException>(() => h.Lifecycle.RecordCombat(h.Combat("overflow") with
        {
            AttackKind = CombatAttackKind.Melee,
            KillsByYou = 1,
            TargetIsEnemy = true,
            ActualDamageDealt = 5
        }));
        LevelManager.RaiseEvacuated();
        var run = h.Completed!;
        Assert.Equal(0, run.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(0, run.CombatStatistics.Totals.PlayerKills.Melee);
        var profile = new ProfileStatistics { SaveGenerationId = "generation" };
        Assert.True(RunReducer.Apply(profile, run));
        var additional = RoundTrip(run);
        additional.RunId = "another";
        Assert.Throws<OverflowException>(() => RunReducer.Apply(profile, additional));
        Assert.Single(profile.Runs);
        Assert.Equal(1, profile.RunTotals.TotalRuns);
        profile.RunTotals.CombatStatistics.Totals.PlayerKills.Validate(long.MaxValue);
    }

    [Fact]
    public void InterruptionHasNoTerminalEvidenceAndNewSupportedZeroIsExact()
    {
        using var h = new NativeHarness();
        h.Lifecycle.InterruptForProfileTransition();
        Assert.Equal(0, h.Captures);
        Assert.Equal(RunOutcome.Interrupted, h.Completed!.Outcome);
        var data = new RunDataProjection(h.Completed);
        Assert.Equal(TerminalLoadoutState.Unavailable, data.TerminalState);
        Assert.Empty(data.TerminalSlots);
        Assert.True(data.RangedMeleeExact);
        Assert.Equal(0, data.RangedKills);
    }

    private static ProfileRepository Repository(string path) => new(path, () => DateTime.UtcNow, () => "generation");
    private static SaveIdentitySnapshot Identity() => new()
    {
        Slot = 1,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = 100,
        ObservedWriteUtcTicks = 110,
        ObservedLength = 4096,
        GameVersion = "2.3.30",
        ContentSha256 = new string('a', 64),
        SaveTimeBinary = DateTime.UnixEpoch.ToBinary()
    };

    private static void AssertBucket(PlayerKillPartition partition, CombatAttackKind kind, long expected)
    {
        Assert.Equal(kind == CombatAttackKind.Ranged ? expected : 0, partition.Ranged);
        Assert.Equal(kind == CombatAttackKind.Melee ? expected : 0, partition.Melee);
        Assert.Equal(kind == CombatAttackKind.Throwable ? expected : 0, partition.Throwables);
        Assert.Equal(kind == CombatAttackKind.Effect ? expected : 0, partition.Effect);
        Assert.Equal(kind == CombatAttackKind.Environmental ? expected : 0, partition.Environmental);
        Assert.Equal(kind == CombatAttackKind.Unknown ? expected : 0, partition.Unknown);
    }

    private static IEnumerable<CombatStatisticsAggregate> CombatScopes(ProfileDocument profile)
    {
        yield return profile.Statistics.RunTotals.CombatStatistics;
        foreach (var map in profile.Statistics.RunTotals.Maps.Values) yield return map.CombatStatistics;
        foreach (var map in profile.Statistics.RunTotals.RouteMaps.Values) yield return map.CombatStatistics;
        foreach (var run in profile.Statistics.Runs)
        {
            yield return run.CombatStatistics;
            foreach (var segment in run.Segments) yield return segment.CombatStatistics;
        }
    }

    private static IEnumerable<EquipmentStatisticsAggregate> EquipmentScopes(ProfileDocument profile)
    {
        yield return profile.Statistics.RunTotals.EquipmentStatistics;
        foreach (var map in profile.Statistics.RunTotals.Maps.Values) yield return map.EquipmentStatistics;
        foreach (var map in profile.Statistics.RunTotals.RouteMaps.Values) yield return map.EquipmentStatistics;
        foreach (var run in profile.Statistics.Runs)
        {
            yield return run.EquipmentStatistics;
            foreach (var segment in run.Segments) yield return segment.EquipmentStatistics;
        }
    }

    private static T RoundTrip<T>(T value)
    {
        using var stream = new MemoryStream();
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
        serializer.WriteObject(stream, value);
        stream.Position = 0;
        return (T)serializer.ReadObject(stream)!;
    }

    [Fact]
    public void NativeExtractionCompletionRetriesAndCleanupRespectTheSameBudget()
    {
        using var h = new NativeHarness();
        h.Now = 807.06;
        h.CompletionDurable = false;
        LevelManager.RaiseEvacuated();
        var pending = Assert.IsType<RunSummary>(h.Completed);
        var checkpoint = h.Checkpoints.Last();
        Assert.Equal(RunOutcome.Extracted, checkpoint.PendingTerminalOutcome);
        Assert.Equal(pending.RunId, checkpoint.RunId);
        Assert.Equal(1, h.Completions);
        Assert.Throws<IOException>(() => h.Lifecycle.InterruptForProfileTransition());
        for (var frame = 0; frame < 10000; frame++)
        {
            h.Lifecycle.Tick();
            Assert.False(h.Lifecycle.TryCleanup());
        }
        Assert.Equal(1, h.Completions);
        Assert.Same(pending, h.Completed);
        Assert.Same(checkpoint, h.Checkpoints.Last());
        Assert.Single(h.Diagnostics, message => message.Contains("persistence remains pending", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Diagnostics, message => message.Contains("Run finalized", StringComparison.Ordinal));
        h.CompletionDurable = true;
        h.Now += 1;
        h.Lifecycle.Tick();
        Assert.Equal(2, h.Completions);
        Assert.Same(pending, h.Completed);
        Assert.True(h.Lifecycle.TryCleanup());
        Assert.Single(h.Diagnostics, message => message.Contains("Run finalized", StringComparison.Ordinal));
    }

    private sealed class NativeHarness : IDisposable
    {
        public double Now { get; set; }
        public bool EconomyDurable { get; set; } = true;
        public bool CheckpointDurable { get; set; } = true;
        public bool CompletionDurable { get; set; } = true;
        public int EconomyCalls { get; private set; }
        public int Captures { get; private set; }
        public int Completions { get; private set; }
        public int DeathObservers { get; private set; }
        public RunSummary? Completed { get; private set; }
        public List<ActiveRunCheckpoint> Checkpoints { get; } = new();
        public List<string> Diagnostics { get; } = new();
        public Item Item { get; } = new() { TypeID = 1, Inventory = new Inventory() };
        public Slot Root { get; } = new() { Key = "ModRoot", DisplayName = "Mod root", Content = new Item { TypeID = 900, DisplayName = "Mod item" } };
        public CharacterMainControl Main { get; }
        public NativeRunLifecycleAdapter Lifecycle { get; }
        public NativeEquipmentAdapter Equipment { get; }

        public NativeHarness()
        {
            Reset();
            Item.Slots.Add(Root);
            Item.Slots.Add(new Slot { Key = "ModEmpty", DisplayName = "Empty mod slot" });
            Main = new CharacterMainControl { IsMainCharacter = true, CharacterItem = Item };
            CharacterMainControl.Main = Main;
            LevelManager.Instance = new LevelManagerInstance { MainCharacter = Main };
            RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };
            Lifecycle = new NativeRunLifecycleAdapter(() => "generation", checkpoint =>
            {
                Checkpoints.Add(checkpoint);
                return CheckpointDurable;
            }, run => { Completed = run; Completions++; return CompletionDurable; }, _ => { }, Diagnostics.Add,
                combatCapabilitiesProvider: CombatNativeContractPolicy.CreateSupportedCapabilities,
                equipmentCapabilitiesProvider: EquipmentNativeContractPolicy.CreateSupportedCapabilities,
                monotonicSecondsProvider: () => Now);
            Equipment = new NativeEquipmentAdapter(() => Lifecycle.IsActive, Lifecycle.ObserveEquipment,
                Lifecycle.InvalidateEquipmentObservation, _ => { }, Diagnostics.Add, () => Now, () => Lifecycle.CurrentSegmentId);
            Equipment.Initialize();
            Lifecycle.SetTerminalLoadoutCapture(outcome => { Captures++; return Equipment.CaptureTerminalLoadout(outcome); });
            Lifecycle.SetTerminalObserver(() => { EconomyCalls++; return EconomyDurable; });
            Lifecycle.SetPlayerDeathObserver(_ =>
            {
                DeathObservers++;
                Lifecycle.RecordCombat(Combat("player-death-observer") with
                {
                    Ownership = CombatOwnership.OtherNpc,
                    PlayerDeaths = 1
                });
            });
            Lifecycle.Initialize();
            Lifecycle.Tick();
            Item.RaiseItemTreeChanged();
            Assert.True(Lifecycle.IsActive);
            Diagnostics.Clear();
        }

        public CombatRecorded Combat(string id) => new()
        {
            EventId = id,
            SaveGenerationId = "generation",
            RunId = Lifecycle.CurrentRunId!,
            MapId = Lifecycle.CurrentMapId!,
            GameplayContext = GameplayContext.Raid,
            Ownership = CombatOwnership.Player,
            TimestampUtc = DateTime.UtcNow,
            OutcomeSegmentId = Lifecycle.CurrentSegmentId,
            OutcomeMapId = Lifecycle.CurrentMapId,
            SourceSegmentId = Lifecycle.CurrentSegmentId,
            SourceMapId = Lifecycle.CurrentMapId,
            Capabilities = CombatNativeContractPolicy.CreateSupportedCapabilities(),
            EquipmentAssociation = Equipment.CaptureAssociation()
        };

        public void Dispose()
        {
            EconomyDurable = CheckpointDurable = true;
            Lifecycle.Dispose();
            Equipment.Dispose();
            Reset();
        }

        private static void Reset()
        {
            CharacterMainControl.ResetNativeState(); LevelManager.ResetNativeState(); RaidUtilities.ResetNativeState();
            InputManager.InputActived = true; GameManager.Paused = false;
            NativeRaidContext.GameplayContext = GameplayContext.Raid;
            Duckov.Scenes.SceneLoader.IsSceneLoading = false;
            UnityEngine.Application.version = "2.3.30";
        }
    }
}
