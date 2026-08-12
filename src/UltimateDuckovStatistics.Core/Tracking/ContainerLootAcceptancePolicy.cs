namespace UltimateDuckovStatistics.Core.Tracking;

public static class ContainerLootAcceptancePolicy
{
    public static bool RequiresStableIdentity(
        bool runActive,
        bool raidContext,
        bool exactMainDuck,
        bool corpse) =>
        runActive && raidContext && exactMainDuck && !corpse;

    public static bool ShouldAccept(
        bool runActive,
        bool raidContext,
        bool exactMainDuck,
        bool corpse,
        bool stableKeyAvailable) =>
        RequiresStableIdentity(runActive, raidContext, exactMainDuck, corpse) && stableKeyAvailable;

    public static bool TryReadStableKey(Func<object?> reader, out int key, out string detail)
    {
        key = default;
        if (reader == null)
        {
            detail = "GetKey reader is missing.";
            return false;
        }
        try
        {
            var result = reader();
            if (result is not int stableKey)
            {
                detail = result == null ? "GetKey returned no identity." : "GetKey returned an incompatible identity type.";
                return false;
            }
            key = stableKey;
            detail = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            detail = $"GetKey failed: {exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }
}
