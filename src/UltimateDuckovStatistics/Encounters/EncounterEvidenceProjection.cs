using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Core.Encounters;
using Calibration = UltimateDuckovStatistics.Core.Encounters.EncounterMapCalibration;

namespace UltimateDuckovStatistics.Encounters;

// Worker-owned reducer. Inputs contain copied scalars/arrays, never game objects.
// Flush emits changed records only; sealed route chunks leave the mutable tail.
internal sealed class EncounterEvidenceProjection
{
    private readonly string session;
    private readonly Dictionary<string, EncounterRecord> actors = new();
    private readonly Dictionary<string, EncounterRecord> damage = new();
    private readonly Dictionary<string, EncounterRecord> inventories = new();
    private readonly Dictionary<string, Dictionary<int, long>> observedItems = new();
    private readonly Dictionary<string, EncounterRecord> changed = new();
    private readonly List<EncounterRecord> sealedRoutes = new();
    private readonly Dictionary<string, Calibration> calibrations = new();
    private EncounterRecord? visit;
    private EncounterRouteRecorder? route;
    private string run = string.Empty;
    private int ordinal;
    private RouteConnection connection = RouteConnection.Start;
    private double lastTime;

    public EncounterEvidenceProjection(string session) { this.session = session; }
    public static bool Accepts(string kind) => kind is "path-visit" or "path-point" or "path-gap"
        or "path-teleport" or "path-discontinuity" or "map-calibration" or "combat_hurt_complete"
        or "combat_fatal" or "loot.inventory-open" or "loot.inspection" or "loot.inventory-row"
        or "loot.transfer" or "session-end";

    public void Apply(string runId, string map, string segment, double time, string kind, JObject data)
    {
        if (run.Length == 0) run = runId;
        if (run != runId) throw new InvalidOperationException("Drain the previous run before changing capture ownership.");
        lastTime = Math.Max(lastTime, time);
        if (kind == "session-end") { EndVisit(time); return; }
        if (string.IsNullOrWhiteSpace(map) || string.IsNullOrWhiteSpace(segment)) return;
        // Combat can arrive before the map observer's first sample. Its later
        // path-visit announcement must not split the same run/segment again.
        if (visit == null || visit.Visit!.MapId != map || visit.Visit.SegmentId != segment) NewVisit(map, segment, time);
        if (kind == "path-gap" || kind == "path-discontinuity")
        { connection = RouteConnection.Gap; visit!.Visit!.HasGaps = true; Mark(visit); return; }
        if (kind == "path-point")
        {
            if (Coordinates(data["position"], map) is { } position)
            { route!.Append(time, position, connection); connection = RouteConnection.Walk; }
            return;
        }
        if (kind == "path-teleport")
        {
            if ((bool?)data["provenSameMap"] == true && Coordinates(data["from"], map) is { } from
                && Coordinates(data["arrival"], map) is { } to)
            { route!.Append(time, from, connection);
                route.Append(time, to, EncounterPathMath.HasMapDisplacement(from.X, from.Z, to.X, to.Z) ? RouteConnection.Teleport : RouteConnection.Walk);
                connection = RouteConnection.Walk; }
            else connection = RouteConnection.Gap;
            return;
        }
        if (kind == "map-calibration")
        {
            var m = data["metadata"]!;
            var size = (float?)m["imageWorldSize"] ?? 0;
            if (size <= 0) return;
            var c = new Calibration
            {
                CenterX = (float?)m["worldCenter"]?[0] ?? 0, CenterZ = (float?)m["worldCenter"]?[2] ?? 0,
                Size = size, OffsetX = (float?)m["offset"]?[0] ?? 0, OffsetZ = (float?)m["offset"]?[1] ?? 0,
                Combined = (bool?)m["combined"] == true, CombinedCenterX = (float?)m["combinedCenter"]?[0] ?? 0,
                CombinedCenterZ = (float?)m["combinedCenter"]?[1] ?? 0, CombinedSize = (float?)m["combinedSize"] ?? 0,
                Hidden = (bool?)m["hide"] == true, NoSignal = (bool?)m["noSignal"] == true,
                ArtworkKey = (bool?)m["available"] == true ? (string?)data["calibrationId"] : null
            };
            calibrations[map] = c; visit!.Visit!.Calibration = c; Mark(visit); return;
        }
        if (kind is "combat_hurt_complete" or "combat_fatal") Combat(time, kind, data);
        else if (kind.StartsWith("loot.", StringComparison.Ordinal)) Loot(time, kind, data);
    }

