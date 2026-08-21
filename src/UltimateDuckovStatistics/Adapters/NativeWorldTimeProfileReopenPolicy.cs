using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeWorldTimeProfileReopenPolicy
{
    public static NativeWorldTimeProfilePreOpenState CapturePreOpenState(
        ProfileRepository repository,
        SaveIdentitySnapshot observedIdentity,
        Func<int, SaveIdentitySnapshot> readIdentity)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (observedIdentity == null) throw new ArgumentNullException(nameof(observedIdentity));
        if (readIdentity == null) throw new ArgumentNullException(nameof(readIdentity));

        var priorSlot = repository.Current.Slot;
        if (priorSlot != observedIdentity.Slot)
            repository.RefreshIdentity(readIdentity(priorSlot));
        return new NativeWorldTimeProfilePreOpenState(priorSlot, repository.CurrentGenerationId);
    }

    public static ProfileOpenResult OpenAndDetermineCurrentClockReuse(
        ProfileRepository repository,
        SaveIdentitySnapshot observedIdentity,
        NativeWorldTimeProfilePreOpenState preOpenState,
        string creationReason,
        out bool canReuseCurrentClock)
    {
        if (repository == null) throw new ArgumentNullException(nameof(repository));
        if (observedIdentity == null) throw new ArgumentNullException(nameof(observedIdentity));
        if (preOpenState == null) throw new ArgumentNullException(nameof(preOpenState));

        var result = repository.Open(observedIdentity, creationReason);
        canReuseCurrentClock = CanReuseCurrentClock(
            result,
            observedIdentity.Slot,
            preOpenState.Slot,
            repository.CurrentGenerationId,
            preOpenState.GenerationId);
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

internal sealed class NativeWorldTimeProfilePreOpenState
{
    public NativeWorldTimeProfilePreOpenState(int slot, string generationId)
    {
        Slot = slot;
        GenerationId = generationId;
    }

    public int Slot { get; }

    public string GenerationId { get; }
}
