using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Export;

[DataContract]
public sealed class StatisticsExportDocument
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public DateTime ExportedUtc { get; set; }

    [DataMember(Order = 3)]
    public string GenerationId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int Slot { get; set; }

    [DataMember(Order = 5)]
    public long Revision { get; set; }

    [DataMember(Order = 6)]
    public AggregateTotals Overall { get; set; } = new();

    [DataMember(Order = 7)]
    public List<GroupExportRow> Groups { get; set; } = new();

    [DataMember(Order = 8)]
    public List<ItemExportRow> Items { get; set; } = new();

    [DataMember(Order = 9)]
    public RunAggregateTotals RunTotals { get; set; } = new();

    [DataMember(Order = 10)]
    public List<RunSummary> Runs { get; set; } = new();

    [DataMember(Order = 11)]
    public RunDurationRecords RunRecords { get; set; } = new();

    [DataMember(Order = 12)]
    public List<CapabilityRecord> Capabilities { get; set; } = new();
}

[DataContract]
public sealed class GroupExportRow
{
    [DataMember(Order = 1)]
    public string Group { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public AggregateTotals Totals { get; set; } = new();
}

[DataContract]
public sealed class ItemExportRow
{
    [DataMember(Order = 1)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Group { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> EffectTags { get; set; } = new();

    [DataMember(Order = 5)]
    public AggregateTotals Totals { get; set; } = new();
}

public sealed class StatisticsExportBundle
{
    public StatisticsExportBundle(
        StatisticsExportDocument document,
        string json,
        string overviewCsv,
        string groupsCsv,
        string itemsCsv,
        string runsCsv,
        string runTotalsCsv,
        string mapTotalsCsv,
        string recordsCsv,
        string combatTotalsCsv,
        string combatAttributionCsv,
        string weaponTotalsCsv,
        string ammunitionTotalsCsv)
    {
        Document = document;
        Json = json;
        OverviewCsv = overviewCsv;
        GroupsCsv = groupsCsv;
        ItemsCsv = itemsCsv;
        RunsCsv = runsCsv;
        RunTotalsCsv = runTotalsCsv;
        MapTotalsCsv = mapTotalsCsv;
        RecordsCsv = recordsCsv;
        CombatTotalsCsv = combatTotalsCsv;
        CombatAttributionCsv = combatAttributionCsv;
        WeaponTotalsCsv = weaponTotalsCsv;
        AmmunitionTotalsCsv = ammunitionTotalsCsv;
    }

    public StatisticsExportDocument Document { get; }

    public string Json { get; }

    public string OverviewCsv { get; }

    public string GroupsCsv { get; }

    public string ItemsCsv { get; }

    public string RunsCsv { get; }

    public string RunTotalsCsv { get; }

    public string MapTotalsCsv { get; }

    public string RecordsCsv { get; }

    public string CombatTotalsCsv { get; }

    public string CombatAttributionCsv { get; }

    public string WeaponTotalsCsv { get; }

    public string AmmunitionTotalsCsv { get; }
}

public static class StatisticsExporter
{
    private static readonly string[] AmountUnits =
    {
        "Item",
        "StackUnit",
        "Durability",
        "UnknownAmount"
    };
    private static readonly char[] CsvSpecialCharacters = { ',', '"', '\r', '\n' };

    public static StatisticsExportBundle Create(ProfileDocument profile, DateTime exportedUtc)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        exportedUtc = EnsureUtc(exportedUtc);
        var runTotals = CloneRunTotals(profile.Statistics.RunTotals);
        runTotals.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
            runTotals.WeaponStatistics,
            profile.Capabilities,
            allowUninitializedFallback: true);
        runTotals.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
            runTotals.CombatStatistics, profile.Capabilities, allowUninitializedFallback: true);
        foreach (var map in runTotals.Maps.Values)
        {
            map.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                map.WeaponStatistics,
                profile.Capabilities,
                allowUninitializedFallback: false);
            map.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                map.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
        }

