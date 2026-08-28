using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class WeaponCapabilityIds
{
    public const string TriggerAttempts = "native-trigger-attempts";
    public const string FiringActions = "native-firing-actions";
    public const string AmmunitionConsumption = "native-ammunition-consumption";
    public const string Projectiles = "native-projectile-count";
    public const string WeaponIdentity = "native-weapon-identity";
    public const string AmmunitionIdentity = "native-ammunition-identity";
    public const string WeaponAmmunitionPairing = "native-weapon-ammunition-pairing";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        TriggerAttempts,
        FiringActions,
        AmmunitionConsumption,
        Projectiles,
        WeaponIdentity,
        AmmunitionIdentity,
        WeaponAmmunitionPairing
    };
}

[DataContract]
public sealed class WeaponMetricTotals
{
    [DataMember(Order = 1)]
    public long FiringActions { get; set; }

    [DataMember(Order = 2)]
    public long AmmunitionUnitsConsumed { get; set; }

    [DataMember(Order = 3)]
    public long Projectiles { get; set; }
}

[DataContract]
public sealed class WeaponAggregate
{
    [DataMember(Order = 1)]
    public string WeaponId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public WeaponMetricTotals Totals { get; set; } = new();
}

[DataContract]
public sealed class AmmunitionAggregate
{
    [DataMember(Order = 1)]
    public string AmmunitionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public WeaponMetricTotals Totals { get; set; } = new();
}

[DataContract]
public sealed class WeaponAmmunitionPairAggregate
{
    [DataMember(Order = 1)] public string WeaponId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string WeaponDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public string AmmunitionId { get; set; } = string.Empty;
    [DataMember(Order = 4)] public string AmmunitionDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 5)] public long FiringActions { get; set; }
}

[DataContract]
public sealed class WeaponStatisticsAggregate
{
    [DataMember(Order = 1)]
    public WeaponMetricTotals Totals { get; set; } = new();

    [DataMember(Order = 2)]
    public Dictionary<string, WeaponAggregate> Weapons { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 3)]
    public Dictionary<string, AmmunitionAggregate> AmmunitionTypes { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 4)]
    public WeaponMetricCapabilities Capabilities { get; set; } = new();

    [DataMember(Order = 5)]
    public bool WasRepairedFromInvalidState { get; set; }

    [DataMember(Order = 6)]
    public Dictionary<string, WeaponAmmunitionPairAggregate> WeaponAmmunitionPairs { get; set; } =
        new(StringComparer.Ordinal);

    [DataMember(Order = 7)]
    public Dictionary<string, long> UncorrelatedWeaponFiringActions { get; set; } =
        new(StringComparer.Ordinal);

    [DataMember(Order = 8)]
    public Dictionary<string, long> UncorrelatedAmmunitionFiringActions { get; set; } =
        new(StringComparer.Ordinal);

    [DataMember(Order = 9)]
    public long UncorrelatedFiringActions { get; set; }

    [DataMember(Order = 10)]
    public bool HistoricalPairingUnavailable { get; set; }

    [DataMember(Order = 11)]
    public string HistoricalPairingProvenance { get; set; } = string.Empty;
}

public sealed class WeaponStatisticsNormalizationResult
{
    public bool Changed { get; internal set; }

    public bool InvalidCounters { get; internal set; }

    public bool InvalidCapabilities { get; internal set; }

    public bool InvalidIdentityEntries { get; internal set; }
}

public static class WeaponStatisticsReducer
{
    public static void Apply(WeaponStatisticsAggregate target, ShotRecorded shot)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        ValidateAggregate(target);
        Validate(shot);
        PreflightPairingApply(target, shot);
        target.Capabilities = MergeCapabilities(target.Capabilities, shot.Capabilities);
        Add(target.Totals, shot);

        if (shot.Capabilities.WeaponIdentity.State != AdapterCapabilityState.DisabledIncompatible)
        {
            var weapon = GetOrCreateWeapon(target, shot);
            Add(weapon.Totals, shot);
        }

        if (shot.Capabilities.AmmunitionIdentity.State != AdapterCapabilityState.DisabledIncompatible)
        {
            var ammunition = GetOrCreateAmmunition(target, shot);
            Add(ammunition.Totals, shot);
        }

