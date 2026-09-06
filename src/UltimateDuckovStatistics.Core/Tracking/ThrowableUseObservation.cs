using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Tracking;

// One accepted native release, not one explosion, damaged target, or spawned projectile.
public sealed class ThrowableUseObservation
{
    public const string CapabilityId = "throwable-releases";
    private readonly ItemUseSnapshot snapshot;
    private bool completed;
    public bool Released { get; private set; }
    private ThrowableUseObservation(ItemUseSnapshot snapshot) { this.snapshot = snapshot; }
    public static ThrowableUseObservation? Begin(ItemUseSnapshot snapshot, bool mainPlayer, bool throwable)
        => mainPlayer && throwable && snapshot.GameplayContext == GameplayContext.Raid
            && !string.IsNullOrWhiteSpace(snapshot.SaveGenerationId) && !string.IsNullOrWhiteSpace(snapshot.RunId)
            && !string.IsNullOrWhiteSpace(snapshot.ItemId) ? new(snapshot) : null;
    public void MarkReleased() { if (!completed) Released = true; }
    public ItemUseRecorded? Complete(string generation, string? run, string? segment, int? finalStack,
        bool destroyed, DateTime utc)
    {
        if (completed) return null;
        completed = true;
        if (!Released || generation != snapshot.SaveGenerationId || run != snapshot.RunId || segment != snapshot.SegmentId) return null;
        var unit = ConsumptionUnit.UnknownAmount; double amount = 0;
        if (snapshot.Stackable && finalStack.HasValue && finalStack.Value >= 0 && finalStack <= snapshot.StackCount)
        { unit = ConsumptionUnit.StackUnit; amount = snapshot.StackCount - finalStack.Value; }
        else if (destroyed) { unit = snapshot.Stackable ? ConsumptionUnit.StackUnit : ConsumptionUnit.Item; amount = snapshot.Stackable ? snapshot.StackCount : 1; }
        return new ItemUseRecorded
        {
            EventId = Guid.NewGuid().ToString("N"), TimestampUtc = utc, SaveGenerationId = snapshot.SaveGenerationId,
            RunId = snapshot.RunId, MapId = snapshot.MapId, SegmentId = snapshot.SegmentId,
            GameVersion = snapshot.GameVersion, GameBuild = snapshot.GameBuild, GameplayContext = snapshot.GameplayContext,
            IntegrityTags = snapshot.IntegrityTags, AdapterCapability = snapshot.AdapterCapability, AdapterVersion = snapshot.AdapterVersion,
            ItemId = snapshot.ItemId, DisplayName = snapshot.DisplayName, Group = CanonicalItemGroup.Special,
            EffectTags = new() { ItemEffectTag.Throwable }, ActivationCount = 1, AmountConsumed = amount, ConsumptionUnit = unit
        };
    }
}
