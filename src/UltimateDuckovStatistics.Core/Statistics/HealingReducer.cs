using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class HealingReducer
{
    private const int MaximumRecentEventIds = 512;

    public static bool Apply(ProfileStatistics profile, HealingApplied healing)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (healing == null)
        {
            throw new ArgumentNullException(nameof(healing));
        }

        if (healing.GameplayContext != GameplayContext.Raid)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(healing.EventId)
            || string.IsNullOrWhiteSpace(healing.ApplicationId)
            || string.IsNullOrWhiteSpace(healing.SourceItemUseEventId)
            || string.IsNullOrWhiteSpace(healing.ItemId)
            || healing.ActualHealthRestored <= 0
            || double.IsNaN(healing.ActualHealthRestored)
            || double.IsInfinity(healing.ActualHealthRestored))
        {
            throw new ArgumentException("Healing event is invalid.", nameof(healing));
        }

        if (!string.Equals(profile.SaveGenerationId, healing.SaveGenerationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A healing event cannot be reduced into a different save generation.");
        }

        if (profile.RecentEventIds.Contains(healing.EventId, StringComparer.Ordinal))
        {
            return false;
        }

        if (!profile.Items.TryGetValue(healing.ItemId, out var item))
        {
            throw new InvalidOperationException("Healing cannot be attributed before its successful item use is recorded.");
        }

        // A delayed healing buff may initially look like a generic buff or a
        // hydration item. Positive, proven healing is decisive classification
        // evidence, so move the item's complete historical totals exactly once.
        ProfileGroupReconciler.PromoteItemToHealing(profile, item);
        item.Totals.ActualHealthRestored += healing.ActualHealthRestored;
        var groupKey = item.Group.ToString();
        if (!profile.Groups.TryGetValue(groupKey, out var group))
        {
            throw new InvalidOperationException("Healing cannot be attributed without the source item's canonical group.");
        }

        group.ActualHealthRestored += healing.ActualHealthRestored;
        profile.Overall.ActualHealthRestored += healing.ActualHealthRestored;
        profile.UpdatedUtc = healing.TimestampUtc;
        profile.RecentEventIds.Add(healing.EventId);
        while (profile.RecentEventIds.Count > MaximumRecentEventIds)
        {
            profile.RecentEventIds.RemoveAt(0);
        }

        return true;
    }
}
