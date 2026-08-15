using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class PersistenceTests
{
    private static readonly DateTime TestTime = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ExpectedDiagnosticMessages = { "two", "three" };
    private static readonly string[] SchemaTwoRecentEventIds = { "use-1", "heal-1" };

    [Fact]
    [Trait("Category", "Persistence")]
    public void AtomicStoreRoundTripsAndRecoversCorruptPrimaryFromBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var first = CreateDocument("generation-a", revision: 1);
        first.Statistics.RunTotals.CombatStatistics.Totals.DamageDealt = 5;
        var second = CreateDocument("generation-a", revision: 2);
        second.Statistics.RunTotals.CombatStatistics.Totals.DamageDealt = 8;
        store.Save(path, first);
        store.Save(path, second);

        File.WriteAllText(path, "{ definitely-not-json");
        var recovered = store.Load(path);

        Assert.Equal(AtomicJsonLoadSource.Backup, recovered.Source);
        Assert.True(recovered.Recovered);
        Assert.True(recovered.PrimaryRepaired);
        Assert.Equal(1, recovered.Value!.Revision);
        Assert.Equal(5, recovered.Value.Statistics.RunTotals.CombatStatistics.Totals.DamageDealt);
        Assert.NotEmpty(recovered.Failures);
        Assert.Equal(1, store.Load(path).Value!.Revision);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void AtomicStoreRecoversOrphanedTemporarySnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profile.json");
        var temporaryPath = AtomicJsonPaths.GetTemporaryPath(path);
        var store = new AtomicJsonStore<ProfileDocument>();
        var document = CreateDocument("generation-a", revision: 7);
        document.Statistics.RunTotals.CombatStatistics.Totals.DamageDealt = 7;
        store.Save(path, document);
        File.Move(path, temporaryPath);

        var recovered = store.Load(path);

        Assert.Equal(AtomicJsonLoadSource.Temporary, recovered.Source);
        Assert.True(recovered.PrimaryRepaired);
        Assert.Equal(7, recovered.Value!.Revision);
        Assert.Equal(7, recovered.Value.Statistics.RunTotals.CombatStatistics.Totals.DamageDealt);
        Assert.True(File.Exists(path));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryMigratesLegacySchemaWithoutChangingGeneration()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-legacy", revision: 4);
        legacy.SchemaVersion = 0;
        legacy.Statistics.SchemaVersion = 0;
        legacy.Statistics.SaveGenerationId = string.Empty;
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-legacy", repository.Current.GenerationId);
        Assert.Equal("generation-legacy", repository.Current.Statistics.SaveGenerationId);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaIncompleteRouteTotalsPrimaryLosesToIntactBackupBeforeMigration()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateDocument("generation-a", revision: 7);
        backup.Statistics.RunTotals.RouteMaps["duckov:map:A"] = new RouteAwareMapAggregate
        {
            MapId = "duckov:map:A",
            DisplayName = "A",
            IsKnown = true,
            RunsVisited = 3,
            SegmentVisits = 5,
            HistoricalUnavailable = false,
            WasRepairedFromInvalidState = false
        };
        var incompletePrimary = CreateDocument("generation-a", revision: 8);
        incompletePrimary.Statistics.RunTotals.RouteMaps = null!;
        store.Save(path, backup);
        store.Save(path, incompletePrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.False(result.MigratedSchema);
        Assert.Contains(result.LoadFailures, failure => failure.Contains("Current-schema profile roots are incomplete.", StringComparison.Ordinal));
        Assert.Equal(7, repository.Current.Revision);
        var routeMap = Assert.Single(repository.Current.Statistics.RunTotals.RouteMaps).Value;
        Assert.Equal(3, routeMap.RunsVisited);
        Assert.Equal(5, routeMap.SegmentVisits);
        Assert.False(routeMap.HistoricalUnavailable);
        Assert.False(routeMap.WasRepairedFromInvalidState);
        repository.CloseClean();

        var persisted = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate).Value!;
        var persistedRouteMap = Assert.Single(persisted.Statistics.RunTotals.RouteMaps).Value;
        Assert.Equal(3, persistedRouteMap.RunsVisited);
        Assert.False(persistedRouteMap.HistoricalUnavailable);
    }

    public static TheoryData<string> CurrentSchemaRequiredRootCases => new()
    {
        "Profile.Identity",
        "Profile.Capabilities",
        "DeferredItemPersistence.AppliedLifetimeStatistics",
        "DeferredItemPersistence.AppliedLifetimeEconomy",
        "Statistics.Overall",
        "Statistics.Items",
        "Statistics.Groups",
        "Statistics.RecentEventIds",
        "Statistics.Runs",
        "Statistics.RunTotals",
        "Statistics.RunRecords",
        "Statistics.Economy",
        "Statistics.Economy.Currencies",
        "Statistics.Economy.CashRaidOutcomes",
        "Statistics.Economy.Capabilities",
        "Statistics.Economy.RecentEventIds",
        "Statistics.Economy.Capabilities.MoneyAmountDirection",
        "Statistics.Economy.Capabilities.MoneySourceAttribution",
        "Statistics.Economy.Capabilities.MoneyContextAttribution",
        "Statistics.Economy.Capabilities.CashAmountDirection",
        "Statistics.Economy.Capabilities.CashExternalAcquisition",
        "Statistics.Economy.Capabilities.CashContextAttribution",
        "Statistics.Economy.Capabilities.CashTerminalOutcomes",
        "Statistics.Economy.Capabilities.RouteAttribution",
        "Statistics.Economy.Currency.Totals",
        "Statistics.Economy.Currency.Sources",
        "Statistics.Economy.Currency.Contexts",
        "Statistics.Overall.AmountsByUnit",
        "Statistics.Item.EffectTags",
        "Statistics.Item.Totals",
        "Statistics.Item.Totals.AmountsByUnit",
        "Statistics.Group.AmountsByUnit",
        "RunTotals.Outcomes",
        "RunTotals.Maps",
        "RunTotals.RouteMaps",
        "RunTotals.ItemStatistics",
        "RunTotals.WeaponStatistics",
        "RunTotals.CombatStatistics",
        "RunTotals.EquipmentStatistics",
        "RunTotals.ContainerStatistics",
        "RunTotals.Economy",
        "RunTotals.ItemStatistics.Overall",
        "RunTotals.ItemStatistics.Items",
        "RunTotals.ItemStatistics.Groups",
        "RunTotals.ItemStatistics.RecentEventIds",
        "RunTotals.WeaponStatistics.Totals",
        "RunTotals.WeaponStatistics.Weapons",
        "RunTotals.WeaponStatistics.AmmunitionTypes",
        "RunTotals.WeaponStatistics.Capabilities",
        "RunTotals.WeaponStatistics.Weapon.Totals",
        "RunTotals.WeaponStatistics.Ammunition.Totals",
        "RunTotals.CombatStatistics.Totals",
        "RunTotals.CombatStatistics.Enemies",
        "RunTotals.CombatStatistics.Killers",
        "RunTotals.CombatStatistics.Families",
        "RunTotals.CombatStatistics.Causes",
        "RunTotals.CombatStatistics.Weapons",
        "RunTotals.CombatStatistics.Ammunition",
        "RunTotals.CombatStatistics.Ownership",
        "RunTotals.CombatStatistics.Capabilities",
        "RunTotals.CombatStatistics.Enemy.Totals",
        "RunTotals.EquipmentStatistics.Capabilities",
        "RunTotals.EquipmentStatistics.Items",
        "RunTotals.EquipmentStatistics.SelectedWeapons",
        "RunTotals.EquipmentStatistics.Loadouts",
        "RunTotals.EquipmentStatistics.TotemSets",
        "RunTotals.EquipmentStatistics.CombatAssociations",
        "RunTotals.EquipmentStatistics.Transitions",
        "RunTotals.EquipmentStatistics.TotemStates",
        "RunTotals.EquipmentStatistics.Slots",
        "RunTotals.EquipmentStatistics.SlottedWeapons",
        "RunTotals.ContainerStatistics.Capabilities",
        "RunTotals.ContainerStatistics.Capabilities.UniqueContainersLooted",
        "Map.Outcomes",
        "Map.ItemStatistics",
        "Map.WeaponStatistics",
        "Map.CombatStatistics",
        "Map.EquipmentStatistics",
        "Map.ContainerStatistics",
        "Map.Economy",
        "RouteMap.ItemStatistics",
        "RouteMap.WeaponStatistics",
        "RouteMap.CombatStatistics",
        "RouteMap.EquipmentStatistics",
        "RouteMap.ContainerStatistics",
        "RouteMap.Economy",
        "RunRecords.Extraction",
        "RunRecords.Death",
        "RunRecords.Maps",
        "RunRecordMap.Extraction",
        "RunRecordMap.Death",
        "Run.WeaponStatistics",
        "Run.CombatStatistics",
        "Run.EquipmentStatistics",
        "Run.ContainerStatistics",
        "Run.Economy",
        "Run.Segments",
        "Run.RouteCapabilities",
        "Run.RouteCapabilities.OrderedRoute",
        "Run.RouteCapabilities.Segments",
        "Run.RouteCapabilities.EventAttribution",
        "Run.RouteCapabilities.RouteAwareMapTotals",
        "Run.SegmentEventAssociations",
        "Run.ItemStatistics",
        "Segment.ItemStatistics",
        "Segment.WeaponStatistics",
        "Segment.CombatStatistics",
        "Segment.EquipmentStatistics",
        "Segment.ContainerStatistics",
        "Segment.Economy"
    };

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void SchemaEightMigrationPreservesPriorStatisticsAndMarksEveryEconomyScopeUnavailable()
    {
        var document = CreateCompleteCurrentSchemaDocument("generation-m8", revision: 81);
        document.SchemaVersion = 8;
        document.Statistics.SchemaVersion = 8;
        document.Statistics.Overall.ActivationCount = 17;
        document.Statistics.Economy = null!;
        document.DeferredItemPersistence!.AppliedLifetimeEconomy = null!;
        document.Statistics.RunTotals.Economy = null!;
        foreach (var map in document.Statistics.RunTotals.Maps.Values) map.Economy = null!;
        foreach (var map in document.Statistics.RunTotals.RouteMaps.Values) map.Economy = null!;
        foreach (var run in document.Statistics.Runs)
        {
            run.SchemaVersion = 8;
            run.ActiveDurationSeconds = 60;
            run.RouteSignature = "duckov:map:A";
            run.RouteCapabilities = RouteStatisticsReducer.Supported("test");
            run.Economy = null!;
            foreach (var segment in run.Segments)
            {
                segment.SegmentIndex = 0;
                segment.ActiveDurationSeconds = 60;
                segment.ExitReason = MapSegmentExitReason.Extracted;
                segment.Economy = null!;
            }
        }

        Assert.True(ProfileMigrator.Migrate(document));

        Assert.Equal(9, document.SchemaVersion);
        Assert.Equal(9, document.Statistics.SchemaVersion);
        Assert.Equal("generation-m8", document.GenerationId);
        Assert.Equal(17, document.Statistics.Overall.ActivationCount);
        var migratedMap = Assert.Single(document.Statistics.RunTotals.Maps).Value;
        var migratedRouteMap = Assert.Single(document.Statistics.RunTotals.RouteMaps).Value;
        var migratedRun = Assert.Single(document.Statistics.Runs);
        var migratedSegment = Assert.Single(migratedRun.Segments);
        var scopes = new[]
        {
            document.Statistics.Economy,
            document.Statistics.RunTotals.Economy,
            migratedMap.Economy,
            migratedRouteMap.Economy,
            migratedRun.Economy,
            migratedSegment.Economy
        };
        Assert.All(scopes, economy =>
        {
            Assert.True(economy.HistoricalUnavailable);
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, economy.Capabilities.MoneyAmountDirection.State);
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, economy.Capabilities.CashTerminalOutcomes.State);
            Assert.Contains("predates M9", economy.Capabilities.MoneyAmountDirection.Provenance, StringComparison.Ordinal);
            Assert.Empty(economy.Currencies);
        });
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Theory]
    [InlineData("negative-counter")]
    [InlineData("overlapping-raid-outcomes")]
    [InlineData("duplicate-deduplication-identity")]
    [InlineData("malformed-replay-cursor")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void CurrentSchemaUnsafeEconomyStateLosesToIntactBackupBeforeNormalization(string corruption)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        var invalidPrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        if (corruption == "negative-counter")
        {
            invalidPrimary.Statistics.Economy.Currencies["Money"].Totals.GrossInflow = -1;
            invalidPrimary.Statistics.Economy.Currencies["Money"].Sources["UnknownAdjustment"] =
                new CurrencyFlowTotals { GrossInflow = -1 };
            invalidPrimary.Statistics.Economy.Currencies["Money"].Contexts["Unknown"] =
                new CurrencyFlowTotals { GrossInflow = -1 };
        }
        else if (corruption == "overlapping-raid-outcomes")
        {
            invalidPrimary.Statistics.Economy.CashRaidOutcomes = new CashRaidOutcomeAggregate
            {
                Acquired = 5,
                Secured = 4,
                Lost = 4
            };
        }
        else if (corruption == "duplicate-deduplication-identity")
        {
            invalidPrimary.Statistics.Economy.RecentEventIds.Add("duplicate");
            invalidPrimary.Statistics.Economy.RecentEventIds.Add("duplicate");
        }
        else
        {
            invalidPrimary.Statistics.Economy.ReplayCursor = new EconomyReplayCursor
            {
                ActivationId = "malformed:activation",
                ClosedThroughSequence = -1
            };
        }
        store.Save(path, backup);
        store.Save(path, invalidPrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.Contains(result.LoadFailures, failure =>
            failure.Contains("contains invalid economy state", StringComparison.Ordinal));
        Assert.Equal(7, repository.Current.Revision);
        Assert.Equal(0, repository.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void ValidTemporaryProfileDefeatsMalformedReplayMetadataInPrimaryAndBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var malformedBackup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        malformedBackup.Statistics.Economy.ReplayCursor = new EconomyReplayCursor
        {
            ActivationId = "invalid:backup",
            ClosedThroughSequence = 1
        };
        var malformedPrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        malformedPrimary.Statistics.Economy.ReplayCursor = new EconomyReplayCursor
        {
            ActivationId = "invalid-primary",
            ClosedThroughSequence = -1
        };
        var validTemporary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 9);
        SetMoneyInflow(validTemporary.Statistics.Economy, 9);
        store.Save(path, malformedBackup);
        store.Save(path, malformedPrimary);
        store.Save(AtomicJsonPaths.GetTemporaryPath(path), validTemporary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.Equal(2, result.LoadFailures.Count(failure =>
            failure.Contains("invalid economy state", StringComparison.Ordinal)));
        Assert.Equal(9, repository.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(9, repository.Current.Revision);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void UnsaturatedSchemaNineCandidateCompactsLegacyIdentitiesWithoutChangingTotals()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var candidate = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        SetMoneyInflow(candidate.Statistics.Economy, 3);
        candidate.Statistics.Economy.RecentEventIds.AddRange(["legacy:1", "legacy:2", "legacy:3"]);
        candidate.Statistics.Economy.ReplayCursor = null;
        new AtomicJsonStore<ProfileDocument>().Save(path, candidate);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        var economy = repository.Current.Statistics.Economy;
        Assert.Equal(3, economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Empty(economy.RecentEventIds);
        Assert.False(economy.DeduplicationSaturated);
        Assert.False(economy.LegacyIdentitySaturationIncomplete);
        Assert.NotNull(economy.ReplayCursor);
        Assert.Equal(string.Empty, economy.ReplayCursor!.ActivationId);
        Assert.False(ProfileMigrator.CompactEconomyReplayEvidenceAfterRecovery(repository.Current));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void SaturatedSchemaNineCandidatePreservesExactTotalsAndResumesUnderANewActivation()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var candidate = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        SetMoneyInflow(candidate.Statistics.Economy, 2048);
        candidate.Statistics.Economy.RecentEventIds.AddRange(
            Enumerable.Range(1, 2048).Select(value => $"legacy:{value}"));
        candidate.Statistics.Economy.DeduplicationSaturated = true;
        candidate.Statistics.Economy.ReplayCursor = null;
        new AtomicJsonStore<ProfileDocument>().Save(path, candidate);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        var economy = repository.Current.Statistics.Economy;
        Assert.Equal(2048, economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Empty(economy.RecentEventIds);
        Assert.False(economy.DeduplicationSaturated);
        Assert.True(economy.LegacyIdentitySaturationIncomplete);

        repository.BeginEconomyActivation("corrected-activation");
        Assert.True(repository.Record(new CurrencyFlowRecorded
        {
            EventId = "corrected:1",
            TimestampUtc = TestTime,
            SaveGenerationId = repository.CurrentGenerationId,
            MapId = MapIdentity.UnknownId,
            Currency = CurrencyKind.Money,
            Direction = CurrencyFlowDirection.Inflow,
            Amount = 1,
            Source = CurrencySourceCategory.UnknownAdjustment,
            GameplayContext = GameplayContext.Base,
            ProducerActivationId = "corrected-activation",
            ProducerSequence = 1
        }));
        Assert.Equal(2049, economy.Currencies["Money"].Totals.GrossInflow);
        Assert.True(economy.LegacyIdentitySaturationIncomplete);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void ProcessRestartUsesANewActivationWithoutReopeningThePriorReplayWindow()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var first = CreateRepository(temporaryDirectory.Path, "session-first");
        first.Open(CreateIdentity(slot: 1, creationTicks: 100));
        var generation = first.CurrentGenerationId;
        Assert.True(first.BeginEconomyActivation("activation-a"));
        var firstFlow = EconomyFlow(generation, "activation-a", 1, CurrencyKind.Money);
        Assert.True(first.Record(firstFlow));
        Assert.False(first.Record(firstFlow));
        first.CloseClean();

        var second = CreateRepository(temporaryDirectory.Path, "session-second");
        second.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.True(second.BeginEconomyActivation("activation-b"));
        var secondFlow = EconomyFlow(generation, "activation-b", 1, CurrencyKind.Money);
        Assert.True(second.Record(secondFlow));
        Assert.False(second.Record(firstFlow));
        Assert.False(second.Record(secondFlow));
        Assert.Equal(2, second.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal("activation-b", second.Current.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(1, second.Current.Statistics.Economy.ReplayCursor.ClosedThroughSequence);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void RegisteredActivationSurvivesDeferredSnapshotBeforeItsFirstEvent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var first = CreateRepository(temporaryDirectory.Path, "session-first");
        first.Open(CreateIdentity(slot: 1, creationTicks: 100));
        var generation = first.CurrentGenerationId;

        Assert.True(first.BeginEconomyActivation("activation-before-event"));
        var snapshot = first.CapturePersistenceSnapshot();
        Assert.Equal(
            "activation-before-event",
            first.Current.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(0, first.Current.Statistics.Economy.ReplayCursor.ClosedThroughSequence);
        Assert.False(first.Current.Statistics.Economy.WasRepairedFromInvalidState);
        first.SaveSnapshot(snapshot);
        var persisted = new AtomicJsonStore<ProfileDocument>().Load(first.CurrentProfilePath!).Value!;
        Assert.Equal(
            "activation-before-event",
            persisted.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(0, persisted.Statistics.Economy.ReplayCursor.ClosedThroughSequence);
        Assert.False(persisted.Statistics.Economy.WasRepairedFromInvalidState);
        EconomyStatisticsReducer.Validate(persisted.Statistics.Economy);
        first.CloseClean();

        var second = CreateRepository(temporaryDirectory.Path, "session-second");
        second.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.Equal(
            "activation-before-event",
            second.Current.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(0, second.Current.Statistics.Economy.ReplayCursor.ClosedThroughSequence);

        Assert.True(second.BeginEconomyActivation("activation-after-restart"));
        var restartedSnapshot = second.CapturePersistenceSnapshot();
        Assert.Equal(
            "activation-after-restart",
            second.Current.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(0, second.Current.Statistics.Economy.ReplayCursor.ClosedThroughSequence);
        second.SaveSnapshot(restartedSnapshot);
        var restartedPersisted = new AtomicJsonStore<ProfileDocument>().Load(second.CurrentProfilePath!).Value!;
        Assert.Equal(
            "activation-after-restart",
            restartedPersisted.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(0, restartedPersisted.Statistics.Economy.ReplayCursor.ClosedThroughSequence);
        Assert.True(second.Record(EconomyFlow(
            generation,
            "activation-after-restart",
            1,
            CurrencyKind.Money)));
        Assert.False(second.Record(EconomyFlow(
            generation,
            "activation-before-event",
            1,
            CurrencyKind.Money)));
        Assert.Equal(1, second.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void SaveSlotRotationKeepsEachGenerationsReplayCursorAndTotalsIndependent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>(["generation-slot-1", "session-slot-1", "generation-slot-2", "session-slot-2", "session-slot-1-reopen"]);
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.True(repository.BeginEconomyActivation("activation-slot-1"));
        Assert.True(repository.Record(EconomyFlow(
            repository.CurrentGenerationId,
            "activation-slot-1",
            1,
            CurrencyKind.Money)));

        repository.Rotate(CreateIdentity(slot: 2, creationTicks: 200), "DuckovNewGame");
        Assert.True(repository.BeginEconomyActivation("activation-slot-2"));
        Assert.True(repository.Record(EconomyFlow(
            repository.CurrentGenerationId,
            "activation-slot-2",
            1,
            CurrencyKind.Cash)));
        Assert.False(repository.Current.Statistics.Economy.Currencies.ContainsKey("Money"));
        Assert.Equal(1, repository.Current.Statistics.Economy.Currencies["Cash"].Totals.GrossInflow);
        repository.CloseClean();

        var reopened = CreateRepository(temporaryDirectory.Path, ids);
        reopened.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.Equal(1, reopened.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.False(reopened.Current.Statistics.Economy.Currencies.ContainsKey("Cash"));
        Assert.Equal("activation-slot-1", reopened.Current.Statistics.Economy.ReplayCursor!.ActivationId);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void CurrentSchemaRepairableEconomyBreakdownKeysRemainEligibleForNormalization()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        var repairablePrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        var money = repairablePrimary.Statistics.Economy.Currencies["Money"];
        money.Totals.GrossInflow = 9;
        money.Sources["NotASource"] = new CurrencyFlowTotals { GrossInflow = 9 };
        money.Contexts["NotAContext"] = new CurrencyFlowTotals { GrossInflow = 9 };
        store.Save(path, backup);
        store.Save(path, repairablePrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        var normalized = repository.Current.Statistics.Economy;
        Assert.Equal(9, normalized.Currencies["Money"].Sources["UnknownAdjustment"].GrossInflow);
        Assert.Equal(9, normalized.Currencies["Money"].Contexts["Unknown"].GrossInflow);
        Assert.True(normalized.WasRepairedFromInvalidState);
        repository.CloseClean();
    }

    public static TheoryData<string> CurrentSchemaNonRepairableNullDictionaryRowCases => new()
    {
        "Statistics.Items",
        "Statistics.Groups",
        "RunTotals.Maps",
        "RunTotals.RouteMaps",
        "RunRecords.Maps",
        "Statistics.Economy.Currencies",
        "RunTotals.ItemStatistics.Items"
    };

    [Theory]
    [MemberData(nameof(CurrentSchemaRequiredRootCases))]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaMissingRequiredRootLosesToIntactBackupBeforeMigration(string missingRoot)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        var incompletePrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        RemoveRequiredRoot(incompletePrimary, missingRoot);
        store.Save(path, backup);
        store.Save(path, incompletePrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.Contains(result.LoadFailures, failure =>
            failure.Contains("Current-schema profile roots are incomplete.", StringComparison.Ordinal));
        Assert.Equal(7, repository.Current.Revision);
        Assert.Equal(37, repository.Current.Statistics.RunTotals.Maps["duckov:map:A"].TotalRuns);
        repository.CloseClean();

        var persisted = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate).Value!;
        Assert.Equal(7, persisted.Revision);
        Assert.Equal(37, persisted.Statistics.RunTotals.Maps["duckov:map:A"].TotalRuns);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void DeferredLifetimeWatermarkThatExceedsLifetimeTotalsLosesToIntactBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        var invalidPrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        invalidPrimary.DeferredItemPersistence!.RunId = "run-active";
        invalidPrimary.DeferredItemPersistence.AppliedLifetimeStatistics.Overall.ActivationCount = 1;
        invalidPrimary.DeferredItemPersistence.AppliedLifetimeStatistics.Items["duckov:item:test"] = new ItemAggregate
        {
            ItemId = "duckov:item:test",
            DisplayName = "Test item",
            Totals = new AggregateTotals { ActivationCount = 1 }
        };
        invalidPrimary.DeferredItemPersistence.AppliedLifetimeStatistics.Groups[nameof(CanonicalItemGroup.Healing)] =
            new AggregateTotals { ActivationCount = 1 };
        store.Save(path, backup);
        store.Save(path, invalidPrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.Contains(result.LoadFailures, failure =>
            failure.Contains("watermark is not a valid subset", StringComparison.Ordinal));
        Assert.Equal(7, repository.Current.Revision);
        repository.CloseClean();
    }

    [Theory]
    [InlineData("group")]
    [InlineData("item-identity")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void CompositionallyInvalidDeferredLifetimeWatermarkLosesToIntactBackup(string corruption)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        var invalidPrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        var applied = invalidPrimary.DeferredItemPersistence!.AppliedLifetimeStatistics;
        invalidPrimary.DeferredItemPersistence.RunId = "run-active";
        applied.Overall.AmountsByUnit["Item"] = 1;
        applied.Items["duckov:item:test"] = new ItemAggregate
        {
            ItemId = corruption == "item-identity" ? "duckov:item:wrong" : "duckov:item:test",
            DisplayName = "Test item",
            Totals = new AggregateTotals { AmountsByUnit = new() { ["Item"] = 1 } }
        };
        applied.Groups[nameof(CanonicalItemGroup.OtherUnknown)] = new AggregateTotals
        {
            AmountsByUnit = new() { ["Item"] = corruption == "group" ? 2 : 1 }
        };
        store.Save(path, backup);
        store.Save(path, invalidPrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.Contains(result.LoadFailures, failure =>
            failure.Contains("watermark is compositionally inconsistent", StringComparison.Ordinal));
        Assert.Equal(7, repository.Current.Revision);
        repository.CloseClean();
    }

    [Theory]
    [MemberData(nameof(CurrentSchemaNonRepairableNullDictionaryRowCases))]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaNonRepairableNullDictionaryRowLosesToIntactBackupBeforeMigration(string dictionary)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        var incompletePrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        AddNullDictionaryRow(incompletePrimary, dictionary);
        store.Save(path, backup);
        store.Save(path, incompletePrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.Contains(result.LoadFailures, failure =>
            failure.Contains("Current-schema profile roots are incomplete.", StringComparison.Ordinal));
        Assert.Equal(7, repository.Current.Revision);
        Assert.Equal(37, repository.Current.Statistics.RunTotals.Maps["duckov:map:A"].TotalRuns);
        repository.CloseClean();

        var persisted = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate).Value!;
        Assert.Equal(7, persisted.Revision);
        Assert.Equal(37, persisted.Statistics.RunTotals.Maps["duckov:map:A"].TotalRuns);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaExplicitlyRepairableNullDictionaryRowsRemainEligibleForNormalization()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var backup = CreateCompleteCurrentSchemaDocument("generation-a", revision: 7);
        var repairablePrimary = CreateCompleteCurrentSchemaDocument("generation-a", revision: 8);
        repairablePrimary.Statistics.RunTotals.WeaponStatistics.Weapons["weapon:null"] = null!;
        repairablePrimary.Statistics.RunTotals.CombatStatistics.Enemies["enemy:null"] = null!;
        repairablePrimary.Statistics.RunTotals.EquipmentStatistics.Items["equipment:null"] = null!;
        repairablePrimary.Statistics.RunTotals.ItemStatistics.Groups["group:null"] = null!;
        store.Save(path, backup);
        store.Save(path, repairablePrimary);

        var repository = CreateRepository(temporaryDirectory.Path, "session-new");
        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.False(result.RecoveredSnapshot);
        Assert.True(result.MigratedSchema);
        Assert.Equal(8, repository.Current.Revision);
        Assert.DoesNotContain("weapon:null", repository.Current.Statistics.RunTotals.WeaponStatistics.Weapons.Keys);
        Assert.DoesNotContain("enemy:null", repository.Current.Statistics.RunTotals.CombatStatistics.Enemies.Keys);
        Assert.DoesNotContain("equipment:null", repository.Current.Statistics.RunTotals.EquipmentStatistics.Items.Keys);
        Assert.DoesNotContain("group:null", repository.Current.Statistics.RunTotals.ItemStatistics.Groups.Keys);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentProfileSemanticSelectionAllowsHistoricalRunSchemaProvenance()
    {
        var profile = CreateDocument("generation-a", revision: 7);
        profile.Statistics.Runs.Add(new RunSummary { SchemaVersion = 6 });

        Assert.Null(ProfileMigrator.ValidateRecoveryCandidate(profile));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryMigratesEveryV01AggregateWithoutLosingUsageStatistics()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-v01", revision: 12);
        legacy.SchemaVersion = 1;
        legacy.Statistics.SchemaVersion = 1;
        legacy.Statistics.Overall.ActivationCount = 3;
        legacy.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.StackUnit)] = 3;
        legacy.Statistics.Items["item:a"] = new()
        {
            ItemId = "item:a",
            DisplayName = "Legacy medkit",
            Group = CanonicalItemGroup.Healing,
            Totals = new()
            {
                ActivationCount = 3,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.StackUnit)] = 3 }
            }
        };
        legacy.Statistics.Groups[nameof(CanonicalItemGroup.Healing)] = new()
        {
            ActivationCount = 3,
            AmountsByUnit = new() { [nameof(ConsumptionUnit.StackUnit)] = 3 }
        };
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(9, repository.Current.SchemaVersion);
        Assert.Equal(9, repository.Current.Statistics.SchemaVersion);
        Assert.Equal(3, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(3, repository.Current.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.StackUnit)]);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(3, repository.Current.Statistics.Items["item:a"].Totals.ActivationCount);
        Assert.Empty(repository.Current.Statistics.Runs);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.TotalRuns);
        Assert.Null(repository.Current.Statistics.RunRecords.Extraction.Shortest);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Migration")]
    public void RepositoryMigratesSchemaTwoWithoutChangingM1M2DataCapabilitiesOrArchives()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var slotDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01");
        var path = System.IO.Path.Combine(slotDirectory, "current", "profile.json");
        var archivePath = System.IO.Path.Combine(slotDirectory, "archives", "historical-generation", "profile.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(archivePath)!);
        File.WriteAllText(archivePath, "historical archive bytes");
        File.SetAttributes(archivePath, File.GetAttributes(archivePath) | FileAttributes.ReadOnly);
        var archiveHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)));

        var legacy = CreateDocument("generation-v02", revision: 41);
        legacy.SchemaVersion = 2;
        legacy.Statistics.SchemaVersion = 2;
        legacy.InterruptedSessionCount = 3;
        legacy.Statistics.Overall.ActivationCount = 4;
        legacy.Statistics.Overall.ActualHealthRestored = 72.5;
        legacy.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.Durability)] = 50;
        legacy.Statistics.Groups[nameof(CanonicalItemGroup.Healing)] = new AggregateTotals
        {
            ActivationCount = 4,
            ActualHealthRestored = 72.5,
            AmountsByUnit = new() { [nameof(ConsumptionUnit.Durability)] = 50 }
        };
        legacy.Statistics.Items["item:medkit"] = new ItemAggregate
        {
            ItemId = "item:medkit",
            DisplayName = "Medkit",
            Group = CanonicalItemGroup.Healing,
            EffectTags = new() { ItemEffectTag.Healing },
            Totals = new AggregateTotals
            {
                ActivationCount = 4,
                ActualHealthRestored = 72.5,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.Durability)] = 50 }
            }
        };
        legacy.Statistics.RecentEventIds.AddRange(SchemaTwoRecentEventIds);
        legacy.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = "native-healing-attribution",
            State = AdapterCapabilityState.Supported,
            Version = "native-healing-attribution/2.3.30"
        });
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-v02", repository.Current.GenerationId);
        Assert.Equal(41, repository.Current.Revision);
        Assert.Equal(3, repository.Current.InterruptedSessionCount);
        Assert.Equal(4, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(72.5, repository.Current.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(50, repository.Current.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.Durability)]);
        Assert.Equal(4, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
        Assert.Equal(72.5, repository.Current.Statistics.Items["item:medkit"].Totals.ActualHealthRestored);
        Assert.Equal(SchemaTwoRecentEventIds, repository.Current.Statistics.RecentEventIds);
        Assert.Equal("native-healing-attribution", Assert.Single(repository.Current.Capabilities).AdapterId);
        Assert.Empty(repository.Current.Statistics.Runs);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.TotalRuns);
        Assert.Equal(archiveHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath))));
        Assert.True(File.GetAttributes(archivePath).HasFlag(FileAttributes.ReadOnly));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Migration")]
    [Trait("Category", "Weapon")]
    public void RepositoryMigratesSchemaThreeWithoutChangingM1ToM3Data()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-v03", revision: 73);
        legacy.SchemaVersion = 3;
        legacy.Statistics.SchemaVersion = 3;
        legacy.InterruptedSessionCount = 2;
        legacy.Statistics.Overall.ActivationCount = 5;
        legacy.Statistics.Overall.ActualHealthRestored = 24;
        legacy.Statistics.RunTotals.TotalRuns = 1;
        legacy.Statistics.RunTotals.PhysicalDistance = 120;
        legacy.Statistics.RunTotals.TeleportDistance = 3;
        legacy.Statistics.RunTotals.Outcomes[nameof(RunOutcome.Extracted)] = 1;
        legacy.Statistics.RunTotals.WeaponStatistics = null!;
        legacy.Statistics.RunTotals.Maps["duckov:map:test"] = new MapRunAggregate
        {
            MapId = "duckov:map:test",
            DisplayName = "Test map",
            IsKnown = true,
            TotalRuns = 1,
            PhysicalDistance = 120,
            TeleportDistance = 3,
            Outcomes = new() { [nameof(RunOutcome.Extracted)] = 1 },
            WeaponStatistics = null!
        };
        legacy.Statistics.Runs.Add(new RunSummary
        {
            RunId = "run-v03",
            SaveGenerationId = "generation-v03",
            MapId = "duckov:map:test",
            MapDisplayName = "Test map",
            MapKnown = true,
            StartedUtc = TestTime.AddMinutes(-2),
            EndedUtc = TestTime,
            ActiveDurationSeconds = 90,
            WallClockDurationSeconds = 120,
            Outcome = RunOutcome.Extracted,
            PhysicalDistance = 120,
            TeleportDistance = 3,
            RecordEligible = true,
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported,
            MapCapability = AdapterCapabilityState.Supported,
            WeaponStatistics = null!
        });
        legacy.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = RunStatisticsViewModelFactory.MovementAdapterId,
            State = AdapterCapabilityState.Supported,
            Version = "native-main-duck-movement/2.3.30"
        });
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(9, repository.Current.SchemaVersion);
        Assert.Equal(9, repository.Current.Statistics.SchemaVersion);
        Assert.Equal("generation-v03", repository.Current.GenerationId);
        Assert.Equal(73, repository.Current.Revision);
        Assert.Equal(2, repository.Current.InterruptedSessionCount);
        Assert.Equal(5, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(24, repository.Current.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(1, repository.Current.Statistics.RunTotals.TotalRuns);
        Assert.Equal(120, repository.Current.Statistics.RunTotals.PhysicalDistance);
        Assert.Equal(3, repository.Current.Statistics.RunTotals.TeleportDistance);
        Assert.Equal("run-v03", Assert.Single(repository.Current.Statistics.Runs).RunId);
        Assert.Equal("native-main-duck-movement", Assert.Single(repository.Current.Capabilities).AdapterId);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Empty(repository.Current.Statistics.RunTotals.WeaponStatistics.Weapons);
        Assert.Empty(repository.Current.Statistics.RunTotals.WeaponStatistics.AmmunitionTypes);
        Assert.NotNull(repository.Current.Statistics.RunTotals.Maps["duckov:map:test"].WeaponStatistics);
        Assert.NotNull(repository.Current.Statistics.Runs[0].WeaponStatistics);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Migration")]
    [Trait("Category", "Weapon")]
    public void RepositoryPersistsInvalidLifetimeRepairMarkerAcrossReopen()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(
            temporaryDirectory.Path,
            "profiles",
            "slot-01",
            "current",
            "profile.json");
        var profile = CreateDocument("generation-repaired", revision: 9);
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.FiringActions,
            State = AdapterCapabilityState.Supported,
            Version = ProductInfo.Version
        });
        profile.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions = -7;
        new AtomicJsonStore<ProfileDocument>().Save(path, profile);

        var first = CreateRepository(temporaryDirectory.Path, "session-first");
        var firstResult = first.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(firstResult.MigratedSchema);
        Assert.Equal(0, first.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.True(first.Current.Statistics.RunTotals.WeaponStatistics.WasRepairedFromInvalidState);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            WeaponStatisticsViewModelFactory.Create(first.Current).Capabilities.FiringActions.State);
        first.CloseClean();

        var second = CreateRepository(temporaryDirectory.Path, "session-second");
        var secondResult = second.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.False(secondResult.MigratedSchema);
        Assert.True(second.Current.Statistics.RunTotals.WeaponStatistics.WasRepairedFromInvalidState);
        Assert.False(WeaponStatisticsReducer.IsEmpty(
            second.Current.Statistics.RunTotals.WeaponStatistics));
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            WeaponStatisticsViewModelFactory.Create(second.Current).Capabilities.FiringActions.State);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Migration")]
    [Trait("Category", "Weapon")]
    public void SerializerRepositoryBackupAndRotationPreserveIdentityEntryRepair()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(
            temporaryDirectory.Path,
            "profiles",
            "slot-01",
            "current",
            "profile.json");
        var profile = CreateDocument("generation-corrupt-identities", revision: 10);
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.FiringActions,
            State = AdapterCapabilityState.Supported,
            Version = ProductInfo.Version
        });
        var lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        lifetime.Weapons["weapon:null"] = null!;
        lifetime.Weapons[string.Empty] = new WeaponAggregate { WeaponId = "weapon:empty" };
        lifetime.Weapons[" \t"] = new WeaponAggregate { WeaponId = "weapon:whitespace" };
        lifetime.AmmunitionTypes["ammo:null"] = null!;
        lifetime.AmmunitionTypes[string.Empty] = new AmmunitionAggregate { AmmunitionId = "ammo:empty" };
        lifetime.AmmunitionTypes[" \t"] = new AmmunitionAggregate { AmmunitionId = "ammo:whitespace" };
        var store = new AtomicJsonStore<ProfileDocument>();
        store.Save(path, profile);

        var roundTripped = store.Load(path).Value!;
        Assert.Null(roundTripped.Statistics.RunTotals.WeaponStatistics.Weapons["weapon:null"]);
        Assert.True(roundTripped.Statistics.RunTotals.WeaponStatistics.Weapons.ContainsKey(string.Empty));
        Assert.True(roundTripped.Statistics.RunTotals.WeaponStatistics.Weapons.ContainsKey(" \t"));
        Assert.Null(roundTripped.Statistics.RunTotals.WeaponStatistics.AmmunitionTypes["ammo:null"]);
        Assert.True(roundTripped.Statistics.RunTotals.WeaponStatistics.AmmunitionTypes.ContainsKey(string.Empty));
        Assert.True(roundTripped.Statistics.RunTotals.WeaponStatistics.AmmunitionTypes.ContainsKey(" \t"));

        var first = CreateRepository(temporaryDirectory.Path, "session-first");
        var firstResult = first.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(firstResult.MigratedSchema);
        Assert.Empty(first.Current.Statistics.RunTotals.WeaponStatistics.Weapons);
        Assert.Empty(first.Current.Statistics.RunTotals.WeaponStatistics.AmmunitionTypes);
        Assert.True(first.Current.Statistics.RunTotals.WeaponStatistics.WasRepairedFromInvalidState);
        Assert.False(WeaponStatisticsReducer.IsEmpty(
            first.Current.Statistics.RunTotals.WeaponStatistics));
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            WeaponStatisticsViewModelFactory.Create(first.Current).Capabilities.FiringActions.State);
        first.CloseClean();

        var repaired = store.Load(path).Value!;
        store.Save(path, repaired);
        File.WriteAllText(path, "{ corrupt-primary");
        var second = CreateRepository(temporaryDirectory.Path, "session-second");
        var secondResult = second.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(secondResult.RecoveredSnapshot);
        Assert.True(second.Current.Statistics.RunTotals.WeaponStatistics.WasRepairedFromInvalidState);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            WeaponStatisticsViewModelFactory.Create(second.Current).Capabilities.FiringActions.State);
        second.CloseClean();

        var ids = new Queue<string>();
        ids.Enqueue("generation-after-rotation");
        ids.Enqueue("session-after-rotation");
        var third = CreateRepository(temporaryDirectory.Path, ids);
        var thirdResult = third.Open(CreateIdentity(slot: 1, creationTicks: 999));

        Assert.True(thirdResult.RotatedGeneration);
        Assert.False(third.Current.Statistics.RunTotals.WeaponStatistics.WasRepairedFromInvalidState);
        var archive = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        var archived = store.Load(System.IO.Path.Combine(archive, "profile.json")).Value!;
        Assert.True(archived.Statistics.RunTotals.WeaponStatistics.WasRepairedFromInvalidState);
        third.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Healing")]
    public void RepositoryRepairsPreReleaseDelayedHealingGroupWithoutChangingGenerationOrTotals()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var profile = CreateDocument("generation-schema-2", revision: 58);
        profile.Statistics.Overall.ActivationCount = 2;
        profile.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.Durability)] = 50;
        profile.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.StackUnit)] = 1;
        profile.Statistics.Overall.ActualHealthRestored = 60;
        profile.Statistics.Items["item:water"] = new()
        {
            ItemId = "item:water",
            DisplayName = "Water",
            Group = CanonicalItemGroup.Drink,
            EffectTags = new List<ItemEffectTag> { ItemEffectTag.Drink },
            Totals = new()
            {
                ActivationCount = 1,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.Durability)] = 50 }
            }
        };
        profile.Statistics.Items["item:injector"] = new()
        {
            ItemId = "item:injector",
            DisplayName = "Recovery Injector",
            Group = CanonicalItemGroup.Drink,
            EffectTags = new List<ItemEffectTag> { ItemEffectTag.Drink, ItemEffectTag.Buff },
            Totals = new()
            {
                ActivationCount = 1,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.StackUnit)] = 1 },
                ActualHealthRestored = 60
            }
        };
        profile.Statistics.Groups[nameof(CanonicalItemGroup.Drink)] = new()
        {
            ActivationCount = 2,
            AmountsByUnit = new()
            {
                [nameof(ConsumptionUnit.Durability)] = 50,
                [nameof(ConsumptionUnit.StackUnit)] = 1
            },
            ActualHealthRestored = 60
        };
        new AtomicJsonStore<ProfileDocument>().Save(path, profile);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal("generation-schema-2", repository.Current.GenerationId);
        Assert.Equal(2, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(60, repository.Current.Statistics.Overall.ActualHealthRestored);
        var injector = repository.Current.Statistics.Items["item:injector"];
        Assert.Equal(CanonicalItemGroup.Healing, injector.Group);
        Assert.Contains(ItemEffectTag.Healing, injector.EffectTags);
        Assert.Equal(1, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Drink)].ActivationCount);
        Assert.Equal(0, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Drink)].ActualHealthRestored);
        Assert.Equal(1, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
        Assert.Equal(60, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Healing)].ActualHealthRestored);
        Assert.Equal(
            repository.Current.Statistics.Overall.ActivationCount,
            repository.Current.Statistics.Groups.Values.Sum(group => group.ActivationCount));
        Assert.Equal(
            repository.Current.Statistics.Overall.ActualHealthRestored,
            repository.Current.Statistics.Groups.Values.Sum(group => group.ActualHealthRestored));
        repository.CloseClean();

        var persisted = new AtomicJsonStore<ProfileDocument>().Load(path).Value!;
        Assert.Equal("generation-schema-2", persisted.GenerationId);
        Assert.Equal(CanonicalItemGroup.Healing, persisted.Statistics.Items["item:injector"].Group);
        Assert.Equal(60, persisted.Statistics.Overall.ActualHealthRestored);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryRecoversAndMigratesV01BackupSnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var legacy = CreateDocument("generation-v01-backup", revision: 8);
        legacy.SchemaVersion = 1;
        legacy.Statistics.SchemaVersion = 1;
        legacy.Statistics.Overall.ActivationCount = 2;
        store.Save(path, legacy);
        store.Save(path, CreateDocument("generation-discarded", revision: 9));
        File.WriteAllText(path, "{ corrupt-primary");
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-v01-backup", repository.Current.GenerationId);
        Assert.Equal(2, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActualHealthRestored);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryRecoversAndMigratesV01TemporarySnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-v01-temporary", revision: 6);
        legacy.SchemaVersion = 1;
        legacy.Statistics.SchemaVersion = 1;
        legacy.Statistics.Overall.ActivationCount = 4;
        new AtomicJsonStore<ProfileDocument>().Save(AtomicJsonPaths.GetTemporaryPath(path), legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-v01-temporary", repository.Current.GenerationId);
        Assert.Equal(4, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActualHealthRestored);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryNormalizesMissingLegacyFieldsBeforeIdentityChecks()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-legacy", revision: 4);
        legacy.SchemaVersion = 0;
        legacy.Identity = null!;
        legacy.Statistics = null!;
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var ids = new Queue<string>();
        ids.Enqueue("session-new");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false });

        Assert.True(result.MigratedSchema);
        Assert.False(result.RotatedGeneration);
        Assert.Equal("generation-legacy", repository.Current.GenerationId);
        Assert.NotNull(repository.Current.Identity);
        Assert.NotNull(repository.Current.Statistics);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void UnsupportedProfileSchemaIsArchivedByteForByteWithoutDowngrade()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var future = CreateDocument("generation-future", revision: 9);
        future.SchemaVersion = ProductInfo.SchemaVersion + 1;
        new AtomicJsonStore<ProfileDocument>().Save(path, future);
        var raw = File.ReadAllText(path).Insert(1, "\"FutureData\":{\"keep\":\"verbatim\"},");
        File.WriteAllText(path, raw);
        var ids = new Queue<string>();
        ids.Enqueue("generation-safe");
        ids.Enqueue("session-safe");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.UnsupportedSchemaArchived);
        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-safe", repository.Current.GenerationId);
        var archive = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        var archivedProfile = System.IO.Path.Combine(archive, "profile.json");
        Assert.Equal(raw, File.ReadAllText(archivedProfile));
        Assert.True((File.GetAttributes(archivedProfile) & FileAttributes.ReadOnly) != 0);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void UnsupportedStatisticsSchemaIsArchivedWithoutBeingSaved()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var future = CreateDocument("generation-future", revision: 9);
        future.Statistics.SchemaVersion = ProductInfo.SchemaVersion + 1;
        new AtomicJsonStore<ProfileDocument>().Save(path, future);
        var raw = File.ReadAllText(path);
        var ids = new Queue<string>();
        ids.Enqueue("generation-safe");
        ids.Enqueue("session-safe");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.UnsupportedSchemaArchived);
        var archive = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        Assert.Equal(raw, File.ReadAllText(System.IO.Path.Combine(archive, "profile.json")));
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.Statistics.SchemaVersion);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SaveGuardNeverDowngradesAnUnsupportedCurrentDocument()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-a");
        ids.Enqueue("session-a");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        var profilePath = repository.CurrentProfilePath!;
        var before = File.ReadAllText(profilePath);
        repository.Current.SchemaVersion = ProductInfo.SchemaVersion + 1;

        Assert.Throws<NotSupportedException>(() => repository.Flush());

        Assert.Equal(before, File.ReadAllText(profilePath));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SaveSlotsRemainIsolated()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-one");
        ids.Enqueue("session-one");
        ids.Enqueue("generation-two");
        ids.Enqueue("session-two");
        var repository = CreateRepository(temporaryDirectory.Path, () => ids.Dequeue());
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.True(repository.Record(CreateUse("event-one", "generation-one")));
        repository.CloseClean();

        repository.Open(CreateIdentity(slot: 2, creationTicks: 200));

        Assert.Equal("generation-two", repository.Current.GenerationId);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActivationCount);
        Assert.True(File.Exists(System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-02", "current", "profile.json")));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void DeferredProfileSnapshotIsIsolatedFromLaterItemMutationsAndPersistsExactRevision()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-one");
        ids.Enqueue("session-one");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        repository.EnableDeferredItemPersistence();
        var profilePath = repository.CurrentProfilePath!;

        Assert.True(repository.RecordDeferred(CreateUse("event-one", "generation-one", runId: "run-one")));
        Assert.True(repository.RecordDeferred(CreateHealing(
            "healing-one",
            "event-one",
            "generation-one",
            12.5,
            runId: "run-one")));

        var beforeDeferredSave = new AtomicJsonStore<ProfileDocument>().Load(profilePath).Value!;
        Assert.Equal(0, beforeDeferredSave.Statistics.Overall.ActivationCount);
        Assert.Equal(0, beforeDeferredSave.Statistics.Overall.ActualHealthRestored);

        var snapshot = repository.CapturePersistenceSnapshot();
        Assert.True(repository.RecordDeferred(CreateUse("event-two", "generation-one", runId: "run-one")));
        repository.SaveSnapshot(snapshot);

        var persistedSnapshot = new AtomicJsonStore<ProfileDocument>().Load(profilePath).Value!;
        Assert.Equal(snapshot.Revision, persistedSnapshot.Revision);
        Assert.Equal(1, persistedSnapshot.Statistics.Overall.ActivationCount);
        Assert.Equal(12.5, persistedSnapshot.Statistics.Overall.ActualHealthRestored, precision: 6);
        Assert.DoesNotContain("event-two", persistedSnapshot.Statistics.RecentEventIds);
        Assert.Equal(2, repository.Current.Statistics.Overall.ActivationCount);

        repository.Flush();
        var persistedCurrent = new AtomicJsonStore<ProfileDocument>().Load(profilePath).Value!;
        Assert.Equal(2, persistedCurrent.Statistics.Overall.ActivationCount);
        Assert.Contains("event-two", persistedCurrent.Statistics.RecentEventIds);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void DeferredItemPersistenceModeFollowsSlotTransitionsAndGenerationRotations()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-two");
        ids.Enqueue("session-two");
        ids.Enqueue("generation-one");
        ids.Enqueue("session-one");
        ids.Enqueue("session-two-reopened");
        ids.Enqueue("generation-three");
        ids.Enqueue("session-three");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        repository.Open(CreateIdentity(slot: 2, creationTicks: 200));
        Assert.Null(repository.Current.DeferredItemPersistence);
        repository.CloseClean();

        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        repository.EnableDeferredItemPersistence();
        Assert.NotNull(repository.Current.DeferredItemPersistence);

        repository.Open(CreateIdentity(slot: 2, creationTicks: 200), "SaveSlotSelected");
        Assert.Equal("generation-two", repository.Current.GenerationId);
        Assert.NotNull(repository.Current.DeferredItemPersistence);
        Assert.False(repository.CanDeferItemPersistence(null));
        Assert.True(repository.CanDeferItemPersistence("run-two"));
        var completed = new RunSummary { RunId = "run-completed" };
        repository.Current.Statistics.Runs.Add(completed);
        Assert.False(repository.CanDeferItemPersistence(completed.RunId));
        repository.Current.Statistics.Runs.Remove(completed);
        Assert.True(repository.RecordDeferred(CreateUse(
            "event-two",
            "generation-two",
            runId: "run-two")));
        repository.EnableDeferredItemPersistence();
        Assert.Equal("run-two", repository.Current.DeferredItemPersistence!.RunId);
        Assert.Equal(
            1,
            repository.Current.DeferredItemPersistence.AppliedLifetimeStatistics.Overall.ActivationCount);

        repository.Rotate(CreateIdentity(slot: 2, creationTicks: 200), "DuckovNewGame");
        Assert.Equal("generation-three", repository.Current.GenerationId);
        Assert.NotNull(repository.Current.DeferredItemPersistence);
        Assert.True(repository.RecordDeferred(CreateUse(
            "event-three",
            "generation-three",
            runId: "run-three")));
        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());

        var persisted = new AtomicJsonStore<ProfileDocument>().Load(repository.CurrentProfilePath!).Value!;
        Assert.Equal(1, persisted.Statistics.Overall.ActivationCount);
        Assert.Equal("run-three", persisted.DeferredItemPersistence!.RunId);
        Assert.Equal(1, persisted.DeferredItemPersistence.AppliedLifetimeStatistics.Overall.ActivationCount);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RuntimeCapabilitiesFollowSlotTransitionsAndGenerationRotations()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-one");
        ids.Enqueue("session-one");
        ids.Enqueue("generation-two");
        ids.Enqueue("session-two");
        ids.Enqueue("generation-three");
        ids.Enqueue("session-three");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        var capabilities = new[]
        {
            new CapabilityRecord
            {
                AdapterId = "native-item-use",
                State = AdapterCapabilityState.Supported,
                Version = "native-item-use/2.3.30",
                Detail = "public native hooks"
            }
        };

        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        repository.SetCapabilities(capabilities);
        Assert.Equal("native-item-use", Assert.Single(repository.Current.Capabilities).AdapterId);

        repository.Open(CreateIdentity(slot: 2, creationTicks: 200), "SaveSlotSelected");
        Assert.Equal("native-item-use", Assert.Single(repository.Current.Capabilities).AdapterId);

        repository.Rotate(CreateIdentity(slot: 2, creationTicks: 200), "DuckovNewGame");
        var rotatedCapability = Assert.Single(repository.Current.Capabilities);
        Assert.Equal(AdapterCapabilityState.Supported, rotatedCapability.State);
        Assert.Equal("native-item-use/2.3.30", rotatedCapability.Version);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void ReusedSaveSlotArchivesOldGenerationReadOnlyAndStartsAtZero()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        first.Open(CreateIdentity(slot: 1, creationTicks: 100));
        first.Record(CreateUse("event-one", "generation-old"));
        first.Record(CreateHealing("healing-one", "event-one", "generation-old", 12.5));
        var firstGenerationDirectory = System.IO.Path.GetDirectoryName(first.CurrentProfilePath)!;
        new DiagnosticStore(
            System.IO.Path.Combine(firstGenerationDirectory, "diagnostics.json"),
            capacity: 5,
            () => TestTime).Add("old-generation evidence");
        first.CloseClean();

        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);
        var result = second.Open(CreateIdentity(slot: 1, creationTicks: 999));

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, second.Current.Statistics.Overall.ActualHealthRestored);
        var archive = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        Assert.Contains("generation-old", archive, StringComparison.Ordinal);
        var archivedProfile = System.IO.Path.Combine(archive, "profile.json");
        var archivedDiagnostics = System.IO.Path.Combine(archive, "diagnostics.json");
        Assert.True((File.GetAttributes(archivedProfile) & FileAttributes.ReadOnly) != 0);
        Assert.True((File.GetAttributes(archivedDiagnostics) & FileAttributes.ReadOnly) != 0);
        Assert.Equal(
            "old-generation evidence",
            Assert.Single(new AtomicJsonStore<DiagnosticsDocument>().Load(archivedDiagnostics).Value!.Entries).Message);
        var archived = new AtomicJsonStore<ProfileDocument>().Load(archivedProfile).Value!;
        Assert.Equal(1, archived.Statistics.Overall.ActivationCount);
        Assert.Equal(12.5, archived.Statistics.Overall.ActualHealthRestored, precision: 6);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void ReusedSaveWithUnchangedCreationTimestampButDifferentFingerprintRotates()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-old"));
        first.PrepareForNativeSave(original);

        var replacement = CreateIdentity(slot: 1, creationTicks: 100);
        replacement.ContentSha256 = new string('f', 64);
        replacement.SaveTimeBinary = original.SaveTimeBinary;
        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(replacement);

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void InterruptedSessionSurvivesOneProvenNativeSaveStep()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-a");
        firstIds.Enqueue("session-a");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-a"));
        first.PrepareForNativeSave(original);

        var evolved = CreateIdentity(slot: 1, creationTicks: 100);
        evolved.ContentSha256 = new string('f', 64);
        evolved.SaveTimeBinary = TestTime
            .AddSeconds(1)
            .ToBinary();
        evolved.ObservedWriteUtcTicks++;
        evolved.ObservedLength++;
        var secondIds = new Queue<string>();
        secondIds.Enqueue("session-b");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(evolved);

        Assert.False(result.RotatedGeneration);
        Assert.False(result.CreatedNew);
        Assert.True(result.InterruptedSessionRecovered);
        Assert.Equal("generation-a", second.Current.GenerationId);
        Assert.Equal(1, second.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(evolved.ContentSha256, second.Current.Identity.ContentSha256);
        Assert.Null(second.Current.PendingSave);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SameInstanceSameSlotReopenConsumesNativeSaveProofWithoutRotating()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-a");
        ids.Enqueue("session-a");
        ids.Enqueue("session-b");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        repository.Open(original);
        repository.Record(CreateUse("event-one", "generation-a"));
        repository.PrepareForNativeSave(original);

        var evolved = CreateIdentity(slot: 1, creationTicks: 100);
        evolved.ContentSha256 = new string('f', 64);
        evolved.SaveTimeBinary = TestTime
            .AddSeconds(1)
            .ToBinary();
        evolved.ObservedWriteUtcTicks++;
        evolved.ObservedLength++;

        var result = repository.Open(evolved, "SaveSlotSelected");

        Assert.False(result.RotatedGeneration);
        Assert.False(result.CreatedNew);
        Assert.False(result.InterruptedSessionRecovered);
        Assert.Equal("generation-a", repository.Current.GenerationId);
        Assert.Equal(1, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, repository.Current.InterruptedSessionCount);
        Assert.Equal(evolved.ContentSha256, repository.Current.Identity.ContentSha256);
        Assert.Null(repository.Current.PendingSave);
        Assert.False(Directory.Exists(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void CleanCloseClearsPendingSaveIntentBeforeLaterChange()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-old"));
        first.PrepareForNativeSave(original);
        first.CloseClean();

        var replacement = CreateIdentity(slot: 1, creationTicks: 100);
        replacement.ContentSha256 = new string('f', 64);
        replacement.SaveTimeBinary = TestTime
            .AddSeconds(1)
            .ToBinary();
        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(replacement);

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void ExpiredPendingSaveIntentCannotBridgeLaterSlotReuse()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-old"));
        first.PrepareForNativeSave(original);

        var replacement = CreateIdentity(slot: 1, creationTicks: 100);
        replacement.ContentSha256 = new string('f', 64);
        replacement.SaveTimeBinary = TestTime
            .AddMinutes(1)
            .ToBinary();
        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(replacement);

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void NonzeroLegacyProfileWithoutFingerprintRotatesConservatively()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var legacyIdentity = CreateIdentity(slot: 1, creationTicks: 100);
        legacyIdentity.ContentSha256 = null;
        first.Open(legacyIdentity);
        first.Record(CreateUse("event-one", "generation-old"));
        first.CloseClean();

        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void TransientFingerprintReadFailureDoesNotEraseStoredContinuityProof()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-a");
        ids.Enqueue("session-a");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        var stable = CreateIdentity(slot: 1, creationTicks: 100);
        repository.Open(stable);
        var unavailable = CreateIdentity(slot: 1, creationTicks: 100);
        unavailable.ObservedWriteUtcTicks = 999;
        unavailable.ContentSha256 = null;

        repository.RefreshIdentity(unavailable);

        Assert.Equal(stable.ContentSha256, repository.Current.Identity.ContentSha256);
        Assert.Equal(stable.ObservedWriteUtcTicks, repository.Current.Identity.ObservedWriteUtcTicks);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void InterruptedSessionPreservesProfileAndIsRecordedOnce()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-a");
        firstIds.Enqueue("session-a");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        first.Open(CreateIdentity(slot: 1, creationTicks: 100));
        first.Record(CreateUse("event-one", "generation-a"));
        first.Record(CreateHealing("healing-one", "event-one", "generation-a", 9));

        var secondIds = new Queue<string>();
        secondIds.Enqueue("session-b");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);
        var result = second.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.InterruptedSessionRecovered);
        Assert.Equal(1, second.Current.InterruptedSessionCount);
        Assert.Equal(1, second.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(9, second.Current.Statistics.Overall.ActualHealthRestored, precision: 6);
        second.CloseClean();

        var thirdIds = new Queue<string>();
        thirdIds.Enqueue("session-c");
        var third = CreateRepository(temporaryDirectory.Path, thirdIds);
        var cleanResult = third.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.False(cleanResult.InterruptedSessionRecovered);
        Assert.Equal(1, third.Current.InterruptedSessionCount);
        third.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void UnrecoverableProfileIsArchivedInsteadOfOverwritten()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var currentDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current");
        Directory.CreateDirectory(currentDirectory);
        File.WriteAllText(System.IO.Path.Combine(currentDirectory, "profile.json"), "bad primary");
        File.WriteAllText(System.IO.Path.Combine(currentDirectory, "profile.json.bak"), "bad backup");
        var ids = new Queue<string>();
        ids.Enqueue("generation-safe");
        ids.Enqueue("session-safe");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.CreatedNew);
        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-safe", repository.Current.GenerationId);
        Assert.Equal(2, result.LoadFailures.Count);
        Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void DiagnosticsAreBoundedAndRawTraceIsDisabledByDefault()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "diagnostics.json");
        var diagnostics = new DiagnosticStore(path, capacity: 2, () => TestTime);

        diagnostics.Add("one");
        diagnostics.Add("two");
        diagnostics.Add("three");

        Assert.False(diagnostics.RawEventTraceEnabled);
        Assert.Equal(ExpectedDiagnosticMessages, diagnostics.Entries.Select(entry => entry.Message));
        var persisted = new AtomicJsonStore<DiagnosticsDocument>().Load(path).Value!;
        Assert.False(persisted.RawEventTraceEnabled);
        Assert.Equal(2, persisted.Entries.Count);
    }

    private static ProfileRepository CreateRepository(string path, string id) =>
        CreateRepository(path, () => id);

    private static ProfileRepository CreateRepository(string path, Queue<string> ids) =>
        CreateRepository(path, () => ids.Dequeue());

    private static ProfileRepository CreateRepository(string path, Func<string> idFactory) =>
        new(path, () => TestTime, idFactory);

    private static ProfileDocument CreateDocument(string generationId, long revision) => new()
    {
        GenerationId = generationId,
        Slot = 1,
        Revision = revision,
        CreatedUtc = TestTime,
        UpdatedUtc = TestTime,
        Identity = CreateIdentity(slot: 1, creationTicks: 100),
        Statistics = new()
        {
            SaveGenerationId = generationId,
            CreatedUtc = TestTime,
            UpdatedUtc = TestTime
        }
    };

    private static ProfileDocument CreateCompleteCurrentSchemaDocument(string generationId, long revision)
    {
        var document = CreateDocument(generationId, revision);
        document.DeferredItemPersistence = new DeferredItemPersistenceState();
        document.Statistics.Overall.AmountsByUnit["Item"] = 1;
        document.Statistics.Items["duckov:item:test"] = new ItemAggregate
        {
            ItemId = "duckov:item:test",
            DisplayName = "Test item",
            Group = CanonicalItemGroup.OtherUnknown,
            Totals = new AggregateTotals { AmountsByUnit = new() { ["Item"] = 1 } }
        };
        document.Statistics.Groups[nameof(CanonicalItemGroup.OtherUnknown)] = new AggregateTotals
        {
            AmountsByUnit = new() { ["Item"] = 1 }
        };
        document.Statistics.Economy.Currencies["Money"] = new CurrencyEconomyAggregate
        {
            Currency = CurrencyKind.Money,
            Sources = new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal),
            Contexts = new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal)
        };

        var totals = document.Statistics.RunTotals;
        totals.Maps["duckov:map:A"] = new MapRunAggregate
        {
            MapId = "duckov:map:A",
            DisplayName = "A",
            IsKnown = true,
            TotalRuns = 37
        };
        totals.RouteMaps["duckov:map:A"] = new RouteAwareMapAggregate
        {
            MapId = "duckov:map:A",
            DisplayName = "A",
            IsKnown = true,
            RunsVisited = 37
        };
        totals.WeaponStatistics.Weapons["duckov:weapon:test"] = new WeaponAggregate
        {
            WeaponId = "duckov:weapon:test",
            DisplayName = "Test weapon"
        };
        totals.WeaponStatistics.AmmunitionTypes["duckov:ammo:test"] = new AmmunitionAggregate
        {
            AmmunitionId = "duckov:ammo:test",
            DisplayName = "Test ammunition"
        };
        totals.CombatStatistics.Enemies["duckov:enemy:test"] = new CombatBreakdownAggregate
        {
            Id = "duckov:enemy:test",
            DisplayName = "Test enemy"
        };

        document.Statistics.RunRecords.Maps["duckov:map:A"] = new MapRunDurationRecords
        {
            MapId = "duckov:map:A",
            DisplayName = "A"
        };
        document.Statistics.Runs.Add(new RunSummary
        {
            RunId = "run-test",
            SaveGenerationId = generationId,
            MapId = "duckov:map:A",
            MapDisplayName = "A",
            MapKnown = true,
            StartedUtc = TestTime,
            EndedUtc = TestTime.AddMinutes(1),
            StartingMapId = "duckov:map:A",
            StartingMapDisplayName = "A",
            StartingMapKnown = true,
            EndingMapId = "duckov:map:A",
            EndingMapDisplayName = "A",
            EndingMapKnown = true,
            Segments =
            [
                new MapSegmentSummary
                {
                    SegmentId = "segment-test",
                    MapId = "duckov:map:A",
                    MapDisplayName = "A",
                    MapKnown = true,
                    EnteredUtc = TestTime,
                    ExitedUtc = TestTime.AddMinutes(1)
                }
            ]
        });
        return document;
    }

    private static void SetMoneyInflow(EconomyStatisticsAggregate economy, long amount)
    {
        var money = economy.Currencies[CurrencyKind.Money.ToString()];
        money.Totals.GrossInflow = amount;
        money.Sources[CurrencySourceCategory.UnknownAdjustment.ToString()] = new CurrencyFlowTotals
        {
            GrossInflow = amount
        };
        money.Contexts[GameplayContext.Unknown.ToString()] = new CurrencyFlowTotals
        {
            GrossInflow = amount
        };
    }

    private static CurrencyFlowRecorded EconomyFlow(
        string generation,
        string activation,
        long sequence,
        CurrencyKind currency) => new()
        {
            EventId = $"economy:{activation}:{sequence}",
            TimestampUtc = TestTime,
            SaveGenerationId = generation,
            MapId = MapIdentity.UnknownId,
            Currency = currency,
            Direction = CurrencyFlowDirection.Inflow,
            Amount = 1,
            Source = CurrencySourceCategory.UnknownAdjustment,
            GameplayContext = GameplayContext.Base,
            ProducerActivationId = activation,
            ProducerSequence = sequence
        };

    private static void RemoveRequiredRoot(ProfileDocument document, string root)
    {
        var statistics = document.Statistics;
        var totals = statistics.RunTotals;
        var map = totals.Maps["duckov:map:A"];
        var routeMap = totals.RouteMaps["duckov:map:A"];
        var run = statistics.Runs[0];
        var segment = run.Segments[0];
        var recordMap = statistics.RunRecords.Maps["duckov:map:A"];
        switch (root)
        {
            case "Profile.Identity": document.Identity = null!; break;
            case "Profile.Capabilities": document.Capabilities = null!; break;
            case "DeferredItemPersistence.AppliedLifetimeStatistics": document.DeferredItemPersistence!.AppliedLifetimeStatistics = null!; break;
            case "DeferredItemPersistence.AppliedLifetimeEconomy": document.DeferredItemPersistence!.AppliedLifetimeEconomy = null!; break;
            case "Statistics.Overall": statistics.Overall = null!; break;
            case "Statistics.Items": statistics.Items = null!; break;
            case "Statistics.Groups": statistics.Groups = null!; break;
            case "Statistics.RecentEventIds": statistics.RecentEventIds = null!; break;
            case "Statistics.Runs": statistics.Runs = null!; break;
            case "Statistics.RunTotals": statistics.RunTotals = null!; break;
            case "Statistics.RunRecords": statistics.RunRecords = null!; break;
            case "Statistics.Economy": statistics.Economy = null!; break;
            case "Statistics.Economy.Currencies": statistics.Economy.Currencies = null!; break;
            case "Statistics.Economy.CashRaidOutcomes": statistics.Economy.CashRaidOutcomes = null!; break;
            case "Statistics.Economy.Capabilities": statistics.Economy.Capabilities = null!; break;
            case "Statistics.Economy.RecentEventIds": statistics.Economy.RecentEventIds = null!; break;
            case "Statistics.Economy.Capabilities.MoneyAmountDirection": statistics.Economy.Capabilities.MoneyAmountDirection = null!; break;
            case "Statistics.Economy.Capabilities.MoneySourceAttribution": statistics.Economy.Capabilities.MoneySourceAttribution = null!; break;
            case "Statistics.Economy.Capabilities.MoneyContextAttribution": statistics.Economy.Capabilities.MoneyContextAttribution = null!; break;
            case "Statistics.Economy.Capabilities.CashAmountDirection": statistics.Economy.Capabilities.CashAmountDirection = null!; break;
            case "Statistics.Economy.Capabilities.CashExternalAcquisition": statistics.Economy.Capabilities.CashExternalAcquisition = null!; break;
            case "Statistics.Economy.Capabilities.CashContextAttribution": statistics.Economy.Capabilities.CashContextAttribution = null!; break;
            case "Statistics.Economy.Capabilities.CashTerminalOutcomes": statistics.Economy.Capabilities.CashTerminalOutcomes = null!; break;
            case "Statistics.Economy.Capabilities.RouteAttribution": statistics.Economy.Capabilities.RouteAttribution = null!; break;
            case "Statistics.Economy.Currency.Totals": statistics.Economy.Currencies["Money"].Totals = null!; break;
            case "Statistics.Economy.Currency.Sources": statistics.Economy.Currencies["Money"].Sources = null!; break;
            case "Statistics.Economy.Currency.Contexts": statistics.Economy.Currencies["Money"].Contexts = null!; break;
            case "Statistics.Overall.AmountsByUnit": statistics.Overall.AmountsByUnit = null!; break;
            case "Statistics.Item.EffectTags": statistics.Items["duckov:item:test"].EffectTags = null!; break;
            case "Statistics.Item.Totals": statistics.Items["duckov:item:test"].Totals = null!; break;
            case "Statistics.Item.Totals.AmountsByUnit": statistics.Items["duckov:item:test"].Totals.AmountsByUnit = null!; break;
            case "Statistics.Group.AmountsByUnit": statistics.Groups[nameof(CanonicalItemGroup.OtherUnknown)].AmountsByUnit = null!; break;
            case "RunTotals.Outcomes": totals.Outcomes = null!; break;
            case "RunTotals.Maps": totals.Maps = null!; break;
            case "RunTotals.RouteMaps": totals.RouteMaps = null!; break;
            case "RunTotals.ItemStatistics": totals.ItemStatistics = null!; break;
            case "RunTotals.WeaponStatistics": totals.WeaponStatistics = null!; break;
            case "RunTotals.CombatStatistics": totals.CombatStatistics = null!; break;
            case "RunTotals.EquipmentStatistics": totals.EquipmentStatistics = null!; break;
            case "RunTotals.ContainerStatistics": totals.ContainerStatistics = null!; break;
            case "RunTotals.Economy": totals.Economy = null!; break;
            case "RunTotals.ItemStatistics.Overall": totals.ItemStatistics.Overall = null!; break;
            case "RunTotals.ItemStatistics.Items": totals.ItemStatistics.Items = null!; break;
            case "RunTotals.ItemStatistics.Groups": totals.ItemStatistics.Groups = null!; break;
            case "RunTotals.ItemStatistics.RecentEventIds": totals.ItemStatistics.RecentEventIds = null!; break;
            case "RunTotals.WeaponStatistics.Totals": totals.WeaponStatistics.Totals = null!; break;
            case "RunTotals.WeaponStatistics.Weapons": totals.WeaponStatistics.Weapons = null!; break;
            case "RunTotals.WeaponStatistics.AmmunitionTypes": totals.WeaponStatistics.AmmunitionTypes = null!; break;
            case "RunTotals.WeaponStatistics.Capabilities": totals.WeaponStatistics.Capabilities = null!; break;
            case "RunTotals.WeaponStatistics.Weapon.Totals": totals.WeaponStatistics.Weapons["duckov:weapon:test"].Totals = null!; break;
            case "RunTotals.WeaponStatistics.Ammunition.Totals": totals.WeaponStatistics.AmmunitionTypes["duckov:ammo:test"].Totals = null!; break;
            case "RunTotals.CombatStatistics.Totals": totals.CombatStatistics.Totals = null!; break;
            case "RunTotals.CombatStatistics.Enemies": totals.CombatStatistics.Enemies = null!; break;
            case "RunTotals.CombatStatistics.Killers": totals.CombatStatistics.Killers = null!; break;
            case "RunTotals.CombatStatistics.Families": totals.CombatStatistics.Families = null!; break;
            case "RunTotals.CombatStatistics.Causes": totals.CombatStatistics.Causes = null!; break;
            case "RunTotals.CombatStatistics.Weapons": totals.CombatStatistics.Weapons = null!; break;
            case "RunTotals.CombatStatistics.Ammunition": totals.CombatStatistics.Ammunition = null!; break;
            case "RunTotals.CombatStatistics.Ownership": totals.CombatStatistics.Ownership = null!; break;
            case "RunTotals.CombatStatistics.Capabilities": totals.CombatStatistics.Capabilities = null!; break;
            case "RunTotals.CombatStatistics.Enemy.Totals": totals.CombatStatistics.Enemies["duckov:enemy:test"].Totals = null!; break;
            case "RunTotals.EquipmentStatistics.Capabilities": totals.EquipmentStatistics.Capabilities = null!; break;
            case "RunTotals.EquipmentStatistics.Items": totals.EquipmentStatistics.Items = null!; break;
            case "RunTotals.EquipmentStatistics.SelectedWeapons": totals.EquipmentStatistics.SelectedWeapons = null!; break;
            case "RunTotals.EquipmentStatistics.Loadouts": totals.EquipmentStatistics.Loadouts = null!; break;
            case "RunTotals.EquipmentStatistics.TotemSets": totals.EquipmentStatistics.TotemSets = null!; break;
            case "RunTotals.EquipmentStatistics.CombatAssociations": totals.EquipmentStatistics.CombatAssociations = null!; break;
            case "RunTotals.EquipmentStatistics.Transitions": totals.EquipmentStatistics.Transitions = null!; break;
            case "RunTotals.EquipmentStatistics.TotemStates": totals.EquipmentStatistics.TotemStates = null!; break;
            case "RunTotals.EquipmentStatistics.Slots": totals.EquipmentStatistics.Slots = null!; break;
            case "RunTotals.EquipmentStatistics.SlottedWeapons": totals.EquipmentStatistics.SlottedWeapons = null!; break;
            case "RunTotals.ContainerStatistics.Capabilities": totals.ContainerStatistics.Capabilities = null!; break;
            case "RunTotals.ContainerStatistics.Capabilities.UniqueContainersLooted": totals.ContainerStatistics.Capabilities.UniqueContainersLooted = null!; break;
            case "Map.Outcomes": map.Outcomes = null!; break;
            case "Map.ItemStatistics": map.ItemStatistics = null!; break;
            case "Map.WeaponStatistics": map.WeaponStatistics = null!; break;
            case "Map.CombatStatistics": map.CombatStatistics = null!; break;
            case "Map.EquipmentStatistics": map.EquipmentStatistics = null!; break;
            case "Map.ContainerStatistics": map.ContainerStatistics = null!; break;
            case "Map.Economy": map.Economy = null!; break;
            case "RouteMap.ItemStatistics": routeMap.ItemStatistics = null!; break;
            case "RouteMap.WeaponStatistics": routeMap.WeaponStatistics = null!; break;
            case "RouteMap.CombatStatistics": routeMap.CombatStatistics = null!; break;
            case "RouteMap.EquipmentStatistics": routeMap.EquipmentStatistics = null!; break;
            case "RouteMap.ContainerStatistics": routeMap.ContainerStatistics = null!; break;
            case "RouteMap.Economy": routeMap.Economy = null!; break;
            case "RunRecords.Extraction": statistics.RunRecords.Extraction = null!; break;
            case "RunRecords.Death": statistics.RunRecords.Death = null!; break;
            case "RunRecords.Maps": statistics.RunRecords.Maps = null!; break;
            case "RunRecordMap.Extraction": recordMap.Extraction = null!; break;
            case "RunRecordMap.Death": recordMap.Death = null!; break;
            case "Run.WeaponStatistics": run.WeaponStatistics = null!; break;
            case "Run.CombatStatistics": run.CombatStatistics = null!; break;
            case "Run.EquipmentStatistics": run.EquipmentStatistics = null!; break;
            case "Run.ContainerStatistics": run.ContainerStatistics = null!; break;
            case "Run.Economy": run.Economy = null!; break;
            case "Run.Segments": run.Segments = null!; break;
            case "Run.RouteCapabilities": run.RouteCapabilities = null!; break;
            case "Run.RouteCapabilities.OrderedRoute": run.RouteCapabilities.OrderedRoute = null!; break;
            case "Run.RouteCapabilities.Segments": run.RouteCapabilities.Segments = null!; break;
            case "Run.RouteCapabilities.EventAttribution": run.RouteCapabilities.EventAttribution = null!; break;
            case "Run.RouteCapabilities.RouteAwareMapTotals": run.RouteCapabilities.RouteAwareMapTotals = null!; break;
            case "Run.SegmentEventAssociations": run.SegmentEventAssociations = null!; break;
            case "Run.ItemStatistics": run.ItemStatistics = null!; break;
            case "Segment.ItemStatistics": segment.ItemStatistics = null!; break;
            case "Segment.WeaponStatistics": segment.WeaponStatistics = null!; break;
            case "Segment.CombatStatistics": segment.CombatStatistics = null!; break;
            case "Segment.EquipmentStatistics": segment.EquipmentStatistics = null!; break;
            case "Segment.ContainerStatistics": segment.ContainerStatistics = null!; break;
            case "Segment.Economy": segment.Economy = null!; break;
            default: throw new ArgumentOutOfRangeException(nameof(root), root, "Unknown required root test case.");
        }
    }

    private static void AddNullDictionaryRow(ProfileDocument document, string dictionary)
    {
        switch (dictionary)
        {
            case "Statistics.Items": document.Statistics.Items["null"] = null!; break;
            case "Statistics.Groups": document.Statistics.Groups["null"] = null!; break;
            case "RunTotals.Maps": document.Statistics.RunTotals.Maps["null"] = null!; break;
            case "RunTotals.RouteMaps": document.Statistics.RunTotals.RouteMaps["null"] = null!; break;
            case "RunRecords.Maps": document.Statistics.RunRecords.Maps["null"] = null!; break;
            case "Statistics.Economy.Currencies": document.Statistics.Economy.Currencies["null"] = null!; break;
            case "RunTotals.ItemStatistics.Items": document.Statistics.RunTotals.ItemStatistics.Items["null"] = null!; break;
            default: throw new ArgumentOutOfRangeException(nameof(dictionary), dictionary, "Unknown null-row test case.");
        }
    }

    private static SaveIdentitySnapshot CreateIdentity(int slot, long creationTicks) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = creationTicks,
        ObservedWriteUtcTicks = creationTicks + 10,
        ObservedLength = 4096,
        GameVersion = "2.3.30",
        ContentSha256 = creationTicks.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0'),
        SaveTimeBinary = TestTime.AddTicks(creationTicks).ToBinary()
    };

    private static ItemUseRecorded CreateUse(string eventId, string generationId, string? runId = null) => new()
    {
        EventId = eventId,
        TimestampUtc = TestTime,
        SaveGenerationId = generationId,
        RunId = runId,
        GameplayContext = GameplayContext.Raid,
        ItemId = "duckov:item:42",
        DisplayName = "Test item",
        Group = CanonicalItemGroup.Healing,
        ActivationCount = 1,
        AmountConsumed = 1,
        ConsumptionUnit = ConsumptionUnit.Item
    };

    private static HealingApplied CreateHealing(
        string eventId,
        string sourceItemUseEventId,
        string generationId,
        double amount,
        string? runId = null) => new()
        {
            EventId = eventId,
            ApplicationId = $"application-{eventId}",
            SourceItemUseEventId = sourceItemUseEventId,
            TimestampUtc = TestTime,
            SaveGenerationId = generationId,
            RunId = runId,
            GameplayContext = GameplayContext.Raid,
            ItemId = "duckov:item:42",
            DisplayName = "Test item",
            Group = CanonicalItemGroup.Healing,
            ActualHealthRestored = amount
        };
}
