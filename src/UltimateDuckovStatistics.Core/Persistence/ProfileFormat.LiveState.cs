using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

public static partial class ProfileFormat
{
    // Owned SQLite records are validated before commit and checked when read.
    // Live flags (such as holdings no longer being current) still change on open.
    // Normalize the same shared live aggregates without enumerating run detail.
    internal static bool NormalizeTrustedLiveState(ProfileDocument profile)
    {
        var live = LiveProjection(profile);
        var changed = Normalize(live);
        profile.Statistics.CreatedUtc = live.Statistics.CreatedUtc;
        profile.Statistics.UpdatedUtc = live.Statistics.UpdatedUtc;
        return changed;
    }

    internal static bool CompactTrustedLiveReplay(ProfileDocument profile) => CompactEconomyReplayEvidenceAfterRecovery(LiveProjection(profile));

    private static ProfileDocument LiveProjection(ProfileDocument profile)
    {
        var result = ProfileMetadataRecord.From(profile).ToProfile();
        var source = profile.Statistics;
        result.DeferredItemPersistence = profile.DeferredItemPersistence;
        result.Statistics = new ProfileStatistics
        {
            SchemaVersion = source.SchemaVersion,
            SaveGenerationId = source.SaveGenerationId,
            CreatedUtc = source.CreatedUtc,
            UpdatedUtc = source.UpdatedUtc,
            HealingCaptureComplete = source.HealingCaptureComplete,
            Overall = source.Overall,
            Items = source.Items,
            Groups = source.Groups,
            RecentEventIds = source.RecentEventIds,
            RunTotals = source.RunTotals,
            RunRecords = source.RunRecords,
            Economy = source.Economy,
            WorldTime = source.WorldTime,
            Crafting = source.Crafting,
            Holdings = source.Holdings,
            BaseMovement = source.BaseMovement
        };
        return result;
    }
}
