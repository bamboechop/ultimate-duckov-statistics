using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Encounters;

public static class EncounterRecordValidation
{
    public static void Validate(EncounterRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        ProfileFormat.ValidateRecordMembers(record);
        Require(record.Version == 1 && Enum.IsDefined(typeof(EncounterRecordKind), record.Kind), "Unsupported encounter record.");
        Identifier(record.RunId); Identifier(record.Id);
        if (record.Kind == EncounterRecordKind.Coverage) Require(record.VisitId == string.Empty, "Run coverage cannot invent a visit.");
        else Identifier(record.VisitId);
        if (record.EncounterId != null) Identifier(record.EncounterId);
        var bodies = new object?[] { record.Visit, record.Route, record.Encounter, record.Damage, record.Inventory, record.Loot, record.Coverage };
        Require(bodies.Count(body => body != null) == 1 && bodies[(int)record.Kind - 1] != null, "Encounter record body does not match its kind.");
        Require((record.Kind is EncounterRecordKind.Damage or EncounterRecordKind.Inventory or EncounterRecordKind.Loot) == (record.EncounterId != null), "Encounter child ownership is missing or unexpected.");
        if (record.Coverage is { } coverage)
        {
            Require(Enum.IsDefined(typeof(EncounterCaptureIssue), coverage.Issue), "Unknown capture issue.");
            Seconds(coverage.ObservedSeconds);
            Require(coverage.CaptureStopped == (coverage.Issue <= EncounterCaptureIssue.NativeCaptureFailed), "Capture issue and stopped state disagree.");
        }
        if (record.Visit is { } visit)
        {
            Require(record.Id == record.VisitId, "Visit identity disagrees with its address.");
            Identifier(visit.MapId); Identifier(visit.SegmentId); Require(visit.Ordinal >= 0, "Visit ordinal is negative.");
            Interval(visit.StartedSeconds, visit.EndedSeconds);
            if (visit.Calibration is { } map)
            {
                Finite(map.CenterX); Finite(map.CenterZ); Finite(map.OffsetX); Finite(map.OffsetZ); Finite(map.Size);
                Require(map.Size > 0, "Map size must be positive.");
                Finite(map.CombinedCenterX); Finite(map.CombinedCenterZ); Finite(map.CombinedSize);
                Require(!map.Combined || map.CombinedSize > 0, "Combined map size must be positive.");
                if (map.ArtworkKey != null)
                    Require(map.ArtworkKey.Length == 64 && map.ArtworkKey.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), "Artwork must be a content hash.");
            }
        }
        if (record.Route is { } route)
        {
            Require(record.Id == record.VisitId + "/" + route.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Route address must identify its visit and chunk index.");
            Require(route.Index >= 0 && route.Points.Count is > 0 and <= EncounterRouteChunk.MaximumPoints, "Route chunk is out of bounds.");
            double previous = -1;
            foreach (var point in route.Points)
            {
                Require(point != null, "Route contains a null point.");
                Seconds(point!.Seconds); Position(point.Position);
                Require(point.Seconds >= previous && Enum.IsDefined(typeof(RouteConnection), point.Connection), "Route ordering or connection is invalid.");
                previous = point.Seconds;
            }
        }
        if (record.Encounter is { } encounter)
        {
            Identifier(encounter.ActorId); OptionalLabel(encounter.EnemyPresetKey);
            Interval(encounter.StartedSeconds, encounter.EndedSeconds);
            Require(encounter.EndedSeconds.HasValue == encounter.Outcome.HasValue, "Encounter outcome needs its event time.");
            if (encounter.Outcome.HasValue) Require(Enum.IsDefined(typeof(EncounterOutcome), encounter.Outcome.Value), "Invalid encounter outcome.");
            if (encounter.PlayerPosition != null) Position(encounter.PlayerPosition);
            if (encounter.EnemyPosition != null) Position(encounter.EnemyPosition);
            if (encounter.SourcePosition != null) Position(encounter.SourcePosition);
            if (encounter.FinalSource != null) Source(encounter.FinalSource);
            if (encounter.OutcomeVisitId != null) Identifier(encounter.OutcomeVisitId);
        }
        if (record.Damage is { } damage)
        {
            Source(damage.Source); Finite(damage.Amount);
            Require(damage.Amount >= 0 && damage.Hits >= 0 && damage.Headshots >= 0 && damage.Headshots <= damage.Hits, "Damage counts are invalid.");
        }
        if (record.Inventory is { } inventory)
        {
            Identifier(inventory.CorpseId); Seconds(inventory.ObservedSeconds);
            Require(inventory.Slots.Count <= EncounterInventory.MaximumSlots, "Inventory snapshot is out of bounds.");
            var slots = new HashSet<int>();
            foreach (var slot in inventory.Slots)
            {
                Require(slot != null && slot.Slot >= 0 && slots.Add(slot.Slot), "Inventory slots are invalid or duplicated.");
                Require(slot!.ItemTypeId.HasValue == slot.Quantity.HasValue, "Inspected inventory evidence is incomplete.");
                Require(slot.Inspected || (!slot.ItemTypeId.HasValue && !slot.Quantity.HasValue), "Hidden inventory contents must not be disclosed.");
                if (slot.ItemTypeId.HasValue) Require(slot.ItemTypeId.Value > 0 && slot.Quantity > 0, "Inventory quantity or identity is invalid.");
            }
        }
        if (record.Loot is { } loot)
        {
            Identifier(loot.CorpseId);
            Require(loot.ItemTypeId > 0 && loot.TakenToPlayer >= 0 && loot.TakenToPet >= 0 && loot.ReturnedByPlayer >= 0 && loot.ReturnedByPet >= 0, "Loot counts are invalid.");
        }
    }

