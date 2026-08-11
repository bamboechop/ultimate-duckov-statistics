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

    public static IReadOnlyList<string> All { get; } = new[]
    {
        TriggerAttempts,
        FiringActions,
        AmmunitionConsumption,
        Projectiles,
        WeaponIdentity,
        AmmunitionIdentity
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
}

public sealed class WeaponStatisticsNormalizationResult
{
    public bool Changed { get; internal set; }

    public bool InvalidCounters { get; internal set; }
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
        target.Capabilities = MergeCapabilities(target.Capabilities, source.Capabilities);
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
        }

        NormalizeCapabilities(statistics.Capabilities, result);
        foreach (var entry in statistics.Weapons.ToArray())
        {
            var weapon = entry.Value;
            if (weapon == null || string.IsNullOrWhiteSpace(entry.Key))
            {
                statistics.Weapons.Remove(entry.Key);
                result.Changed = true;
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

        return result;
    }

    public static void ValidateAggregate(WeaponStatisticsAggregate statistics)
    {
        if (statistics == null
            || statistics.Totals == null
            || statistics.Weapons == null
            || statistics.AmmunitionTypes == null
            || statistics.Capabilities == null)
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
                || entry.Value.Totals == null)
            {
                throw new ArgumentException("A persisted ammunition aggregate is incomplete.", nameof(statistics));
            }

            ValidateTotals(entry.Value.Totals);
        }
    }

    public static WeaponMetricCapabilities CloneCapabilities(WeaponMetricCapabilities source) => new()
    {
        FiringActions = CloneAvailability(source.FiringActions),
        AmmunitionConsumption = CloneAvailability(source.AmmunitionConsumption),
        Projectiles = CloneAvailability(source.Projectiles),
        WeaponIdentity = CloneAvailability(source.WeaponIdentity),
        AmmunitionIdentity = CloneAvailability(source.AmmunitionIdentity)
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
        return aggregate.Totals.FiringActions == 0
               && aggregate.Totals.AmmunitionUnitsConsumed == 0
               && aggregate.Totals.Projectiles == 0
               && aggregate.Weapons.Count == 0
               && aggregate.AmmunitionTypes.Count == 0;
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
            AmmunitionIdentity = MergeAvailability(current.AmmunitionIdentity, observed.AmmunitionIdentity)
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
        }

        if (capabilities.AmmunitionConsumption == null)
        {
            capabilities.AmmunitionConsumption = new MetricAvailability();
            result.Changed = true;
        }

        if (capabilities.Projectiles == null)
        {
            capabilities.Projectiles = new MetricAvailability();
            result.Changed = true;
        }

        if (capabilities.WeaponIdentity == null)
        {
            capabilities.WeaponIdentity = new MetricAvailability();
            result.Changed = true;
        }

        if (capabilities.AmmunitionIdentity == null)
        {
            capabilities.AmmunitionIdentity = new MetricAvailability();
            result.Changed = true;
        }

        foreach (var availability in new[]
                 {
                     capabilities.FiringActions,
                     capabilities.AmmunitionConsumption,
                     capabilities.Projectiles,
                     capabilities.WeaponIdentity,
                     capabilities.AmmunitionIdentity
                 })
        {
            if (!Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
            {
                availability.State = AdapterCapabilityState.DisabledIncompatible;
                result.Changed = true;
            }

            if (availability.Provenance == null)
            {
                availability.Provenance = string.Empty;
                result.Changed = true;
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
            || capabilities.AmmunitionIdentity == null)
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
