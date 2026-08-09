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
}