    private void NewVisit(string map, string segment, double time)
    {
        EndVisit(time);
        var id = session + "/v" + ++ordinal;
        visit = new EncounterRecord { RunId = run, Id = id, VisitId = id, Kind = EncounterRecordKind.Visit,
            Visit = new EncounterVisit { MapId = map, SegmentId = segment, Ordinal = ordinal,
                StartedSeconds = time, Calibration = calibrations.TryGetValue(map, out var c) ? c : null } };
        Mark(visit);
        route = new EncounterRouteRecorder(run, id, record =>
        { if (record.Route!.Sealed) { changed.Remove(Key(record)); sealedRoutes.Add(record); } else Mark(record); });
        connection = RouteConnection.Start;
    }

    private void EndVisit(double time)
    {
        if (visit == null) return;
        route!.Flush(true); visit.Visit!.EndedSeconds = Math.Max(visit.Visit.StartedSeconds, time); Mark(visit);
        route = null; visit = null;
    }

    private EncounterRecord Actor(JToken? actor, double time, string? suffix = null)
    {
        var id = session + "/a" + ((long?)actor?["Id"] ?? 0) + (suffix ?? string.Empty);
        if (actors.TryGetValue(id, out var found)) return found;
        var record = new EncounterRecord { RunId = run, Id = id, VisitId = visit!.Id, Kind = EncounterRecordKind.Encounter,
            Encounter = new EncounterDetail { ActorId = id, EnemyPresetKey = (string?)actor?["PresetKey"], StartedSeconds = time } };
        actors.Add(id, record); Mark(record); return record;
    }

    private void Combat(double time, string kind, JObject data)
    {
        var target = data["Target"]; var source = data["Source"];
        var incoming = (bool?)target?["IsMain"] == true;
        var attribution = Source(source);
        var creditedPlayer = attribution.Credit == EncounterCredit.Player;
        var physical = source?["Physical"];
        // Unknown attackers cannot be joined to one another through the shared zero ID.
        var enemy = incoming ? (HasResolvedCredit(source) ? ((long?)physical?["Id"] is > 0 ? physical : source?["Credited"]) : null) : target;
        var suffix = (long?)enemy?["Id"] is > 0 ? null : "/unknown/" + (data["Transaction"] ?? data["FatalSequence"]);
        var record = Actor(enemy, time, suffix);
        if (kind == "combat_fatal")
        {
            if (record.Encounter!.Outcome.HasValue)
            {
                if (!incoming) return;
                // A projectile may kill the player after its shooter has died. Keep both events.
                var prior = record;
                record = new EncounterRecord { RunId = run, Id = prior.Id + "/death/" + data["FatalSequence"],
                    VisitId = visit!.Id, Kind = EncounterRecordKind.Encounter,
                    Encounter = new EncounterDetail { ActorId = prior.Encounter!.ActorId,
                        EnemyPresetKey = prior.Encounter.EnemyPresetKey, StartedSeconds = prior.Encounter.StartedSeconds } };
            }
            record.Encounter.Outcome = incoming ? EncounterOutcome.PlayerDeath : creditedPlayer ? EncounterOutcome.PlayerKill : EncounterOutcome.OtherDeath;
            record.Encounter.OutcomeVisitId = visit!.Id;
            record.Encounter.EndedSeconds = time;
            record.Encounter.FatalSequence = (long?)data["FatalSequence"];
            record.Encounter.FinalSource = attribution;
            record.Encounter.PlayerPosition = Position(data["Candidate"]?["PlayerPosition"]);
            record.Encounter.EnemyPosition = Position(data["Candidate"]?[incoming ? "SourcePosition" : "TargetPosition"]);
            record.Encounter.SourcePosition = Position(data["Candidate"]?["SourcePosition"]);
            Mark(record); return;
        }
        if ((bool?)data["NativeCallCompleted"] != true) return;
        // Do not add NPC-vs-NPC damage to the player's outgoing total.
        if (!incoming && !creditedPlayer) return;
        var amount = (double?)data["ProposedHpLossOwnedAssignments"] ?? 0;
        if (!double.IsFinite(amount) || amount <= 0) return;
        var id = record.Id + (incoming ? "/in/" : "/out/") + attribution.WeaponTypeId + "/" + attribution.AmmunitionTypeId
            + "/" + attribution.Mechanism + "/" + attribution.Credit;
        if (!damage.TryGetValue(id, out var tally))
        {
            tally = Child(record, id, EncounterRecordKind.Damage);
            tally.Damage = new EncounterDamage { Incoming = incoming, Source = attribution };
            damage[id] = tally;
        }
        tally.Damage!.Amount += amount; tally.Damage.Hits++;
        if ((bool?)data["Headshot"] == true) tally.Damage.Headshots++;
        Mark(tally);
    }

