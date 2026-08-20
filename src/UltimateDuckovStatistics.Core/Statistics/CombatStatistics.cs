using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class CombatCapabilityIds
{
    public const string DamageDealt = "native-damage-dealt";
    public const string DamageReceived = "native-damage-received";
    public const string RangedHits = "native-ranged-hits";
    public const string Accuracy = "native-projectile-accuracy";
    public const string MeleeSwings = "native-melee-swings";
    public const string MeleeHits = "native-melee-hits";
    public const string EnemiesKilled = "native-enemies-killed";
    public const string PlayerDeaths = "native-player-deaths";
    public const string Ownership = "native-combat-ownership";
    public const string EnemyIdentity = "native-enemy-identity";
    public const string EnemyFamily = "native-enemy-family";
    public const string Cause = "native-damage-cause";
    public const string WeaponIdentity = "native-damage-weapon-identity";
    public const string AmmunitionIdentity = "native-damage-ammunition-identity";
    public const string DamageOverTime = "native-damage-over-time";
    public const string Headshots = "native-headshots";
    public const string HeadshotFinalBlows = "native-headshot-final-blows";
    public const string KillsByYou = "native-proven-player-final-blows";
    public const string ObservedWorldDeaths = "native-observed-world-deaths";
}

[DataContract]
public sealed class CombatMetricTotals
{
    [DataMember(Order = 1)] public double DamageCaused { get; set; }
    [DataMember(Order = 2)] public double DamageDealt { get; set; }
    [DataMember(Order = 3)] public double DamageReceived { get; set; }
    [DataMember(Order = 4)] public long CompletedPlayerProjectiles { get; set; }
    [DataMember(Order = 5)] public long RangedHits { get; set; }
    [DataMember(Order = 6)] public long MeleeSwings { get; set; }
    [DataMember(Order = 7)] public long MeleeHits { get; set; }
    [DataMember(Order = 8, EmitDefaultValue = false)] public long EnemiesKilled { get; set; }
    [DataMember(Order = 9)] public long PlayerDeaths { get; set; }
    [DataMember(Order = 10)] public long Headshots { get; set; }
    [DataMember(Order = 11)] public long HeadshotFinalBlows { get; set; }
    [DataMember(Order = 12)] public long KillsByYou { get; set; }
    [DataMember(Order = 13)] public long ObservedWorldDeaths { get; set; }
    [DataMember(Order = 14)] public long LegacyUnclassifiedDeaths { get; set; }
}

[DataContract]
public sealed class CombatBreakdownAggregate
{
    [DataMember(Order = 1)] public string Id { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public CombatMetricTotals Totals { get; set; } = new();
}

[DataContract]
public sealed class CombatStatisticsAggregate
{
    [DataMember(Order = 1)] public CombatMetricTotals Totals { get; set; } = new();
    [DataMember(Order = 2)] public Dictionary<string, CombatBreakdownAggregate> Enemies { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 3)] public Dictionary<string, CombatBreakdownAggregate> Killers { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 4)] public Dictionary<string, CombatBreakdownAggregate> Families { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 5)] public Dictionary<string, CombatBreakdownAggregate> Causes { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 6)] public Dictionary<string, CombatBreakdownAggregate> Weapons { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 7)] public Dictionary<string, CombatBreakdownAggregate> Ammunition { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 8)] public Dictionary<string, CombatBreakdownAggregate> Ownership { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 9)] public CombatMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 10)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 11)] public bool HistoricalOwnershipUnavailable { get; set; }
    [DataMember(Order = 12)] public string HistoricalOwnershipProvenance { get; set; } = string.Empty;
}

public sealed class CombatStatisticsNormalizationResult
{
    public bool Changed { get; internal set; }
    public bool Repaired { get; internal set; }
}

public static class CombatStatisticsReducer
{
    public static void Apply(CombatStatisticsAggregate target, CombatRecorded value)
    {
        ValidateAggregate(target);
        Validate(value);
        target.Capabilities = MergeCapabilities(target.Capabilities, value.Capabilities);
        Add(target.Totals, value);

        if (value.TargetIsEnemy && (value.ActualDamageToTarget > 0 || value.KillsByYou > 0
                                    || value.ObservedWorldDeaths > 0 || value.LegacyUnclassifiedDeaths > 0))
        {
            Add(GetOrCreate(target.Enemies, value.TargetId, value.TargetDisplayName).Totals, value);
            Add(GetOrCreate(target.Families, value.TargetFamilyId, value.TargetFamilyDisplayName).Totals, value);
        }

        if (value.ActualDamageReceived > 0 || value.PlayerDeaths > 0)
        {
            Add(GetOrCreate(target.Killers, value.AttackerId, value.AttackerDisplayName).Totals, value);
        }

        Add(GetOrCreate(target.Causes, value.CauseId, value.CauseDisplayName).Totals, value);
        Add(GetOrCreate(target.Weapons, value.WeaponId, value.WeaponDisplayName).Totals, value);
        Add(GetOrCreate(target.Ammunition, value.AmmunitionId, value.AmmunitionDisplayName).Totals, value);
        var ownershipName = CombatObservationPolicy.OwnershipDisplayName(value.Ownership);
        Add(GetOrCreate(target.Ownership, ownershipName, ownershipName).Totals, value);
    }

