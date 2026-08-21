using System.Reflection;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal readonly struct NativeSleepPatchState
{
    public NativeSleepPatchState(
        bool valid,
        string generationId,
        long beforeCoordinate,
        long requestedTicks,
        long profileTransitionId = 0)
    {
        Valid = valid;
        GenerationId = generationId;
        BeforeCoordinate = beforeCoordinate;
        RequestedTicks = requestedTicks;
        ProfileTransitionId = profileTransitionId;
    }

    public bool Valid { get; }
    public string GenerationId { get; }
    public long BeforeCoordinate { get; }
    public long RequestedTicks { get; }
    public long ProfileTransitionId { get; }
    public bool IsProfileHandoff => ProfileTransitionId > 0;
}

internal static class WorldTimeHarmonyBridge
{
    private static NativeWorldTimeAdapter? adapter;

    public static void Attach(NativeWorldTimeAdapter value) =>
        adapter = value ?? throw new ArgumentNullException(nameof(value));

    public static void Detach(NativeWorldTimeAdapter value)
    {
        if (ReferenceEquals(adapter, value)) adapter = null;
    }

    public static NativeSleepPatchState Begin(float seconds) =>
        adapter?.BeginNativeSleepAdvance(seconds) ?? default;

    public static void Complete(NativeSleepPatchState state) => adapter?.CompleteNativeSleepAdvance(state);
}

internal static class WorldTimeHarmonyCallbacks
{
    private static void SleepAdvancePrefix(float seconds, out NativeSleepPatchState __state) =>
        __state = SafeBegin(seconds);

    private static void SleepAdvancePostfix(NativeSleepPatchState __state) =>
        SafeComplete(__state);

    public static MethodInfo SleepAdvancePrefixMethod => Get(nameof(SleepAdvancePrefix));
    public static MethodInfo SleepAdvancePostfixMethod => Get(nameof(SleepAdvancePostfix));

    private static NativeSleepPatchState SafeBegin(float seconds)
    {
        try { return WorldTimeHarmonyBridge.Begin(seconds); }
        catch { return default; }
    }

    private static void SafeComplete(NativeSleepPatchState state)
    {
        try { WorldTimeHarmonyBridge.Complete(state); }
        catch { }
    }

    private static MethodInfo Get(string name) => typeof(WorldTimeHarmonyCallbacks).GetMethod(
        name,
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(WorldTimeHarmonyCallbacks).FullName, name);
}
