namespace UltimateDuckovStatistics.Adapters;

internal sealed class RetryableHarmonyPatcherLease
{
    public ReflectiveHarmonyPatcher? Value { get; private set; }

    public bool HasValue => Value != null;

    public void Attach(ReflectiveHarmonyPatcher patcher)
    {
        if (patcher == null)
        {
            throw new ArgumentNullException(nameof(patcher));
        }

        if (Value != null)
        {
            throw new InvalidOperationException("A Harmony patcher is already attached.");
        }

        Value = patcher;
    }

    public bool TryCleanup(out string detail)
    {
        if (Value == null)
        {
            detail = "No Harmony patcher requires cleanup.";
            return true;
        }

        if (!Value.TryDispose(out detail))
        {
            return false;
        }

        Value = null;
        return true;
    }
}
