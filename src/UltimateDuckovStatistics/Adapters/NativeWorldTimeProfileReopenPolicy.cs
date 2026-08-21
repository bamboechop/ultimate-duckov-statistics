using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeWorldTimeProfileReopenPolicy
{
    public static bool CanReuseCurrentClock(
        ProfileOpenResult result,
        int observedSlot,
        int priorSlot,
        string openedGenerationId,
        string priorGenerationId)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        return observedSlot == priorSlot
            && string.Equals(openedGenerationId, priorGenerationId, StringComparison.Ordinal)
            && !result.CreatedNew
            && !result.RotatedGeneration;
    }
}
