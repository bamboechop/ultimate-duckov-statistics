using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal interface IHealingAttributionObserver
{
    void BeginUse(ItemUseSnapshot snapshot);

    void BeginApplication(int runtimeItemId);

    void EndApplication(int runtimeItemId);

    void MarkSuccessful(int runtimeItemId);

    void CompleteUse(int runtimeItemId, ItemUseRecorded? successfulUse);

    int ExpirePendingBefore(DateTime cutoffUtc);

    void Reset();
}