        var runs = profile.Statistics.Runs.Select(CloneRun).ToList();
        foreach (var run in runs)
        {
            run.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                run.WeaponStatistics,
                profile.Capabilities,
                allowUninitializedFallback: false);
            run.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                run.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
        }

        var document = new StatisticsExportDocument
        {
            ExportedUtc = exportedUtc,
            GenerationId = profile.GenerationId,
            Slot = profile.Slot,
            Revision = profile.Revision,
            Overall = CloneTotals(profile.Statistics.Overall),
            Groups = profile.Statistics.Groups
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new GroupExportRow
                {
                    Group = entry.Key,
                    Totals = CloneTotals(entry.Value)
                })
                .ToList(),
            Items = profile.Statistics.Items.Values
                .OrderBy(item => item.ItemId, StringComparer.Ordinal)
                .Select(item => new ItemExportRow
                {
                    ItemId = item.ItemId,
                    DisplayName = item.DisplayName,
                    Group = item.Group.ToString(),
                    EffectTags = item.EffectTags.Select(tag => tag.ToString()).OrderBy(tag => tag, StringComparer.Ordinal).ToList(),
                    Totals = CloneTotals(item.Totals)
                })
                .ToList(),
            RunTotals = runTotals,
            Runs = runs,
            RunRecords = CloneRunRecords(profile.Statistics.RunRecords),
            Capabilities = profile.Capabilities.Select(CloneCapability).ToList()
        };

        return new StatisticsExportBundle(
            document,
            SerializeJson(document),
            CreateOverviewCsv(document),
            CreateGroupsCsv(document),
            CreateItemsCsv(document),
            CreateRunsCsv(document),
            CreateRunTotalsCsv(document),
            CreateMapTotalsCsv(document),
            CreateRecordsCsv(document),
            CreateCombatTotalsCsv(document),
            CreateCombatAttributionCsv(document),
            CreateWeaponTotalsCsv(document),
            CreateAmmunitionTotalsCsv(document));
    }

    private static string SerializeJson(StatisticsExportDocument document)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, document);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CreateOverviewCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "generation_id,slot,revision,exported_utc");
        builder.Append(Csv(document.GenerationId)).Append(',')
            .Append(document.Slot.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(document.Revision.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Csv(document.ExportedUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
        AppendTotals(builder, document.Overall);
        return builder.ToString();
    }

    private static string CreateGroupsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "group");
        foreach (var group in document.Groups)
        {
            builder.Append(Csv(group.Group)).Append(',');
            AppendTotals(builder, group.Totals);
        }

        return builder.ToString();
    }

    private static string CreateItemsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "item_id,display_name,group,effect_tags");
        foreach (var item in document.Items)
        {
            builder.Append(Csv(item.ItemId)).Append(',')
                .Append(Csv(item.DisplayName)).Append(',')
                .Append(Csv(item.Group)).Append(',')
                .Append(Csv(string.Join("|", item.EffectTags))).Append(',');
            AppendTotals(builder, item.Totals);
        }

        return builder.ToString();
    }

    private static string CreateRunsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "run_id,save_generation_id,native_raid_id,map_id,map_display_name,map_known,started_utc,ended_utc,active_duration_seconds,wall_clock_duration_seconds,outcome,physical_distance,teleport_distance,integrity_tags,record_eligible,game_version,game_build,lifecycle_capability,lifecycle_adapter_version,movement_capability,movement_adapter_version,map_capability,map_adapter_version");
        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            builder.Append(Csv(run.RunId)).Append(',')
                .Append(Csv(run.SaveGenerationId)).Append(',')
                .Append(Csv(run.NativeRaidId ?? string.Empty)).Append(',')
                .Append(Csv(run.MapId)).Append(',')
                .Append(Csv(run.MapDisplayName)).Append(',')
                .Append(run.MapKnown ? "true" : "false").Append(',')
                .Append(Csv(run.StartedUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(run.EndedUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(run.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.WallClockDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.Outcome).Append(',')
                .Append(run.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(run.IntegrityTags.ToString())).Append(',')
                .Append(run.RecordEligible ? "true" : "false").Append(',')
                .Append(Csv(run.GameVersion)).Append(',')
                .Append(Csv(run.GameBuild)).Append(',')
                .Append(run.LifecycleCapability).Append(',')
                .Append(Csv(run.LifecycleAdapterVersion)).Append(',')
                .Append(run.MovementCapability).Append(',')
                .Append(Csv(run.MovementAdapterVersion)).Append(',')
                .Append(run.MapCapability).Append(',')
                .Append(Csv(run.MapAdapterVersion)).AppendLine();
        }

        return builder.ToString();
    }

    private static string CreateRunTotalsCsv(StatisticsExportDocument document)
    {
        var totals = document.RunTotals;
        var builder = new StringBuilder();
        builder.AppendLine("generation_id,total_runs,extracted,died,interrupted,physical_distance,teleport_distance");
        builder.Append(Csv(document.GenerationId)).Append(',')
            .Append(totals.TotalRuns.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(ReadOutcome(totals.Outcomes, RunOutcome.Extracted)).Append(',')
            .Append(ReadOutcome(totals.Outcomes, RunOutcome.Died)).Append(',')
            .Append(ReadOutcome(totals.Outcomes, RunOutcome.Interrupted)).Append(',')
            .Append(totals.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        return builder.ToString();
    }

    private static string CreateMapTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("map_id,map_display_name,map_known,total_runs,extracted,died,interrupted,physical_distance,teleport_distance");
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            builder.Append(Csv(map.MapId)).Append(',')
                .Append(Csv(map.DisplayName)).Append(',')
                .Append(map.IsKnown ? "true" : "false").Append(',')
                .Append(map.TotalRuns.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(ReadOutcome(map.Outcomes, RunOutcome.Extracted)).Append(',')
                .Append(ReadOutcome(map.Outcomes, RunOutcome.Died)).Append(',')
                .Append(ReadOutcome(map.Outcomes, RunOutcome.Interrupted)).Append(',')
                .Append(map.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        return builder.ToString();
    }

    private static string CreateRecordsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,map_id,map_display_name,outcome,record,run_id,active_duration_seconds,started_utc");
        AppendRecordPair(builder, "overall", string.Empty, string.Empty, RunOutcome.Extracted, document.RunRecords.Extraction);
        AppendRecordPair(builder, "overall", string.Empty, string.Empty, RunOutcome.Died, document.RunRecords.Death);
        foreach (var map in document.RunRecords.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendRecordPair(builder, "map", map.MapId, map.DisplayName, RunOutcome.Extracted, map.Extraction);
            AppendRecordPair(builder, "map", map.MapId, map.DisplayName, RunOutcome.Died, map.Death);
        }

        return builder.ToString();
    }

    private static string CreateCombatTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,scope_display_name,firing_actions,ammunition_units_consumed,projectiles,trigger_attempts_state,firing_actions_state,ammunition_consumption_state,projectiles_state,weapon_identity_state,ammunition_identity_state");
        AppendCombatTotals(builder, "lifetime", document.GenerationId, "Lifetime", document.RunTotals.WeaponStatistics, document.Capabilities);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendCombatTotals(builder, "map", map.MapId, map.DisplayName, map.WeaponStatistics, document.Capabilities);
        }

        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            AppendCombatTotals(builder, "run", run.RunId, run.MapDisplayName, run.WeaponStatistics, document.Capabilities);
        }

        return builder.ToString();
    }

    private static string CreateWeaponTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,weapon_id,display_name,firing_actions,ammunition_units_consumed,projectiles,firing_actions_state,ammunition_consumption_state,projectiles_state");
        AppendWeaponTotals(builder, "lifetime", document.GenerationId, document.RunTotals.WeaponStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendWeaponTotals(builder, "map", map.MapId, map.WeaponStatistics);
        }

        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            AppendWeaponTotals(builder, "run", run.RunId, run.WeaponStatistics);
        }

        return builder.ToString();
    }

    private static string CreateCombatAttributionCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,breakdown,entity_id,display_name,damage_caused,damage_dealt,damage_received,completed_player_projectiles,ranged_hits,accuracy,melee_swings,melee_hits,enemies_killed,player_deaths,headshots,headshot_final_blows,damage_dealt_state,damage_received_state,accuracy_state,melee_swings_state,melee_hits_state,enemies_killed_state,player_deaths_state,ownership_state,enemy_identity_state,enemy_family_state,cause_state,weapon_identity_state,ammunition_identity_state,damage_over_time_state,headshots_state,headshot_final_blows_state,repaired");
        AppendCombatAttributionScope(builder, "lifetime", document.GenerationId, document.RunTotals.CombatStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(x => x.MapId, StringComparer.Ordinal))
            AppendCombatAttributionScope(builder, "map", map.MapId, map.CombatStatistics);
        foreach (var run in document.Runs.OrderBy(x => x.StartedUtc).ThenBy(x => x.RunId, StringComparer.Ordinal))
            AppendCombatAttributionScope(builder, "run", run.RunId, run.CombatStatistics);
        return builder.ToString();
    }

    private static void AppendCombatAttributionScope(
        StringBuilder builder, string scope, string scopeId, CombatStatisticsAggregate statistics)
    {
        AppendCombatAttributionRow(builder, scope, scopeId, "total", string.Empty, "Total", statistics.Totals, statistics);
        AppendRows("enemy", statistics.Enemies);
        AppendRows("killer", statistics.Killers);
        AppendRows("family", statistics.Families);
        AppendRows("cause", statistics.Causes);
        AppendRows("weapon", statistics.Weapons);
        AppendRows("ammunition", statistics.Ammunition);
        AppendRows("ownership", statistics.Ownership);
        return;

        void AppendRows(string kind, Dictionary<string, CombatBreakdownAggregate> rows)
        {
            foreach (var row in rows.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
                AppendCombatAttributionRow(builder, scope, scopeId, kind, row.Id, row.DisplayName, row.Totals, statistics);
        }
    }

    private static void AppendCombatAttributionRow(
        StringBuilder builder, string scope, string scopeId, string breakdown, string entityId,
        string displayName, CombatMetricTotals totals, CombatStatisticsAggregate statistics)
    {
        var caps = statistics.Capabilities;
        var accuracy = totals.CompletedPlayerProjectiles > 0
            ? ((double)totals.RangedHits / totals.CompletedPlayerProjectiles).ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;
        builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',').Append(breakdown).Append(',')
            .Append(Csv(entityId)).Append(',').Append(Csv(displayName)).Append(',')
            .Append(totals.DamageCaused.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.DamageDealt.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.DamageReceived.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.CompletedPlayerProjectiles.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.RangedHits.ToString(CultureInfo.InvariantCulture)).Append(',').Append(accuracy).Append(',')
            .Append(totals.MeleeSwings.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.MeleeHits.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.EnemiesKilled.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.PlayerDeaths.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.Headshots.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.HeadshotFinalBlows.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(caps.DamageDealt.State).Append(',').Append(caps.DamageReceived.State).Append(',')
            .Append(caps.Accuracy.State).Append(',').Append(caps.MeleeSwings.State).Append(',')
            .Append(caps.MeleeHits.State).Append(',').Append(caps.EnemiesKilled.State).Append(',')
            .Append(caps.PlayerDeaths.State).Append(',').Append(caps.Ownership.State).Append(',')
            .Append(caps.EnemyIdentity.State).Append(',').Append(caps.EnemyFamily.State).Append(',')
            .Append(caps.Cause.State).Append(',').Append(caps.WeaponIdentity.State).Append(',')
            .Append(caps.AmmunitionIdentity.State).Append(',').Append(caps.DamageOverTime.State).Append(',')
            .Append(caps.Headshots.State).Append(',').Append(caps.HeadshotFinalBlows.State).Append(',')
            .Append(statistics.WasRepairedFromInvalidState ? "true" : "false").AppendLine();
    }

    private static string CreateAmmunitionTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,ammunition_id,display_name,firing_actions,ammunition_units_consumed,projectiles,firing_actions_state,ammunition_consumption_state,projectiles_state");
        AppendAmmunitionTotals(builder, "lifetime", document.GenerationId, document.RunTotals.WeaponStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendAmmunitionTotals(builder, "map", map.MapId, map.WeaponStatistics);
        }

        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            AppendAmmunitionTotals(builder, "run", run.RunId, run.WeaponStatistics);
        }

        return builder.ToString();
    }

    private static void AppendCombatTotals(
        StringBuilder builder,
        string scope,
        string scopeId,
        string scopeDisplayName,
        WeaponStatisticsAggregate statistics,
        IReadOnlyList<CapabilityRecord>? currentCapabilities = null)
    {
        var capabilities = statistics.Capabilities;
        builder.Append(scope).Append(',')
            .Append(Csv(scopeId)).Append(',')
            .Append(Csv(scopeDisplayName)).Append(',')
            .Append(statistics.Totals.FiringActions.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(statistics.Totals.AmmunitionUnitsConsumed.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(statistics.Totals.Projectiles.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(ReadCapabilityState(currentCapabilities, WeaponCapabilityIds.TriggerAttempts, AdapterCapabilityState.DisabledIncompatible)).Append(',')
            .Append(capabilities.FiringActions.State).Append(',')
            .Append(capabilities.AmmunitionConsumption.State).Append(',')
            .Append(capabilities.Projectiles.State).Append(',')
            .Append(capabilities.WeaponIdentity.State).Append(',')
            .Append(capabilities.AmmunitionIdentity.State).AppendLine();
    }

    private static void AppendWeaponTotals(
        StringBuilder builder,
        string scope,
        string scopeId,
        WeaponStatisticsAggregate statistics)
    {
        foreach (var weapon in statistics.Weapons.Values.OrderBy(value => value.WeaponId, StringComparer.Ordinal))
        {
            builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',')
                .Append(Csv(weapon.WeaponId)).Append(',').Append(Csv(weapon.DisplayName)).Append(',');
            AppendWeaponMetricTotals(builder, weapon.Totals, statistics.Capabilities);
        }
    }

    private static void AppendAmmunitionTotals(
        StringBuilder builder,
        string scope,
        string scopeId,
        WeaponStatisticsAggregate statistics)
    {
        foreach (var ammunition in statistics.AmmunitionTypes.Values.OrderBy(value => value.AmmunitionId, StringComparer.Ordinal))
        {
            builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',')
                .Append(Csv(ammunition.AmmunitionId)).Append(',').Append(Csv(ammunition.DisplayName)).Append(',');
            AppendWeaponMetricTotals(builder, ammunition.Totals, statistics.Capabilities);
        }
    }

    private static void AppendWeaponMetricTotals(
        StringBuilder builder,
        WeaponMetricTotals totals,
        WeaponMetricCapabilities capabilities)
    {
        builder.Append(totals.FiringActions.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.AmmunitionUnitsConsumed.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.Projectiles.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(capabilities.FiringActions.State).Append(',')
            .Append(capabilities.AmmunitionConsumption.State).Append(',')
            .Append(capabilities.Projectiles.State).AppendLine();
    }

    private static AdapterCapabilityState ReadCapabilityState(
        IReadOnlyList<CapabilityRecord>? capabilities,
        string adapterId,
        AdapterCapabilityState fallback) => capabilities?
            .FirstOrDefault(capability => string.Equals(capability.AdapterId, adapterId, StringComparison.Ordinal))
            ?.State ?? fallback;

    private static WeaponMetricCapabilities ApplyCurrentWeaponCapabilityStates(
        WeaponStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var clone = WeaponStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        clone.FiringActions.State = ResolveAvailability(
            aggregate,
            clone.FiringActions,
            ReadCapabilityState(current, WeaponCapabilityIds.FiringActions, clone.FiringActions.State),
            allowUninitializedFallback);
        clone.AmmunitionConsumption.State = ResolveAvailability(
            aggregate,
            clone.AmmunitionConsumption,
            ReadCapabilityState(current, WeaponCapabilityIds.AmmunitionConsumption, clone.AmmunitionConsumption.State),
            allowUninitializedFallback);
        clone.Projectiles.State = ResolveAvailability(
            aggregate,
            clone.Projectiles,
            ReadCapabilityState(current, WeaponCapabilityIds.Projectiles, clone.Projectiles.State),
            allowUninitializedFallback);
        clone.WeaponIdentity.State = ResolveAvailability(
            aggregate,
            clone.WeaponIdentity,
            ReadCapabilityState(current, WeaponCapabilityIds.WeaponIdentity, clone.WeaponIdentity.State),
            allowUninitializedFallback);
        clone.AmmunitionIdentity.State = ResolveAvailability(
            aggregate,
            clone.AmmunitionIdentity,
            ReadCapabilityState(current, WeaponCapabilityIds.AmmunitionIdentity, clone.AmmunitionIdentity.State),
            allowUninitializedFallback);
        return clone;
    }

    private static CombatMetricCapabilities ApplyCurrentCombatCapabilityStates(
        CombatStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var result = CombatStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        Apply(result.DamageDealt, CombatCapabilityIds.DamageDealt);
        Apply(result.DamageReceived, CombatCapabilityIds.DamageReceived);
        Apply(result.RangedHits, CombatCapabilityIds.RangedHits);
        Apply(result.Accuracy, CombatCapabilityIds.Accuracy);
        Apply(result.MeleeSwings, CombatCapabilityIds.MeleeSwings);
        Apply(result.MeleeHits, CombatCapabilityIds.MeleeHits);
        Apply(result.EnemiesKilled, CombatCapabilityIds.EnemiesKilled);
        Apply(result.PlayerDeaths, CombatCapabilityIds.PlayerDeaths);
        Apply(result.Ownership, CombatCapabilityIds.Ownership);
        Apply(result.EnemyIdentity, CombatCapabilityIds.EnemyIdentity);
        Apply(result.EnemyFamily, CombatCapabilityIds.EnemyFamily);
        Apply(result.Cause, CombatCapabilityIds.Cause);
        Apply(result.WeaponIdentity, CombatCapabilityIds.WeaponIdentity);
        Apply(result.AmmunitionIdentity, CombatCapabilityIds.AmmunitionIdentity);
        Apply(result.DamageOverTime, CombatCapabilityIds.DamageOverTime);
        Apply(result.Headshots, CombatCapabilityIds.Headshots);
        Apply(result.HeadshotFinalBlows, CombatCapabilityIds.HeadshotFinalBlows);
        return result;

        void Apply(MetricAvailability value, string id)
        {
            var state = ReadCapabilityState(current, id, AdapterCapabilityState.DisabledIncompatible);
            value.State = allowUninitializedFallback
                ? CombatStatisticsReducer.ResolveCurrentAvailability(aggregate, value, state)
                : CombatStatisticsReducer.RestrictAvailability(value, state);
        }
    }

    private static AdapterCapabilityState ResolveAvailability(
        WeaponStatisticsAggregate aggregate,
        MetricAvailability recorded,
        AdapterCapabilityState current,
        bool allowUninitializedFallback) => allowUninitializedFallback
            ? WeaponStatisticsReducer.ResolveCurrentAvailability(aggregate, recorded, current)
            : WeaponStatisticsReducer.RestrictAvailability(recorded, current);

    private static void AppendRecordPair(
        StringBuilder builder,
        string scope,
        string mapId,
        string mapName,
        RunOutcome outcome,
        DurationRecordPair pair)
    {
        AppendRecord(builder, scope, mapId, mapName, outcome, "shortest", pair.Shortest);
        AppendRecord(builder, scope, mapId, mapName, outcome, "longest", pair.Longest);
    }

    private static void AppendRecord(
        StringBuilder builder,
        string scope,
        string mapId,
        string mapName,
        RunOutcome outcome,
        string recordKind,
        DurationRecordReference? record)
    {
        if (record == null)
        {
            return;
        }

        builder.Append(scope).Append(',')
            .Append(Csv(mapId)).Append(',')
            .Append(Csv(mapName)).Append(',')
            .Append(outcome).Append(',')
            .Append(recordKind).Append(',')
            .Append(Csv(record.RunId)).Append(',')
            .Append(record.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(Csv(record.StartedUtc.ToString("O", CultureInfo.InvariantCulture))).AppendLine();
    }

    private static void AppendTotalsHeader(StringBuilder builder, string prefix)
    {
        builder.Append(prefix).Append(",activation_count,actual_hp_restored");
        foreach (var unit in AmountUnits)
        {
            builder.Append(',').Append(GetAmountColumnName(unit));
        }

        builder.AppendLine();
    }

    private static void AppendTotals(StringBuilder builder, AggregateTotals totals)
    {
        builder.Append(totals.ActivationCount.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(totals.ActualHealthRestored.ToString("R", CultureInfo.InvariantCulture));
        foreach (var unit in AmountUnits)
        {
            builder.Append(',').Append(ReadAmount(totals, unit).ToString("R", CultureInfo.InvariantCulture));
        }

        builder.AppendLine();
    }

    private static double ReadAmount(AggregateTotals totals, string unit) =>
        totals.AmountsByUnit.TryGetValue(unit, out var value) ? value : 0;

    private static AggregateTotals CloneTotals(AggregateTotals source) => new()
    {
        ActivationCount = source.ActivationCount,
        ActualHealthRestored = source.ActualHealthRestored,
        AmountsByUnit = source.AmountsByUnit.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal)
    };

    private static RunAggregateTotals CloneRunTotals(RunAggregateTotals source) => new()
    {
        TotalRuns = source.TotalRuns,
        Outcomes = source.Outcomes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        Maps = source.Maps.ToDictionary(
            entry => entry.Key,
            entry => new MapRunAggregate
            {
                MapId = entry.Value.MapId,
                DisplayName = entry.Value.DisplayName,
                IsKnown = entry.Value.IsKnown,
                TotalRuns = entry.Value.TotalRuns,
                Outcomes = entry.Value.Outcomes.ToDictionary(outcome => outcome.Key, outcome => outcome.Value, StringComparer.Ordinal),
                PhysicalDistance = entry.Value.PhysicalDistance,
                TeleportDistance = entry.Value.TeleportDistance,
                WeaponStatistics = WeaponStatisticsReducer.Clone(entry.Value.WeaponStatistics),
                CombatStatistics = CombatStatisticsReducer.Clone(entry.Value.CombatStatistics)
            },
            StringComparer.Ordinal)
    };

    private static RunSummary CloneRun(RunSummary source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        RunId = source.RunId,
        SaveGenerationId = source.SaveGenerationId,
        NativeRaidId = source.NativeRaidId,
        MapId = source.MapId,
        MapDisplayName = source.MapDisplayName,
        MapKnown = source.MapKnown,
        StartedUtc = source.StartedUtc,
        EndedUtc = source.EndedUtc,
        ActiveDurationSeconds = source.ActiveDurationSeconds,
        WallClockDurationSeconds = source.WallClockDurationSeconds,
        Outcome = source.Outcome,
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        IntegrityTags = source.IntegrityTags,
        RecordEligible = source.RecordEligible,
        GameVersion = source.GameVersion,
        GameBuild = source.GameBuild,
        LifecycleCapability = source.LifecycleCapability,
        LifecycleAdapterVersion = source.LifecycleAdapterVersion,
        MovementCapability = source.MovementCapability,
        MovementAdapterVersion = source.MovementAdapterVersion,
        MapCapability = source.MapCapability,
        MapAdapterVersion = source.MapAdapterVersion,
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics)
    };

    private static CapabilityRecord CloneCapability(CapabilityRecord source) => new()
    {
        AdapterId = source.AdapterId,
        State = source.State,
        Version = source.Version,
        Detail = source.Detail
    };

    private static RunDurationRecords CloneRunRecords(RunDurationRecords source) => new()
    {
        Extraction = ClonePair(source.Extraction),
        Death = ClonePair(source.Death),
        Maps = source.Maps.ToDictionary(
            entry => entry.Key,
            entry => new MapRunDurationRecords
            {
                MapId = entry.Value.MapId,
                DisplayName = entry.Value.DisplayName,
                Extraction = ClonePair(entry.Value.Extraction),
                Death = ClonePair(entry.Value.Death)
            },
            StringComparer.Ordinal)
    };

    private static DurationRecordPair ClonePair(DurationRecordPair source) => new()
    {
        Shortest = CloneRecord(source.Shortest),
        Longest = CloneRecord(source.Longest)
    };

    private static DurationRecordReference? CloneRecord(DurationRecordReference? source) => source == null
        ? null
        : new DurationRecordReference
        {
            RunId = source.RunId,
            ActiveDurationSeconds = source.ActiveDurationSeconds,
            StartedUtc = source.StartedUtc,
            MapId = source.MapId,
            MapDisplayName = source.MapDisplayName
        };

    private static long ReadOutcome(Dictionary<string, long> outcomes, RunOutcome outcome) =>
        outcomes.TryGetValue(outcome.ToString(), out var value) ? value : 0;

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(CsvSpecialCharacters) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string GetAmountColumnName(string unit) =>
        string.Equals(unit, "UnknownAmount", StringComparison.Ordinal)
            ? "unknown_amount"
            : $"{ToSnakeCase(unit)}_amount";

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
