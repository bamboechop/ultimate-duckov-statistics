namespace UltimateDuckovStatistics.Encounters;

// No native dependencies; both independently observed stack sides must agree.
internal static class LootProbeQuantityEvidence
{
    internal static bool TryCombineDelta(int incomingBefore, int incomingAfter,
        int receivingBefore, int receivingAfter, bool nativeFault, out int moved)
    {
        moved = 0;
        if (nativeFault || incomingBefore < 0 || incomingAfter < 0 || receivingBefore < 0 || receivingAfter < 0)
            return false;
        var loss = (long)incomingBefore - incomingAfter;
        var gain = (long)receivingAfter - receivingBefore;
        if (gain < 0 || gain != loss || gain > int.MaxValue) return false;
        moved = (int)gain;
        return true;
    }
}
