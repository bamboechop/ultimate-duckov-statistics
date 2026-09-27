using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Tracking;

// Transient presentation evidence, deliberately separate from the persisted combat event.
public sealed class KillFeedEntry(string eventId, bool playerDied, string actorId,
    string actorName, string weaponId, string weaponName, bool headshot, double? meters, double createdAt)
{
    public string EventId { get; } = eventId;
    public bool PlayerDied { get; } = playerDied;
    public string ActorId { get; } = actorId;
    public string ActorName { get; } = actorName;
    public string WeaponId { get; } = weaponId;
    public string WeaponName { get; } = weaponName;
    public bool Headshot { get; } = headshot;
    public double? Meters { get; } = meters;
    public double CreatedAt { get; } = createdAt;
}

public sealed class PlayerKillFeed
{
    private readonly List<KillFeedEntry> entries = new(6);
    private readonly HashSet<string> recentIds = new(StringComparer.Ordinal);
    private readonly Queue<string> idOrder = new();
    private string generation = string.Empty, run = string.Empty;
    public IReadOnlyList<KillFeedEntry> Entries => entries;

    public bool Add(CombatRecorded value, double? meters, double now, KillFeedSettings settings)
    {
        if (!settings.Enabled || double.IsNaN(now) || double.IsInfinity(now)
            || value.GameplayContext != GameplayContext.Raid || !value.IsFinalBlow
            || string.IsNullOrEmpty(value.EventId) || string.IsNullOrEmpty(value.SaveGenerationId)
            || string.IsNullOrEmpty(value.RunId)) return false;
        var died = value.PlayerDeaths > 0;
        if (died ? value.Capabilities.PlayerDeaths.State != AdapterCapabilityState.Supported
            : !(value.KillsByYou > 0 && value.TargetIsEnemy && value.Ownership == CombatOwnership.Player
                && value.Capabilities.KillsByYou.State == AdapterCapabilityState.Supported)) return false;
        if (generation != value.SaveGenerationId || run != value.RunId)
        {
            Clear(); generation = value.SaveGenerationId; run = value.RunId!;
        }
        if (!recentIds.Add(value.EventId)) return false;
        idOrder.Enqueue(value.EventId);
        while (idOrder.Count > 128) recentIds.Remove(idOrder.Dequeue());
        if (meters.HasValue && (double.IsNaN(meters.Value) || double.IsInfinity(meters.Value) || meters.Value < 0)) meters = null;
        entries.Insert(0, new KillFeedEntry(value.EventId, died,
            died ? value.AttackerId : value.TargetId, died ? value.AttackerDisplayName : value.TargetDisplayName,
            value.WeaponId, value.WeaponDisplayName, !died && value.HeadshotFinalBlows > 0
                && value.Capabilities.HeadshotFinalBlows.State == AdapterCapabilityState.Supported, meters, now));
        Trim(now, settings);
        return true;
    }

    public void Trim(double now, KillFeedSettings settings)
    {
        if (!settings.Enabled) { Clear(); return; }
        for (var i = entries.Count - 1; i >= 0; i--)
            if (now - entries[i].CreatedAt >= settings.DurationSeconds || now < entries[i].CreatedAt) entries.RemoveAt(i);
        var maximum = Math.Clamp(settings.MaximumEntries, 1, 6);
        if (entries.Count > maximum) entries.RemoveRange(maximum, entries.Count - maximum);
    }

    public void Clear()
    {
        entries.Clear(); recentIds.Clear(); idOrder.Clear(); generation = run = string.Empty;
    }

    public static float Opacity(KillFeedEntry entry, double now, int seconds) =>
        (float)Math.Clamp(Math.Min((now - entry.CreatedAt) / .12, seconds - (now - entry.CreatedAt)), 0, 1);

    public static double? Distance(EncounterPosition? player, EncounterPosition? other, string map) =>
        // Live HUD positions need their own map evidence; no stored visit supplies it.
        !string.IsNullOrWhiteSpace(player?.MapId) && !string.IsNullOrWhiteSpace(other?.MapId)
        && KillDistanceHighlights.TryDistance(player, other, map, out var meters) ? meters : null;
}
