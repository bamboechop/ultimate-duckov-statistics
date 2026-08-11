using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

public static class ProfileMigrator
{
    public static bool Migrate(ProfileDocument profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (profile.SchemaVersion > ProductInfo.SchemaVersion)
        {
            throw new NotSupportedException(
                $"Profile schema {profile.SchemaVersion} is newer than supported schema {ProductInfo.SchemaVersion}.");
        }

        if (profile.Statistics?.SchemaVersion > ProductInfo.SchemaVersion)
        {
            throw new NotSupportedException(
                $"Statistics schema {profile.Statistics.SchemaVersion} is newer than supported schema {ProductInfo.SchemaVersion}.");
        }

        var changed = false;
        if (profile.SchemaVersion < 1)
        {
            profile.SchemaVersion = 1;
            changed = true;
        }

        if (profile.Identity == null)
        {
            profile.Identity = new SaveIdentitySnapshot { Slot = profile.Slot };
            changed = true;
        }

        if (profile.Statistics == null)
        {
            profile.Statistics = new ProfileStatistics();
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 1)
        {
            profile.Statistics.SchemaVersion = 1;
            changed = true;
        }

        if (profile.Statistics.CreatedUtc == default)
        {
            profile.Statistics.CreatedUtc = profile.CreatedUtc;
            changed = true;
        }

        if (profile.Statistics.UpdatedUtc == default)
        {
            profile.Statistics.UpdatedUtc = profile.UpdatedUtc;
            changed = true;
        }

        if (profile.Statistics.Items == null)
        {
            profile.Statistics.Items = new Dictionary<string, ItemAggregate>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.Groups == null)
        {
            profile.Statistics.Groups = new Dictionary<string, AggregateTotals>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.RecentEventIds == null)
        {
            profile.Statistics.RecentEventIds = new List<string>();
            changed = true;
        }

        if (profile.Statistics.Overall == null)
        {
            profile.Statistics.Overall = new AggregateTotals();
            changed = true;
        }

        if (profile.Capabilities == null)
        {
            profile.Capabilities = new List<CapabilityRecord>();
            changed = true;
        }

        foreach (var item in profile.Statistics.Items.Values)
        {
            if (item.EffectTags == null)
            {
                item.EffectTags = new List<Domain.ItemEffectTag>();
                changed = true;
            }

            if (item.Totals == null)
            {
                item.Totals = new AggregateTotals();
                changed = true;
            }

            if (item.Totals.AmountsByUnit == null)
            {
                item.Totals.AmountsByUnit = new Dictionary<string, double>(StringComparer.Ordinal);
                changed = true;
            }
        }

        foreach (var group in profile.Statistics.Groups.Values)
        {
            if (group.AmountsByUnit == null)
            {
                group.AmountsByUnit = new Dictionary<string, double>(StringComparer.Ordinal);
                changed = true;
            }
        }

        if (profile.Statistics.Overall.AmountsByUnit == null)
        {
            profile.Statistics.Overall.AmountsByUnit = new Dictionary<string, double>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.Runs == null)
        {
            profile.Statistics.Runs = new List<Domain.RunSummary>();
            changed = true;
        }

        if (profile.Statistics.RunTotals == null)
        {
            profile.Statistics.RunTotals = new RunAggregateTotals();
            changed = true;
        }

        if (profile.Statistics.RunTotals.Outcomes == null)
        {
            profile.Statistics.RunTotals.Outcomes = new Dictionary<string, long>(StringComparer.Ordinal);
            changed = true;
        }

        if (profile.Statistics.RunTotals.Maps == null)
        {
            profile.Statistics.RunTotals.Maps = new Dictionary<string, MapRunAggregate>(StringComparer.Ordinal);
            changed = true;
        }

        foreach (var map in profile.Statistics.RunTotals.Maps.Values)
        {
            if (map.Outcomes == null)
            {
                map.Outcomes = new Dictionary<string, long>(StringComparer.Ordinal);
                changed = true;
            }

            if (map.WeaponStatistics == null)
            {
                map.WeaponStatistics = new WeaponStatisticsAggregate();
                changed = true;
            }

            changed |= NormalizeWeaponStatistics(map.WeaponStatistics);
        }

        if (profile.Statistics.RunTotals.WeaponStatistics == null)
        {
            profile.Statistics.RunTotals.WeaponStatistics = new WeaponStatisticsAggregate();
            changed = true;
        }

        changed |= NormalizeWeaponStatistics(profile.Statistics.RunTotals.WeaponStatistics);

        foreach (var run in profile.Statistics.Runs)
        {
            if (run.WeaponStatistics == null)
            {
                run.WeaponStatistics = new WeaponStatisticsAggregate();
                changed = true;
            }

            changed |= NormalizeWeaponStatistics(run.WeaponStatistics);
        }

        if (profile.Statistics.RunRecords == null)
        {
            profile.Statistics.RunRecords = new RunDurationRecords();
            changed = true;
        }

        if (profile.Statistics.RunRecords.Extraction == null)
        {
            profile.Statistics.RunRecords.Extraction = new DurationRecordPair();
            changed = true;
        }

        if (profile.Statistics.RunRecords.Death == null)
        {
            profile.Statistics.RunRecords.Death = new DurationRecordPair();
            changed = true;
        }

        if (profile.Statistics.RunRecords.Maps == null)
        {
            profile.Statistics.RunRecords.Maps = new Dictionary<string, MapRunDurationRecords>(StringComparer.Ordinal);
            changed = true;
        }

        foreach (var map in profile.Statistics.RunRecords.Maps.Values)
        {
            if (map.Extraction == null)
            {
                map.Extraction = new DurationRecordPair();
                changed = true;
            }

            if (map.Death == null)
            {
                map.Death = new DurationRecordPair();
                changed = true;
            }
        }

        if (profile.SchemaVersion < 2)
        {
            profile.SchemaVersion = 2;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 2)
        {
            profile.Statistics.SchemaVersion = 2;
            changed = true;
        }

        if (profile.SchemaVersion < 3)
        {
            profile.SchemaVersion = 3;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 3)
        {
            profile.Statistics.SchemaVersion = 3;
            changed = true;
        }

        if (profile.SchemaVersion < 4)
        {
            profile.SchemaVersion = 4;
            changed = true;
        }

        if (profile.Statistics.SchemaVersion < 4)
        {
            profile.Statistics.SchemaVersion = 4;
            changed = true;
        }

        if (!string.Equals(profile.Statistics.SaveGenerationId, profile.GenerationId, StringComparison.Ordinal))
        {
            profile.Statistics.SaveGenerationId = profile.GenerationId;
            changed = true;
        }

        // Repair schema-2 pre-release profiles written before delayed healing
        // buffs were promoted to the canonical Healing group.
        changed |= ProfileGroupReconciler.PromoteProvenHealingItems(profile.Statistics);

        return changed;
    }

