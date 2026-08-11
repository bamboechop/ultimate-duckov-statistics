namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class WeaponFireSequenceTracker
{
    private readonly Func<string> eventIdFactory;
    private readonly int capacity;
    private readonly Dictionary<int, State> states = new();
    private long sequence;

    public WeaponFireSequenceTracker(Func<string> eventIdFactory, int capacity = 32)
    {
        this.eventIdFactory = eventIdFactory ?? throw new ArgumentNullException(nameof(eventIdFactory));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.capacity = capacity;
    }

    public int Count => states.Count;

    public string GetEventId(int runtimeWeaponId, int ammunitionTypeId, int remainingAmmunition)
    {
        if (states.TryGetValue(runtimeWeaponId, out var state)
            && state.AmmunitionTypeId == ammunitionTypeId
            && state.RemainingAmmunition == remainingAmmunition)
        {
            state.LastObservedSequence = ++sequence;
            return state.EventId;
        }

        var eventId = eventIdFactory();
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new InvalidOperationException("Firing event ID factory returned an empty value.");
        }

        states[runtimeWeaponId] = new State
        {
            AmmunitionTypeId = ammunitionTypeId,
            RemainingAmmunition = remainingAmmunition,
            EventId = eventId,
            LastObservedSequence = ++sequence
        };
        Trim();
        return eventId;
    }

    public void Clear()
    {
        states.Clear();
        sequence = 0;
    }

    private void Trim()
    {
        while (states.Count > capacity)
        {
            var oldest = states.OrderBy(entry => entry.Value.LastObservedSequence).First();
            states.Remove(oldest.Key);
        }
    }

    private sealed class State
    {
        public int AmmunitionTypeId { get; set; }

        public int RemainingAmmunition { get; set; }

        public string EventId { get; set; } = string.Empty;

        public long LastObservedSequence { get; set; }
    }
}
