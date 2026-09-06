using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

// The Combat VM is a clone; reference equality against persisted totals cannot prove its origin.
// Bind the factory's complete publication before allowing a retained snapshot to consume it.
internal sealed class CombatProjectionBinding
{
    private readonly ProfileDocument profile;
    private readonly CombatStatisticsViewModel combat;
    private readonly WeaponStatisticsViewModel weapons;
    private readonly CombatStatisticsAggregate combatLifetime;
    private readonly object combatWeapons;
    private readonly IReadOnlyList<WeaponAmmunitionGroupProjection> groups;
    private readonly string generation;
    public CombatProjectionBinding(StatisticsPanelProjection p)
    { profile = p.Profile; combat = p.Combat; weapons = p.Weapons; groups = p.WeaponAmmunitionGroups; generation = profile.GenerationId;
        combatLifetime = combat.Lifetime; combatWeapons = combatLifetime.Weapons; }
    public bool Matches(StatisticsPanelProjection p, string current) => generation == current
        && ReferenceEquals(profile, p.Profile) && ReferenceEquals(combat, p.Combat)
        && ReferenceEquals(weapons, p.Weapons) && ReferenceEquals(groups, p.WeaponAmmunitionGroups)
        && ReferenceEquals(combatLifetime, combat.Lifetime) && ReferenceEquals(combatWeapons, combat.Lifetime.Weapons)
        && ReferenceEquals(weapons.Lifetime, profile.Statistics.RunTotals.WeaponStatistics);
}