    public static void Merge(CombatStatisticsAggregate target, CombatStatisticsAggregate source)
    {
        ValidateAggregate(target);
        ValidateAggregate(source);
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
        target.HistoricalOwnershipUnavailable |= source.HistoricalOwnershipUnavailable;
        target.HistoricalOwnershipProvenance = MergeProvenance(
            target.HistoricalOwnershipProvenance,
            source.HistoricalOwnershipProvenance);
        target.Capabilities = MergeCapabilities(target.Capabilities, source.Capabilities);
        Add(target.Totals, source.Totals);
        MergeRows(target.Enemies, source.Enemies);
        MergeRows(target.Killers, source.Killers);
        MergeRows(target.Families, source.Families);
        MergeRows(target.Causes, source.Causes);
        MergeRows(target.Weapons, source.Weapons);
        MergeRows(target.Ammunition, source.Ammunition);
        MergeRows(target.Ownership, source.Ownership);
    }

    public static CombatStatisticsAggregate Clone(CombatStatisticsAggregate source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var clone = new CombatStatisticsAggregate
        {
            Totals = source.Totals == null ? null! : CloneTotals(source.Totals),
            Enemies = CloneRows(source.Enemies),
            Killers = CloneRows(source.Killers),
            Families = CloneRows(source.Families),
            Causes = CloneRows(source.Causes),
            Weapons = CloneRows(source.Weapons),
            Ammunition = CloneRows(source.Ammunition),
            Ownership = CloneRows(source.Ownership),
            Capabilities = source.Capabilities == null ? new CombatMetricCapabilities() : CloneCapabilities(source.Capabilities),
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
            HistoricalOwnershipUnavailable = source.HistoricalOwnershipUnavailable,
            HistoricalOwnershipProvenance = source.HistoricalOwnershipProvenance
        };
        NormalizePersisted(clone);
        return clone;
    }

    public static CombatStatisticsNormalizationResult NormalizePersisted(CombatStatisticsAggregate statistics)
    {
        if (statistics == null) throw new ArgumentNullException(nameof(statistics));
        var result = new CombatStatisticsNormalizationResult();
        statistics.Totals ??= Changed(new CombatMetricTotals(), result, repaired: true);
        NormalizeTotals(statistics.Totals, result, enforceRelationships: true);
        statistics.Capabilities ??= Changed(new CombatMetricCapabilities(), result, repaired: true);
        NormalizeCapabilities(statistics.Capabilities, result);
        statistics.Enemies = NormalizeRows(statistics.Enemies, result);
        statistics.Killers = NormalizeRows(statistics.Killers, result);
        statistics.Families = NormalizeRows(statistics.Families, result);
        statistics.Causes = NormalizeRows(statistics.Causes, result);
        statistics.Weapons = NormalizeRows(statistics.Weapons, result);
        statistics.Ammunition = NormalizeRows(statistics.Ammunition, result);
        statistics.Ownership = NormalizeRows(statistics.Ownership, result);
        if (statistics.HistoricalOwnershipProvenance == null)
            statistics.HistoricalOwnershipProvenance = Changed(string.Empty, result);
        if (result.Repaired && !statistics.WasRepairedFromInvalidState)
        {
            statistics.WasRepairedFromInvalidState = true;
            result.Changed = true;
        }
        return result;
    }