    private static bool NormalizeWeaponStatistics(WeaponStatisticsAggregate statistics)
    {
        var changed = false;
        if (statistics.Totals == null)
        {
            statistics.Totals = new WeaponMetricTotals();
            changed = true;
        }

        if (statistics.Weapons == null)
        {
            statistics.Weapons = new Dictionary<string, WeaponAggregate>(StringComparer.Ordinal);
            changed = true;
        }

        if (statistics.AmmunitionTypes == null)
        {
            statistics.AmmunitionTypes = new Dictionary<string, AmmunitionAggregate>(StringComparer.Ordinal);
            changed = true;
        }

        if (statistics.Capabilities == null)
        {
            statistics.Capabilities = new Domain.WeaponMetricCapabilities();
            changed = true;
        }

        changed |= NormalizeCapabilities(statistics.Capabilities);

        foreach (var weapon in statistics.Weapons.Values)
        {
            if (weapon.Totals == null)
            {
                weapon.Totals = new WeaponMetricTotals();
                changed = true;
            }
        }

        foreach (var ammunition in statistics.AmmunitionTypes.Values)
        {
            if (ammunition.Totals == null)
            {
                ammunition.Totals = new WeaponMetricTotals();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeCapabilities(Domain.WeaponMetricCapabilities capabilities)
    {
        var changed = false;
        if (capabilities.FiringActions == null)
        {
            capabilities.FiringActions = new Domain.MetricAvailability();
            changed = true;
        }

        if (capabilities.AmmunitionConsumption == null)
        {
            capabilities.AmmunitionConsumption = new Domain.MetricAvailability();
            changed = true;
        }

        if (capabilities.Projectiles == null)
        {
            capabilities.Projectiles = new Domain.MetricAvailability();
            changed = true;
        }

        if (capabilities.WeaponIdentity == null)
        {
            capabilities.WeaponIdentity = new Domain.MetricAvailability();
            changed = true;
        }

        if (capabilities.AmmunitionIdentity == null)
        {
            capabilities.AmmunitionIdentity = new Domain.MetricAvailability();
            changed = true;
        }

        return changed;
    }
}