internal enum CombatEvidence { Supported, Partial, Unavailable }
internal sealed class CombatValue
{
    public string Text { get; }
    public CombatEvidence Evidence { get; }
    public CombatValue(string text, CombatEvidence evidence) { Text = text; Evidence = evidence; }
}
internal sealed class CombatMetric
{
    public string Label { get; }
    public CombatValue Value { get; }
    public CombatMetric(string label, CombatValue value) { Label = label; Value = value; }
}
internal sealed class CombatTableRow
{
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<CombatValue> Values { get; }
    public string Detail { get; }
    // Deliberately unavailable: no enemy-by-ownership cross dimension is persisted.
    public IReadOnlyList<CombatMetric>? OwnershipBreakdown { get; }
    public bool CanExpand => OwnershipBreakdown is { Count: > 0 };
    public double? SortDamage { get; }
    public double? SortShare { get; }
    public long? SortDeaths { get; }
    public long? SortKills { get; }
    public long? SortWorld { get; }
    public CombatTableRow(string id, string name, IEnumerable<CombatValue> values, string detail = "",
        double? damage = null, double? share = null, long? deaths = null, IEnumerable<CombatMetric>? ownership = null,
        long? kills = null, long? world = null)
    {
        Id = id; Name = name; Values = Array.AsReadOnly(values.ToArray()); Detail = detail;
        SortDamage = damage; SortShare = share; SortDeaths = deaths;
        SortKills = kills; SortWorld = world;
        OwnershipBreakdown = ownership == null ? null : Array.AsReadOnly(ownership.ToArray());
    }
}
internal sealed class CombatItemRow
{
    public string Id { get; }
    public string Name { get; }
    public CombatValue Actions { get; }
    public CombatValue Percentage { get; }
    public string PercentageBasis { get; }
    public CombatItemRow(string id, string name, CombatValue actions, CombatValue percentage, string basis)
    { Id = id; Name = name; Actions = actions; Percentage = percentage; PercentageBasis = basis; }
}
internal sealed class CombatWeapon
{
    public CombatItemRow Row { get; }
    public IReadOnlyList<CombatItemRow> Ammunition { get; }
    public string Notice { get; }
    public IReadOnlyList<CombatMetric> Metrics { get; }
    public string ActionLabel { get; }
    public bool HasRangedEvidence { get; }
    public CombatWeapon(CombatItemRow row, IEnumerable<CombatItemRow> ammunition, string notice,
        IEnumerable<CombatMetric> metrics, string actionLabel, bool hasRangedEvidence)
    { Row = row; Ammunition = Array.AsReadOnly(ammunition.ToArray()); Notice = notice;
        Metrics = Array.AsReadOnly(metrics.ToArray()); ActionLabel = actionLabel; HasRangedEvidence = hasRangedEvidence; }
}
internal sealed class CombatPresentation
{
    public string GenerationId { get; }
    public IReadOnlyList<CombatMetric> Overall { get; }
    public IReadOnlyList<CombatMetric> Ranged { get; }
    public IReadOnlyList<CombatMetric> Melee { get; }
    public IReadOnlyList<CombatMetric> OtherKills { get; }
    public string KillNotice { get; }
    public CombatValue WorldTotal { get; }
    public IReadOnlyList<CombatMetric> Ownership { get; }
    public string OwnershipNotice { get; }
    public IReadOnlyList<CombatTableRow> Enemies { get; }
    public string EnemyNotice { get; }
    public IReadOnlyList<CombatWeapon> Weapons { get; }
    public string WeaponNotice { get; }
    public IReadOnlyList<CombatMetric> IncomingCards { get; }
    public CombatTableRow IncomingTotal { get; }
    public IReadOnlyList<CombatTableRow> Attackers { get; }
    public string IncomingNotice { get; }
    public CombatPresentation(string generation, IEnumerable<CombatMetric> overall, IEnumerable<CombatMetric> ranged,
        IEnumerable<CombatMetric> melee, IEnumerable<CombatMetric> otherKills, string killNotice, CombatValue worldTotal,
        IEnumerable<CombatMetric> ownership, string ownershipNotice, IEnumerable<CombatTableRow> enemies, string enemyNotice,
        IEnumerable<CombatWeapon> weapons, string weaponNotice, IEnumerable<CombatMetric> incomingCards,
        CombatTableRow incomingTotal, IEnumerable<CombatTableRow> attackers, string incomingNotice)
    {
        GenerationId = generation; Overall = Freeze(overall); Ranged = Freeze(ranged); Melee = Freeze(melee);
        OtherKills = Freeze(otherKills); KillNotice = killNotice; WorldTotal = worldTotal; Ownership = Freeze(ownership);
        OwnershipNotice = ownershipNotice; Enemies = Freeze(enemies); EnemyNotice = enemyNotice; Weapons = Freeze(weapons);
        WeaponNotice = weaponNotice; IncomingCards = Freeze(incomingCards); IncomingTotal = incomingTotal;
        Attackers = Freeze(attackers); IncomingNotice = incomingNotice;
    }
    private static System.Collections.ObjectModel.ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

internal static class CombatPresentationFactory
{
    public static CombatPresentation? Create(StatisticsPanelProjection p, string generation, Func<string, string>? text = null)
    {
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(p.Profile, generation)
            || p.CombatBinding?.Matches(p, generation) != true) return null;
        var t = text ?? UiText.Get;
        var c = p.Combat; var a = c.Lifetime; var n = a.Totals; var cap = c.Capabilities;
        var w = p.Weapons; var wc = w.Capabilities; var kills = n.PlayerKills;
        CombatValue V(double value, MetricAvailability availability, bool partial = false) =>
            Metric(value, availability.State, partial || a.WasRepairedFromInvalidState, t);
        CombatValue C(long value, MetricAvailability availability, bool partial = false) =>
            Metric(value, availability.State, partial || a.WasRepairedFromInvalidState, t);
        CombatValue WV(long value, MetricAvailability availability) =>
            Metric(value, availability.State, w.Lifetime.WasRepairedFromInvalidState, t);
        CombatValue Scoped(double value, MetricAvailability availability, bool partial = false) =>
            Metric(value, Both(availability.State, cap.EnemyIdentity.State), partial || a.WasRepairedFromInvalidState, t);
        CombatValue ScopedCount(long value, MetricAvailability availability, bool partial = false) =>
            Metric(value, Both(availability.State, cap.EnemyIdentity.State), partial || a.WasRepairedFromInvalidState, t);
        CombatMetric M(string key, CombatValue value) => new(t(key), value);
        CombatValue Unavailable() => new(t("ui.unavailable"), CombatEvidence.Unavailable);
        var damage = V(n.DamageDealt, cap.DamageDealt);
        var received = V(n.DamageReceived, cap.DamageReceived);
        var deaths = C(n.PlayerDeaths, cap.PlayerDeaths);
        var overall = new[] { M("ui.overview_damage_dealt", damage), M("ui.overview_damage_taken", received),
            M("ui.kills_by_you", C(n.KillsByYou, cap.KillsByYou)), M("ui.overview_deaths", deaths) };
        var ranged = new[] { M("ui.firing_actions", WV(w.Lifetime.Totals.FiringActions, wc.FiringActions)),
            M("ui.combat_hits", C(n.RangedHits, cap.RangedHits)), M("ui.combat_kills", C(kills.Ranged, cap.KillsByYou)),
            M("ui.accuracy", c.Accuracy.HasValue && !a.WasRepairedFromInvalidState ? Percent(c.Accuracy.Value * 100, t) : Unavailable()),
            M("ui.runs_headshots", C(n.Headshots, cap.Headshots)), M("ui.combat_headshot_final_blows", C(n.HeadshotFinalBlows, cap.HeadshotFinalBlows)) };
        var melee = new[] { M("ui.combat_swings", C(n.MeleeSwings, cap.MeleeSwings)), M("ui.combat_hits", C(n.MeleeHits, cap.MeleeHits)),
            M("ui.combat_kills", C(kills.Melee, cap.KillsByYou)) };
        var other = new List<CombatMetric>();
        foreach (var entry in new[] { ("effect", kills.Effect), ("environmental", kills.Environmental), ("unknown", kills.Unknown) })
            if (entry.Item2 > 0) other.Add(M("ui.combat_" + entry.Item1, C(entry.Item2, cap.KillsByYou)));
        var killNotice = "";
        var history = a.HistoricalOwnershipUnavailable || n.LegacyUnclassifiedDeaths > 0;
        var world = C(n.ObservedWorldDeaths, cap.ObservedWorldDeaths, history);
        var ownership = new List<CombatMetric>();
        foreach (var id in new[] { "Other NPC", "Environmental", "Unknown", "Companion" })
        {
            a.Ownership.TryGetValue(id, out var row);
            var count = row?.Totals.ObservedWorldDeaths ?? 0;
            if (id == "Companion" && count == 0) continue;
            var state = Both(cap.ObservedWorldDeaths.State, cap.Ownership.State);
            ownership.Add(new CombatMetric(t("ui.combat_owner_" + id.Replace(" ", "").ToLowerInvariant()),
                Metric(count, state, history || a.WasRepairedFromInvalidState, t)));
        }
        var ownershipNotice = history ? Join(t("ui.historical_ownership_unavailable"), a.HistoricalOwnershipProvenance,
            n.LegacyUnclassifiedDeaths > 0 ? t("ui.legacy_unclassified_deaths") + ": " + Number(n.LegacyUnclassifiedDeaths) : "") : "";
        var enemyRows = c.Enemies.OrderByDescending(r => r.Totals.KillsByYou).ThenByDescending(r => r.Totals.DamageCaused)
            .ThenByDescending(r => r.Totals.ObservedWorldDeaths).ThenBy(r => Name(r.DisplayName, r.Id, t), StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal).Select(r =>
            {
                var wd = ScopedCount(r.Totals.ObservedWorldDeaths, cap.ObservedWorldDeaths, history);
                var values = new[] { V(r.Totals.DamageCaused, cap.EnemyIdentity), ScopedCount(r.Totals.KillsByYou, cap.KillsByYou), wd };
                return new CombatTableRow(r.Id, Name(r.DisplayName, r.Id, t), values,
                    t("ui.combat_enemy_world") + ": " + wd.Text + "\n" + t("ui.combat_ownership_unavailable"),
                    damage: values[0].Evidence == CombatEvidence.Unavailable ? null : r.Totals.DamageCaused,
                    kills: values[1].Evidence == CombatEvidence.Unavailable ? null : r.Totals.KillsByYou,
                    world: wd.Evidence == CombatEvidence.Unavailable ? null : r.Totals.ObservedWorldDeaths);
            }).ToArray();
        var enemyNotice = Notice(enemyRows.Length, "ui.combat_no_enemies", history || a.WasRepairedFromInvalidState,
            new[] { cap.EnemyIdentity, cap.KillsByYou, cap.ObservedWorldDeaths }, t);
        var sources = new List<(WeaponAmmunitionGroupProjection? Fire, CombatBreakdownAggregate? Combat, string Id)>();
        var firingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in p.WeaponAmmunitionGroups)
        {
            CombatBreakdownAggregate? combat = null;
            if (ExactWeaponId(group.WeaponId))
            {
                firingIds.Add(group.WeaponId);
                if (a.Weapons.TryGetValue(group.WeaponId, out var candidate) && candidate.Id == group.WeaponId) combat = candidate;
            }
            sources.Add((group, combat, ExactWeaponId(group.WeaponId) ? group.WeaponId : "unattributed-firing:" + group.WeaponId));
        }
        foreach (var pair in a.Weapons)
            if (!ExactWeaponId(pair.Key) || pair.Key != pair.Value.Id || !firingIds.Contains(pair.Key))
                sources.Add((null, pair.Value, ExactWeaponId(pair.Key) && pair.Key == pair.Value.Id
                    ? pair.Key : "unattributed-combat:" + pair.Key));
        var unattributed = a.Weapons.Where(pair => !ExactWeaponId(pair.Key) || pair.Key != pair.Value.Id).Select(pair => pair.Value.Totals).ToArray();
        var missingFiringAttribution = w.Lifetime.Totals.FiringActions > p.WeaponAmmunitionGroups.Where(g => ExactWeaponId(g.WeaponId)).Sum(g => (decimal)g.TotalFiringActions);
        var weaponRows = sources.OrderByDescending(s => s.Fire?.TotalFiringActions ?? 0)
            .ThenBy(s => Name(s.Fire?.DisplayName ?? s.Combat?.DisplayName ?? "", s.Id, t), StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(source =>
            {
                var exact = source.Fire != null ? ExactWeaponId(source.Fire.WeaponId)
                    : source.Combat != null && source.Id == source.Combat.Id && ExactWeaponId(source.Id);
                var stats = exact ? source.Combat?.Totals : null;
                var g = source.Fire ?? new WeaponAmmunitionGroupProjection { WeaponId = source.Id, DisplayName = source.Combat?.DisplayName ?? "" };
                var rangedWeapon = source.Fire != null || stats != null && (stats.RangedHits > 0 || stats.CompletedPlayerProjectiles > 0
                    || stats.Headshots > 0 || stats.HeadshotFinalBlows > 0 || stats.PlayerKills.Ranged > 0);
                var meleeWeapon = stats != null && (stats.MeleeSwings > 0 || stats.MeleeHits > 0 || stats.PlayerKills.Melee > 0);
                CombatValue Count(Func<CombatMetricTotals, long> get, MetricAvailability availability, bool historical = false) => stats == null ? Unavailable()
                    : Metric(get(stats), Both(availability.State, cap.WeaponIdentity.State), a.WasRepairedFromInvalidState
                        || historical || unattributed.Any(row => get(row) > 0), t);
                var actions = source.Fire == null ? rangedWeapon || !meleeWeapon ? Unavailable() : Count(row => row.MeleeSwings, cap.MeleeSwings)
                    : Metric(g.TotalFiringActions, Both(wc.FiringActions.State, wc.WeaponIdentity.State), w.Lifetime.WasRepairedFromInvalidState || !exact || missingFiringAttribution, t);
                var metrics = new List<CombatMetric>();
                if (rangedWeapon)
                {
                    metrics.Add(M("ui.firing_actions", source.Fire == null ? Unavailable() : actions));
                    metrics.Add(M(meleeWeapon ? "ui.combat_ranged_hits" : "ui.combat_hits", Count(row => row.RangedHits, cap.RangedHits)));
                    metrics.Add(M("ui.runs_headshots", Count(row => row.Headshots, cap.Headshots)));
                    metrics.Add(M("ui.combat_headshot_final_blows", Count(row => row.HeadshotFinalBlows, cap.HeadshotFinalBlows)));
                }
                if (meleeWeapon)
                {
                    metrics.Add(M("ui.combat_swings", Count(row => row.MeleeSwings, cap.MeleeSwings)));
                    metrics.Add(M(rangedWeapon ? "ui.combat_melee_hits" : "ui.combat_hits", Count(row => row.MeleeHits, cap.MeleeHits)));
                }
                metrics.Add(M("ui.kills_by_you", Count(row => row.KillsByYou, cap.KillsByYou,
                    a.HistoricalOwnershipUnavailable || stats?.PlayerKills.HistoricalIncomplete == true || stats?.LegacyUnclassifiedDeaths > 0)));
                metrics.Add(M("ui.overview_damage_dealt", stats == null ? Unavailable() : Metric(stats.DamageDealt,
                    Both(cap.DamageDealt.State, cap.WeaponIdentity.State), a.WasRepairedFromInvalidState || unattributed.Any(row => row.DamageDealt > 0), t)));
                var complete = !g.HistoricalPairingUnavailable && g.UncorrelatedFiringActions == 0 && g.CorrelatedFiringActions == g.TotalFiringActions;
                var basis = t(complete ? "ui.combat_weapon_basis" : "ui.combat_pair_basis");
                var row = new CombatItemRow(source.Id, exact ? Name(g.DisplayName, source.Id, t) : t("ui.combat_unknown"), actions,
                    source.Fire != null && exact && wc.FiringActions.State == AdapterCapabilityState.Supported && wc.WeaponIdentity.State == AdapterCapabilityState.Supported
                        && !w.Lifetime.WasRepairedFromInvalidState && w.Lifetime.Totals.FiringActions > 0
                        ? Percent(g.TotalFiringActions * 100d / w.Lifetime.Totals.FiringActions, t) : Unavailable(), source.Fire == null ? "" : t("ui.combat_all_basis"));
                var ammo = g.Ammunition.Where(pair => exact && pair.Pair.WeaponId == g.WeaponId)
                    .OrderByDescending(pair => pair.Pair.FiringActions).ThenBy(pair => Name(pair.Pair.AmmunitionDisplayName, pair.Pair.AmmunitionId, t), StringComparer.Ordinal)
                    .ThenBy(pair => pair.Pair.AmmunitionId, StringComparer.Ordinal).Select(pair => new CombatItemRow(pair.Pair.AmmunitionId,
                        Name(pair.Pair.AmmunitionDisplayName, pair.Pair.AmmunitionId, t),
                        Metric(pair.Pair.FiringActions, Both(wc.WeaponAmmunitionPairing.State, Both(wc.WeaponIdentity.State, wc.AmmunitionIdentity.State)), w.Lifetime.WasRepairedFromInvalidState, t),
                        wc.WeaponAmmunitionPairing.State == AdapterCapabilityState.Supported && wc.WeaponIdentity.State == AdapterCapabilityState.Supported
                            && wc.AmmunitionIdentity.State == AdapterCapabilityState.Supported && !w.Lifetime.WasRepairedFromInvalidState && g.CorrelatedFiringActions > 0
                            ? Percent(pair.PercentageWithinObservedWeaponPairs, t) : Unavailable(), basis)).ToArray();
                var notice = Join(g.UncorrelatedFiringActions > 0 ? t("ui.combat_uncorrelated") + ": " + WV(g.UncorrelatedFiringActions, wc.FiringActions).Text : "",
                    g.HistoricalPairingUnavailable ? Join(t("ui.combat_pair_history"), w.Lifetime.HistoricalPairingProvenance) : "",
                    wc.WeaponAmmunitionPairing.State != AdapterCapabilityState.Supported ? Join(t("ui.unavailable"), wc.WeaponAmmunitionPairing.Provenance) : "",
                    ammo.Length == 0 ? t("ui.combat_no_pairs") : "");
                if (!rangedWeapon) notice = t(meleeWeapon ? "ui.combat_melee_no_ammo" : "ui.combat_weapon_type_unavailable");
                if (!exact || stats == null) notice = Join(t("ui.combat_weapon_attribution_unavailable"), notice);
                return new CombatWeapon(row, ammo, notice, metrics, t(rangedWeapon ? "ui.firing_actions" : meleeWeapon ? "ui.combat_swings" : "ui.combat_actions"), rangedWeapon);
            }).ToArray();
        var weaponNotice = Notice(weaponRows.Length, "ui.no_combat", w.Lifetime.WasRepairedFromInvalidState || a.WasRepairedFromInvalidState,
            p.WeaponAmmunitionGroups.Count > 0 ? new[] { wc.FiringActions, wc.WeaponIdentity } : new[] { cap.WeaponIdentity }, t);
        var attackers = c.Killers.Where(r => r.Totals.DamageReceived > 0 || r.Totals.PlayerDeaths > 0)
            .OrderByDescending(r => r.Totals.DamageReceived).ThenByDescending(r => r.Totals.PlayerDeaths)
            .ThenBy(r => Name(r.DisplayName, r.Id, t), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var identities = cap.EnemyIdentity.State == AdapterCapabilityState.Supported && attackers.All(r => !string.IsNullOrWhiteSpace(r.Id));
        var exactDeaths = deaths.Evidence == CombatEvidence.Supported && identities
            && attackers.Sum(r => (decimal)r.Totals.PlayerDeaths) == n.PlayerDeaths;
        var winner = attackers.OrderByDescending(r => r.Totals.PlayerDeaths).ThenByDescending(r => r.Totals.DamageReceived)
            .ThenBy(r => Name(r.DisplayName, r.Id, t), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        var deadliest = !exactDeaths ? Unavailable() : new CombatValue(n.PlayerDeaths == 0 ? t("ui.combat_none")
            : Name(winner!.DisplayName, winner.Id, t), CombatEvidence.Supported);
        var attackerCount = identities && received.Evidence == CombatEvidence.Supported && exactDeaths
            ? new CombatValue(Number(attackers.Length), CombatEvidence.Supported) : Unavailable();
        CombatValue Share(double value) => received.Evidence == CombatEvidence.Supported && n.DamageReceived > 0
            ? Percent(value * 100 / n.DamageReceived, t) : Unavailable();
        var incoming = attackers.Select(r =>
        {
            var values = new[] { Scoped(r.Totals.DamageReceived, cap.DamageReceived), identities ? Share(r.Totals.DamageReceived) : Unavailable(), ScopedCount(r.Totals.PlayerDeaths, cap.PlayerDeaths) };
            return new CombatTableRow(r.Id, Name(r.DisplayName, r.Id, t), values,
                damage: values[0].Evidence == CombatEvidence.Unavailable ? null : r.Totals.DamageReceived,
                share: values[1].Evidence == CombatEvidence.Unavailable ? null : r.Totals.DamageReceived / n.DamageReceived,
                deaths: values[2].Evidence == CombatEvidence.Unavailable ? null : r.Totals.PlayerDeaths);
        }).ToArray();
        return new CombatPresentation(generation, overall, ranged, melee, other, killNotice, world, ownership, ownershipNotice,
            enemyRows, Join(enemyNotice, ownershipNotice), weaponRows, weaponNotice,
            new[] { M("ui.overview_damage_taken", received), M("ui.overview_deaths", deaths), M("ui.combat_attacker_types", attackerCount), M("ui.combat_deadliest", deadliest) },
            new CombatTableRow("total", t("ui.combat_total"), new[] { received, Share(n.DamageReceived), deaths }), incoming,
            Notice(incoming.Length, "ui.combat_no_attackers", a.WasRepairedFromInvalidState,
                new[] { cap.DamageReceived, cap.PlayerDeaths, cap.EnemyIdentity }, t));
    }

    private static bool ExactWeaponId(string id) => !string.IsNullOrWhiteSpace(id)
        && id != "unknown" && id != "duckov:weapon:unknown" && id != EquipmentEventAssociation.UnavailableId;

    internal static CombatValue Metric(double value, AdapterCapabilityState state, bool partial, Func<string, string> t)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return new(t("ui.unavailable"), CombatEvidence.Unavailable);
        if (state == AdapterCapabilityState.Supported && !partial) return new(Number(value), CombatEvidence.Supported);
        return value > 0 ? new(Number(value) + " (" + t("ui.runs_partial") + ")", CombatEvidence.Partial)
            : new(t("ui.unavailable") + (partial ? " (" + t("ui.runs_partial") + ")" : ""), CombatEvidence.Unavailable);
    }
    internal static CombatValue Metric(long value, AdapterCapabilityState state, bool partial, Func<string, string> t)
    {
        if (value < 0) return new(t("ui.unavailable"), CombatEvidence.Unavailable);
        if (state == AdapterCapabilityState.Supported && !partial) return new(Number(value), CombatEvidence.Supported);
        return value > 0 ? new(Number(value) + " (" + t("ui.runs_partial") + ")", CombatEvidence.Partial)
            : new(t("ui.unavailable") + (partial ? " (" + t("ui.runs_partial") + ")" : ""), CombatEvidence.Unavailable);
    }
    private static AdapterCapabilityState Both(AdapterCapabilityState a, AdapterCapabilityState b) =>
        a == AdapterCapabilityState.Supported && b == AdapterCapabilityState.Supported ? a : AdapterCapabilityState.DisabledIncompatible;
    private static string Notice(int count, string empty, bool partial, IEnumerable<MetricAvailability> caps, Func<string, string> t)
    {
        var unavailable = caps.Where(c => c.State != AdapterCapabilityState.Supported).ToArray();
        if (partial || unavailable.Length > 0) return Join(t("ui.unavailable") + " (" + t("ui.runs_partial") + ")",
            string.Join("\n", unavailable.Select(c => c.Provenance).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal)));
        return count == 0 ? t(empty) : "";
    }
    internal static string Name(string name, string id, Func<string, string> t) => !string.IsNullOrWhiteSpace(name) ? name
        : string.IsNullOrWhiteSpace(id) || id == "unknown" ? t("ui.combat_unknown") : t("ui.combat_unknown") + " (" + id + ")";
    private static CombatValue Percent(double n, Func<string, string> t) => double.IsNaN(n) || double.IsInfinity(n) || n < 0 || n > 100
        ? new(t("ui.unavailable"), CombatEvidence.Unavailable) : new(n.ToString("0.##", CultureInfo.InvariantCulture) + "%", CombatEvidence.Supported);
    private static string Number(double n) => n.ToString("#,0.##", CultureInfo.InvariantCulture);
    private static string Number(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
    private static string Join(params string[] values) => string.Join("\n", values.Where(s => !string.IsNullOrWhiteSpace(s)));
}
