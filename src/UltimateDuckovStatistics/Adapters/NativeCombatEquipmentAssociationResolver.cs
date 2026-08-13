using System.Runtime.CompilerServices;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeCombatEquipmentAssociationResolver
{
    private readonly object sync = new();
    private ConditionalWeakTable<object, DelayedEffectOrigin> delayedEffectOrigins = new();

    public static EquipmentEventAssociation ResolveHealthTransition(
        EquipmentEventAssociation? originatingScope,
        EquipmentEventAssociation observedAtImpact) =>
        Clone(originatingScope ?? observedAtImpact ?? new EquipmentEventAssociation());

    public EquipmentEventAssociation ResolveEffect(
        object source,
        bool delayed,
        EquipmentEventAssociation? originatingScope,
        Func<EquipmentEventAssociation> currentAssociationProvider,
        string generationId,
        string runId,
        string mapId)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (currentAssociationProvider == null) throw new ArgumentNullException(nameof(currentAssociationProvider));
        if (!delayed)
            return Clone(originatingScope ?? currentAssociationProvider() ?? new EquipmentEventAssociation());

        lock (sync)
        {
            delayedEffectOrigins.TryGetValue(source, out var existing);
            if (existing != null && !existing.Matches(generationId, runId))
            {
                delayedEffectOrigins.Remove(source);
                existing = null;
            }

            if (originatingScope != null)
                existing = Capture(source, existing, originatingScope, generationId, runId, mapId, string.Empty);

            return existing is { Ambiguous: false }
                ? Clone(existing.Association)
                : new EquipmentEventAssociation();
        }
    }

    public void CaptureDelayedEffectOrigin(
        object source,
        EquipmentEventAssociation association,
        string generationId,
        string runId,
        string mapId,
        string segmentId = "")
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (association == null) throw new ArgumentNullException(nameof(association));
        lock (sync)
        {
            delayedEffectOrigins.TryGetValue(source, out var existing);
            if (existing != null && !existing.Matches(generationId, runId))
            {
                delayedEffectOrigins.Remove(source);
                existing = null;
            }
            Capture(source, existing, association, generationId, runId, mapId, segmentId);
        }
    }

    public bool TryGetOrigin(
        object source,
        string generationId,
        string runId,
        out string mapId,
        out string segmentId)
    {
        lock (sync)
        {
            if (delayedEffectOrigins.TryGetValue(source, out var origin)
                && origin.Matches(generationId, runId)
                && !origin.Ambiguous)
            {
                mapId = origin.MapId;
                segmentId = origin.SegmentId;
                return true;
            }
        }
        mapId = MapIdentity.UnknownId;
        segmentId = string.Empty;
        return false;
    }

    public void Clear()
    {
        lock (sync) delayedEffectOrigins = new ConditionalWeakTable<object, DelayedEffectOrigin>();
    }

    private static bool Same(EquipmentEventAssociation left, EquipmentEventAssociation right) =>
        string.Equals(left.LoadoutId, right.LoadoutId, StringComparison.Ordinal)
        && string.Equals(left.SelectedWeaponSlotId, right.SelectedWeaponSlotId, StringComparison.Ordinal)
        && string.Equals(left.SelectedWeaponId, right.SelectedWeaponId, StringComparison.Ordinal)
        && string.Equals(left.TotemSetId, right.TotemSetId, StringComparison.Ordinal);

    private DelayedEffectOrigin Capture(
        object source,
        DelayedEffectOrigin? existing,
        EquipmentEventAssociation association,
        string generationId,
        string runId,
        string mapId,
        string segmentId)
    {
        if (existing == null)
        {
            existing = new DelayedEffectOrigin(
                generationId, runId, mapId, segmentId, Clone(association), ambiguous: false);
            delayedEffectOrigins.Add(source, existing);
        }
        else if (!Same(existing.Association, association))
        {
            existing.Association = new EquipmentEventAssociation();
            existing.Ambiguous = true;
        }
        return existing;
    }

    private static EquipmentEventAssociation Clone(EquipmentEventAssociation value) => new()
    {
        LoadoutId = value.LoadoutId,
        SelectedWeaponSlotId = value.SelectedWeaponSlotId,
        SelectedWeaponId = value.SelectedWeaponId,
        TotemSetId = value.TotemSetId
    };

    private sealed class DelayedEffectOrigin
    {
        public DelayedEffectOrigin(
            string generationId,
            string runId,
            string mapId,
            string segmentId,
            EquipmentEventAssociation association,
            bool ambiguous)
        {
            GenerationId = generationId ?? string.Empty;
            RunId = runId ?? string.Empty;
            MapId = mapId ?? string.Empty;
            SegmentId = segmentId ?? string.Empty;
            Association = association;
            Ambiguous = ambiguous;
        }

        private string GenerationId { get; }
        private string RunId { get; }
        public string MapId { get; }
        public string SegmentId { get; }
        public EquipmentEventAssociation Association { get; set; }
        public bool Ambiguous { get; set; }

        public bool Matches(string generationId, string runId) =>
            string.Equals(GenerationId, generationId, StringComparison.Ordinal)
            && string.Equals(RunId, runId, StringComparison.Ordinal);
    }
}
