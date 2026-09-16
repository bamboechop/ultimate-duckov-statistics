namespace UltimateDuckovStatistics.Encounters;

// Pure arithmetic/state used by the opt-in capture and its game-independent tests.
internal sealed class CombatProbeBoundary
{
    public bool HasCandidate { get; private set; }
    public long CandidateSequence { get; private set; }
    public double ProposedLoss { get; private set; }
    public int MutationCount { get; private set; }

    public bool ObserveAssignment(double before, double proposed, long sequence)
    {
        if (!Finite(before) || !Finite(proposed)) return false;
        MutationCount++;
        // Negative native overkill is not additional target HP. Each nested Hurt
        // owns a distinct boundary, so child mutations are not added to its parent.
        ProposedLoss += Math.Max(0, Math.Max(0, before) - Math.Max(0, proposed));
        if (HasCandidate || proposed > 0 || before <= 0) return false;
        HasCandidate = true;
        CandidateSequence = sequence;
        return true;
    }

    public static double NetLoss(double before, double after) =>
        Finite(before) && Finite(after) ? Math.Max(0, before - after) : 0;

    public static bool IsFatal(bool wasDead, bool isDead, bool nativeCallCompleted) =>
        nativeCallCompleted && !wasDead && isDead;

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
