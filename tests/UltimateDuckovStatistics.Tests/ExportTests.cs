using System.Globalization;
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
    private static readonly string[] ExpectedExportFileNames =
    {
        "ammunition_totals.csv",
        "cash_raid_outcomes.csv",
        "character_equipment_slots.csv",
        "combat_attribution.csv",
        "combat_totals.csv",
        "containers.csv",
        "crafting_recipes.csv",
        "crafting_totals.csv",
        "economy_contexts.csv",
        "economy_sources.csv",
        "economy_totals.csv",
        "equipment_combat.csv",
        "equipment_totals.csv",
        "equipped_item_nested_slots.csv",
        "groups.csv",
        "items.csv",
        "map_totals.csv",
        "overview.csv",
        "records.csv",
        "recurring_loadouts.csv",
        "route_map_totals.csv",
        "routes.csv",
        "run_totals.csv",
        "runs.csv",
        "segment_events.csv",
        "segments.csv",
        "statistics.json",
        "weapon_ammunition_pairs.csv",
        "weapon_totals.csv",
        "world_time.csv"
    };

    [Fact]
    [Trait("Category", "Export")]
    public void JsonAndCsvExportsRepresentTheSameTotals()
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
        var overview = ParseCsv(bundle.OverviewCsv);
        var groups = ParseCsv(bundle.GroupsCsv);
        var items = ParseCsv(bundle.ItemsCsv);
        var runs = ParseCsv(bundle.RunsCsv);
        var runTotals = Assert.Single(ParseCsv(bundle.RunTotalsCsv));
        var mapTotals = Assert.Single(ParseCsv(bundle.MapTotalsCsv));
        var records = ParseCsv(bundle.RecordsCsv);
        var combatTotals = ParseCsv(bundle.CombatTotalsCsv);
        var weaponTotals = ParseCsv(bundle.WeaponTotalsCsv);
        var ammunitionTotals = ParseCsv(bundle.AmmunitionTotalsCsv);

        Assert.Equal(2, json.Overall.ActivationCount);
        Assert.Equal(json.Overall.ActivationCount, ReadLong(Assert.Single(overview), "activation_count"));
        Assert.Equal(json.Groups.Sum(group => group.Totals.ActivationCount), groups.Sum(row => ReadLong(row, "activation_count")));
        Assert.Equal(json.Items.Sum(item => item.Totals.ActivationCount), items.Sum(row => ReadLong(row, "activation_count")));
        Assert.Equal(json.Overall.ActivationCount, json.Groups.Sum(group => group.Totals.ActivationCount));
        Assert.Equal(json.Overall.ActivationCount, json.Items.Sum(item => item.Totals.ActivationCount));
        Assert.Equal(1, ReadDouble(Assert.Single(overview), "item_amount"));
        Assert.Equal(2.5, ReadDouble(Assert.Single(overview), "durability_amount"), precision: 6);
        Assert.Equal(12.5, ReadDouble(Assert.Single(overview), "actual_hp_restored"), precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            groups.Sum(row => ReadDouble(row, "actual_hp_restored")),
            precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            items.Sum(row => ReadDouble(row, "actual_hp_restored")),
            precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            json.Groups.Sum(group => group.Totals.ActualHealthRestored),
            precision: 6);
        Assert.Equal(
            json.Overall.ActualHealthRestored,
            json.Items.Sum(item => item.Totals.ActualHealthRestored),
            precision: 6);
        Assert.True(Assert.Single(overview).ContainsKey("unknown_amount"));
        Assert.DoesNotContain("unknown_amount_amount", Assert.Single(overview).Keys);
        Assert.Equal(json.RunTotals.TotalRuns, runs.Count);
        Assert.Equal(json.RunTotals.TotalRuns, ReadLong(runTotals, "total_runs"));
        Assert.Equal(json.RunTotals.TotalRuns, ReadLong(mapTotals, "total_runs"));
        Assert.Equal(json.RunTotals.PhysicalDistance, ReadDouble(runTotals, "physical_distance"), precision: 6);
        Assert.Equal(json.RunTotals.TeleportDistance, ReadDouble(runTotals, "teleport_distance"), precision: 6);
        Assert.Equal(json.Runs.Sum(run => run.PhysicalDistance), ReadDouble(runTotals, "physical_distance"), precision: 6);
        Assert.Equal(json.Runs.Sum(run => run.TeleportDistance), ReadDouble(runTotals, "teleport_distance"), precision: 6);
        Assert.Equal(4, records.Count(row => row["scope"] == "overall"));
        Assert.Equal(
            json.RunRecords.Extraction.Shortest!.RunId,
            Assert.Single(records, row => row["scope"] == "overall"
                && row["outcome"] == nameof(RunOutcome.Extracted)
                && row["record"] == "shortest")["run_id"]);
        Assert.Equal(
            json.RunRecords.Death.Shortest!.RunId,
            Assert.Single(records, row => row["scope"] == "overall"
                && row["outcome"] == nameof(RunOutcome.Died)
                && row["record"] == "shortest")["run_id"]);
        var lifetimeCombat = Assert.Single(combatTotals, row => row["scope"] == "lifetime");
        Assert.Equal(json.RunTotals.WeaponStatistics.Totals.FiringActions, ReadLong(lifetimeCombat, "firing_actions"));
        Assert.Equal(json.RunTotals.WeaponStatistics.Totals.FiringActions, uiModel.Lifetime.Totals.FiringActions);
        Assert.Equal(json.RunTotals.WeaponStatistics.Totals.AmmunitionUnitsConsumed, ReadLong(lifetimeCombat, "ammunition_units_consumed"));
        Assert.Equal(json.RunTotals.WeaponStatistics.Totals.Projectiles, ReadLong(lifetimeCombat, "projectiles"));
        Assert.Equal(nameof(AdapterCapabilityState.Supported), lifetimeCombat["firing_actions_state"]);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), lifetimeCombat["trigger_attempts_state"]);
        Assert.Equal(uiModel.Capabilities.FiringActions.State, json.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
        Assert.Equal(4, combatTotals.Count);
        Assert.Equal(
            json.RunTotals.WeaponStatistics.Weapons.Values.Sum(value => value.Totals.FiringActions),
            weaponTotals.Where(row => row["scope"] == "lifetime").Sum(row => ReadLong(row, "firing_actions")));
        Assert.Equal(
            json.RunTotals.WeaponStatistics.AmmunitionTypes.Values.Sum(value => value.Totals.AmmunitionUnitsConsumed),
            ammunitionTotals.Where(row => row["scope"] == "lifetime").Sum(row => ReadLong(row, "ammunition_units_consumed")));
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Combat")]
    [Trait("Category", "M11")]
    public void JsonUiAndCsvAgreeOnPlayerKillsObservedDeathsOwnershipAndEquipmentCredit()
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
        var combatRows = ParseCsv(bundle.CombatAttributionCsv);
        var total = Assert.Single(combatRows,
            row => row["scope"] == "lifetime" && row["breakdown"] == "total");
        var companionRow = Assert.Single(combatRows,
            row => row["scope"] == "lifetime" && row["breakdown"] == "ownership"
                   && row["entity_id"] == "Companion");
        var equipment = Assert.Single(ParseCsv(bundle.EquipmentCombatCsv),
            row => row["scope"] == "lifetime");
        var runRow = Assert.Single(ParseCsv(bundle.RunsCsv));
        var runTotals = Assert.Single(ParseCsv(bundle.RunTotalsCsv));
        var mapTotals = Assert.Single(ParseCsv(bundle.MapTotalsCsv));

        Assert.Equal(1, view.Lifetime.Totals.KillsByYou);
        Assert.Equal(2, view.Lifetime.Totals.ObservedWorldDeaths);
        Assert.Equal(1, json.RunTotals.CombatStatistics.Totals.KillsByYou);
        Assert.Equal(2, json.RunTotals.CombatStatistics.Totals.ObservedWorldDeaths);
        Assert.Equal("1", total["kills_by_you"]);
        Assert.Equal("2", total["observed_world_deaths"]);
        Assert.Equal("1", companionRow["observed_world_deaths"]);
        Assert.Equal("1", equipment["kills_by_you"]);
        Assert.Equal("0", equipment["legacy_unclassified_death_credit"]);
        Assert.Equal("1", runRow["kills_by_you"]);
        Assert.Equal("2", runRow["observed_world_deaths"]);
        Assert.Equal("1", runTotals["kills_by_you"]);
        Assert.Equal("2", mapTotals["observed_world_deaths"]);
        Assert.DoesNotContain("enemies_killed", bundle.CombatAttributionCsv.Split('\n')[0], StringComparison.Ordinal);
        Assert.DoesNotContain("enemies_killed", bundle.EquipmentCombatCsv.Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Combat")]
    [Trait("Category", "M11")]
    public void EquipmentCombatCsvCarriesCurrentUnavailableCombatCapabilityStates()
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

        var row = Assert.Single(
            ParseCsv(StatisticsExporter.Create(profile, TestTime).EquipmentCombatCsv),
            value => value["scope"] == "lifetime");

        Assert.Equal("10", row["damage_dealt"]);
        Assert.Equal("1", row["kills_by_you"]);
        Assert.Equal("DisabledIncompatible", row["damage_dealt_state"]);
        Assert.Equal("DisabledIncompatible", row["damage_received_state"]);
        Assert.Equal("DisabledIncompatible", row["ranged_hits_state"]);
        Assert.Equal("DisabledIncompatible", row["melee_hits_state"]);
        Assert.Equal("DisabledIncompatible", row["kills_by_you_state"]);
        Assert.Equal("DisabledIncompatible", row["player_deaths_state"]);
        Assert.Equal("DisabledIncompatible", row["ownership_state"]);
    }

    [Fact]
    [Trait("Category", "Export")]
    public void CsvEscapesItemNamesWithoutChangingTheirValue()
    {
        var profile = CreateProfile();
        const string name = "Soup, \"Deluxe\"\r\nLarge";
        ItemUseReducer.Apply(profile.Statistics, CreateUse("one", "item:one", name, CanonicalItemGroup.Food, 1, ConsumptionUnit.StackUnit));

        var row = Assert.Single(ParseCsv(StatisticsExporter.Create(profile, TestTime).ItemsCsv));

        Assert.Equal(name, row["display_name"]);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "Weapon")]
    public void CurrentUnavailableOutcomeMetricsRestrictEveryJsonAndCsvScope()
    {
        var profile = CreateProfile();
        RunReducer.Apply(profile.Statistics, CreateRun("run-one", RunOutcome.Extracted, 95, 123.5, 8));
        profile.Capabilities.Single(
            capability => capability.AdapterId == WeaponCapabilityIds.AmmunitionConsumption).State =
            AdapterCapabilityState.DisabledIncompatible;
        profile.Capabilities.Single(
            capability => capability.AdapterId == WeaponCapabilityIds.Projectiles).State =
            AdapterCapabilityState.DisabledIncompatible;

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var combatRows = ParseCsv(bundle.CombatTotalsCsv);
        var weaponRows = ParseCsv(bundle.WeaponTotalsCsv);
        var ammunitionRows = ParseCsv(bundle.AmmunitionTotalsCsv);

        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            json.RunTotals.WeaponStatistics.Capabilities.AmmunitionConsumption.State);
        Assert.All(json.RunTotals.Maps.Values, map => Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            map.WeaponStatistics.Capabilities.Projectiles.State));
        Assert.All(json.Runs, run => Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            run.WeaponStatistics.Capabilities.AmmunitionConsumption.State));
        Assert.All(combatRows, row =>
        {
            Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), row["ammunition_consumption_state"]);
            Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), row["projectiles_state"]);
        });
        Assert.All(weaponRows.Concat(ammunitionRows), row =>
        {
            Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), row["ammunition_consumption_state"]);
            Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), row["projectiles_state"]);
        });
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
        var runCsv = Assert.Single(
            ParseCsv(bundle.CombatTotalsCsv),
            row => row["scope"] == "run" && row["scope_id"] == "historical-run");

        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            runJson.WeaponStatistics.Capabilities.FiringActions.State);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), runCsv["firing_actions_state"]);
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
        var lifetimeCsv = Assert.Single(
            ParseCsv(bundle.CombatTotalsCsv),
            row => row["scope"] == "lifetime");

        Assert.Equal(7, json.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            json.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), lifetimeCsv["firing_actions_state"]);
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

        Assert.True(ProfileMigrator.Migrate(profile));

        var model = WeaponStatisticsViewModelFactory.Create(profile);
        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var lifetimeCsv = Assert.Single(
            ParseCsv(bundle.CombatTotalsCsv),
            row => row["scope"] == "lifetime");

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
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), lifetimeCsv["firing_actions_state"]);
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

        Assert.True(ProfileMigrator.Migrate(profile));

        var model = WeaponStatisticsViewModelFactory.Create(profile);
        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var lifetimeCsv = Assert.Single(
            ParseCsv(bundle.CombatTotalsCsv),
            row => row["scope"] == "lifetime");

        Assert.True(lifetime.WasRepairedFromInvalidState);
        Assert.False(WeaponStatisticsReducer.IsEmpty(lifetime));
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            model.Capabilities.FiringActions.State);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            json.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), lifetimeCsv["firing_actions_state"]);
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
        profile.SchemaVersion = 13;
        profile.Statistics.SchemaVersion = 13;
        var lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        lifetime.Weapons["weapon:corrupt"] = null!;
        lifetime.AmmunitionTypes["ammo:corrupt"] = null!;
        new AtomicJsonStore<ProfileDocument>().Save(path, profile);
        var repository = new ProfileRepository(
            temporaryDirectory.Path,
            () => TestTime,
            () => "session-corrupt-identities");

        Assert.True(repository.Open(new SaveIdentitySnapshot { Slot = 1 }).MigratedSchema);

        lifetime = repository.Current.Statistics.RunTotals.WeaponStatistics;
        Assert.Empty(lifetime.Weapons);
        Assert.Empty(lifetime.AmmunitionTypes);
        Assert.True(lifetime.WasRepairedFromInvalidState);
        Assert.False(WeaponStatisticsReducer.IsEmpty(lifetime));
        var initialModel = WeaponStatisticsViewModelFactory.Create(repository.Current);
        var initialBundle = StatisticsExporter.Create(repository.Current, TestTime);
        var initialJson = Deserialize(initialBundle.Json);
        var initialCombat = Assert.Single(
            ParseCsv(initialBundle.CombatTotalsCsv),
            row => row["scope"] == "lifetime");
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            initialModel.Capabilities.FiringActions.State);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            initialJson.RunTotals.WeaponStatistics.Capabilities.FiringActions.State);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), initialCombat["firing_actions_state"]);
        Assert.Empty(ParseCsv(initialBundle.WeaponTotalsCsv));
        Assert.Empty(ParseCsv(initialBundle.AmmunitionTotalsCsv));

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
        var weapon = Assert.Single(ParseCsv(firstBundle.WeaponTotalsCsv));
        var ammunition = Assert.Single(ParseCsv(firstBundle.AmmunitionTotalsCsv));

        Assert.Equal(before, after);
        Assert.Equal(firstBundle.Json, secondBundle.Json);
        Assert.Equal(firstBundle.CombatTotalsCsv, secondBundle.CombatTotalsCsv);
        Assert.Equal(firstBundle.WeaponTotalsCsv, secondBundle.WeaponTotalsCsv);
        Assert.Equal(firstBundle.AmmunitionTotalsCsv, secondBundle.AmmunitionTotalsCsv);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            firstModel.Capabilities.FiringActions.State);
        Assert.Equal(firstModel.Capabilities.FiringActions.State, secondModel.Capabilities.FiringActions.State);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), weapon["firing_actions_state"]);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), ammunition["firing_actions_state"]);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), weapon["ammunition_consumption_state"]);
        Assert.Equal(nameof(AdapterCapabilityState.DisabledIncompatible), ammunition["projectiles_state"]);
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
        var lifetimeCsv = Assert.Single(
            ParseCsv(firstBundle.CombatTotalsCsv),
            row => row["scope"] == "lifetime");

        Assert.True(WeaponStatisticsReducer.IsEmpty(profile.Statistics.RunTotals.WeaponStatistics));
        Assert.Equal(AdapterCapabilityState.Supported, firstModel.Capabilities.FiringActions.State);
        Assert.Equal(firstModel.Capabilities.FiringActions.State, secondModel.Capabilities.FiringActions.State);
        Assert.Equal(nameof(AdapterCapabilityState.Supported), lifetimeCsv["firing_actions_state"]);
        Assert.Equal(before, after);
        Assert.Equal(firstBundle.Json, secondBundle.Json);
        Assert.Equal(firstBundle.CombatTotalsCsv, secondBundle.CombatTotalsCsv);
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
        var item = Assert.Single(ParseCsv(bundle.ItemsCsv));
        var group = Assert.Single(ParseCsv(bundle.GroupsCsv));

        Assert.Equal(nameof(CanonicalItemGroup.Healing), item["group"]);
        Assert.Equal(item["group"], group["group"]);
        Assert.Equal(ReadLong(item, "activation_count"), ReadLong(group, "activation_count"));
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "M9")]
    public void EconomyJsonAndFlattenedCsvAgreeWithStableDimensionsAndCapabilities()
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
        EconomyStatisticsReducer.FinalizeCashRaidOutcome(profile.Statistics.Economy, RunOutcome.Interrupted);

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var json = Deserialize(bundle.Json);
        var totals = ParseCsv(bundle.EconomyTotalsCsv);
        var sources = ParseCsv(bundle.EconomySourcesCsv);
        var contexts = ParseCsv(bundle.EconomyContextsCsv);
        var outcomes = ParseCsv(bundle.CashRaidOutcomesCsv);
        var money = Assert.Single(totals, row => row["scope"] == "lifetime" && row["currency"] == "Money");

        Assert.Equal(100, json.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(30, json.Economy.Currencies["Money"].Totals.GrossOutflow);
        Assert.Equal(70, json.Economy.Currencies["Money"].Totals.NetFlow);
        Assert.Equal(profile.Statistics.Economy.ReplayCursor!.ActivationId, json.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(profile.Statistics.Economy.ReplayCursor.ClosedThroughSequence, json.Economy.ReplayCursor.ClosedThroughSequence);
        Assert.False(json.Economy.LegacyIdentitySaturationIncomplete);
        Assert.Equal(100, ReadLong(money, "gross_inflow"));
        Assert.Equal(30, ReadLong(money, "gross_outflow"));
        Assert.Equal(70, ReadLong(money, "net_flow"));
        Assert.Equal("Supported", money["amount_capability"]);
        Assert.Equal("test", money["amount_capability_provenance"]);
        Assert.Equal("false", money["arithmetic_saturated"]);
        Assert.Equal("false", money["legacy_identity_saturation_incomplete"]);
        Assert.False(money.ContainsKey("deduplication_saturated"));
        Assert.Equal(100, sources.Where(row => row["scope"] == "lifetime" && row["currency"] == "Money").Sum(row => ReadLong(row, "gross_inflow")));
        Assert.Equal(30, sources.Where(row => row["scope"] == "lifetime" && row["currency"] == "Money").Sum(row => ReadLong(row, "gross_outflow")));
        Assert.Contains(sources, row => row["scope"] == "lifetime" && row["source"] == "Reward" && row["gross_inflow"] == "100");
        Assert.Contains(sources, row => row["scope"] == "lifetime" && row["source"] == "Purchase" && row["gross_outflow"] == "30");
        Assert.Contains(contexts, row => row["scope"] == "lifetime" && row["gameplay_context"] == "Reward" && row["gross_inflow"] == "100");
        Assert.Contains(contexts, row => row["scope"] == "lifetime" && row["gameplay_context"] == "Shop" && row["gross_outflow"] == "30");
        var lifetimeOutcome = Assert.Single(outcomes, row => row["scope"] == "lifetime");
        Assert.Equal("7", lifetimeOutcome["acquired"]);
        Assert.Equal("7", lifetimeOutcome["unresolved"]);
        Assert.Equal("true", lifetimeOutcome["terminal_recorded"]);
        Assert.Equal(bundle.EconomyTotalsCsv, StatisticsExporter.Create(profile, TestTime).EconomyTotalsCsv);
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "M9")]
    public void EconomySourceAndContextCsvUseInvariantAsciiNegativeNumbers()
    {
        var profile = CreateProfile();
        profile.Statistics.Economy.Capabilities = EconomyCapabilities();
        EconomyStatisticsReducer.Record(
            profile.Statistics.Economy,
            profile.GenerationId,
            EconomyFlow(
                "culture-outflow",
                CurrencyKind.Money,
                CurrencyFlowDirection.Outflow,
                3,
                CurrencySourceCategory.Purchase,
                GameplayContext.Shop));
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            var bundle = StatisticsExporter.Create(profile, TestTime);
            var source = Assert.Single(
                ParseCsv(bundle.EconomySourcesCsv),
                row => row["scope"] == "lifetime" && row["source"] == "Purchase");
            var context = Assert.Single(
                ParseCsv(bundle.EconomyContextsCsv),
                row => row["scope"] == "lifetime" && row["gameplay_context"] == "Shop");

            Assert.Equal("-3", source["net_flow"]);
            Assert.Equal("-3", context["net_flow"]);
            Assert.Equal(-3, ReadLong(source, "net_flow"));
            Assert.Equal(-3, ReadLong(context, "net_flow"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    [Trait("Category", "Export")]
    [Trait("Category", "M9")]
    public void HistoricalEconomyCsvLeavesUnavailablePreM9ValuesBlank()
    {
        var profile = CreateProfile();
        profile.Statistics.Economy.HistoricalUnavailable = true;
        profile.Statistics.Economy.Capabilities = EconomyCapabilities();

        var bundle = StatisticsExporter.Create(profile, TestTime);
        var totals = ParseCsv(bundle.EconomyTotalsCsv);
        var money = Assert.Single(totals, row => row["scope"] == "lifetime" && row["currency"] == "Money");
        var cash = Assert.Single(totals, row => row["scope"] == "lifetime" && row["currency"] == "Cash");
        var outcome = Assert.Single(ParseCsv(bundle.CashRaidOutcomesCsv), row => row["scope"] == "lifetime");

        Assert.Equal(string.Empty, money["gross_inflow"]);
        Assert.Equal(string.Empty, money["gross_outflow"]);
        Assert.Equal(string.Empty, money["net_flow"]);
        Assert.Equal(string.Empty, cash["gross_inflow"]);
        Assert.Equal(string.Empty, outcome["acquired"]);
        Assert.Equal(string.Empty, outcome["secured"]);
        Assert.Equal(string.Empty, outcome["lost"]);
        Assert.Equal(string.Empty, outcome["unresolved"]);
        Assert.Equal("true", money["historical_unavailable"]);
        Assert.Equal("true", outcome["historical_unavailable"]);
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

        Assert.Equal(30, result.Files.Count);
        Assert.All(result.Files, path => Assert.True(File.Exists(path)));
        Assert.Equal(
            ExpectedExportFileNames,
            result.Files.Select(System.IO.Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(result.Directory, "*.tmp"));
        Assert.Contains("generation-a", result.Directory, StringComparison.Ordinal);
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
            State = id == WeaponCapabilityIds.TriggerAttempts
                ? AdapterCapabilityState.DisabledIncompatible
                : AdapterCapabilityState.Supported,
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
            MapId = "duckov:map:warehouse",
            MapDisplayName = "Warehouse",
            MapKnown = true,
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
            AmmunitionUnitsConsumed = 1,
            ProjectileCount = runId == "run-one" ? 6 : 1,
            Capabilities = new WeaponMetricCapabilities
            {
                FiringActions = Available(),
                AmmunitionConsumption = Available(),
                Projectiles = Available(),
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
        CashTerminalOutcomes = Available(),
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

    private static List<IReadOnlyDictionary<string, string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }

                continue;
            }

            if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                field.Append(character);
            }
        }

        var headers = rows[0];
        return rows.Skip(1)
            .Where(values => values.Count > 1)
            .Select(values => (IReadOnlyDictionary<string, string>)headers
                .Select((header, index) => new KeyValuePair<string, string>(header, values[index]))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
            .ToList();
    }

    private static long ReadLong(IReadOnlyDictionary<string, string> row, string key) =>
        long.Parse(row[key], NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double ReadDouble(IReadOnlyDictionary<string, string> row, string key) =>
        double.Parse(row[key], NumberStyles.Float, CultureInfo.InvariantCulture);
}
