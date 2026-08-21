namespace UltimateDuckovStatistics.Adapters;

internal static class NativeWorldTimeProfileBinding
{
    public static Func<string> CaptureGenerationProvider<TCoordinator>(
        TCoordinator coordinator,
        Func<TCoordinator, string> generationSelector)
        where TCoordinator : class
    {
        if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));
        if (generationSelector == null) throw new ArgumentNullException(nameof(generationSelector));
        return () => generationSelector(coordinator);
    }
}