        RecordPairing(target, shot);
    }

    public static void Merge(WeaponStatisticsAggregate target, WeaponStatisticsAggregate source)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        ValidateAggregate(target);
        ValidateAggregate(source);
        PreflightPairingMerge(target, source);
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
        target.Capabilities = MergeCapabilities(target.Capabilities, source.Capabilities);
        target.HistoricalPairingUnavailable |= source.HistoricalPairingUnavailable;
        target.HistoricalPairingProvenance = MergeProvenance(
            target.HistoricalPairingProvenance,
            source.HistoricalPairingProvenance);
        Add(target.Totals, source.Totals);
        foreach (var sourceWeapon in source.Weapons.Values)
        {
            if (!target.Weapons.TryGetValue(sourceWeapon.WeaponId, out var targetWeapon))
            {
                targetWeapon = new WeaponAggregate
                {
                    WeaponId = sourceWeapon.WeaponId,
                    DisplayName = sourceWeapon.DisplayName
                };
                target.Weapons[sourceWeapon.WeaponId] = targetWeapon;
            }

            targetWeapon.DisplayName = sourceWeapon.DisplayName;
            Add(targetWeapon.Totals, sourceWeapon.Totals);
        }

        foreach (var sourceAmmunition in source.AmmunitionTypes.Values)
        {
            if (!target.AmmunitionTypes.TryGetValue(sourceAmmunition.AmmunitionId, out var targetAmmunition))
            {
                targetAmmunition = new AmmunitionAggregate
                {
                    AmmunitionId = sourceAmmunition.AmmunitionId,
                    DisplayName = sourceAmmunition.DisplayName
                };
                target.AmmunitionTypes[sourceAmmunition.AmmunitionId] = targetAmmunition;
            }

            targetAmmunition.DisplayName = sourceAmmunition.DisplayName;
            Add(targetAmmunition.Totals, sourceAmmunition.Totals);
        }

        foreach (var sourcePair in source.WeaponAmmunitionPairs.Values)
        {
            var key = PairKey(sourcePair.WeaponId, sourcePair.AmmunitionId);
            if (!target.WeaponAmmunitionPairs.TryGetValue(key, out var targetPair))
            {
                targetPair = new WeaponAmmunitionPairAggregate
                {
                    WeaponId = sourcePair.WeaponId,
                    WeaponDisplayName = sourcePair.WeaponDisplayName,
                    AmmunitionId = sourcePair.AmmunitionId,
                    AmmunitionDisplayName = sourcePair.AmmunitionDisplayName
                };
                target.WeaponAmmunitionPairs[key] = targetPair;
            }
            if (!string.IsNullOrWhiteSpace(sourcePair.WeaponDisplayName))
                targetPair.WeaponDisplayName = sourcePair.WeaponDisplayName;
            if (!string.IsNullOrWhiteSpace(sourcePair.AmmunitionDisplayName))
                targetPair.AmmunitionDisplayName = sourcePair.AmmunitionDisplayName;
            targetPair.FiringActions = CheckedAdd(targetPair.FiringActions, sourcePair.FiringActions);
        }
        MergeCheckedCounts(target.UncorrelatedWeaponFiringActions, source.UncorrelatedWeaponFiringActions);
        MergeCheckedCounts(target.UncorrelatedAmmunitionFiringActions, source.UncorrelatedAmmunitionFiringActions);
        target.UncorrelatedFiringActions = CheckedAdd(
            target.UncorrelatedFiringActions,
            source.UncorrelatedFiringActions);
    }

    public static WeaponStatisticsAggregate Clone(WeaponStatisticsAggregate source)
    {
        var clone = new WeaponStatisticsAggregate();
        Merge(clone, source);
        return clone;
    }

    public static WeaponStatisticsNormalizationResult NormalizePersisted(WeaponStatisticsAggregate statistics)
    {
        if (statistics == null)
        {
            throw new ArgumentNullException(nameof(statistics));
        }

        var result = new WeaponStatisticsNormalizationResult();
        if (statistics.Totals == null)
        {
            statistics.Totals = new WeaponMetricTotals();
            result.Changed = true;
        }

        NormalizeTotals(statistics.Totals, result);
        if (statistics.Weapons == null)
        {
            statistics.Weapons = new Dictionary<string, WeaponAggregate>(StringComparer.Ordinal);
            result.Changed = true;
        }

        if (statistics.AmmunitionTypes == null)
        {
            statistics.AmmunitionTypes = new Dictionary<string, AmmunitionAggregate>(StringComparer.Ordinal);
            result.Changed = true;
        }

        if (statistics.Capabilities == null)
        {
            statistics.Capabilities = new WeaponMetricCapabilities();
            result.Changed = true;
            result.InvalidCapabilities = true;
        }

        if (statistics.WeaponAmmunitionPairs == null)
        {
            statistics.WeaponAmmunitionPairs = new Dictionary<string, WeaponAmmunitionPairAggregate>(StringComparer.Ordinal);
            result.Changed = true;
        }
        if (statistics.UncorrelatedWeaponFiringActions == null)
        {
            statistics.UncorrelatedWeaponFiringActions = new Dictionary<string, long>(StringComparer.Ordinal);
            result.Changed = true;
        }
        if (statistics.UncorrelatedAmmunitionFiringActions == null)
        {
            statistics.UncorrelatedAmmunitionFiringActions = new Dictionary<string, long>(StringComparer.Ordinal);
            result.Changed = true;
        }
        if (statistics.HistoricalPairingProvenance == null)
        {
            statistics.HistoricalPairingProvenance = string.Empty;
            result.Changed = true;
        }

        NormalizeCapabilities(statistics.Capabilities, result);
        foreach (var entry in statistics.Weapons.ToArray())
        {
            var weapon = entry.Value;
            if (weapon == null || string.IsNullOrWhiteSpace(entry.Key))
            {
                statistics.Weapons.Remove(entry.Key);
                result.Changed = true;
                result.InvalidIdentityEntries = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(weapon.WeaponId))
            {
                weapon.WeaponId = entry.Key;
                result.Changed = true;
            }

            if (string.IsNullOrWhiteSpace(weapon.DisplayName))
            {
                weapon.DisplayName = weapon.WeaponId;
                result.Changed = true;
            }

            if (weapon.Totals == null)
            {
                weapon.Totals = new WeaponMetricTotals();
                result.Changed = true;
            }

            NormalizeTotals(weapon.Totals, result);
        }

        foreach (var entry in statistics.AmmunitionTypes.ToArray())
        {
            var ammunition = entry.Value;
            if (ammunition == null || string.IsNullOrWhiteSpace(entry.Key))
            {
                statistics.AmmunitionTypes.Remove(entry.Key);
                result.Changed = true;
                result.InvalidIdentityEntries = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(ammunition.AmmunitionId))
            {
                ammunition.AmmunitionId = entry.Key;
                result.Changed = true;
            }

            if (string.IsNullOrWhiteSpace(ammunition.DisplayName))
            {
                ammunition.DisplayName = ammunition.AmmunitionId;
                result.Changed = true;
            }

            if (ammunition.Totals == null)
            {
                ammunition.Totals = new WeaponMetricTotals();
                result.Changed = true;
            }

            NormalizeTotals(ammunition.Totals, result);
        }


        var normalizedPairs = new Dictionary<string, WeaponAmmunitionPairAggregate>(StringComparer.Ordinal);
        foreach (var entry in statistics.WeaponAmmunitionPairs.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var pair = entry.Value;
            if (pair == null || string.IsNullOrWhiteSpace(pair.WeaponId)
                || string.IsNullOrWhiteSpace(pair.AmmunitionId) || pair.FiringActions < 0)
            {
                result.Changed = true;
                result.InvalidIdentityEntries = true;
                continue;
            }
            pair.WeaponId = pair.WeaponId.Trim();
            pair.AmmunitionId = pair.AmmunitionId.Trim();
            pair.WeaponDisplayName = string.IsNullOrWhiteSpace(pair.WeaponDisplayName)
                ? pair.WeaponId : pair.WeaponDisplayName.Trim();
            pair.AmmunitionDisplayName = string.IsNullOrWhiteSpace(pair.AmmunitionDisplayName)
                ? pair.AmmunitionId : pair.AmmunitionDisplayName.Trim();
            var key = PairKey(pair.WeaponId, pair.AmmunitionId);
            if (!string.Equals(entry.Key, key, StringComparison.Ordinal)) result.Changed = true;
            if (!normalizedPairs.TryGetValue(key, out var existing))
            {
                normalizedPairs[key] = pair;
            }
            else
            {
                try { existing.FiringActions = CheckedAdd(existing.FiringActions, pair.FiringActions); }
                catch (OverflowException) { existing.FiringActions = 0; result.InvalidCounters = true; }
                result.Changed = true;
            }
        }
        statistics.WeaponAmmunitionPairs = normalizedPairs;
        statistics.UncorrelatedWeaponFiringActions = NormalizeCheckedCounts(
            statistics.UncorrelatedWeaponFiringActions, statistics.Weapons.Keys, result);
        statistics.UncorrelatedAmmunitionFiringActions = NormalizeCheckedCounts(
            statistics.UncorrelatedAmmunitionFiringActions, statistics.AmmunitionTypes.Keys, result);
        if (statistics.UncorrelatedFiringActions < 0)
        {
            statistics.UncorrelatedFiringActions = 0;
            result.Changed = true;
            result.InvalidCounters = true;
        }
        else if (statistics.UncorrelatedFiringActions > statistics.Totals.FiringActions)
        {
            statistics.UncorrelatedFiringActions = statistics.Totals.FiringActions;
            result.Changed = true;
            result.InvalidIdentityEntries = true;
        }

        if ((result.InvalidCounters || result.InvalidCapabilities || result.InvalidIdentityEntries)
            && !statistics.WasRepairedFromInvalidState)
        {
            statistics.WasRepairedFromInvalidState = true;
            result.Changed = true;
        }

        return result;
    }

    public static void ValidateAggregate(WeaponStatisticsAggregate statistics)
    {
        if (statistics == null
            || statistics.Totals == null
            || statistics.Weapons == null
            || statistics.AmmunitionTypes == null
            || statistics.Capabilities == null
            || statistics.WeaponAmmunitionPairs == null
            || statistics.UncorrelatedWeaponFiringActions == null
            || statistics.UncorrelatedAmmunitionFiringActions == null
            || statistics.HistoricalPairingProvenance == null)
        {
            throw new ArgumentException("Weapon statistics are incomplete.", nameof(statistics));
        }

        ValidateTotals(statistics.Totals);
        ValidateCapabilities(statistics.Capabilities);
        foreach (var entry in statistics.Weapons)
        {
            if (string.IsNullOrWhiteSpace(entry.Key)
                || entry.Value == null
                || string.IsNullOrWhiteSpace(entry.Value.WeaponId)
                || !string.Equals(entry.Key, entry.Value.WeaponId, StringComparison.Ordinal)
                || entry.Value.Totals == null)
            {
                throw new ArgumentException("A persisted weapon aggregate is incomplete.", nameof(statistics));
            }

            ValidateTotals(entry.Value.Totals);
        }

        foreach (var entry in statistics.AmmunitionTypes)
        {
            if (string.IsNullOrWhiteSpace(entry.Key)
                || entry.Value == null
                || string.IsNullOrWhiteSpace(entry.Value.AmmunitionId)
                || !string.Equals(entry.Key, entry.Value.AmmunitionId, StringComparison.Ordinal)
                || entry.Value.Totals == null)
            {
                throw new ArgumentException("A persisted ammunition aggregate is incomplete.", nameof(statistics));
            }

            ValidateTotals(entry.Value.Totals);
        }

        if (statistics.UncorrelatedFiringActions < 0)
            throw new ArgumentOutOfRangeException(nameof(statistics), "Uncorrelated firing actions cannot be negative.");
        ValidateCheckedCounts(statistics.UncorrelatedWeaponFiringActions, statistics.Weapons.Keys, "weapon");
        ValidateCheckedCounts(statistics.UncorrelatedAmmunitionFiringActions, statistics.AmmunitionTypes.Keys, "ammunition");
        foreach (var entry in statistics.WeaponAmmunitionPairs)
        {
            var pair = entry.Value;
            if (pair == null || string.IsNullOrWhiteSpace(pair.WeaponId)
                || string.IsNullOrWhiteSpace(pair.WeaponDisplayName)
                || string.IsNullOrWhiteSpace(pair.AmmunitionId)
                || string.IsNullOrWhiteSpace(pair.AmmunitionDisplayName)
                || pair.FiringActions < 0
                || !statistics.Weapons.ContainsKey(pair.WeaponId)
                || !statistics.AmmunitionTypes.ContainsKey(pair.AmmunitionId)
                || !string.Equals(entry.Key, PairKey(pair.WeaponId, pair.AmmunitionId), StringComparison.Ordinal))
                throw new ArgumentException("A persisted weapon-ammunition pair is incomplete.", nameof(statistics));
        }
        ValidatePairingReconciliation(statistics);
    }

    public static WeaponMetricCapabilities CloneCapabilities(WeaponMetricCapabilities source) => new()
    {
        FiringActions = CloneAvailability(source.FiringActions),
        AmmunitionConsumption = CloneAvailability(source.AmmunitionConsumption),
        Projectiles = CloneAvailability(source.Projectiles),
        WeaponIdentity = CloneAvailability(source.WeaponIdentity),
        AmmunitionIdentity = CloneAvailability(source.AmmunitionIdentity),
        WeaponAmmunitionPairing = CloneAvailability(source.WeaponAmmunitionPairing)
    };

    public static AdapterCapabilityState RestrictAvailability(
        MetricAvailability recorded,
        AdapterCapabilityState current)
    {
        if (recorded == null)
        {
            throw new ArgumentNullException(nameof(recorded));
        }

        return (AdapterCapabilityState)Math.Max((int)recorded.State, (int)current);
    }

    public static AdapterCapabilityState ResolveCurrentAvailability(
        WeaponStatisticsAggregate aggregate,
        MetricAvailability recorded,
        AdapterCapabilityState current)
    {
        if (aggregate == null)
        {
            throw new ArgumentNullException(nameof(aggregate));
        }

        if (recorded == null)
        {
            throw new ArgumentNullException(nameof(recorded));
        }

        return recorded.State == AdapterCapabilityState.DisabledIncompatible
               && string.IsNullOrWhiteSpace(recorded.Provenance)
               && IsEmpty(aggregate)
            ? current
            : RestrictAvailability(recorded, current);
    }

    public static bool IsEmpty(WeaponStatisticsAggregate aggregate)
    {
        if (aggregate == null)
        {
            throw new ArgumentNullException(nameof(aggregate));
        }

        ValidateAggregate(aggregate);
        return !aggregate.WasRepairedFromInvalidState
               && aggregate.Totals.FiringActions == 0
               && aggregate.Totals.AmmunitionUnitsConsumed == 0
               && aggregate.Totals.Projectiles == 0
               && aggregate.Weapons.Count == 0
               && aggregate.AmmunitionTypes.Count == 0
               && aggregate.WeaponAmmunitionPairs.Count == 0
               && aggregate.UncorrelatedWeaponFiringActions.Count == 0
               && aggregate.UncorrelatedAmmunitionFiringActions.Count == 0
               && aggregate.UncorrelatedFiringActions == 0
               && !aggregate.HistoricalPairingUnavailable;
    }

    private static WeaponAggregate GetOrCreateWeapon(WeaponStatisticsAggregate target, ShotRecorded shot)
    {
        if (!target.Weapons.TryGetValue(shot.WeaponId, out var weapon))
        {
            weapon = new WeaponAggregate
            {
                WeaponId = shot.WeaponId,
                DisplayName = shot.WeaponDisplayName
            };
            target.Weapons[shot.WeaponId] = weapon;
        }

        weapon.DisplayName = shot.WeaponDisplayName;
        return weapon;
    }

    private static AmmunitionAggregate GetOrCreateAmmunition(WeaponStatisticsAggregate target, ShotRecorded shot)
    {
        if (!target.AmmunitionTypes.TryGetValue(shot.AmmunitionId, out var ammunition))
        {
            ammunition = new AmmunitionAggregate
            {
                AmmunitionId = shot.AmmunitionId,
                DisplayName = shot.AmmunitionDisplayName
            };
            target.AmmunitionTypes[shot.AmmunitionId] = ammunition;
        }

        ammunition.DisplayName = shot.AmmunitionDisplayName;
        return ammunition;
    }

    public static string PairKey(string weaponId, string ammunitionId) =>
        weaponId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + weaponId
        + ammunitionId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + ammunitionId;

    private static void PreflightPairingApply(WeaponStatisticsAggregate target, ShotRecorded shot)
    {
        var actions = shot.FiringActionCount ?? 0;
        if (actions == 0) return;
        var weaponKnown = shot.Capabilities.WeaponIdentity.State != AdapterCapabilityState.DisabledIncompatible;
        var ammunitionKnown = shot.Capabilities.AmmunitionIdentity.State != AdapterCapabilityState.DisabledIncompatible;
        var pairSupported = shot.Capabilities.WeaponAmmunitionPairing.State != AdapterCapabilityState.DisabledIncompatible;
        if (weaponKnown && ammunitionKnown && pairSupported)
        {
            target.WeaponAmmunitionPairs.TryGetValue(PairKey(shot.WeaponId, shot.AmmunitionId), out var pair);
            _ = CheckedAdd(pair?.FiringActions ?? 0, actions);
            return;
        }
        _ = CheckedAdd(target.UncorrelatedFiringActions, actions);
        if (weaponKnown)
            _ = CheckedAdd(target.UncorrelatedWeaponFiringActions.GetValueOrDefault(shot.WeaponId), actions);
        if (ammunitionKnown)
            _ = CheckedAdd(target.UncorrelatedAmmunitionFiringActions.GetValueOrDefault(shot.AmmunitionId), actions);
    }

    private static void RecordPairing(WeaponStatisticsAggregate target, ShotRecorded shot)
    {
        var actions = shot.FiringActionCount ?? 0;
        if (actions == 0) return;
        var weaponKnown = shot.Capabilities.WeaponIdentity.State != AdapterCapabilityState.DisabledIncompatible;
        var ammunitionKnown = shot.Capabilities.AmmunitionIdentity.State != AdapterCapabilityState.DisabledIncompatible;
        var pairSupported = shot.Capabilities.WeaponAmmunitionPairing.State != AdapterCapabilityState.DisabledIncompatible;
        if (weaponKnown && ammunitionKnown && pairSupported)
        {
            var key = PairKey(shot.WeaponId, shot.AmmunitionId);
            if (!target.WeaponAmmunitionPairs.TryGetValue(key, out var pair))
            {
                pair = new WeaponAmmunitionPairAggregate
                {
                    WeaponId = shot.WeaponId,
                    WeaponDisplayName = shot.WeaponDisplayName,
                    AmmunitionId = shot.AmmunitionId,
                    AmmunitionDisplayName = shot.AmmunitionDisplayName
                };
                target.WeaponAmmunitionPairs[key] = pair;
            }
            pair.WeaponDisplayName = shot.WeaponDisplayName;
            pair.AmmunitionDisplayName = shot.AmmunitionDisplayName;
            pair.FiringActions = CheckedAdd(pair.FiringActions, actions);
            return;
        }
        target.UncorrelatedFiringActions = CheckedAdd(target.UncorrelatedFiringActions, actions);
        if (weaponKnown)
            target.UncorrelatedWeaponFiringActions[shot.WeaponId] = CheckedAdd(
                target.UncorrelatedWeaponFiringActions.GetValueOrDefault(shot.WeaponId), actions);
        if (ammunitionKnown)
            target.UncorrelatedAmmunitionFiringActions[shot.AmmunitionId] = CheckedAdd(
                target.UncorrelatedAmmunitionFiringActions.GetValueOrDefault(shot.AmmunitionId), actions);
    }

    private static void PreflightPairingMerge(WeaponStatisticsAggregate target, WeaponStatisticsAggregate source)
    {
        foreach (var sourcePair in source.WeaponAmmunitionPairs.Values)
        {
            target.WeaponAmmunitionPairs.TryGetValue(
                PairKey(sourcePair.WeaponId, sourcePair.AmmunitionId), out var targetPair);
            _ = CheckedAdd(targetPair?.FiringActions ?? 0, sourcePair.FiringActions);
        }
        foreach (var entry in source.UncorrelatedWeaponFiringActions)
            _ = CheckedAdd(target.UncorrelatedWeaponFiringActions.GetValueOrDefault(entry.Key), entry.Value);
        foreach (var entry in source.UncorrelatedAmmunitionFiringActions)
            _ = CheckedAdd(target.UncorrelatedAmmunitionFiringActions.GetValueOrDefault(entry.Key), entry.Value);
        _ = CheckedAdd(target.UncorrelatedFiringActions, source.UncorrelatedFiringActions);
    }

    private static void MergeCheckedCounts(Dictionary<string, long> target, Dictionary<string, long> source)
    {
        foreach (var entry in source)
            target[entry.Key] = CheckedAdd(target.GetValueOrDefault(entry.Key), entry.Value);
    }

    private static void Add(WeaponMetricTotals target, ShotRecorded shot)
    {
        if (shot.FiringActionCount.HasValue)
        {
            target.FiringActions = SaturatingAdd(target.FiringActions, shot.FiringActionCount.Value);
        }

        if (shot.AmmunitionUnitsConsumed.HasValue)
        {
            target.AmmunitionUnitsConsumed = SaturatingAdd(
                target.AmmunitionUnitsConsumed,
                shot.AmmunitionUnitsConsumed.Value);
        }

        if (shot.ProjectileCount.HasValue)
        {
            target.Projectiles = SaturatingAdd(target.Projectiles, shot.ProjectileCount.Value);
        }
    }

    private static void Add(WeaponMetricTotals target, WeaponMetricTotals source)
    {
        target.FiringActions = SaturatingAdd(target.FiringActions, source.FiringActions);
        target.AmmunitionUnitsConsumed = SaturatingAdd(
            target.AmmunitionUnitsConsumed,
            source.AmmunitionUnitsConsumed);
        target.Projectiles = SaturatingAdd(target.Projectiles, source.Projectiles);
    }

    private static WeaponMetricCapabilities MergeCapabilities(
        WeaponMetricCapabilities current,
        WeaponMetricCapabilities observed) => new()
        {
            FiringActions = MergeAvailability(current.FiringActions, observed.FiringActions),
            AmmunitionConsumption = MergeAvailability(current.AmmunitionConsumption, observed.AmmunitionConsumption),
            Projectiles = MergeAvailability(current.Projectiles, observed.Projectiles),
            WeaponIdentity = MergeAvailability(current.WeaponIdentity, observed.WeaponIdentity),
            AmmunitionIdentity = MergeAvailability(current.AmmunitionIdentity, observed.AmmunitionIdentity),
            WeaponAmmunitionPairing = MergeAvailability(
                current.WeaponAmmunitionPairing,
                observed.WeaponAmmunitionPairing)
        };

    private static MetricAvailability MergeAvailability(MetricAvailability current, MetricAvailability observed)
    {
        if (current.State == AdapterCapabilityState.DisabledIncompatible
            && string.IsNullOrWhiteSpace(current.Provenance))
        {
            return CloneAvailability(observed);
        }

        var state = (AdapterCapabilityState)Math.Max((int)current.State, (int)observed.State);
        return new MetricAvailability
        {
            State = state,
            Provenance = string.Equals(current.Provenance, observed.Provenance, StringComparison.Ordinal)
                ? current.Provenance
                : string.Join(" | ", new[] { current.Provenance, observed.Provenance }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal))
        };
    }

    private static MetricAvailability CloneAvailability(MetricAvailability source) => new()
    {
        State = source.State,
        Provenance = source.Provenance
    };

    private static void NormalizeCapabilities(
        WeaponMetricCapabilities capabilities,
        WeaponStatisticsNormalizationResult result)
    {
        if (capabilities.FiringActions == null)
        {
            capabilities.FiringActions = new MetricAvailability();
            result.Changed = true;
            result.InvalidCapabilities = true;
        }

        if (capabilities.AmmunitionConsumption == null)
        {
            capabilities.AmmunitionConsumption = new MetricAvailability();
            result.Changed = true;
            result.InvalidCapabilities = true;
        }

        if (capabilities.Projectiles == null)
        {
            capabilities.Projectiles = new MetricAvailability();
            result.Changed = true;
            result.InvalidCapabilities = true;
        }

        if (capabilities.WeaponIdentity == null)
        {
            capabilities.WeaponIdentity = new MetricAvailability();
            result.Changed = true;
            result.InvalidCapabilities = true;
        }

        if (capabilities.AmmunitionIdentity == null)
        {
            capabilities.AmmunitionIdentity = new MetricAvailability();
            result.Changed = true;
            result.InvalidCapabilities = true;
        }

        if (capabilities.WeaponAmmunitionPairing == null)
        {
            capabilities.WeaponAmmunitionPairing = new MetricAvailability();
            result.Changed = true;
            result.InvalidCapabilities = true;
        }

        foreach (var availability in new[]
                 {
                     capabilities.FiringActions,
                     capabilities.AmmunitionConsumption,
                     capabilities.Projectiles,
                     capabilities.WeaponIdentity,
                     capabilities.AmmunitionIdentity,
                     capabilities.WeaponAmmunitionPairing
                 })
        {
            if (!Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
            {
                availability.State = AdapterCapabilityState.DisabledIncompatible;
                result.Changed = true;
                result.InvalidCapabilities = true;
            }

            if (availability.Provenance == null)
            {
                availability.Provenance = string.Empty;
                result.Changed = true;
                result.InvalidCapabilities = true;
            }
        }
    }

    private static void NormalizeTotals(
        WeaponMetricTotals totals,
        WeaponStatisticsNormalizationResult result)
    {
        if (totals.FiringActions < 0)
        {
            totals.FiringActions = 0;
            result.Changed = true;
            result.InvalidCounters = true;
        }

        if (totals.AmmunitionUnitsConsumed < 0)
        {
            totals.AmmunitionUnitsConsumed = 0;
            result.Changed = true;
            result.InvalidCounters = true;
        }

        if (totals.Projectiles < 0)
        {
            totals.Projectiles = 0;
            result.Changed = true;
            result.InvalidCounters = true;
        }
    }

    private static void ValidateCapabilities(WeaponMetricCapabilities capabilities)
    {
        if (capabilities.FiringActions == null
            || capabilities.AmmunitionConsumption == null
            || capabilities.Projectiles == null
            || capabilities.WeaponIdentity == null
            || capabilities.AmmunitionIdentity == null
            || capabilities.WeaponAmmunitionPairing == null)
        {
            throw new ArgumentException("Weapon metric availability is incomplete.", nameof(capabilities));
        }
    }

    private static void ValidateTotals(WeaponMetricTotals totals)
    {
        if (totals.FiringActions < 0
            || totals.AmmunitionUnitsConsumed < 0
            || totals.Projectiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totals), "Weapon counters cannot be negative.");
        }
    }

    private static long SaturatingAdd(long current, long addition)
    {
        if (current < 0 || addition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(addition), "Weapon counters cannot be negative.");
        }

        return current > long.MaxValue - addition ? long.MaxValue : current + addition;
    }

    private static long CheckedAdd(long current, long addition)
    {
        if (current < 0 || addition < 0)
            throw new ArgumentOutOfRangeException(nameof(addition), "Weapon-ammunition association counters cannot be negative.");
        return checked(current + addition);
    }

    private static Dictionary<string, long> NormalizeCheckedCounts(
        Dictionary<string, long> source,
        IEnumerable<string> validIdentities,
        WeaponStatisticsNormalizationResult result)
    {
        var valid = validIdentities.ToHashSet(StringComparer.Ordinal);
        var normalized = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var entry in source)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0 || !valid.Contains(entry.Key))
            {
                result.Changed = true;
                result.InvalidIdentityEntries = true;
                continue;
            }
            normalized[entry.Key] = entry.Value;
        }
        return normalized;
    }

    private static void ValidateCheckedCounts(
        Dictionary<string, long> values,
        IEnumerable<string> validIdentities,
        string label)
    {
        var valid = validIdentities.ToHashSet(StringComparer.Ordinal);
        if (values.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value < 0 || !valid.Contains(entry.Key)))
            throw new ArgumentException($"Persisted uncorrelated {label} counts are invalid.");
    }

    private static void ValidatePairingReconciliation(WeaponStatisticsAggregate statistics)
    {
        var pairTotal = CheckedSum(statistics.WeaponAmmunitionPairs.Values.Select(value => value.FiringActions));
        if (pairTotal > statistics.Totals.FiringActions
            || CheckedAdd(pairTotal, statistics.UncorrelatedFiringActions) > statistics.Totals.FiringActions)
            throw new ArgumentException("Weapon-ammunition pairs exceed the independent firing-action total.");

        foreach (var weapon in statistics.Weapons.Values)
        {
            var paired = CheckedSum(statistics.WeaponAmmunitionPairs.Values
                .Where(value => string.Equals(value.WeaponId, weapon.WeaponId, StringComparison.Ordinal))
                .Select(value => value.FiringActions));
            var uncorrelated = statistics.UncorrelatedWeaponFiringActions.GetValueOrDefault(weapon.WeaponId);
            if (CheckedAdd(paired, uncorrelated) > weapon.Totals.FiringActions)
                throw new ArgumentException($"Weapon-ammunition pairs exceed weapon '{weapon.WeaponId}' firing actions.");
            if (!statistics.HistoricalPairingUnavailable
                && statistics.Capabilities.WeaponAmmunitionPairing.State == AdapterCapabilityState.Supported
                && CheckedAdd(paired, uncorrelated) != weapon.Totals.FiringActions)
                throw new ArgumentException($"Weapon '{weapon.WeaponId}' firing actions do not reconcile with correlated and explicitly uncorrelated actions.");
        }
        foreach (var ammunition in statistics.AmmunitionTypes.Values)
        {
            var paired = CheckedSum(statistics.WeaponAmmunitionPairs.Values
                .Where(value => string.Equals(value.AmmunitionId, ammunition.AmmunitionId, StringComparison.Ordinal))
                .Select(value => value.FiringActions));
            var uncorrelated = statistics.UncorrelatedAmmunitionFiringActions.GetValueOrDefault(ammunition.AmmunitionId);
            if (CheckedAdd(paired, uncorrelated) > ammunition.Totals.FiringActions)
                throw new ArgumentException($"Weapon-ammunition pairs exceed ammunition '{ammunition.AmmunitionId}' firing actions.");
            if (!statistics.HistoricalPairingUnavailable
                && statistics.Capabilities.WeaponAmmunitionPairing.State == AdapterCapabilityState.Supported
                && CheckedAdd(paired, uncorrelated) != ammunition.Totals.FiringActions)
                throw new ArgumentException($"Ammunition '{ammunition.AmmunitionId}' firing actions do not reconcile with correlated and explicitly uncorrelated actions.");
        }
        if (!statistics.HistoricalPairingUnavailable
            && statistics.Capabilities.WeaponAmmunitionPairing.State == AdapterCapabilityState.Supported
            && CheckedAdd(pairTotal, statistics.UncorrelatedFiringActions) != statistics.Totals.FiringActions)
            throw new ArgumentException("Firing actions do not reconcile with correlated and explicitly uncorrelated actions.");
    }

    private static long CheckedSum(IEnumerable<long> values)
    {
        var total = 0L;
        foreach (var value in values) total = CheckedAdd(total, value);
        return total;
    }

    private static string MergeProvenance(string? left, string? right) => string.Join(
        " | ",
        new[] { left, right }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));

    private static void Validate(ShotRecorded shot)
    {
        if (shot == null)
        {
            throw new ArgumentNullException(nameof(shot));
        }

        if (shot.GameplayContext != GameplayContext.Raid
            || string.IsNullOrWhiteSpace(shot.EventId)
            || string.IsNullOrWhiteSpace(shot.SaveGenerationId)
            || string.IsNullOrWhiteSpace(shot.RunId)
            || string.IsNullOrWhiteSpace(shot.MapId))
        {
            throw new ArgumentException("Firing event context is invalid.", nameof(shot));
        }

        ValidateMetric(shot.FiringActionCount, shot.Capabilities.FiringActions, nameof(shot.FiringActionCount));
        ValidateMetric(shot.AmmunitionUnitsConsumed, shot.Capabilities.AmmunitionConsumption, nameof(shot.AmmunitionUnitsConsumed));
        ValidateMetric(shot.ProjectileCount, shot.Capabilities.Projectiles, nameof(shot.ProjectileCount));
        ValidateIdentity(shot.WeaponId, shot.WeaponDisplayName, shot.Capabilities.WeaponIdentity, "weapon");
        ValidateIdentity(
            shot.AmmunitionId,
            shot.AmmunitionDisplayName,
            shot.Capabilities.AmmunitionIdentity,
            "ammunition");
    }

    private static void ValidateMetric(long? value, MetricAvailability availability, string name)
    {
        if ((availability.State == AdapterCapabilityState.DisabledIncompatible) == value.HasValue
            || value < 0)
        {
            throw new ArgumentException($"{name} must agree with its explicit availability and be non-negative.");
        }
    }

    private static void ValidateIdentity(
        string id,
        string displayName,
        MetricAvailability availability,
        string label)
    {
        if (availability.State != AdapterCapabilityState.DisabledIncompatible
            && (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName)))
        {
            throw new ArgumentException($"Supported {label} identity must include a stable ID and fallback display name.");
        }
    }
}
