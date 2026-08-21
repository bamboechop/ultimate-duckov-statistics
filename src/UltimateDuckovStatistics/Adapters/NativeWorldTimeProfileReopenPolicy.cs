using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeWorldTimeProfileReopenPolicy
{
    public static ProfileOpenResult OpenAndDetermineCurrentClockReuse(
        ProfileRepository repository,
        SaveIdentitySnapshot observedIdentity,
        Func<int, SaveIdentitySnapshot> readIdentity,
        string creationReason,
        out bool canReuseCurrentClock)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (observedIdentity == null) throw new ArgumentNullException(nameof(observedIdentity));
        if (readIdentity == null) throw new ArgumentNullException(nameof(readIdentity));

        var priorSlot = repository.Current.Slot;
        if (priorSlot != observedIdentity.Slot)
            repository.RefreshIdentity(readIdentity(priorSlot));
        var priorGenerationId = repository.CurrentGenerationId;
        var result = repository.Open(observedIdentity, creationReason);
        canReuseCurrentClock = CanReuseCurrentClock(
            result,
            observedIdentity.Slot,
            priorSlot,
            repository.CurrentGenerationId,
            priorGenerationId);
        return result;
    }

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