    private void Loot(double time, string kind, JObject data)
    {
        var actorId = session + "/a" + (long?)data["ActorId"];
        // A corpse with no captured encounter is not fabricated into a player kill.
        if (!actors.TryGetValue(actorId, out var owner)) return;
        var corpseId = session + "/c" + (long?)data["CorpseId"];
        if (kind == "loot.transfer")
        {
            if ((int?)data["TypeId"] is not > 0 || data["Quantity"]?.Type == JTokenType.Null) return;
            var type = (int)data["TypeId"]!;
            var record = Child(owner, corpseId + "/loot/" + type, EncounterRecordKind.Loot);
            record.Loot = new EncounterLoot { CorpseId = corpseId, ItemTypeId = type,
                TakenToPlayer = (long?)data["TakenToPlayer"] ?? 0, TakenToPet = (long?)data["TakenToPet"] ?? 0,
                ReturnedByPlayer = (long?)data["ReturnedFromPlayer"] ?? 0, ReturnedByPet = (long?)data["ReturnedFromPet"] ?? 0 };
            Mark(record); return;
        }
        if (kind == "loot.inventory-open")
        {
            var record = Child(owner, corpseId + "/observed/" + time.ToString("R", System.Globalization.CultureInfo.InvariantCulture), EncounterRecordKind.Inventory);
            record.Inventory = new EncounterInventory { CorpseId = corpseId, ObservedSeconds = time,
                Complete = false }; // Only top-level observed contents, never a full nested inventory claim.
            foreach (var row in data["Rows"] ?? new JArray()) ObserveSlot(record.Inventory, row);
            observedItems[corpseId] = (data["Rows"] ?? new JArray()).Where(row => (int?)row["Index"] is >= 0 && (long?)row["ItemId"] is > 0)
                .GroupBy(row => (int)row["Index"]!).ToDictionary(group => group.Key, group => (long)group.First()["ItemId"]!);
            inventories[corpseId] = record; Mark(record); return;
        }
        if (kind == "loot.inspection" && inventories.TryGetValue(corpseId, out var current) && data["Row"] is JObject slot
            && observedItems.TryGetValue(corpseId, out var items) && items.TryGetValue((int?)slot["Index"] ?? -1, out var item)
            && item == (long?)slot["ItemId"])
        { ObserveSlot(current.Inventory!, slot); Mark(current); }
    }