    // Used at import/restore, never by the routine save cadence.
    public static void ValidateHistory(IEnumerable<EncounterRecord>? records, ISet<string>? allowedRuns = null)
    {
        if (records == null) return;
        var owners = new Dictionary<(string Run, EncounterRecordKind Kind, string Id), string>();
        var children = new List<EncounterRecord>();
        var chunks = new HashSet<(string Run, string Visit, int Index)>();
        foreach (var record in records)
        {
            Validate(record);
            Require(allowedRuns == null || allowedRuns.Contains(record.RunId), "Encounter belongs to a missing run.");
            Require(!owners.ContainsKey(Key(record)), "Duplicate encounter record address.");
            owners.Add(Key(record), record.VisitId);
            children.Add(record);
            if (record.Route != null) Require(chunks.Add((record.RunId, record.VisitId, record.Route.Index)), "Duplicate route chunk index.");
        }
        foreach (var record in children)
        {
            if (record.Kind == EncounterRecordKind.Coverage) continue;
            if (record.Encounter?.OutcomeVisitId is { } outcomeVisit)
                Require(owners.ContainsKey((record.RunId, EncounterRecordKind.Visit, outcomeVisit)), "Encounter outcome visit is missing.");
            Require(owners.ContainsKey((record.RunId, EncounterRecordKind.Visit, record.VisitId)), "Encounter visit is missing.");
            if (record.EncounterId != null)
                Require(owners.TryGetValue((record.RunId, EncounterRecordKind.Encounter, record.EncounterId), out var visit) && visit == record.VisitId, "Encounter child has no matching owner.");
        }
    }

