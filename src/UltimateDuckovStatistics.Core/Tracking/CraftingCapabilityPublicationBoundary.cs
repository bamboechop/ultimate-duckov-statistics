using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class CraftingCapabilityPublicationBoundary
{
    private readonly object sync = new();
    private readonly object publishSync = new();
    private PendingPublication? pending;
    private long revision;

    public bool IsPending
    {
        get { lock (sync) return pending != null; }
    }

    public void Stage(
        IReadOnlyList<CapabilityRecord> records,
        CraftingMetricCapabilities capabilities)
    {
        if (records == null) throw new ArgumentNullException(nameof(records));
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        var recordSnapshot = CloneRecords(records);
        var capabilitySnapshot = CraftingStatisticsReducer.CloneCapabilities(capabilities);
        lock (sync)
        {
            pending = new PendingPublication(
                checked(++revision),
                recordSnapshot,
                capabilitySnapshot);
        }
    }

    public bool TryPublish(
        Action<IReadOnlyList<CapabilityRecord>, CraftingMetricCapabilities> publisher)
    {
        if (publisher == null) throw new ArgumentNullException(nameof(publisher));
        lock (publishSync)
        {
            PendingPublication? snapshot;
            lock (sync) snapshot = pending;
            if (snapshot == null) return true;

            publisher(
                CloneRecords(snapshot.Records),
                CraftingStatisticsReducer.CloneCapabilities(snapshot.Capabilities));

            lock (sync)
            {
                if (pending?.Revision == snapshot.Revision) pending = null;
                return pending == null;
            }
        }
    }

    private static CapabilityRecord[] CloneRecords(
        IReadOnlyList<CapabilityRecord> records) => records
            .Select(record => new CapabilityRecord
            {
                AdapterId = record.AdapterId,
                State = record.State,
                Version = record.Version,
                Detail = record.Detail
            })
            .ToArray();

    private sealed class PendingPublication
    {
        public PendingPublication(
            long revision,
            IReadOnlyList<CapabilityRecord> records,
            CraftingMetricCapabilities capabilities)
        {
            Revision = revision;
            Records = records;
            Capabilities = capabilities;
        }

        public long Revision { get; }
        public IReadOnlyList<CapabilityRecord> Records { get; }
        public CraftingMetricCapabilities Capabilities { get; }
    }
}