    public static bool MigrateLegacyOwnershipSemantics(
        CombatStatisticsAggregate statistics,
        string provenance)
    {
        if (statistics == null) throw new ArgumentNullException(nameof(statistics));
        NormalizePersisted(statistics);
        if (statistics.Capabilities.EnemiesKilled.Provenance.StartsWith(
                "Schema-11 replaced the ambiguous enemies-killed metric",
                StringComparison.Ordinal)) return false;
        var legacyTotal = statistics.Totals.EnemiesKilled;
        var legacyPlayer = LegacyKills(statistics.Ownership, "Player");
        var legacyCompanion = SaturatingAdd(
            LegacyKills(statistics.Ownership, "PetCompanion"),
            LegacyKills(statistics.Ownership, "Companion"));
        var hadHistoricalOwnershipEvidence = legacyTotal > 0
                                             || statistics.Totals.DamageCaused > 0
                                             || statistics.Totals.DamageReceived > 0
                                             || statistics.Ownership.Count > 0;

        MigrateRows(statistics.Enemies, LegacyDeathDisposition.Unclassified);
        MigrateRows(statistics.Killers, LegacyDeathDisposition.Unclassified);
        MigrateRows(statistics.Families, LegacyDeathDisposition.Unclassified);
        MigrateRows(statistics.Causes, LegacyDeathDisposition.Unclassified);
        MigrateRows(statistics.Weapons, LegacyDeathDisposition.Unclassified);
        MigrateRows(statistics.Ammunition, LegacyDeathDisposition.Unclassified);

        var migratedOwnership = new Dictionary<string, CombatBreakdownAggregate>(StringComparer.Ordinal);
        foreach (var entry in statistics.Ownership)
        {
            var disposition = entry.Key switch
            {
                "Player" => LegacyDeathDisposition.Player,
                "PetCompanion" or "Companion" => LegacyDeathDisposition.ObservedWorld,
                _ => LegacyDeathDisposition.Unclassified
            };
            MigrateLegacyTotals(entry.Value.Totals, disposition);
            var canonicalName = entry.Key == "PetCompanion" ? "Companion" : entry.Key;
            var row = GetOrCreate(migratedOwnership, canonicalName, canonicalName);
            Add(row.Totals, entry.Value.Totals);
        }
        statistics.Ownership = migratedOwnership;

        var provenPlayer = Math.Min(legacyTotal, Math.Max(legacyPlayer, statistics.Totals.HeadshotFinalBlows));
        var provenCompanion = Math.Min(legacyTotal - provenPlayer, legacyCompanion);
        statistics.Totals.EnemiesKilled = 0;
        statistics.Totals.KillsByYou = SaturatingAdd(statistics.Totals.KillsByYou, provenPlayer);
        statistics.Totals.ObservedWorldDeaths = SaturatingAdd(
            statistics.Totals.ObservedWorldDeaths,
            provenCompanion);
        statistics.Totals.LegacyUnclassifiedDeaths = SaturatingAdd(
            statistics.Totals.LegacyUnclassifiedDeaths,
            legacyTotal - provenPlayer - provenCompanion);

        if (hadHistoricalOwnershipEvidence)
        {
            statistics.HistoricalOwnershipUnavailable = true;
            statistics.HistoricalOwnershipProvenance = MergeProvenance(
                statistics.HistoricalOwnershipProvenance,
                provenance);
        }
        statistics.Capabilities.KillsByYou = Clone(statistics.Capabilities.EnemiesKilled);
        statistics.Capabilities.ObservedWorldDeaths = hadHistoricalOwnershipEvidence
            ? new MetricAvailability
            {
                State = AdapterCapabilityState.DisabledIncompatible,
                Provenance = provenance
            }
            : new MetricAvailability();
        statistics.Capabilities.EnemiesKilled = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = "Schema-11 replaced the ambiguous enemies-killed metric with proven player final blows and observed-world deaths."
        };
        ValidateAggregate(statistics);
        return true;
    }

    public static void ValidateAggregate(CombatStatisticsAggregate statistics)
    {
        if (statistics == null || statistics.Totals == null || statistics.Capabilities == null
            || statistics.Enemies == null || statistics.Killers == null || statistics.Families == null
            || statistics.Causes == null || statistics.Weapons == null || statistics.Ammunition == null
            || statistics.Ownership == null)
        {
            throw new ArgumentException("Combat statistics are incomplete.", nameof(statistics));
        }
        ValidateTotals(statistics.Totals, CombatRelationshipScope.Aggregate);
        ValidateCapabilities(statistics.Capabilities);
        foreach (var rows in AllRows(statistics))
        {
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Key) || row.Value == null
                    || string.IsNullOrWhiteSpace(row.Value.Id) || row.Value.Totals == null)
                {
                    throw new ArgumentException("A combat breakdown row is incomplete.", nameof(statistics));
                }
                ValidateTotals(row.Value.Totals, CombatRelationshipScope.Breakdown);
            }
        }
    }

    public static void ValidateRecoveryCandidate(CombatStatisticsAggregate? statistics)
    {
        if (statistics == null) return;
        if (statistics.Totals != null) ValidateTotals(statistics.Totals, CombatRelationshipScope.Aggregate);
        if (statistics.Capabilities != null)
        {
            foreach (var property in typeof(CombatMetricCapabilities).GetProperties())
            {
                if (property.GetValue(statistics.Capabilities) is MetricAvailability availability
                    && !Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
                {
                    throw new ArgumentException("Combat capabilities contain an invalid state.", nameof(statistics));
                }
            }
        }
        foreach (var rows in new[]
                 {
                     statistics.Enemies, statistics.Killers, statistics.Families, statistics.Causes,
                     statistics.Weapons, statistics.Ammunition, statistics.Ownership
                 })
        {
            if (rows == null) continue;
            foreach (var row in rows.Values)
            {
                if (row?.Totals != null) ValidateTotals(row.Totals, CombatRelationshipScope.Breakdown);
            }
        }
    }

    public static CombatMetricCapabilities CloneCapabilities(CombatMetricCapabilities source) => new()
    {
        DamageDealt = Clone(source.DamageDealt),
        DamageReceived = Clone(source.DamageReceived),
        RangedHits = Clone(source.RangedHits),
        Accuracy = Clone(source.Accuracy),
        MeleeSwings = Clone(source.MeleeSwings),
        MeleeHits = Clone(source.MeleeHits),
        EnemiesKilled = Clone(source.EnemiesKilled),
        PlayerDeaths = Clone(source.PlayerDeaths),
        Ownership = Clone(source.Ownership),
        EnemyIdentity = Clone(source.EnemyIdentity),
        EnemyFamily = Clone(source.EnemyFamily),
        Cause = Clone(source.Cause),
        WeaponIdentity = Clone(source.WeaponIdentity),
        AmmunitionIdentity = Clone(source.AmmunitionIdentity),
        DamageOverTime = Clone(source.DamageOverTime),
        Headshots = Clone(source.Headshots),
        HeadshotFinalBlows = Clone(source.HeadshotFinalBlows),
        KillsByYou = Clone(source.KillsByYou),
        ObservedWorldDeaths = Clone(source.ObservedWorldDeaths)
    };

    public static AdapterCapabilityState RestrictAvailability(MetricAvailability recorded, AdapterCapabilityState current) =>
        (AdapterCapabilityState)Math.Max((int)recorded.State, (int)current);

    public static AdapterCapabilityState ResolveCurrentAvailability(
        CombatStatisticsAggregate aggregate,
        MetricAvailability recorded,
        AdapterCapabilityState current)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (recorded == null) throw new ArgumentNullException(nameof(recorded));
        return recorded.State == AdapterCapabilityState.DisabledIncompatible
               && string.IsNullOrWhiteSpace(recorded.Provenance)
               && IsEmpty(aggregate)
            ? current
            : RestrictAvailability(recorded, current);
    }

    public static bool IsEmpty(CombatStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        ValidateAggregate(aggregate);
        var totals = aggregate.Totals;
        return !aggregate.WasRepairedFromInvalidState
               && totals.DamageCaused == 0 && totals.DamageDealt == 0 && totals.DamageReceived == 0
               && totals.CompletedPlayerProjectiles == 0 && totals.RangedHits == 0
               && totals.MeleeSwings == 0 && totals.MeleeHits == 0
               && totals.EnemiesKilled == 0 && totals.KillsByYou == 0
               && totals.ObservedWorldDeaths == 0 && totals.LegacyUnclassifiedDeaths == 0
               && totals.PlayerDeaths == 0
               && totals.Headshots == 0 && totals.HeadshotFinalBlows == 0
               && aggregate.Enemies.Count == 0 && aggregate.Killers.Count == 0
               && aggregate.Families.Count == 0 && aggregate.Causes.Count == 0
               && aggregate.Weapons.Count == 0 && aggregate.Ammunition.Count == 0
               && aggregate.Ownership.Count == 0;
    }

    public static void RestrictCapabilities(
        CombatStatisticsAggregate aggregate,
        CombatMetricCapabilities observed)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (observed == null) throw new ArgumentNullException(nameof(observed));
        ValidateAggregate(aggregate);
        ValidateCapabilities(observed);
        aggregate.Capabilities = MergeCapabilities(aggregate.Capabilities, observed);
    }

    private static void Validate(CombatRecorded value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.EventId) || string.IsNullOrWhiteSpace(value.SaveGenerationId)
            || string.IsNullOrWhiteSpace(value.RunId) || string.IsNullOrWhiteSpace(value.MapId)
            || value.GameplayContext != GameplayContext.Raid || value.Capabilities == null)
        {
            throw new ArgumentException("Combat event is incomplete.", nameof(value));
        }
        if (!Finite(value.ActualDamageToTarget) || !Finite(value.ActualDamageDealt) || !Finite(value.ActualDamageReceived)
            || Counters(value).Any(counter => counter < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Combat values must be finite and non-negative.");
        }
        if (value.RangedHits > value.CompletedPlayerProjectiles
            || value.HeadshotFinalBlows > value.KillsByYou)
        {
            throw new ArgumentException("Combat event outcome relationships are impossible.", nameof(value));
        }
        if ((value.KillsByYou > 0 && value.Ownership != CombatOwnership.Player)
            || (value.ObservedWorldDeaths > 0 && value.Ownership == CombatOwnership.Player))
        {
            throw new ArgumentException("Combat death counters conflict with proven ownership.", nameof(value));
        }
        ValidateCapabilities(value.Capabilities);
    }

    private static IEnumerable<long> Counters(CombatRecorded value)
    {
        yield return value.CompletedPlayerProjectiles; yield return value.RangedHits;
        yield return value.MeleeSwings; yield return value.MeleeHits; yield return value.EnemiesKilled;
        yield return value.KillsByYou; yield return value.ObservedWorldDeaths; yield return value.LegacyUnclassifiedDeaths;
        yield return value.PlayerDeaths; yield return value.Headshots; yield return value.HeadshotFinalBlows;
    }

    private static IEnumerable<Dictionary<string, CombatBreakdownAggregate>> AllRows(CombatStatisticsAggregate value)
    {
        yield return value.Enemies; yield return value.Killers; yield return value.Families;
        yield return value.Causes; yield return value.Weapons; yield return value.Ammunition; yield return value.Ownership;
    }

    private static CombatBreakdownAggregate GetOrCreate(Dictionary<string, CombatBreakdownAggregate> rows, string id, string name)
    {
        id = string.IsNullOrWhiteSpace(id) ? "unknown" : id;
        name = string.IsNullOrWhiteSpace(name) ? id : name;
        if (!rows.TryGetValue(id, out var row))
        {
            row = new CombatBreakdownAggregate { Id = id, DisplayName = name };
            rows[id] = row;
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            row.DisplayName = name;
        }
        return row;
    }

    private static void MergeRows(Dictionary<string, CombatBreakdownAggregate> target, Dictionary<string, CombatBreakdownAggregate> source)
    {
        foreach (var entry in source)
        {
            var row = GetOrCreate(target, entry.Key, entry.Value.DisplayName);
            Add(row.Totals, entry.Value.Totals);
        }
    }

    private static long LegacyKills(
        Dictionary<string, CombatBreakdownAggregate> ownership,
        string key) => ownership.TryGetValue(key, out var row) ? Math.Max(0, row.Totals.EnemiesKilled) : 0;

    private static void MigrateRows(
        Dictionary<string, CombatBreakdownAggregate> rows,
        LegacyDeathDisposition disposition)
    {
        foreach (var row in rows.Values) MigrateLegacyTotals(row.Totals, disposition);
    }

    private static void MigrateLegacyTotals(
        CombatMetricTotals totals,
        LegacyDeathDisposition disposition)
    {
        var legacy = totals.EnemiesKilled;
        if (legacy <= 0)
        {
            totals.EnemiesKilled = 0;
            return;
        }

        if (disposition == LegacyDeathDisposition.Player)
        {
            totals.KillsByYou = SaturatingAdd(totals.KillsByYou, legacy);
        }
        else if (disposition == LegacyDeathDisposition.ObservedWorld)
        {
            totals.ObservedWorldDeaths = SaturatingAdd(totals.ObservedWorldDeaths, legacy);
        }
        else
        {
            var provenPlayer = Math.Min(legacy, totals.HeadshotFinalBlows);
            totals.KillsByYou = SaturatingAdd(totals.KillsByYou, provenPlayer);
            totals.LegacyUnclassifiedDeaths = SaturatingAdd(
                totals.LegacyUnclassifiedDeaths,
                legacy - provenPlayer);
        }
        totals.EnemiesKilled = 0;
    }

    private static void Add(CombatMetricTotals target, CombatRecorded value)
    {
        target.DamageCaused = SaturatingAdd(target.DamageCaused, value.ActualDamageToTarget);
        target.DamageDealt = SaturatingAdd(target.DamageDealt, value.ActualDamageDealt);
        target.DamageReceived = SaturatingAdd(target.DamageReceived, value.ActualDamageReceived);
        target.CompletedPlayerProjectiles = SaturatingAdd(target.CompletedPlayerProjectiles, value.CompletedPlayerProjectiles);
        target.RangedHits = SaturatingAdd(target.RangedHits, value.RangedHits);
        target.MeleeSwings = SaturatingAdd(target.MeleeSwings, value.MeleeSwings);
        target.MeleeHits = SaturatingAdd(target.MeleeHits, value.MeleeHits);
        target.EnemiesKilled = SaturatingAdd(target.EnemiesKilled, value.EnemiesKilled);
        target.KillsByYou = SaturatingAdd(target.KillsByYou, value.KillsByYou);
        target.ObservedWorldDeaths = SaturatingAdd(target.ObservedWorldDeaths, value.ObservedWorldDeaths);
        target.LegacyUnclassifiedDeaths = SaturatingAdd(target.LegacyUnclassifiedDeaths, value.LegacyUnclassifiedDeaths);
        target.PlayerDeaths = SaturatingAdd(target.PlayerDeaths, value.PlayerDeaths);
        target.Headshots = SaturatingAdd(target.Headshots, value.Headshots);
        target.HeadshotFinalBlows = SaturatingAdd(target.HeadshotFinalBlows, value.HeadshotFinalBlows);
    }

    private static void Add(CombatMetricTotals target, CombatMetricTotals source)
    {
        target.DamageCaused = SaturatingAdd(target.DamageCaused, source.DamageCaused);
        target.DamageDealt = SaturatingAdd(target.DamageDealt, source.DamageDealt);
        target.DamageReceived = SaturatingAdd(target.DamageReceived, source.DamageReceived);
        target.CompletedPlayerProjectiles = SaturatingAdd(target.CompletedPlayerProjectiles, source.CompletedPlayerProjectiles);
        target.RangedHits = SaturatingAdd(target.RangedHits, source.RangedHits);
        target.MeleeSwings = SaturatingAdd(target.MeleeSwings, source.MeleeSwings);
        target.MeleeHits = SaturatingAdd(target.MeleeHits, source.MeleeHits);
        target.EnemiesKilled = SaturatingAdd(target.EnemiesKilled, source.EnemiesKilled);
        target.KillsByYou = SaturatingAdd(target.KillsByYou, source.KillsByYou);
        target.ObservedWorldDeaths = SaturatingAdd(target.ObservedWorldDeaths, source.ObservedWorldDeaths);
        target.LegacyUnclassifiedDeaths = SaturatingAdd(target.LegacyUnclassifiedDeaths, source.LegacyUnclassifiedDeaths);
        target.PlayerDeaths = SaturatingAdd(target.PlayerDeaths, source.PlayerDeaths);
        target.Headshots = SaturatingAdd(target.Headshots, source.Headshots);
        target.HeadshotFinalBlows = SaturatingAdd(target.HeadshotFinalBlows, source.HeadshotFinalBlows);
    }

    private static CombatMetricCapabilities MergeCapabilities(CombatMetricCapabilities a, CombatMetricCapabilities b) => new()
    {
        DamageDealt = Merge(a.DamageDealt, b.DamageDealt),
        DamageReceived = Merge(a.DamageReceived, b.DamageReceived),
        RangedHits = Merge(a.RangedHits, b.RangedHits),
        Accuracy = Merge(a.Accuracy, b.Accuracy),
        MeleeSwings = Merge(a.MeleeSwings, b.MeleeSwings),
        MeleeHits = Merge(a.MeleeHits, b.MeleeHits),
        EnemiesKilled = Merge(a.EnemiesKilled, b.EnemiesKilled),
        PlayerDeaths = Merge(a.PlayerDeaths, b.PlayerDeaths),
        Ownership = Merge(a.Ownership, b.Ownership),
        EnemyIdentity = Merge(a.EnemyIdentity, b.EnemyIdentity),
        EnemyFamily = Merge(a.EnemyFamily, b.EnemyFamily),
        Cause = Merge(a.Cause, b.Cause),
        WeaponIdentity = Merge(a.WeaponIdentity, b.WeaponIdentity),
        AmmunitionIdentity = Merge(a.AmmunitionIdentity, b.AmmunitionIdentity),
        DamageOverTime = Merge(a.DamageOverTime, b.DamageOverTime),
        Headshots = Merge(a.Headshots, b.Headshots),
        HeadshotFinalBlows = Merge(a.HeadshotFinalBlows, b.HeadshotFinalBlows),
        KillsByYou = Merge(a.KillsByYou, b.KillsByYou),
        ObservedWorldDeaths = Merge(a.ObservedWorldDeaths, b.ObservedWorldDeaths)
    };

    private static MetricAvailability Merge(MetricAvailability a, MetricAvailability b)
    {
        if (a.State == AdapterCapabilityState.DisabledIncompatible && string.IsNullOrWhiteSpace(a.Provenance)) return Clone(b);
        return new MetricAvailability
        {
            State = (AdapterCapabilityState)Math.Max((int)a.State, (int)b.State),
            Provenance = string.Join(" | ", new[] { a.Provenance, b.Provenance }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
        };
    }

    private static MetricAvailability Clone(MetricAvailability? value) => value == null
        ? null!
        : new MetricAvailability { State = value.State, Provenance = value.Provenance };

    private static CombatMetricTotals CloneTotals(CombatMetricTotals value) => new()
    {
        DamageCaused = value.DamageCaused,
        DamageDealt = value.DamageDealt,
        DamageReceived = value.DamageReceived,
        CompletedPlayerProjectiles = value.CompletedPlayerProjectiles,
        RangedHits = value.RangedHits,
        MeleeSwings = value.MeleeSwings,
        MeleeHits = value.MeleeHits,
        EnemiesKilled = value.EnemiesKilled,
        KillsByYou = value.KillsByYou,
        ObservedWorldDeaths = value.ObservedWorldDeaths,
        LegacyUnclassifiedDeaths = value.LegacyUnclassifiedDeaths,
        PlayerDeaths = value.PlayerDeaths,
        Headshots = value.Headshots,
        HeadshotFinalBlows = value.HeadshotFinalBlows
    };

    private static Dictionary<string, CombatBreakdownAggregate> CloneRows(
        Dictionary<string, CombatBreakdownAggregate>? rows)
    {
        var result = new Dictionary<string, CombatBreakdownAggregate>(StringComparer.Ordinal);
        if (rows == null) return result;
        foreach (var entry in rows)
        {
            result[entry.Key] = entry.Value == null
                ? null!
                : new CombatBreakdownAggregate
                {
                    Id = entry.Value.Id,
                    DisplayName = entry.Value.DisplayName,
                    Totals = entry.Value.Totals == null ? null! : CloneTotals(entry.Value.Totals)
                };
        }
        return result;
    }

    private static Dictionary<string, CombatBreakdownAggregate> NormalizeRows(Dictionary<string, CombatBreakdownAggregate>? rows, CombatStatisticsNormalizationResult result)
    {
        if (rows == null) return Changed(new Dictionary<string, CombatBreakdownAggregate>(StringComparer.Ordinal), result, repaired: true);
        var normalized = new Dictionary<string, CombatBreakdownAggregate>(StringComparer.Ordinal);
        foreach (var entry in rows)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
            {
                result.Changed = result.Repaired = true;
                continue;
            }
            var canonicalId = string.IsNullOrWhiteSpace(entry.Value.Id) ? entry.Key.Trim() : entry.Value.Id.Trim();
            if (!string.Equals(entry.Key, canonicalId, StringComparison.Ordinal)
                || !string.Equals(entry.Value.Id, canonicalId, StringComparison.Ordinal))
            {
                entry.Value.Id = canonicalId;
                result.Changed = result.Repaired = true;
            }
            if (string.IsNullOrWhiteSpace(entry.Value.DisplayName))
            {
                entry.Value.DisplayName = canonicalId;
                result.Changed = result.Repaired = true;
            }
            entry.Value.Totals ??= Changed(new CombatMetricTotals(), result, repaired: true);
            NormalizeTotals(entry.Value.Totals, result, enforceRelationships: false);
            if (normalized.TryGetValue(canonicalId, out var existing))
            {
                Add(existing.Totals, entry.Value.Totals);
                if (string.CompareOrdinal(entry.Value.DisplayName, existing.DisplayName) < 0)
                    existing.DisplayName = entry.Value.DisplayName;
                result.Changed = result.Repaired = true;
            }
            else
            {
                normalized[canonicalId] = entry.Value;
            }
        }
        return normalized;
    }

    private static void NormalizeTotals(
        CombatMetricTotals totals,
        CombatStatisticsNormalizationResult result,
        bool enforceRelationships)
    {
        if (!Finite(totals.DamageCaused)) { totals.DamageCaused = 0; result.Changed = result.Repaired = true; }
        if (!Finite(totals.DamageDealt)) { totals.DamageDealt = 0; result.Changed = result.Repaired = true; }
        if (!Finite(totals.DamageReceived)) { totals.DamageReceived = 0; result.Changed = result.Repaired = true; }
        if (totals.CompletedPlayerProjectiles < 0) { totals.CompletedPlayerProjectiles = 0; result.Changed = result.Repaired = true; }
        if (totals.RangedHits < 0) { totals.RangedHits = 0; result.Changed = result.Repaired = true; }
        if (totals.MeleeSwings < 0) { totals.MeleeSwings = 0; result.Changed = result.Repaired = true; }
        if (totals.MeleeHits < 0) { totals.MeleeHits = 0; result.Changed = result.Repaired = true; }
        if (totals.EnemiesKilled < 0) { totals.EnemiesKilled = 0; result.Changed = result.Repaired = true; }
        if (totals.KillsByYou < 0) { totals.KillsByYou = 0; result.Changed = result.Repaired = true; }
        if (totals.ObservedWorldDeaths < 0) { totals.ObservedWorldDeaths = 0; result.Changed = result.Repaired = true; }
        if (totals.LegacyUnclassifiedDeaths < 0) { totals.LegacyUnclassifiedDeaths = 0; result.Changed = result.Repaired = true; }
        if (totals.PlayerDeaths < 0) { totals.PlayerDeaths = 0; result.Changed = result.Repaired = true; }
        if (totals.Headshots < 0) { totals.Headshots = 0; result.Changed = result.Repaired = true; }
        if (totals.HeadshotFinalBlows < 0) { totals.HeadshotFinalBlows = 0; result.Changed = result.Repaired = true; }
        if (enforceRelationships && totals.RangedHits > totals.CompletedPlayerProjectiles)
        { totals.RangedHits = totals.CompletedPlayerProjectiles; result.Changed = result.Repaired = true; }
        var maximumHeadshotFinalBlows = Math.Min(
            totals.Headshots,
            Math.Max(totals.KillsByYou, totals.EnemiesKilled));
        if (enforceRelationships && totals.HeadshotFinalBlows > maximumHeadshotFinalBlows)
        { totals.HeadshotFinalBlows = maximumHeadshotFinalBlows; result.Changed = result.Repaired = true; }
    }

    private static void NormalizeCapabilities(CombatMetricCapabilities caps, CombatStatisticsNormalizationResult result)
    {
        foreach (var property in typeof(CombatMetricCapabilities).GetProperties())
        {
            if (property.GetValue(caps) is not MetricAvailability availability)
            {
                property.SetValue(caps, new MetricAvailability()); result.Changed = result.Repaired = true; continue;
            }
            if (!Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
            {
                availability.State = AdapterCapabilityState.DisabledIncompatible;
                availability.Provenance = "Invalid persisted capability state was repaired.";
                result.Changed = result.Repaired = true;
            }
        }
    }

    private static void ValidateCapabilities(CombatMetricCapabilities caps)
    {
        foreach (var property in typeof(CombatMetricCapabilities).GetProperties())
        {
            if (property.GetValue(caps) is not MetricAvailability availability
                || !Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
            {
                throw new ArgumentException("Combat capabilities are incomplete.", nameof(caps));
            }
        }
    }

    private static void ValidateTotals(
        CombatMetricTotals totals,
        CombatRelationshipScope relationshipScope = CombatRelationshipScope.None)
    {
        if (!Finite(totals.DamageCaused) || !Finite(totals.DamageDealt) || !Finite(totals.DamageReceived)
            || totals.CompletedPlayerProjectiles < 0 || totals.RangedHits < 0 || totals.MeleeSwings < 0
            || totals.MeleeHits < 0 || totals.EnemiesKilled < 0 || totals.KillsByYou < 0
            || totals.ObservedWorldDeaths < 0 || totals.LegacyUnclassifiedDeaths < 0 || totals.PlayerDeaths < 0
            || totals.Headshots < 0 || totals.HeadshotFinalBlows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totals), "Persisted combat values must be finite and non-negative.");
        }
        if (relationshipScope != CombatRelationshipScope.None
            && (totals.RangedHits > totals.CompletedPlayerProjectiles
                || totals.HeadshotFinalBlows > Math.Max(totals.KillsByYou, totals.EnemiesKilled)
                || (relationshipScope == CombatRelationshipScope.Aggregate
                    && totals.HeadshotFinalBlows > totals.Headshots)))
        {
            throw new ArgumentException("Persisted combat outcome relationships are impossible.", nameof(totals));
        }
    }

    private enum CombatRelationshipScope
    {
        None,
        Aggregate,
        Breakdown
    }

    private enum LegacyDeathDisposition
    {
        Unclassified,
        Player,
        ObservedWorld
    }

    private static T Changed<T>(T value, CombatStatisticsNormalizationResult result, bool repaired = false)
    {
        result.Changed = true; result.Repaired |= repaired; return value;
    }

    private static bool Finite(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    private static long SaturatingAdd(long current, long value) => current > long.MaxValue - value ? long.MaxValue : current + value;
    private static double SaturatingAdd(double current, double value) => current > double.MaxValue - value ? double.MaxValue : current + value;

    private static string MergeProvenance(string? left, string? right) => string.Join(
        " | ",
        new[] { left, right }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));
}
