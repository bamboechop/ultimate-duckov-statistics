using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public readonly struct WorldClockReading
{
    public WorldClockReading(long day, long timeOfDayTicks)
    {
        Day = day;
        TimeOfDayTicks = timeOfDayTicks;
    }

    public long Day { get; }
    public long TimeOfDayTicks { get; }
}

public enum WorldTimeObservationState
{
    BaselineEstablished,
    Accepted,
    Duplicate,
    Invalid,
    Backward,
    Overflow
}

public readonly struct WorldTimeObservationResult
{
    public WorldTimeObservationResult(
        WorldTimeObservationState state,
        WorldTimeMutation mutation,
        string detail)
    {
        State = state;
        Mutation = mutation;
        Detail = detail;
    }

    public WorldTimeObservationState State { get; }
    public WorldTimeMutation Mutation { get; }
    public string Detail { get; }
    public bool Accepted => State == WorldTimeObservationState.Accepted;
}

public sealed class WorldTimeObservationTracker
{
    public const long NativeSecondsPerDay = 86_300;
    public const long NativeTicksPerDay = NativeSecondsPerDay * TimeSpan.TicksPerSecond;
    private string generationId = string.Empty;
    private WorldClockReading baseline;
    private long baselineCoordinate;
    private bool hasBaseline;

    public void Reset()
    {
        generationId = string.Empty;
        baseline = default;
        baselineCoordinate = 0;
        hasBaseline = false;
    }

    public WorldTimeObservationResult Observe(string activeGenerationId, WorldClockReading reading)
    {
        if (string.IsNullOrWhiteSpace(activeGenerationId))
            return Result(WorldTimeObservationState.Invalid, "The active save generation is unavailable.");
        if (!TryCoordinate(reading, out var coordinate, out var detail))
            return Result(WorldTimeObservationState.Invalid, detail);
        if (!hasBaseline || !string.Equals(generationId, activeGenerationId, StringComparison.Ordinal))
        {
            generationId = activeGenerationId;
            baseline = reading;
            baselineCoordinate = coordinate;
            hasBaseline = true;
            return Result(WorldTimeObservationState.BaselineEstablished, "Clock baseline established without counting load hydration.");
        }
        if (coordinate == baselineCoordinate)
            return Result(WorldTimeObservationState.Duplicate, "Duplicate clock observation ignored.");
        if (coordinate < baselineCoordinate || reading.Day < baseline.Day)
        {
            baseline = reading;
            baselineCoordinate = coordinate;
            return Result(WorldTimeObservationState.Backward, "Backward or contradictory clock movement was not counted; baseline was reset.");
        }
        try
        {
            var elapsed = checked(coordinate - baselineCoordinate);
            var days = checked(reading.Day - baseline.Day);
            baseline = reading;
            baselineCoordinate = coordinate;
            return new WorldTimeObservationResult(
                WorldTimeObservationState.Accepted,
                new WorldTimeMutation(days, elapsed, 0, 0),
                string.Empty);
        }
        catch (OverflowException)
        {
            baseline = reading;
            baselineCoordinate = coordinate;
            return Result(WorldTimeObservationState.Overflow, "Clock coordinate arithmetic overflowed; the observation was not counted.");
        }
    }

    public static bool TryCoordinate(WorldClockReading reading, out long coordinate, out string detail)
    {
        coordinate = 0;
        if (reading.Day < 0 || reading.TimeOfDayTicks < 0 || reading.TimeOfDayTicks > NativeTicksPerDay)
        {
            detail = "Clock reading is outside the installed native 86,300-second day contract.";
            return false;
        }
        try
        {
            coordinate = checked(reading.Day * NativeTicksPerDay + reading.TimeOfDayTicks);
            detail = string.Empty;
            return true;
        }
        catch (OverflowException)
        {
            detail = "Clock reading cannot be represented safely as Int64 native ticks.";
            return false;
        }
    }

    private static WorldTimeObservationResult Result(WorldTimeObservationState state, string detail) =>
        new(state, default, detail);
}

public sealed class SleepCompletionGate
{
    private string generationId = string.Empty;
    private long sleepAdvancedTicks;
    private bool pending;

    public bool Begin(string activeGenerationId, long advancedTicks)
    {
        if (string.IsNullOrWhiteSpace(activeGenerationId) || advancedTicks < 0 || pending) return false;
        generationId = activeGenerationId;
        sleepAdvancedTicks = advancedTicks;
        pending = true;
        return true;
    }

    public bool Complete(string activeGenerationId, out WorldTimeMutation mutation)
    {
        mutation = default;
        if (!pending || !string.Equals(generationId, activeGenerationId, StringComparison.Ordinal))
        {
            Clear();
            return false;
        }
        mutation = new WorldTimeMutation(0, 0, 1, sleepAdvancedTicks);
        Clear();
        return true;
    }

    public void Clear()
    {
        generationId = string.Empty;
        sleepAdvancedTicks = 0;
        pending = false;
    }
}

public static class SleepAdvanceContract
{
    public static bool TryValidate(
        long requestedTicks,
        long beforeCoordinate,
        long afterCoordinate,
        out long actualTicks,
        out string detail)
    {
        actualTicks = 0;
        if (requestedTicks < 0 || beforeCoordinate < 0 || afterCoordinate < beforeCoordinate)
        {
            detail = "Sleep advancement coordinates are invalid or backward.";
            return false;
        }
        actualTicks = afterCoordinate - beforeCoordinate;
        var difference = actualTicks >= requestedTicks
            ? actualTicks - requestedTicks
            : requestedTicks - actualTicks;
        if (difference > 2)
        {
            detail = $"Sleep advancement did not match the exact native request: requestedTicks={requestedTicks}, actualTicks={actualTicks}.";
            return false;
        }
        detail = string.Empty;
        return true;
    }
}
