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

public static class WeaponStatisticsReducer
{
    public static void Apply(WeaponStatisticsAggregate target, ShotRecorded shot)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

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

    public static WeaponMetricCapabilities CloneCapabilities(WeaponMetricCapabilities source) => new()
    {
        FiringActions = CloneAvailability(source.FiringActions),
        AmmunitionConsumption = CloneAvailability(source.AmmunitionConsumption),
        Projectiles = CloneAvailability(source.Projectiles),
        WeaponIdentity = CloneAvailability(source.WeaponIdentity),
        AmmunitionIdentity = CloneAvailability(source.AmmunitionIdentity)
    };

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
            target.FiringActions += shot.FiringActionCount.Value;
        }

        if (shot.AmmunitionUnitsConsumed.HasValue)
        {
            target.AmmunitionUnitsConsumed += shot.AmmunitionUnitsConsumed.Value;
        }

        if (shot.ProjectileCount.HasValue)
        {
            target.Projectiles += shot.ProjectileCount.Value;
        }
    }

    private static void Add(WeaponMetricTotals target, WeaponMetricTotals source)
    {
        target.FiringActions += source.FiringActions;
        target.AmmunitionUnitsConsumed += source.AmmunitionUnitsConsumed;
        target.Projectiles += source.Projectiles;
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
