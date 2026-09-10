using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

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
    {
        profile = p.Profile; combat = p.Combat; weapons = p.Weapons; groups = p.WeaponAmmunitionGroups; generation = profile.GenerationId;
        combatLifetime = combat.Lifetime; combatWeapons = combatLifetime.Weapons;
    }
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
    public string Tooltip { get; }
    public CombatMetric(string label, CombatValue value, string tooltip = "") { Label = label; Value = value; Tooltip = tooltip; }
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
    {
        Row = row; Ammunition = Array.AsReadOnly(ammunition.ToArray()); Notice = notice;
        Metrics = Array.AsReadOnly(metrics.ToArray()); ActionLabel = actionLabel; HasRangedEvidence = hasRangedEvidence;
    }
}
internal sealed class CombatPresentation
{
    public string GenerationId { get; }
    public IReadOnlyList<CombatMetric> Overall { get; }
    public IReadOnlyList<CombatMetric> Ranged { get; }
    public IReadOnlyList<CombatMetric> Melee { get; }
    public IReadOnlyList<CombatMetric> Throwables { get; }
    public IReadOnlyList<CombatMetric> OtherPlayerKills { get; }
    public CombatValue WorldTotal { get; }
    public IReadOnlyList<CombatMetric> Ownership { get; }
    public IReadOnlyList<CombatTableRow> Enemies { get; }
    public string EnemyNotice { get; }
    public IReadOnlyList<CombatWeapon> Weapons { get; }
    public string WeaponNotice { get; }
    public IReadOnlyList<CombatMetric> IncomingCards { get; }
    public CombatTableRow IncomingTotal { get; }
    public IReadOnlyList<CombatTableRow> Attackers { get; }
    public string IncomingNotice { get; }
    public bool HasIncomingEvidence { get; }
    public CombatPresentation(string generation, IEnumerable<CombatMetric> overall, IEnumerable<CombatMetric> ranged,
        IEnumerable<CombatMetric> melee, IEnumerable<CombatMetric> throwables, CombatValue worldTotal,
        IEnumerable<CombatMetric> ownership, IEnumerable<CombatTableRow> enemies, string enemyNotice,
        IEnumerable<CombatWeapon> weapons, string weaponNotice, IEnumerable<CombatMetric> incomingCards,
        CombatTableRow incomingTotal, IEnumerable<CombatTableRow> attackers, string incomingNotice, bool hasIncomingEvidence = false,
        IEnumerable<CombatMetric>? otherPlayerKills = null)
    {
        GenerationId = generation; Overall = Freeze(overall); Ranged = Freeze(ranged); Melee = Freeze(melee);
        Throwables = Freeze(throwables);
        OtherPlayerKills = Freeze(otherPlayerKills ?? Array.Empty<CombatMetric>());
        WorldTotal = worldTotal; Ownership = Freeze(ownership);
        Enemies = Freeze(enemies); EnemyNotice = enemyNotice; Weapons = Freeze(weapons);
        WeaponNotice = weaponNotice; IncomingCards = Freeze(incomingCards); IncomingTotal = incomingTotal;
        Attackers = Freeze(attackers); IncomingNotice = incomingNotice;
        HasIncomingEvidence = hasIncomingEvidence || Attackers.Count > 0;
    }
    private static System.Collections.ObjectModel.ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}

