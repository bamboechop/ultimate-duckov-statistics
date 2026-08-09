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

        var changed = false;
        if (profile.SchemaVersion < 1)
        {
            profile.SchemaVersion = 1;
            changed = true;
        }

        profile.Identity ??= new SaveIdentitySnapshot { Slot = profile.Slot };
        profile.Statistics ??= new ProfileStatistics();
        profile.Statistics.Items ??= new Dictionary<string, ItemAggregate>(StringComparer.Ordinal);
        profile.Statistics.Groups ??= new Dictionary<string, AggregateTotals>(StringComparer.Ordinal);
        profile.Statistics.RecentEventIds ??= new List<string>();
        profile.Statistics.Overall ??= new AggregateTotals();
        profile.Capabilities ??= new List<CapabilityRecord>();

        foreach (var item in profile.Statistics.Items.Values)
        {
            item.EffectTags ??= new List<Domain.ItemEffectTag>();
            item.Totals ??= new AggregateTotals();
            item.Totals.AmountsByUnit ??= new Dictionary<string, double>(StringComparer.Ordinal);
        }

        foreach (var group in profile.Statistics.Groups.Values)
        {
            group.AmountsByUnit ??= new Dictionary<string, double>(StringComparer.Ordinal);
        }

        profile.Statistics.Overall.AmountsByUnit ??= new Dictionary<string, double>(StringComparer.Ordinal);

        if (!string.Equals(profile.Statistics.SaveGenerationId, profile.GenerationId, StringComparison.Ordinal))
        {
            profile.Statistics.SaveGenerationId = profile.GenerationId;
            changed = true;
        }

        return changed;
    }
}
