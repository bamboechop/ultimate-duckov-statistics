using System.Runtime.CompilerServices;

namespace UltimateDuckovStatistics.Encounters;

internal sealed partial class NativeEncounterCombatObserver
{
    private ConditionalWeakTable<CharacterMainControl, ActorSnapshot> actorLabels = new();

    private ActorSnapshot Actor(CharacterMainControl? actor)
    {
        if (ReferenceEquals(actor, null)) return new ActorSnapshot();
        // A projectile can outlive its shooter. The managed reference still names
        // the captured actor even when Unity considers its destroyed object null.
        if (actorLabels.TryGetValue(actor, out var label)) return label;
        if (actor == null) return new ActorSnapshot();
        label = new ActorSnapshot
        {
            Id = sink.ActorId(actor), Kind = "character", IsMain = ReferenceEquals(actor, CharacterMainControl.Main),
            PresetKey = actor.characterPreset != null ? actor.characterPreset.nameKey : string.Empty,
            TeamAtFirstObservation = (int)actor.Team
        };
        actorLabels.Add(actor, label);
        return label;
    }

    private sealed class ActorSnapshot
    {
        public int Id { get; set; }
        public string Kind { get; set; } = "unavailable";
        public string PresetKey { get; set; } = string.Empty;
        public bool IsMain { get; set; }
        public int TeamAtFirstObservation { get; set; } = -1;
    }
}