internal static class CombatPresentationFactory
{
    public static CombatPresentation? Create(StatisticsPanelProjection p, string generation, Func<string, string>? text = null,
        Func<string, bool>? isThrowable = null)
    {
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(p.Profile, generation)
            || p.CombatBinding?.Matches(p, generation) != true) return null;
        var t = text ?? UiText.Get;
        string LocalName(string? name, string id) => Name(p.Names.Get(id, name), id, t);
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
        CombatMetric M(string key, CombatValue value, string tooltip = "") => new(t(key), value, tooltip);
        CombatValue Unavailable() => new(t("ui.unavailable"), CombatEvidence.Unavailable);
        CombatValue Accuracy(double? ratio, bool allowAbove100 = false, bool empty = false) => ratio.HasValue ? Percent(ratio.Value * 100, t, allowAbove100)
            : empty ? new("—", CombatEvidence.Supported) : Unavailable();
        var damage = V(n.DamageDealt, cap.DamageDealt);
        var received = V(n.DamageReceived, cap.DamageReceived);
        var deaths = C(n.PlayerDeaths, cap.PlayerDeaths);
        var overall = new[] { M("ui.overview_damage_dealt", damage), M("ui.overview_damage_taken", received),
            M("ui.kills_by_you", C(n.KillsByYou, cap.KillsByYou)), M("ui.overview_deaths", deaths),
            M("ui.overall_accuracy", Accuracy(CombatAccuracyProjection.Overall(n, cap, a.WasRepairedFromInvalidState), allowAbove100: true,
                empty: CombatAccuracyProjection.OverallIsEmpty(n, cap, a.WasRepairedFromInvalidState))) };
        var ranged = new[] { M("ui.firing_actions", WV(w.Lifetime.Totals.FiringActions, wc.FiringActions)),
            M("ui.combat_hits", C(n.RangedHits, cap.RangedHits)), M("ui.combat_kills", C(kills.Ranged, cap.KillsByYou)),
            M("ui.accuracy", Accuracy(c.Accuracy, empty: CombatAccuracyProjection.RangedIsEmpty(n, cap, a.WasRepairedFromInvalidState))),
            M("ui.runs_headshots", C(n.Headshots, cap.Headshots)), M("ui.combat_headshot_final_blows", C(n.HeadshotFinalBlows, cap.HeadshotFinalBlows)) };
        var melee = new[] { M("ui.combat_swings", C(n.MeleeSwings, cap.MeleeSwings)), M("ui.combat_hits", C(n.MeleeHits, cap.MeleeHits)),
            M("ui.combat_kills", C(kills.Melee, cap.KillsByYou)),
            M("ui.melee_accuracy", Accuracy(CombatAccuracyProjection.Melee(n, cap, a.WasRepairedFromInvalidState), allowAbove100: true,
                empty: CombatAccuracyProjection.MeleeIsEmpty(n, cap, a.WasRepairedFromInvalidState))) };
        var otherPlayerKills = new List<CombatMetric> { M("ui.combat_effect_kills", C(kills.Effect, cap.KillsByYou), t("ui.combat_effect_kills_tooltip")) };
        if (kills.Environmental > 0) otherPlayerKills.Add(M("ui.combat_environmental_kills", C(kills.Environmental, cap.KillsByYou)));
        if (kills.Unknown > 0) otherPlayerKills.Add(M("ui.combat_unknown_kills", C(kills.Unknown, cap.KillsByYou)));
        var world = C(n.ObservedWorldDeaths, cap.ObservedWorldDeaths);
        var ownership = new List<CombatMetric>();
        foreach (var id in new[] { "Other NPC", "Environmental", "Unknown", "Companion" })
        {
            a.Ownership.TryGetValue(id, out var row);
            var count = row?.Totals.ObservedWorldDeaths ?? 0;
            if (id == "Companion" && count == 0) continue;
            var state = Both(cap.ObservedWorldDeaths.State, cap.Ownership.State);
            ownership.Add(new CombatMetric(t("ui.combat_owner_" + id.Replace(" ", "").ToLowerInvariant()),
                Metric(count, state, a.WasRepairedFromInvalidState, t)));
        }
        var enemyRows = c.Enemies.OrderByDescending(r => r.Totals.KillsByYou).ThenByDescending(r => r.Totals.DamageCaused)
            .ThenByDescending(r => r.Totals.ObservedWorldDeaths).ThenBy(r => LocalName(r.DisplayName, r.Id), StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal).Select(r =>
            {
                var wd = ScopedCount(r.Totals.ObservedWorldDeaths, cap.ObservedWorldDeaths);
                var values = new[] { V(r.Totals.DamageCaused, cap.EnemyIdentity), ScopedCount(r.Totals.KillsByYou, cap.KillsByYou), wd };
                return new CombatTableRow(r.Id, LocalName(r.DisplayName, r.Id), values,
                    t("ui.combat_enemy_world") + ": " + wd.Text + "\n" + t("ui.combat_ownership_unavailable"),
                    damage: values[0].Evidence == CombatEvidence.Unavailable ? null : r.Totals.DamageCaused,
                    kills: values[1].Evidence == CombatEvidence.Unavailable ? null : r.Totals.KillsByYou,
                    world: wd.Evidence == CombatEvidence.Unavailable ? null : r.Totals.ObservedWorldDeaths);
            }).ToArray();
        var enemyNotice = Notice(enemyRows.Length, "ui.combat_no_enemies", a.WasRepairedFromInvalidState
            || enemyRows.Length == 0 && (n.DamageDealt > 0 || n.KillsByYou > 0 || n.ObservedWorldDeaths > 0),
            new[] { cap.EnemyIdentity, cap.DamageDealt, cap.KillsByYou, cap.ObservedWorldDeaths }, t);
        var sources = new List<(WeaponAmmunitionGroupProjection? Fire, CombatBreakdownAggregate? Combat, string Id)>();
        var throwableItems = p.Profile.Statistics.Items.Where(pair => pair.Key == pair.Value.ItemId
                && pair.Value.EffectTags.Contains(ItemEffectTag.Throwable)
                && NativeItemTypeIdPolicy.TryParse(pair.Key, out var typeId) && typeId > 0
                && pair.Key == "duckov:item:" + typeId.ToString(CultureInfo.InvariantCulture))
            .ToDictionary(pair => pair.Key.Replace("duckov:item:", "duckov:weapon:"), pair => pair.Value, StringComparer.Ordinal);
        var throwableCapability = p.Profile.Capabilities.FirstOrDefault(value => value.AdapterId == ThrowableUseObservation.CapabilityId)?.State
            ?? AdapterCapabilityState.DisabledIncompatible;
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
            if (HasPlayerWeaponEvidence(pair.Value.Totals)
                && (!ExactWeaponId(pair.Key) || pair.Key != pair.Value.Id || !firingIds.Contains(pair.Key)))
                sources.Add((null, pair.Value, ExactWeaponId(pair.Key) && pair.Key == pair.Value.Id
                    ? pair.Key : "unattributed-combat:" + pair.Key));
        foreach (var pair in throwableItems)
            if (pair.Value.Totals.ActivationCount > 0 && !sources.Any(value => value.Id == pair.Key))
                sources.Add((null, null, pair.Key));
        var unattributed = a.Weapons.Where(pair => !ExactWeaponId(pair.Key) || pair.Key != pair.Value.Id).Select(pair => pair.Value.Totals).ToArray();
        var missingFiringAttribution = w.Lifetime.Totals.FiringActions > p.WeaponAmmunitionGroups.Where(g => ExactWeaponId(g.WeaponId)).Sum(g => (decimal)g.TotalFiringActions);
        var weaponRows = sources.OrderByDescending(s => s.Fire?.TotalFiringActions ?? 0)
            .ThenBy(s => LocalName(s.Fire?.DisplayName ?? s.Combat?.DisplayName
                ?? (throwableItems.TryGetValue(s.Id, out var used) ? used.DisplayName : ""), s.Id), StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(source =>
            {
                throwableItems.TryGetValue(source.Id, out var throwableItem);
                var exact = source.Fire != null ? ExactWeaponId(source.Fire.WeaponId)
                    : source.Combat != null ? source.Id == source.Combat.Id && ExactWeaponId(source.Id) : throwableItem != null;
                var throwable = exact && (throwableItem != null || isThrowable?.Invoke(source.Id) == true);
                var stats = exact ? source.Combat?.Totals : null;
                var g = source.Fire ?? new WeaponAmmunitionGroupProjection { WeaponId = source.Id, DisplayName = source.Combat?.DisplayName ?? throwableItem?.DisplayName ?? "" };
                var rangedWeapon = source.Fire != null || stats != null && (stats.RangedHits > 0 || stats.CompletedPlayerProjectiles > 0
                    || stats.Headshots > 0 || stats.HeadshotFinalBlows > 0 || stats.PlayerKills.Ranged > 0);
                var meleeWeapon = stats != null && (stats.MeleeSwings > 0 || stats.MeleeHits > 0 || stats.PlayerKills.Melee > 0);
                CombatValue Count(Func<CombatMetricTotals, long> get, MetricAvailability availability) => stats == null ? Unavailable()
                    : Metric(get(stats), Both(availability.State, cap.WeaponIdentity.State), a.WasRepairedFromInvalidState
                        || unattributed.Any(row => get(row) > 0), t);
                var actions = source.Fire == null ? rangedWeapon || !meleeWeapon ? Unavailable() : Count(row => row.MeleeSwings, cap.MeleeSwings)
                    : Metric(g.TotalFiringActions, Both(wc.FiringActions.State, wc.WeaponIdentity.State), w.Lifetime.WasRepairedFromInvalidState || !exact || missingFiringAttribution, t);
                var metrics = new List<CombatMetric>();
                var firingActions = actions;
                if (throwable)
                {
                    actions = throwableItem == null ? Unavailable() : Metric(throwableItem.Totals.ActivationCount,
                        throwableCapability, false, t);
                    metrics.Add(M("ui.combat_throwable_uses", actions));
                }
                if (rangedWeapon)
                {
                    metrics.Add(M("ui.firing_actions", source.Fire == null ? Unavailable() : firingActions));
                    var hits = Count(row => row.RangedHits, cap.RangedHits);
                    metrics.Add(M(meleeWeapon ? "ui.combat_ranged_hits" : "ui.combat_hits", hits));
                    metrics.Add(M("ui.combat_weapon_accuracy", Accuracy(CombatAccuracyProjection.Ratio(stats?.RangedHits ?? 0,
                        g.TotalFiringActions, exact && source.Fire != null && hits.Evidence == CombatEvidence.Supported
                            && firingActions.Evidence == CombatEvidence.Supported), allowAbove100: true), t("ui.combat_weapon_accuracy_basis")));
                    metrics.Add(M("ui.runs_headshots", Count(row => row.Headshots, cap.Headshots)));
                    metrics.Add(M("ui.combat_headshot_final_blows", Count(row => row.HeadshotFinalBlows, cap.HeadshotFinalBlows)));
                }
                if (meleeWeapon)
                {
                    metrics.Add(M("ui.combat_swings", Count(row => row.MeleeSwings, cap.MeleeSwings)));
                    metrics.Add(M(rangedWeapon ? "ui.combat_melee_hits" : "ui.combat_hits", Count(row => row.MeleeHits, cap.MeleeHits)));
                }
                metrics.Add(M("ui.kills_by_you", Count(row => row.KillsByYou, cap.KillsByYou)));
                metrics.Add(M("ui.overview_damage_dealt", stats == null ? Unavailable() : Metric(stats.DamageDealt,
                    Both(cap.DamageDealt.State, cap.WeaponIdentity.State), a.WasRepairedFromInvalidState || unattributed.Any(row => row.DamageDealt > 0), t)));
                var actionLabel = throwable ? t("ui.combat_throwable_uses") : rangedWeapon ? t("ui.firing_actions") : meleeWeapon ? t("ui.combat_swings") : "";
                if (!throwable && !rangedWeapon && !meleeWeapon && stats != null)
                {
                    // Player-attributed damage/kills need not establish the weapon's attack type.
                    var summary = stats.DamageDealt > 0 ? metrics[metrics.Count - 1] : metrics[metrics.Count - 2];
                    actions = summary.Value; actionLabel = summary.Label;
                }
                var complete = g.UncorrelatedFiringActions == 0 && g.CorrelatedFiringActions == g.TotalFiringActions;
                var basis = t(complete ? "ui.combat_weapon_basis" : "ui.combat_pair_basis");
                var row = new CombatItemRow(source.Id, exact ? LocalName(g.DisplayName, source.Id) : t("ui.combat_unknown"), actions,
                    source.Fire != null && exact && wc.FiringActions.State == AdapterCapabilityState.Supported && wc.WeaponIdentity.State == AdapterCapabilityState.Supported
                        && !w.Lifetime.WasRepairedFromInvalidState && w.Lifetime.Totals.FiringActions > 0
                        ? Percent(g.TotalFiringActions * 100d / w.Lifetime.Totals.FiringActions, t) : Unavailable(), source.Fire == null ? "" : t("ui.combat_all_basis"));
                var ammo = g.Ammunition.Where(pair => exact && pair.Pair.WeaponId == g.WeaponId)
                    .OrderByDescending(pair => pair.Pair.FiringActions).ThenBy(pair => LocalName(pair.Pair.AmmunitionDisplayName, pair.Pair.AmmunitionId), StringComparer.Ordinal)
                    .ThenBy(pair => pair.Pair.AmmunitionId, StringComparer.Ordinal).Select(pair => new CombatItemRow(pair.Pair.AmmunitionId,
                        LocalName(pair.Pair.AmmunitionDisplayName, pair.Pair.AmmunitionId),
                        Metric(pair.Pair.FiringActions, Both(wc.WeaponAmmunitionPairing.State, Both(wc.WeaponIdentity.State, wc.AmmunitionIdentity.State)), w.Lifetime.WasRepairedFromInvalidState, t),
                        wc.WeaponAmmunitionPairing.State == AdapterCapabilityState.Supported && wc.WeaponIdentity.State == AdapterCapabilityState.Supported
                            && wc.AmmunitionIdentity.State == AdapterCapabilityState.Supported && !w.Lifetime.WasRepairedFromInvalidState && g.CorrelatedFiringActions > 0
                            ? Percent(pair.PercentageWithinObservedWeaponPairs, t) : Unavailable(), basis)).ToArray();
                var notice = Join(g.UncorrelatedFiringActions > 0 ? t("ui.combat_uncorrelated") + ": " + WV(g.UncorrelatedFiringActions, wc.FiringActions).Text : "",
                    wc.WeaponAmmunitionPairing.State != AdapterCapabilityState.Supported ? Join(t("ui.unavailable"), wc.WeaponAmmunitionPairing.Provenance) : "",
                    ammo.Length == 0 ? t("ui.combat_no_pairs") : "");
                if (!rangedWeapon) notice = t(meleeWeapon ? "ui.combat_melee_no_ammo" : "ui.combat_weapon_type_unavailable");
                if (throwable && !rangedWeapon) notice = t("ui.combat_throwable_no_ammo");
                if (throwable && throwableCapability != AdapterCapabilityState.Supported)
                    notice = Join(notice, t("ui.combat_throwable_tracking_unavailable"));
                if (!exact || stats == null) notice = Join(t("ui.combat_weapon_attribution_unavailable"), notice);
                return new CombatWeapon(row, ammo, notice, metrics, actionLabel, rangedWeapon);
            }).ToArray();
        var weaponNotice = Notice(weaponRows.Length, "ui.no_combat", w.Lifetime.WasRepairedFromInvalidState || a.WasRepairedFromInvalidState,
            p.WeaponAmmunitionGroups.Count > 0 ? new[] { wc.FiringActions, wc.WeaponIdentity } : new[] { cap.WeaponIdentity }, t);
        var attackers = c.Killers.Where(r => r.Totals.DamageReceived > 0 || r.Totals.PlayerDeaths > 0)
            .OrderByDescending(r => r.Totals.DamageReceived).ThenByDescending(r => r.Totals.PlayerDeaths)
            .ThenBy(r => LocalName(r.DisplayName, r.Id), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var identities = cap.EnemyIdentity.State == AdapterCapabilityState.Supported && attackers.All(r => !string.IsNullOrWhiteSpace(r.Id));
        var exactDeaths = deaths.Evidence == CombatEvidence.Supported && identities
            && attackers.Sum(r => (decimal)r.Totals.PlayerDeaths) == n.PlayerDeaths;
        var winner = attackers.OrderByDescending(r => r.Totals.PlayerDeaths).ThenByDescending(r => r.Totals.DamageReceived)
            .ThenBy(r => LocalName(r.DisplayName, r.Id), StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        var deadliest = !exactDeaths ? Unavailable() : new CombatValue(n.PlayerDeaths == 0 ? t("ui.combat_none")
            : LocalName(winner!.DisplayName, winner.Id), CombatEvidence.Supported);
        var attackerCount = identities && received.Evidence == CombatEvidence.Supported && exactDeaths
            ? new CombatValue(Number(attackers.Length), CombatEvidence.Supported) : Unavailable();
        CombatValue Share(double value) => received.Evidence == CombatEvidence.Supported && n.DamageReceived > 0
            ? Percent(value * 100 / n.DamageReceived, t) : Unavailable();
        var incoming = attackers.Select(r =>
        {
            var values = new[] { Scoped(r.Totals.DamageReceived, cap.DamageReceived), identities ? Share(r.Totals.DamageReceived) : Unavailable(), ScopedCount(r.Totals.PlayerDeaths, cap.PlayerDeaths) };
            return new CombatTableRow(r.Id, LocalName(r.DisplayName, r.Id), values,
                damage: values[0].Evidence == CombatEvidence.Unavailable ? null : r.Totals.DamageReceived,
                share: values[1].Evidence == CombatEvidence.Unavailable ? null : r.Totals.DamageReceived / n.DamageReceived,
                deaths: values[2].Evidence == CombatEvidence.Unavailable ? null : r.Totals.PlayerDeaths);
        }).ToArray();
        return new CombatPresentation(generation, overall, ranged, melee,
            new[] { M("ui.combat_kills", C(kills.Throwables, cap.ThrowableKills)) }, world, ownership,
            enemyRows, enemyNotice, weaponRows, weaponNotice,
            new[] { M("ui.overview_damage_taken", received), M("ui.overview_deaths", deaths), M("ui.combat_attacker_types", attackerCount), M("ui.combat_deadliest", deadliest) },
            new CombatTableRow("total", t("ui.combat_total"), new[] { received, Share(n.DamageReceived), deaths }), incoming,
            Notice(incoming.Length, "ui.combat_no_attackers", a.WasRepairedFromInvalidState
                || incoming.Length == 0 && (n.DamageReceived > 0 || n.PlayerDeaths > 0),
                new[] { cap.DamageReceived, cap.PlayerDeaths, cap.EnemyIdentity }, t), n.DamageReceived > 0 || n.PlayerDeaths > 0,
            otherPlayerKills);
    }

    private static bool HasPlayerWeaponEvidence(CombatMetricTotals totals) => totals.DamageDealt > 0 || totals.KillsByYou > 0
        || totals.CompletedPlayerProjectiles > 0 || totals.RangedHits > 0 || totals.MeleeSwings > 0 || totals.MeleeHits > 0
        || totals.Headshots > 0 || totals.HeadshotFinalBlows > 0;

    private static bool ExactWeaponId(string id) => !string.IsNullOrWhiteSpace(id)
        && id != "unknown" && id != "duckov:weapon:unknown" && id != EquipmentEventAssociation.UnavailableId;

    internal static CombatValue Metric(double value, AdapterCapabilityState state, bool partial, Func<string, string> t)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return new(t("ui.unavailable"), CombatEvidence.Unavailable);
        if (state == AdapterCapabilityState.Supported && !partial) return new(Number(value), CombatEvidence.Supported);
        return value > 0 ? new(Number(value) + " (" + t("ui.combat_partial") + ")", CombatEvidence.Partial)
            : new(t("ui.unavailable") + (partial ? " (" + t("ui.combat_partial") + ")" : ""), CombatEvidence.Unavailable);
    }
    internal static CombatValue Metric(long value, AdapterCapabilityState state, bool partial, Func<string, string> t)
    {
        if (value < 0) return new(t("ui.unavailable"), CombatEvidence.Unavailable);
        if (state == AdapterCapabilityState.Supported && !partial) return new(Number(value), CombatEvidence.Supported);
        return value > 0 ? new(Number(value) + " (" + t("ui.combat_partial") + ")", CombatEvidence.Partial)
            : new(t("ui.unavailable") + (partial ? " (" + t("ui.combat_partial") + ")" : ""), CombatEvidence.Unavailable);
    }
    private static AdapterCapabilityState Both(AdapterCapabilityState a, AdapterCapabilityState b) =>
        a == AdapterCapabilityState.Supported && b == AdapterCapabilityState.Supported ? a : AdapterCapabilityState.DisabledIncompatible;
    private static string Notice(int count, string empty, bool partial, IEnumerable<MetricAvailability> caps, Func<string, string> t)
    {
        var unavailable = caps.Where(c => c.State != AdapterCapabilityState.Supported).ToArray();
        if (partial || unavailable.Length > 0) return Join(t("ui.unavailable") + " (" + t("ui.combat_partial") + ")",
            string.Join("\n", unavailable.Select(c => c.Provenance).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal)));
        return count == 0 ? t(empty) : "";
    }
    internal static string Name(string name, string id, Func<string, string> t) => !string.IsNullOrWhiteSpace(name) ? name
        : string.IsNullOrWhiteSpace(id) || id == "unknown" ? t("ui.combat_unknown") : t("ui.combat_unknown") + " (" + id + ")";
    private static CombatValue Percent(double n, Func<string, string> t, bool allowAbove100 = false) => double.IsNaN(n) || double.IsInfinity(n) || n < 0 || !allowAbove100 && n > 100
        ? new(t("ui.unavailable"), CombatEvidence.Unavailable) : new(n.ToString("0.##", CultureInfo.InvariantCulture) + "%", CombatEvidence.Supported);
    private static string Number(double n) => n.ToString("#,0.##", CultureInfo.InvariantCulture);
    private static string Number(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
    private static string Join(params string[] values) => string.Join("\n", values.Where(s => !string.IsNullOrWhiteSpace(s)));
}