    private static void ObserveSlot(EncounterInventory inventory, JToken row)
    {
        if ((bool?)row["Revealed"] == true && ((int?)row["TypeId"] is not > 0 || (long?)row["Quantity"] is not > 0)) return;
        var index = (int?)row["Index"] ?? -1;
        if (index < 0 || index >= EncounterInventory.MaximumSlots) return;
        var existing = inventory.Slots.FirstOrDefault(slot => slot.Slot == index);
        // Preserve the first revealed observation at each slot in this opening. A take/return
        // changes the live slot but cannot retroactively rewrite what was seen there.
        if (existing?.Inspected == true) return;
        if (existing == null) { existing = new EncounterInventorySlot { Slot = index }; inventory.Slots.Add(existing); }
        existing.Inspected = (bool?)row["Revealed"] == true;
        existing.ItemTypeId = existing.Inspected ? (int?)row["TypeId"] : null;
        existing.Quantity = existing.Inspected ? (long?)row["Quantity"] : null;
    }

    private EncounterSource Source(JToken? data) => new()
    {
        Credit = !HasResolvedCredit(data) ? EncounterCredit.Unknown
            : (bool?)data?["Credited"]?["IsMain"] == true ? EncounterCredit.Player
            : (long?)data?["Credited"]?["Id"] is > 0 ? EncounterCredit.Other : EncounterCredit.Unknown,
        PhysicalActorId = HasResolvedCredit(data) ? Id(data?["Physical"]) : null,
        CreditedActorId = HasResolvedCredit(data) ? Id(data?["Credited"]) : null,
        WeaponTypeId = HasResolvedCredit(data) && (int?)data?["WeaponId"] is > 0 ? (int?)data?["WeaponId"] : null,
        AmmunitionTypeId = HasResolvedCredit(data) && (bool?)data?["AmmoAgreement"] == true && (int?)data?["LoadedAmmoId"] is > 0 ? (int?)data?["LoadedAmmoId"] : null,
        Mechanism = (string?)data?["Kind"]
    };
    private static bool HasResolvedCredit(JToken? data) => (string?)data?["Kind"] is not ("effect" or "unscoped-effect")
        || (bool?)data?["ActorCreditResolved"] == true;
    private string? Id(JToken? actor) => (long?)actor?["Id"] is > 0 ? session + "/a" + (long?)actor?["Id"] : null;
    private static EncounterPosition? Coordinates(JToken? value, string map) => value is JArray a && a.Count == 3
        ? new EncounterPosition { X = (float)a[0], Y = (float)a[1], Z = (float)a[2], MapId = map } : null;
    private static EncounterPosition? Position(JToken? value) => (bool?)value?["Available"] == true
        ? new EncounterPosition { X = (float)value!["X"]!, Y = (float)value["Y"]!, Z = (float)value["Z"]!, MapId = (string?)value["LogicalScene"] } : null;
    private EncounterRecord Child(EncounterRecord owner, string id, EncounterRecordKind kind) => new()
    { RunId = run, Id = id, Kind = kind, VisitId = owner.VisitId, EncounterId = owner.Id };
    private static string Key(EncounterRecord record) => (int)record.Kind + ":" + record.Id;
    private void Mark(EncounterRecord record) => changed[Key(record)] = record;

    public EncounterRecord[] Flush()
    {
        route?.Flush();
        // Parents first, including visits sealed earlier in this batch.
        var result = changed.Values.Concat(sealedRoutes).OrderBy(record => record.Kind == EncounterRecordKind.Visit ? 0
            : record.Kind == EncounterRecordKind.Encounter ? 1 : 2).ToArray();
        foreach (var record in result) EncounterRecordValidation.Validate(record);
        // Deep detach: the next worker batch may update the same accumulator objects.
        var detached = result.Select(record => JObject.FromObject(record).ToObject<EncounterRecord>()!).ToArray();
        changed.Clear(); sealedRoutes.Clear(); return detached;
    }
}
