namespace UltimateDuckovStatistics.Encounters;

// Quantity flow only. It never claims identity for interchangeable original units.
internal sealed class LootTransferAccounting
{
    internal long TakenToPlayer { get; private set; }
    internal long TakenToPet { get; private set; }
    internal long ReturnedFromPlayer { get; private set; }
    internal long ReturnedFromPet { get; private set; }
    internal long NetOutward => TakenToPlayer + TakenToPet - ReturnedFromPlayer - ReturnedFromPet;

    internal void Add(string direction, int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        checked
        {
            switch (direction)
            {
                case "TakenToPlayer": TakenToPlayer += quantity; break;
                case "TakenToPet": TakenToPet += quantity; break;
                case "ReturnedFromPlayer": ReturnedFromPlayer += quantity; break;
                case "ReturnedFromPet": ReturnedFromPet += quantity; break;
                default: throw new ArgumentException("Unknown transfer direction.", nameof(direction));
            }
        }
    }

    // Child primitives are already emitted independently; a parent must emit only
    // its own residual. Conflicting/reversed/overflowed evidence stays unresolved.
    internal static bool TryResidual(int measured, long alreadyCommittedChildren, bool conflictingChildren, out int residual)
    {
        residual = 0;
        if (measured < 0 || alreadyCommittedChildren < 0 || conflictingChildren || alreadyCommittedChildren > measured) return false;
        residual = measured - (int)alreadyCommittedChildren;
        return true;
    }

    internal static bool IsValidSplit(int before, int afterInitialCall, int requested, int resultQuantity, bool sourceAndResultTypesMatch) =>
        sourceAndResultTypesMatch && requested > 0 && requested < before
        && afterInitialCall >= 0 && (long)before - afterInitialCall == requested && resultQuantity == requested;
}
