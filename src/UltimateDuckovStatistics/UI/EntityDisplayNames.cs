namespace UltimateDuckovStatistics.UI;

// Presentation-only lookup: stable identities and recorded fallback labels never change.
internal sealed class EntityDisplayNames(Func<string, string?>? resolve = null,
    Func<string, string, string?>? resolveSlot = null)
{
    internal static EntityDisplayNames Recorded { get; } = new();

    internal string Get(string? id, string? recorded)
    {
        if (resolve != null && !string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var current = resolve(id!);
                if (!string.IsNullOrWhiteSpace(current) && current != id) return current!;
            }
            catch { /* Missing native names must not prevent opening historical statistics. */ }
        }
        return recorded ?? string.Empty;
    }

    internal string Slot(string parentItemId, string key, string? recorded)
    {
        try
        {
            var current = resolveSlot?.Invoke(parentItemId, key);
            if (!string.IsNullOrWhiteSpace(current)) return current!;
        }
        catch { /* Native slot definitions can be absent for historical items. */ }
        return recorded ?? string.Empty;
    }
}