    internal static (string Run, EncounterRecordKind Kind, string Id) Key(EncounterRecord record) => (record.RunId, record.Kind, record.Id);
    internal static void ValidateReplacement(EncounterRecord previous, EncounterRecord next)
    {
        Require(Key(previous) == Key(next) && previous.VisitId == next.VisitId && previous.EncounterId == next.EncounterId, "Encounter ownership cannot change.");
        if (previous.Coverage is { } oldCoverage && next.Coverage is { } newCoverage)
            Require(oldCoverage.Issue == newCoverage.Issue && oldCoverage.ObservedSeconds == newCoverage.ObservedSeconds
                && oldCoverage.CaptureStopped == newCoverage.CaptureStopped, "Recorded capture failures cannot be erased or changed.");
        if (previous.Visit is { } oldVisit && next.Visit is { } newVisit)
            Require(oldVisit.MapId == newVisit.MapId && oldVisit.SegmentId == newVisit.SegmentId && oldVisit.Ordinal == newVisit.Ordinal,
                "A recorded visit cannot move to another map or segment.");
        if (previous.Encounter is { } oldEncounter && next.Encounter is { } newEncounter)
            Require(oldEncounter.ActorId == newEncounter.ActorId && (!oldEncounter.Outcome.HasValue
                || (oldEncounter.Outcome == newEncounter.Outcome && oldEncounter.EndedSeconds == newEncounter.EndedSeconds
                    && oldEncounter.OutcomeVisitId == newEncounter.OutcomeVisitId)), "A recorded fatal outcome cannot change actor, visit or time.");
        if (previous.Damage is { } oldDamage && next.Damage is { } newDamage)
            Require(oldDamage.Incoming == newDamage.Incoming && newDamage.Amount >= oldDamage.Amount
                && newDamage.Hits >= oldDamage.Hits && newDamage.Headshots >= oldDamage.Headshots, "Recorded damage cannot move backwards or change direction.");
        if (previous.Inventory is { } oldInventory && next.Inventory is { } newInventory)
            Require(oldInventory.CorpseId == newInventory.CorpseId && newInventory.ObservedSeconds >= oldInventory.ObservedSeconds, "Inventory observation cannot change corpse or move backwards.");
        if (previous.Loot is { } oldLoot && next.Loot is { } newLoot)
            Require(oldLoot.CorpseId == newLoot.CorpseId && oldLoot.ItemTypeId == newLoot.ItemTypeId && newLoot.TakenToPlayer >= oldLoot.TakenToPlayer
                && newLoot.TakenToPet >= oldLoot.TakenToPet && newLoot.ReturnedByPlayer >= oldLoot.ReturnedByPlayer && newLoot.ReturnedByPet >= oldLoot.ReturnedByPet,
                "Gross loot counts cannot change identity or move backwards.");
        if (previous.Route is not { } before || next.Route is not { } after) return;
        Require(before.Index == after.Index && (!before.Sealed || after.Sealed) && after.Points.Count >= before.Points.Count
            && (!before.Sealed || after.Points.Count == before.Points.Count), "A sealed route cannot change or a route lose samples.");
        for (var i = 0; i < before.Points.Count; i++)
        {
            var a = before.Points[i]; var b = after.Points[i];
            Require(a.Seconds == b.Seconds && a.Connection == b.Connection && a.Position.X == b.Position.X
                && a.Position.Y == b.Position.Y && a.Position.Z == b.Position.Z && a.Position.MapId == b.Position.MapId, "Recorded route samples cannot change.");
        }
    }

    private static void Source(EncounterSource source)
    {
        Require(source != null && Enum.IsDefined(typeof(EncounterCredit), source.Credit), "Damage source is invalid.");
        if (source!.PhysicalActorId != null) Identifier(source.PhysicalActorId);
        if (source.CreditedActorId != null) Identifier(source.CreditedActorId);
        Require((!source.WeaponTypeId.HasValue || source.WeaponTypeId > 0) && (!source.AmmunitionTypeId.HasValue || source.AmmunitionTypeId > 0), "Source item identity is invalid.");
        OptionalLabel(source.Mechanism);
    }
    private static void Position(EncounterPosition position)
    { Require(position != null, "Position is missing."); Finite(position!.X); Finite(position.Y); Finite(position.Z); if (position.MapId != null) Identifier(position.MapId); }
    private static void Interval(double start, double? end)
    { Seconds(start); if (end.HasValue) { Seconds(end.Value); Require(end >= start, "Encounter time moved backwards."); } }
    private static void Seconds(double value) { Finite(value); Require(value >= 0, "Encounter time is negative."); }
    private static void Finite(double value) => Require(!double.IsNaN(value) && !double.IsInfinity(value), "Encounter coordinate or metric is not finite.");
    private static void Identifier(string value) => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl), "Encounter identity is invalid.");
    private static void OptionalLabel(string? value) => Require(value == null || (value.Length <= 512 && !value.Any(char.IsControl)), "Encounter label is invalid.");
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
