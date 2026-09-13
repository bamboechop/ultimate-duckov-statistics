using System.Runtime.Serialization.Json;
using System.Text;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class ExportTests
{
    private static long economySequence;
    private static readonly DateTime TestTime = new(2026, 8, 9, 13, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ExpectedExportFileNames = { "statistics.json" };

    [Fact]
    [Trait("Category", "Export")]
    public void JsonExportPreservesItemRunMapAndRecordTotals()
    {
        var profile = CreateProfile();
        ItemUseReducer.Apply(profile.Statistics, CreateUse("one", "item:one", "Medkit", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item));
        ItemUseReducer.Apply(profile.Statistics, CreateUse("two", "item:two", "Juice", CanonicalItemGroup.Drink, 2.5, ConsumptionUnit.Durability));
        HealingReducer.Apply(profile.Statistics, CreateHealing("heal-one", "one", "item:one", CanonicalItemGroup.Healing, 12.5));
        RunReducer.Apply(profile.Statistics, CreateRun("run-one", RunOutcome.Extracted, 95, 123.5, 8));
        RunReducer.Apply(profile.Statistics, CreateRun("run-two", RunOutcome.Died, 130, 45.25, 2));

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var uiModel = WeaponStatisticsViewModelFactory.Create(profile);
        Assert.Equal(2, json.Overall.ActivationCount);
        Assert.Equal(json.Overall.ActivationCount, json.Groups.Sum(group => group.Totals.ActivationCount));
        Assert.Equal(json.Overall.ActivationCount, json.Items.Sum(item => item.Totals.ActivationCount));
        Assert.Equal(1, json.Overall.AmountsByUnit[nameof(ConsumptionUnit.Item)]);
        Assert.Equal(2.5, json.Overall.AmountsByUnit[nameof(ConsumptionUnit.Durability)], precision: 6);
        Assert.Equal(12.5, json.Overall.ActualHealthRestored, precision: 6);
        Assert.Equal(json.Overall.ActualHealthRestored, json.Groups.Sum(group => group.Totals.ActualHealthRestored), precision: 6);
        Assert.Equal(json.Overall.ActualHealthRestored, json.Items.Sum(item => item.Totals.ActualHealthRestored), precision: 6);
        Assert.Equal(2, json.RunTotals.TotalRuns);
        Assert.Equal(json.RunTotals.TotalRuns, json.Runs.Count);
        Assert.Equal(json.RunTotals.TotalRuns, Assert.Single(json.RunTotals.Maps).Value.TotalRuns);
        Assert.Equal(json.Runs.Sum(run => run.PhysicalDistance), json.RunTotals.PhysicalDistance, precision: 6);
        Assert.Equal(json.Runs.Sum(run => run.TeleportDistance), json.RunTotals.TeleportDistance, precision: 6);
        Assert.Equal("run-one", json.RunRecords.Extraction.Shortest!.RunId);
        Assert.Equal("run-two", json.RunRecords.Death.Shortest!.RunId);
        Assert.Equal(json.RunTotals.WeaponStatistics.Totals.FiringActions, uiModel.Lifetime.Totals.FiringActions);
        Assert.Equal(uiModel.Capabilities.FiringActions.State, json.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Combat")]
    [Trait("Category", "M11")]
    public void JsonAndUiAgreeOnPlayerKillsObservedDeathsOwnershipAndEquipmentCredit()
    {
        var profile = CreateProfile();
        profile.Capabilities.AddRange(CombatNativeContractPolicy.ToRecords(
            CombatNativeContractPolicy.CreateSupportedCapabilities(), "test"));
        var run = CreateRun("ownership-run", RunOutcome.Extracted, 10, 2, 0);
        var association = new EquipmentEventAssociation
        {
            LoadoutId = "loadout-a",
            SelectedWeaponSlotId = "primary",
            SelectedWeaponId = "duckov:weapon:1",
            TotemSetId = "totems:none"
        };
        var player = CombatEvent("player", CombatOwnership.Player, "duckov:target:wolf") with
        {
            ActualDamageToTarget = 10,
            ActualDamageDealt = 10,
            KillsByYou = 1,
            IsFinalBlow = true,
            EquipmentAssociation = association
        };
        var companion = CombatEvent("companion", CombatOwnership.PetCompanion, "duckov:target:wolf") with
        {
            ActualDamageToTarget = 5,
            ObservedWorldDeaths = 1,
            IsFinalBlow = true,
            EquipmentAssociation = association
        };
        var unknown = CombatEvent("unknown", CombatOwnership.Unknown, "duckov:target:fox") with
        {
            ActualDamageToTarget = 3,
            ObservedWorldDeaths = 1,
            IsFinalBlow = true,
            EquipmentAssociation = association
        };
        foreach (var value in new[] { player, companion, unknown })
        {
            CombatStatisticsReducer.Apply(run.CombatStatistics, value);
            EquipmentStatisticsReducer.RecordCombat(run.EquipmentStatistics, value);
        }
        Assert.True(RunReducer.Apply(profile.Statistics, run));

        var view = CombatStatisticsViewModelFactory.Create(profile);
        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        Assert.Equal(1, view.Lifetime.Totals.KillsByYou);
        Assert.Equal(2, view.Lifetime.Totals.ObservedWorldDeaths);
        Assert.Equal(1, json.RunTotals.CombatStatistics.Totals.KillsByYou);
        Assert.Equal(2, json.RunTotals.CombatStatistics.Totals.ObservedWorldDeaths);
        Assert.Equal(1, Assert.Single(json.Runs).CombatStatistics.Totals.KillsByYou);
        Assert.Equal(2, Assert.Single(json.RunTotals.Maps).Value.CombatStatistics.Totals.ObservedWorldDeaths);
        Assert.Equal(1, Assert.Single(json.RunTotals.EquipmentStatistics.CombatAssociations).Value.KillsByYou);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Combat")]
    [Trait("Category", "M11")]
    public void JsonPreservesEquipmentCreditAndCurrentUnavailableCombatCapabilities()
    {
        var profile = CreateProfile();
        profile.Capabilities.AddRange(CombatNativeContractPolicy.ToRecords(
            CombatNativeContractPolicy.CreateSupportedCapabilities(), "test"));
        var run = CreateRun("degraded-equipment-run", RunOutcome.Extracted, 10, 2, 0);
        var player = CombatEvent("player-before-degradation", CombatOwnership.Player, "duckov:target:wolf") with
        {
            ActualDamageToTarget = 10,
            ActualDamageDealt = 10,
            KillsByYou = 1,
            IsFinalBlow = true,
            EquipmentAssociation = new EquipmentEventAssociation
            {
                LoadoutId = "loadout-a",
                SelectedWeaponSlotId = "primary",
                SelectedWeaponId = "duckov:weapon:1",
                TotemSetId = "totems:none"
            }
        };
        CombatStatisticsReducer.Apply(run.CombatStatistics, player);
        EquipmentStatisticsReducer.RecordCombat(run.EquipmentStatistics, player);
        Assert.True(RunReducer.Apply(profile.Statistics, run));

        var disabledCapabilityIds = new HashSet<string>(StringComparer.Ordinal)
        {
            CombatCapabilityIds.DamageDealt,
            CombatCapabilityIds.DamageReceived,
            CombatCapabilityIds.RangedHits,
            CombatCapabilityIds.MeleeHits,
            CombatCapabilityIds.KillsByYou,
            CombatCapabilityIds.PlayerDeaths,
            CombatCapabilityIds.Ownership
        };
        foreach (var capability in profile.Capabilities.Where(value => disabledCapabilityIds.Contains(value.AdapterId)))
            capability.State = AdapterCapabilityState.DisabledIncompatible;

        var json = Deserialize(StatisticsExporter.Create(profile, TestTime).Json);
        var combat = json.RunTotals.CombatStatistics;
        Assert.Equal(10, combat.Totals.DamageDealt);
        Assert.Equal(1, combat.Totals.KillsByYou);
        Assert.Equal(1, Assert.Single(json.RunTotals.EquipmentStatistics.CombatAssociations).Value.KillsByYou);
        Assert.All(new[] { combat.Capabilities.DamageDealt, combat.Capabilities.DamageReceived,
            combat.Capabilities.RangedHits, combat.Capabilities.MeleeHits, combat.Capabilities.KillsByYou,
            combat.Capabilities.PlayerDeaths, combat.Capabilities.Ownership },
            capability => Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capability.State));
    }

    [Fact]
    [Trait("Category", "Export")]
    public void JsonEscapesItemNamesWithoutChangingTheirValue()
    {
        var profile = CreateProfile();
        const string name = "Soup, \"Deluxe\"\r\nLarge";
        ItemUseReducer.Apply(profile.Statistics, CreateUse("one", "item:one", name, CanonicalItemGroup.Food, 1, ConsumptionUnit.StackUnit));

        var row = Assert.Single(Deserialize(StatisticsExporter.Create(profile, TestTime).Json).Items);

        Assert.Equal(name, row.DisplayName);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Weapon")]
    public void CurrentSupportedCapabilityDoesNotUpgradeHistoricalUnavailableRun()
    {
        var profile = CreateProfile();
        RunReducer.Apply(profile.Statistics, CreateRun("historical-run", RunOutcome.Extracted, 95, 123.5, 8));
        var historical = Assert.Single(profile.Statistics.Runs);
        historical.WeaponStatistics.Capabilities.FiringActions = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = string.Empty
        };

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var runJson = Assert.Single(json.Runs);

        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            runJson.WeaponStatistics.Capabilities.FiringActions.State);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Weapon")]
    public void NonemptyLifetimeAggregateWithMissingCapabilityMetadataRemainsUnavailable()
    {
        var profile = CreateProfile();
        var lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        lifetime.Totals.FiringActions = 7;
        lifetime.Weapons["weapon:observed"] = new WeaponAggregate
        {
            WeaponId = "weapon:observed",
            DisplayName = "Observed weapon",
            Totals = new WeaponMetricTotals { FiringActions = 7 }
        };

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);

        Assert.Equal(7, json.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            json.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Weapon")]
    public void RepairedInvalidLifetimeCounterCannotBecomeSupportedZero()
    {
        var profile = CreateProfile();
        var lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        lifetime.Totals.FiringActions = -7;

        Assert.True(ProfileFormat.Normalize(profile));

        var model = WeaponStatisticsViewModelFactory.Create(profile);
        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);

        Assert.Equal(0, lifetime.Totals.FiringActions);
        Assert.True(lifetime.WasRepairedFromInvalidState);
        Assert.False(WeaponStatisticsReducer.IsEmpty(lifetime));
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            lifetime.Capabilities.FiringActions.State);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            model.Capabilities.FiringActions.State);
        Assert.True(json.RunTotals.WeaponStatistics.WasRepairedFromInvalidState);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            json.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Weapon")]
    public void RepairedInvalidLifetimeCapabilityCannotBecomeSupported()
    {
        var profile = CreateProfile();
        var lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        lifetime.Capabilities.FiringActions.State = (AdapterCapabilityState)int.MaxValue;

        Assert.True(ProfileFormat.Normalize(profile));

        var model = WeaponStatisticsViewModelFactory.Create(profile);
        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);

        Assert.True(lifetime.WasRepairedFromInvalidState);
        Assert.False(WeaponStatisticsReducer.IsEmpty(lifetime));
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            model.Capabilities.FiringActions.State);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            json.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Weapon")]
    [Trait("Category", "UI")]
    public void RepairedInvalidIdentityEntriesRemainUnavailableAndPresentationIsDeterministic()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(
            temporaryDirectory.Path,
            "profiles",
            "slot-01",
            "current",
            "profile.json");
        var profile = CreateProfile();
        var lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        lifetime.Weapons["weapon:corrupt"] = null!;
        lifetime.AmmunitionTypes["ammo:corrupt"] = null!;
        Assert.True(ProfileFormat.Normalize(profile));
        new AtomicJsonStore<ProfileDocument>().Save(path, profile);
        var repository = new ProfileRepository(
            temporaryDirectory.Path,
            () => TestTime,
            () => "session-corrupt-identities");

        Assert.False(repository.Open(new SaveIdentitySnapshot { Slot = 1 }).NormalizedProfile);

        lifetime = repository.Current.Statistics.RunTotals.WeaponStatistics;
        Assert.Empty(lifetime.Weapons);
        Assert.Empty(lifetime.AmmunitionTypes);
        Assert.True(lifetime.WasRepairedFromInvalidState);
        Assert.False(WeaponStatisticsReducer.IsEmpty(lifetime));
        var initialModel = WeaponStatisticsViewModelFactory.Create(repository.Current);
        var initialBundle = StatisticsExporter.Create(repository.Current, TestTime);
        var initialJson = Deserialize(initialBundle.Json);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            initialModel.Capabilities.FiringActions.State);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            initialJson.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
        Assert.Empty(initialJson.RunTotals.WeaponStatistics.Weapons);
        Assert.Empty(initialJson.RunTotals.WeaponStatistics.AmmunitionTypes);

        lifetime.Weapons["weapon:valid"] = new WeaponAggregate
        {
            WeaponId = "weapon:valid",
            DisplayName = "Valid weapon"
        };
        lifetime.AmmunitionTypes["ammo:valid"] = new AmmunitionAggregate
        {
            AmmunitionId = "ammo:valid",
            DisplayName = "Valid ammunition"
        };
        var before = Serialize(repository.Current);
        var firstModel = WeaponStatisticsViewModelFactory.Create(repository.Current);
        var firstBundle = StatisticsExporter.Create(repository.Current, TestTime);
        var secondModel = WeaponStatisticsViewModelFactory.Create(repository.Current);
        var secondBundle = StatisticsExporter.Create(repository.Current, TestTime);
        var after = Serialize(repository.Current);

        Assert.Equal(before, after);
        Assert.Equal(firstBundle.Json, secondBundle.Json);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            firstModel.Capabilities.FiringActions.State);
        Assert.Equal(firstModel.Capabilities.FiringActions.State, secondModel.Capabilities.FiringActions.State);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Weapon")]
    [Trait("Category", "UI")]
    public void PristineEmptyLifetimeFallbackIsDeterministicAndDoesNotMutateProfile()
    {
        var profile = CreateProfile();
        var before = Serialize(profile);

        var firstModel = WeaponStatisticsViewModelFactory.Create(profile);
        var firstBundle = StatisticsExporter.Create(profile, TestTime);
        var secondModel = WeaponStatisticsViewModelFactory.Create(profile);
        var secondBundle = StatisticsExporter.Create(profile, TestTime);
        var after = Serialize(profile);

        Assert.True(WeaponStatisticsReducer.IsEmpty(profile.Statistics.RunTotals.WeaponStatistics));
        Assert.Equal(AdapterCapabilityState.Supported, firstModel.Capabilities.FiringActions.State);
        Assert.Equal(firstModel.Capabilities.FiringActions.State, secondModel.Capabilities.FiringActions.State);
        Assert.Equal(before, after);
        Assert.Equal(firstBundle.Json, secondBundle.Json);
    }

    [Fact]
    [Trait("Category", "Export")]
    public void ReclassifiedStableItemKeepsMatchingItemAndGroupExportRows()
    {
        var profile = CreateProfile();
        ItemUseReducer.Apply(
            profile.Statistics,
            CreateUse("one", "item:stable", "Item", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item));
        ItemUseReducer.Apply(
            profile.Statistics,
            CreateUse("two", "item:stable", "Item", CanonicalItemGroup.Drink, 2, ConsumptionUnit.Durability));

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var item = Assert.Single(json.Items);
        var group = Assert.Single(json.Groups);

        Assert.Equal(nameof(CanonicalItemGroup.Healing), item.Group);
        Assert.Equal(item.Group, group.Group);
        Assert.Equal(item.Totals.ActivationCount, group.Totals.ActivationCount);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "M9")]
    public void EconomyJsonPreservesStableDimensionsAndCapabilities()
    {
        var profile = CreateProfile();
        profile.Statistics.Economy.Capabilities = EconomyCapabilities();
        EconomyStatisticsReducer.Record(
            profile.Statistics.Economy,
            profile.GenerationId,
            EconomyFlow("reward", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 100, CurrencySourceCategory.Reward, GameplayContext.Reward));
        EconomyStatisticsReducer.Record(
            profile.Statistics.Economy,
            profile.GenerationId,
            EconomyFlow("purchase", CurrencyKind.Money, CurrencyFlowDirection.Outflow, 30, CurrencySourceCategory.Purchase, GameplayContext.Shop));
        var cash = EconomyFlow("cash", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 7, CurrencySourceCategory.LootOrPickup, GameplayContext.Raid);
        cash.RunId = "run:cash";
        cash.ProvenExternalRaidAcquisition = true;
        EconomyStatisticsReducer.Record(profile.Statistics.Economy, profile.GenerationId, cash);

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);

        Assert.Equal(100, json.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(30, json.Economy.Currencies["Money"].Totals.GrossOutflow);
        Assert.Equal(70, json.Economy.Currencies["Money"].Totals.NetFlow);
        Assert.Equal(profile.Statistics.Economy.ReplayCursor!.ActivationId, json.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(profile.Statistics.Economy.ReplayCursor.ClosedThroughSequence, json.Economy.ReplayCursor.ClosedThroughSequence);
        var money = json.Economy.Currencies["Money"];
        Assert.Equal(100, money.Sources["Reward"].GrossInflow);
        Assert.Equal(30, money.Sources["Purchase"].GrossOutflow);
        Assert.Equal(100, money.Contexts["Reward"].GrossInflow);
        Assert.Equal(30, money.Contexts["Shop"].GrossOutflow);
        Assert.Equal(AdapterCapabilityState.Supported, json.Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Equal("test", json.Economy.Capabilities.MoneyAmountDirection.Provenance);
        Assert.Equal(7, json.Economy.CashAcquired);
        Assert.Equal(bundle.Json, StatisticsExporter.Create(profile, TestTime).Json);
    }

    [Fact]
    [Trait("Category", "Export")]
    public void WriterCreatesOneCompleteGenerationScopedExportSet()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var profile = CreateProfile();
        var current = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current");
        Directory.CreateDirectory(current);
        var profilePath = System.IO.Path.Combine(current, "profile.json");

        var result = ProfileExportWriter.Write(profile, profilePath, TestTime);

        Assert.Single(result.Files);
        Assert.All(result.Files, path => Assert.True(File.Exists(path)));
        Assert.Equal(
            ExpectedExportFileNames,
            result.Files.Select(System.IO.Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(result.Files, Directory.GetFiles(result.Directory));
        Assert.Empty(Directory.EnumerateFiles(result.Directory, "*.tmp"));
        Assert.Contains("generation-a", result.Directory, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Export")]
    public void ExistingExportIsNeverOverwrittenWhenDestinationCollides()
    {
        using var directory = new TemporaryDirectory();
        var profile = CreateProfile();
        var profilePath = Path.Combine(directory.Path, "profile.json");
        var first = ProfileExportWriter.Write(profile, profilePath, TestTime);
        var path = Assert.Single(first.Files);
        var original = File.ReadAllBytes(path);
        profile.Revision++;

        Assert.Throws<IOException>(() => ProfileExportWriter.Write(profile, profilePath, TestTime));
        Assert.Equal(original, File.ReadAllBytes(path));
        var later = ProfileExportWriter.Write(profile, profilePath, TestTime.AddSeconds(1));
        Assert.Equal(profile.Revision, Deserialize(File.ReadAllText(Assert.Single(later.Files))).Revision);
    }

    private static ProfileDocument CreateProfile() => new()
    {
        GenerationId = "generation-a",
        Slot = 1,
        Revision = 4,
        CreatedUtc = TestTime,
        UpdatedUtc = TestTime,
        Identity = new SaveIdentitySnapshot { Slot = 1 },
        Capabilities = WeaponCapabilityIds.All.Select(id => new CapabilityRecord
        {
            AdapterId = id,
            State = AdapterCapabilityState.Supported,
            Version = ProductInfo.Version,
            Detail = "test"
        }).ToList(),
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = "generation-a",
            CreatedUtc = TestTime,
            UpdatedUtc = TestTime
        }
    };

    private static ItemUseRecorded CreateUse(
        string eventId,
        string itemId,
        string displayName,
        CanonicalItemGroup group,
        double amount,
        ConsumptionUnit unit) => new()
        {
            EventId = eventId,
            TimestampUtc = TestTime,
            SaveGenerationId = "generation-a",
            GameplayContext = GameplayContext.Raid,
            ItemId = itemId,
            DisplayName = displayName,
            Group = group,
            EffectTags = new List<ItemEffectTag> { ItemEffectTag.Food },
            ActivationCount = 1,
            AmountConsumed = amount,
            ConsumptionUnit = unit
        };

    private static HealingApplied CreateHealing(
        string eventId,
        string sourceUseEventId,
        string itemId,
        CanonicalItemGroup group,
        double amount) => new()
        {
            EventId = eventId,
            ApplicationId = $"application-{eventId}",
            SourceItemUseEventId = sourceUseEventId,
            TimestampUtc = TestTime,
            SaveGenerationId = "generation-a",
            GameplayContext = GameplayContext.Raid,
            ItemId = itemId,
            DisplayName = itemId,
            Group = group,
            ActualHealthRestored = amount
        };

    private static CombatRecorded CombatEvent(
        string eventId,
        CombatOwnership ownership,
        string targetId) => new()
        {
            EventId = eventId,
            TimestampUtc = TestTime,
            SaveGenerationId = "generation-a",
            RunId = "ownership-run",
            MapId = "duckov:map:warehouse",
            GameplayContext = GameplayContext.Raid,
            Ownership = ownership,
            TargetId = targetId,
            TargetDisplayName = targetId,
            TargetIsEnemy = true,
            TargetFamilyId = "duckov:family:unknown",
            TargetFamilyDisplayName = "Unknown family",
            Capabilities = CombatNativeContractPolicy.CreateSupportedCapabilities()
        };

    private static RunSummary CreateRun(
        string runId,
        RunOutcome outcome,
        double activeDurationSeconds,
        double physicalDistance,
        double teleportDistance) => new()
        {
            RunId = runId,
            SaveGenerationId = "generation-a",
            NativeRaidId = $"native-{runId}",
            StartingMapId = "duckov:map:warehouse",
            StartingMapDisplayName = "Warehouse",
            StartingMapKnown = true,
            StartedUtc = TestTime.AddMinutes(-5),
            EndedUtc = TestTime,
            ActiveDurationSeconds = activeDurationSeconds,
            WallClockDurationSeconds = 300,
            Outcome = outcome,
            PhysicalDistance = physicalDistance,
            TeleportDistance = teleportDistance,
            IntegrityTags = IntegrityTags.Normal,
            RecordEligible = true,
            GameVersion = "2.3.30",
            GameBuild = "24013657",
            LifecycleCapability = AdapterCapabilityState.Supported,
            LifecycleAdapterVersion = ProductInfo.Version,
            MovementCapability = AdapterCapabilityState.Supported,
            MovementAdapterVersion = ProductInfo.Version,
            MapCapability = AdapterCapabilityState.Supported,
            MapAdapterVersion = ProductInfo.Version,
            WeaponStatistics = CreateCombat(runId)
        };

    private static WeaponStatisticsAggregate CreateCombat(string runId)
    {
        var statistics = new WeaponStatisticsAggregate();
        WeaponStatisticsReducer.Apply(statistics, new ShotRecorded
        {
            EventId = $"shot-{runId}",
            TimestampUtc = TestTime,
            SaveGenerationId = "generation-a",
            RunId = runId,
            MapId = "duckov:map:warehouse",
            GameplayContext = GameplayContext.Raid,
            WeaponId = $"weapon-{runId}",
            WeaponDisplayName = $"Weapon {runId}",
            AmmunitionId = $"ammo-{runId}",
            AmmunitionDisplayName = $"Ammo {runId}",
            FiringActionCount = 1,
            Capabilities = new WeaponMetricCapabilities
            {
                FiringActions = Available(),
                WeaponIdentity = Available(),
                AmmunitionIdentity = Available()
            }
        });
        return statistics;
    }

    private static MetricAvailability Available() => new()
    {
        State = AdapterCapabilityState.Supported,
        Provenance = "test"
    };

    private static EconomyMetricCapabilities EconomyCapabilities() => new()
    {
        MoneyAmountDirection = Available(),
        MoneySourceAttribution = Available(),
        MoneyContextAttribution = Available(),
        CashAmountDirection = Available(),
        CashExternalAcquisition = Available(),
        CashContextAttribution = Available(),
        RouteAttribution = Available()
    };

    private static CurrencyFlowRecorded EconomyFlow(
        string id,
        CurrencyKind currency,
        CurrencyFlowDirection direction,
        long amount,
        CurrencySourceCategory source,
        GameplayContext context) => new()
        {
            EventId = id,
            TimestampUtc = TestTime,
            SaveGenerationId = "generation-a",
            MapId = MapIdentity.UnknownId,
            Currency = currency,
            Direction = direction,
            Amount = amount,
            Source = source,
            GameplayContext = context,
            IntegrityTags = IntegrityTags.Normal,
            AdapterVersion = "test",
            ProducerActivationId = "test-export",
            ProducerSequence = Interlocked.Increment(ref economySequence)
        };

    private static StatisticsExportDocument Deserialize(string json)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return Assert.IsType<StatisticsExportDocument>(serializer.ReadObject(stream));
    }

    private static string Serialize(ProfileDocument profile)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(ProfileDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, profile);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
