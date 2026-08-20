using System.Runtime.CompilerServices;

namespace UltimateDuckovStatistics.Core.Tracking;

public readonly struct CombatBuffOwnershipResolution
{
    public CombatBuffOwnershipResolution(CombatActorEvidence actor, bool conflictingEvidence)
    {
        Actor = actor;
        ConflictingEvidence = conflictingEvidence;
    }

    public CombatActorEvidence Actor { get; }

    public bool ConflictingEvidence { get; }
}

public sealed class CombatBuffOwnershipTracker
{
    private readonly object sync = new();
    private ConditionalWeakTable<object, ActorState> actors = new();

    public void Observe(
        object runtimeBuff,
        CombatActorEvidence retainedActor,
        CombatActorEvidence incomingActor)
    {
        if (runtimeBuff == null) throw new ArgumentNullException(nameof(runtimeBuff));

        lock (sync)
        {
            if (!actors.TryGetValue(runtimeBuff, out var state))
            {
                var actor = retainedActor.IsPresent ? retainedActor : incomingActor;
                state = new ActorState(actor)
                {
                    ConflictingEvidence = !SamePresentActor(retainedActor, incomingActor)
                };
                actors.Add(runtimeBuff, state);
                return;
            }

            if (!SamePresentActor(state.Actor, incomingActor))
            {
                state.ConflictingEvidence = true;
            }
        }
    }

    public CombatBuffOwnershipResolution Resolve(
        object runtimeBuff,
        CombatActorEvidence retainedActor,
        bool applicationObservationTrusted = true)
    {
        if (runtimeBuff == null) throw new ArgumentNullException(nameof(runtimeBuff));
        if (!applicationObservationTrusted)
        {
            return new CombatBuffOwnershipResolution(
                CombatActorEvidence.Missing,
                conflictingEvidence: true);
        }

        lock (sync)
        {
            if (!actors.TryGetValue(runtimeBuff, out var state))
            {
                return new CombatBuffOwnershipResolution(retainedActor, conflictingEvidence: false);
            }

            return state.ConflictingEvidence
                ? new CombatBuffOwnershipResolution(CombatActorEvidence.Missing, conflictingEvidence: true)
                : new CombatBuffOwnershipResolution(state.Actor, conflictingEvidence: false);
        }
    }

    public void Clear()
    {
        lock (sync) actors = new ConditionalWeakTable<object, ActorState>();
    }

    private static bool SamePresentActor(CombatActorEvidence left, CombatActorEvidence right) =>
        left.IsPresent
        && right.IsPresent
        && left.Kind == right.Kind
        && left.Identity == right.Identity;

    private sealed class ActorState
    {
        public ActorState(CombatActorEvidence actor)
        {
            Actor = actor;
        }

        public CombatActorEvidence Actor { get; }

        public bool ConflictingEvidence { get; set; }
    }
}
