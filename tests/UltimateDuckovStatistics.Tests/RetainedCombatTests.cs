using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.Tests;

// These single-use expected values belong beside their semantic assertions.
#pragma warning disable CA1861
public sealed class RetainedCombatTests
{
    private static StatisticsPanelProjection Projection(string generation = "g")
    {
        var profile = new ProfileDocument { GenerationId = generation, Statistics = new ProfileStatistics { SaveGenerationId = generation } };
        var p = new StatisticsPanelProjection { Profile = profile };
        p.Combat.Lifetime = profile.Statistics.RunTotals.CombatStatistics;
        p.Weapons.Lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        Support(p.Combat.Capabilities); Support(p.Weapons.Capabilities);
        p.CombatBinding = new CombatProjectionBinding(p); return p;
    }
    private static void Support(object caps)
    {
        foreach (var property in caps.GetType().GetProperties())
            if (property.GetValue(caps) is MetricAvailability value) value.State = AdapterCapabilityState.Supported;
    }
    private static CombatPresentation Present(StatisticsPanelProjection p) => CombatPresentationFactory.Create(p, p.Profile.GenerationId)!;
    private static CombatBreakdownAggregate Row(string id, string name, double damage = 0, long kills = 0, long world = 0, double incoming = 0, long deaths = 0) =>
        new() { Id = id, DisplayName = name, Totals = new CombatMetricTotals { DamageCaused = damage, KillsByYou = kills, ObservedWorldDeaths = world, DamageReceived = incoming, PlayerDeaths = deaths } };
    private static void Enemies(StatisticsPanelProjection p, params CombatBreakdownAggregate[] rows) => p.Combat.Enemies = rows;
    private static void Attackers(StatisticsPanelProjection p, params CombatBreakdownAggregate[] rows) => p.Combat.Killers = rows;
    private static void Weapons(StatisticsPanelProjection p, params WeaponAmmunitionGroupProjection[] groups)
    { p.WeaponAmmunitionGroups = groups; p.CombatBinding = new CombatProjectionBinding(p); }
    private static WeaponAmmunitionGroupProjection Weapon(string id, long count, string? name = null, long uncorrelated = 0, bool historical = false,
        params (string Id, long Count, double Percentage)[] pairs) => new()
        {
            WeaponId = id,
            DisplayName = name ?? id,
            TotalFiringActions = count,
            UncorrelatedFiringActions = uncorrelated,
            HistoricalPairingUnavailable = historical,
            CorrelatedFiringActions = pairs.Sum(pair => pair.Count),
            Ammunition = pairs.Select(pair => new WeaponAmmunitionPairView
            {
                Pair = new WeaponAmmunitionPairAggregate { WeaponId = id, AmmunitionId = pair.Id, AmmunitionDisplayName = pair.Id, FiringActions = pair.Count },
                PercentageWithinObservedWeaponPairs = pair.Percentage
            }).ToArray()
        };
    private static CombatDocument Document() => new((value, width, size) => Math.Max(1, value.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length * size * .5 / Math.Max(1, width))))) * size,
        (value, size) => value.Length * size * .5f);

    [Fact]
    public void FourOverallCardsUseSeparateLifetimeTotals()
    {
        var p = Projection(); var n = p.Combat.Lifetime.Totals;
        n.DamageDealt = 2400.25; n.DamageReceived = 82.25; n.KillsByYou = 14; n.PlayerDeaths = 3; n.ObservedWorldDeaths = 900;
        Assert.Equal(new[] { "2,400.25", "82.25", "14", "3" }, Present(p).Overall.Select(m => m.Value.Text));
        Assert.All(Present(p).Overall, m => Assert.Equal(CombatEvidence.Supported, m.Value.Evidence));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EachCardHonorsItsOwnCurrentCapabilityWithoutHidingSiblings(int index)
    {
        var p = Projection(); var c = p.Combat.Capabilities;
        var caps = new[] { c.DamageDealt, c.DamageReceived, c.KillsByYou, c.PlayerDeaths };
        caps[index].State = AdapterCapabilityState.DisabledIncompatible;
        var values = Present(p).Overall;
        for (var i = 0; i < 4; i++) Assert.Equal(i == index ? CombatEvidence.Unavailable : CombatEvidence.Supported, values[i].Value.Evidence);
    }
    [Theory]
    [InlineData(AdapterCapabilityState.Experimental, 0, CombatEvidence.Unavailable)]
    [InlineData(AdapterCapabilityState.DisabledIncompatible, 0, CombatEvidence.Unavailable)]
    [InlineData(AdapterCapabilityState.Experimental, 7, CombatEvidence.Partial)]
    [InlineData(AdapterCapabilityState.DisabledIncompatible, 7, CombatEvidence.Partial)]
    [InlineData(AdapterCapabilityState.Supported, 0, CombatEvidence.Supported)]
    public void SupportedZeroAndRetainedPartialEvidenceAreDistinct(AdapterCapabilityState state, double n, object evidence)
    { Assert.Equal((CombatEvidence)evidence, CombatPresentationFactory.Metric(n, state, false, UiText.Get).Evidence); }

    [Fact]
    public void AccuracyUsesCompletedPlayerProjectilesAndSameEventPartitionsRemainSeparate()
    {
        var p = Projection(); var n = p.Combat.Lifetime.Totals;
        n.RangedHits = 5; n.CompletedPlayerProjectiles = 20; n.MeleeHits = 6; n.MeleeSwings = 9;
        n.KillsByYou = 8; n.PlayerKills = new PlayerKillPartition { Ranged = 3, Melee = 2, Effect = 1, Environmental = 2 };
        p.Weapons.Lifetime.Totals.FiringActions = 100; n.Headshots = 4; n.HeadshotFinalBlows = 1;
        var result = Present(p);
        Assert.Equal(new[] { "100", "5", "3", "25%", "4", "1" }, result.Ranged.Select(m => m.Value.Text));
        Assert.Equal(new[] { "9", "6", "2" }, result.Melee.Select(m => m.Value.Text));
        Assert.Equal(new[] { "1", "2" }, result.OtherKills.Select(m => m.Value.Text));
        Assert.Empty(result.KillNotice);
    }
    [Fact]
    public void HistoricalOnlyKillContentIsOmittedWithoutQualifyingCurrentBuckets()
    {
        var p = Projection(); var n = p.Combat.Lifetime.Totals;
        n.PlayerKills = new PlayerKillPartition { Unknown = 2, HistoricalUnclassified = 8, HistoricalIncomplete = true }; n.KillsByYou = 10;
        var r = Present(p);
        Assert.Single(r.OtherKills); Assert.Equal("2", r.OtherKills[0].Value.Text);
        Assert.Equal("0", r.Ranged[2].Value.Text); Assert.Equal("0", r.Melee[2].Value.Text); Assert.Empty(r.KillNotice);
        n.PlayerKills.Ranged = 3; p.Combat.Capabilities.KillsByYou.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal(CombatEvidence.Partial, Present(p).Ranged[2].Value.Evidence);
        Assert.Equal("10", r.Overall[2].Value.Text);
    }
    [Fact]
    public void WorldDeathsExcludePlayerCreditAndIncludeConditionalCompanion()
    {
        var p = Projection(); var a = p.Combat.Lifetime;
        a.Totals.KillsByYou = 12; a.Totals.ObservedWorldDeaths = 10;
        a.Ownership["Player"] = Row("Player", "Player", kills: 12);
        a.Ownership["Other NPC"] = Row("Other NPC", "Other NPC", world: 3);
        a.Ownership["Environmental"] = Row("Environmental", "Environmental", world: 5);
        a.Ownership["Companion"] = Row("Companion", "Companion", world: 2);
        var r = Present(p); Assert.Equal("12", r.Overall[2].Value.Text); Assert.Equal("10", r.WorldTotal.Text);
        Assert.Equal(new[] { "3", "5", "0", "2" }, r.Ownership.Select(m => m.Value.Text));
        a.Ownership.Remove("Companion"); Assert.Equal(3, Present(p).Ownership.Count);
    }
    [Fact]
    public void HistoricalOwnershipIsNeverAllocatedToModernCategories()
    {
        var p = Projection(); p.Combat.Lifetime.HistoricalOwnershipUnavailable = true;
        p.Combat.Lifetime.HistoricalOwnershipProvenance = "recorded provenance";
        p.Combat.Lifetime.Totals.LegacyUnclassifiedDeaths = 8;
        var r = Present(p);
        Assert.All(r.Ownership, m => Assert.Equal(CombatEvidence.Unavailable, m.Value.Evidence));
        Assert.Equal(CombatEvidence.Unavailable, r.WorldTotal.Evidence);
        Assert.Contains("8", r.OwnershipNotice, StringComparison.Ordinal); Assert.Contains("recorded provenance", r.OwnershipNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("8", r.Overall[2].Value.Text, StringComparison.Ordinal);
    }
    [Fact]
    public void EnemySortUsesAllTieBreakersAndExpansionHasNoOwnershipCrossDimension()
    {
        var p = Projection();
        Enemies(p, Row("z", "A", 3, 1, 4), Row("b", "A", 3, 1, 4), Row("a", "A", 3, 1, 2), Row("damage", "Z", 9, 1), Row("kills", "Z", 1, 2));
        p.Combat.Lifetime.Ownership["Other NPC"] = Row("Other NPC", "Other NPC", world: 999);
        var r = Present(p);
        Assert.Equal(new[] { "kills", "damage", "b", "z", "a" }, r.Enemies.Select(row => row.Id));
        Assert.All(r.Enemies, row => { Assert.Null(row.OwnershipBreakdown); Assert.DoesNotContain("999", row.Detail, StringComparison.Ordinal); });
        Assert.Contains("4", r.Enemies[2].Detail, StringComparison.Ordinal);
    }
    [Fact]
    public void MissingEnemyAndAttackerNamesRetainStableIdentity()
    {
        var p = Projection(); Enemies(p, Row("mod:enemy", "")); Attackers(p, Row("unknown", "", incoming: 1));
        p.Combat.Lifetime.Totals.DamageReceived = 1;
        Assert.Equal("Unknown (mod:enemy)", Present(p).Enemies[0].Name); Assert.Equal("Unknown", Present(p).Attackers[0].Name);
    }
    [Fact]
    public void EmptyEnemyListRequiresSupportedCompleteEvidence()
    {
        var p = Projection(); var supported = Present(p).EnemyNotice;
        p.Combat.Capabilities.EnemyIdentity.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.NotEqual(supported, Present(p).EnemyNotice);
        p.Combat.Capabilities.EnemyIdentity.State = AdapterCapabilityState.Supported;
        p.Combat.Lifetime.HistoricalOwnershipUnavailable = true; Assert.NotEqual(supported, Present(p).EnemyNotice);
    }
    [Fact]
    public void WeaponsUseOverallActionsAndPairsUseTheSelectedWeaponViewDenominator()
    {
        var p = Projection(); p.Weapons.Lifetime.Totals.FiringActions = 200;
        Weapons(p, Weapon("b", 50), Weapon("a", 150, pairs: new[] { ("ammo1", 100L, 100d * 100 / 150), ("ammo2", 50L, 100d * 50 / 150) }));
        var r = Present(p);
        Assert.Equal(new[] { "a", "b" }, r.Weapons.Select(w => w.Row.Id)); Assert.Equal("75%", r.Weapons[0].Row.Percentage.Text);
        Assert.Equal(new[] { "66.67%", "33.33%" }, r.Weapons[0].Ammunition.Select(a => a.Percentage.Text));
        Assert.All(r.Weapons[0].Ammunition, a => Assert.Equal(UiText.Get("ui.combat_weapon_basis"), a.PercentageBasis));
    }
    [Fact]
    public void WeaponTieOrderAndSelectionAreDeterministic()
    {
        var p = Projection(); Weapons(p, Weapon("z", 10, "A"), Weapon("b", 10, "B"), Weapon("a", 10, "A"));
        var state = new CombatSelection(); state.Refresh(Present(p));
        Assert.Equal(new[] { "a", "z", "b" }, state.Snapshot!.Weapons.Select(w => w.Row.Id)); Assert.Equal("a", state.WeaponId);
        Assert.True(state.SelectWeapon("g", "z")); state.Refresh(Present(p)); Assert.Equal("z", state.WeaponId);
        Weapons(p, Weapon("b", 20)); state.Refresh(Present(p)); Assert.Equal("b", state.WeaponId);
    }
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public void PairHistoryAndUncorrelatedActionsQualifyThePercentageBasis(bool historical, int uncorrelated)
    {
        var p = Projection(); Weapons(p, Weapon("w", 10, uncorrelated: uncorrelated, historical: historical, pairs: new[] { ("ammo", 8L, 100d) }));
        var w = Present(p).Weapons[0];
        Assert.Equal("100%", w.Ammunition[0].Percentage.Text); Assert.Equal(UiText.Get("ui.combat_pair_basis"), w.Ammunition[0].PercentageBasis);
        Assert.NotEmpty(w.Notice);
        if (uncorrelated > 0) Assert.Contains("2", w.Notice, StringComparison.Ordinal);
    }
    [Fact]
    public void NoPairsHasExplicitStateWithoutInventedAmmunition()
    {
        var p = Projection(); Weapons(p, Weapon("w", 10, uncorrelated: 10));
        var w = Present(p).Weapons[0]; Assert.Empty(w.Ammunition);
        Assert.Contains(UiText.Get("ui.combat_no_pairs"), w.Notice, StringComparison.Ordinal);
    }
    [Fact]
    public void IncomingSortTotalsSharesCountAndDeadliestUseIncomingDimensions()
    {
        var p = Projection(); var n = p.Combat.Lifetime.Totals; n.DamageReceived = 100; n.PlayerDeaths = 4; n.KillsByYou = 999;
        Attackers(p, Row("z", "B", incoming: 20, deaths: 2), Row("a", "A", incoming: 20, deaths: 2), Row("damage", "Damage", incoming: 60), Row("inactive", "Inactive"));
        var r = Present(p);
        Assert.Equal(new[] { "damage", "a", "z" }, r.Attackers.Select(a => a.Id));
        Assert.Equal(new[] { "100", "100%", "4" }, r.IncomingTotal.Values.Select(v => v.Text));
        Assert.Equal(new[] { "60%", "20%", "20%" }, r.Attackers.Select(a => a.Values[1].Text));
        Assert.Equal("3", r.IncomingCards[2].Value.Text); Assert.Equal("A", r.IncomingCards[3].Value.Text);
    }
    [Fact]
    public void SupportedZeroHasNoRatioAndNoneRequiresCompleteDeathIdentityEvidence()
    {
        var p = Projection(); var r = Present(p);
        Assert.Equal("0", r.IncomingCards[2].Value.Text); Assert.Equal("None", r.IncomingCards[3].Value.Text);
        Assert.Equal(CombatEvidence.Unavailable, r.IncomingTotal.Values[1].Evidence);
        p.Combat.Capabilities.PlayerDeaths.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal(CombatEvidence.Unavailable, Present(p).IncomingCards[3].Value.Evidence);
    }
    [Fact]
    public void StableUnknownDeadliestIsUnknownAndMissingRequiredIdentityIsUnavailable()
    {
        var p = Projection(); p.Combat.Lifetime.Totals.PlayerDeaths = 1;
        Attackers(p, Row("unknown", "Unknown", deaths: 1)); Assert.Equal("Unknown", Present(p).IncomingCards[3].Value.Text);
        p.Combat.Capabilities.EnemyIdentity.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal(CombatEvidence.Unavailable, Present(p).IncomingCards[3].Value.Evidence);
    }
    [Fact]
    public void MissingDeathRowsCannotClaimNoneOrNameAnUnprovenWinner()
    {
        var p = Projection(); p.Combat.Lifetime.Totals.PlayerDeaths = 2;
        Assert.Equal(CombatEvidence.Unavailable, Present(p).IncomingCards[3].Value.Evidence);
        Attackers(p, Row("a", "A", deaths: 1)); Assert.Equal(CombatEvidence.Unavailable, Present(p).IncomingCards[3].Value.Evidence);
    }
    [Fact]
    public void SnapshotCopiesValuesAndExactPublicationBindingRejectsDetachedModels()
    {
        var p = Projection(); p.Combat.Lifetime.Totals.DamageDealt = 12; var r = Present(p);
        p.Combat.Lifetime.Totals.DamageDealt = 90; Assert.Equal("12", r.Overall[0].Value.Text);
        Assert.Null(CombatPresentationFactory.Create(p, "other"));
        p.Combat = new CombatStatisticsViewModel(); Assert.Null(CombatPresentationFactory.Create(p, "g"));
    }
    [Fact]
    public void ProductionProjectionFactoryBindsCombatPublication()
    {
        var p = Projection(); var built = StatisticsPanelProjectionFactory.Create(p.Profile, new(), new(), new());
        Assert.NotNull(CombatPresentationFactory.Create(built, "g"));
        built.Profile.Statistics.SaveGenerationId = "other"; Assert.Null(CombatPresentationFactory.Create(built, "g"));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnprovableOrChangedGenerationImmediatelyResetsSelectionsOffsetsAndFocus(bool unavailable)
    {
        var p = Projection(); Enemies(p, Row("enemy", "Enemy")); Weapons(p, Weapon("w", 1));
        var state = new CombatSelection(); state.Refresh(Present(p)); state.SelectPage(CombatPanelSection.Enemies);
        state.ToggleEnemy("g", "enemy"); state.Capture("primary", 300); state.Focus("enemy");
        state.Refresh(unavailable ? null : Present(Projection("new")));
        Assert.Equal(CombatPanelSection.Summary, state.Page); Assert.Null(state.EnemyId); Assert.Null(state.FocusId);
        Assert.Equal(0, state.Offset("primary", 100, 1000)); Assert.False(state.ToggleEnemy("g", "enemy"));
        if (unavailable) { state.Refresh(Present(p)); Assert.Equal(CombatPanelSection.Summary, state.Page); }
    }
    [Fact]
    public void SameGenerationRetainsIndependentSubpageAndWeaponColumnOffsetsWithClamping()
    {
        var p = Projection(); Enemies(p, Row("e", "Enemy")); Weapons(p, Weapon("a", 1), Weapon("b", 1));
        var s = new CombatSelection(); s.Refresh(Present(p)); s.Capture("primary", 50);
        s.SelectPage(CombatPanelSection.Enemies); s.ToggleEnemy("g", "e"); s.Capture("primary", 300); s.Focus("e");
        s.Refresh(Present(p)); Assert.Null(s.EnemyId); Assert.Equal("e", s.FocusId); Assert.Equal(200, s.Offset("primary", 100, 300));
        s.SelectPage(CombatPanelSection.WeaponsAndAmmunition); s.Capture("primary", 42); s.Capture("ammo", 80); s.Capture("outer", 12);
        s.SelectWeapon("g", "b"); Assert.Equal(0, s.Offset("ammo", 100, 1000)); Assert.Equal(42, s.Offset("primary", 100, 1000)); Assert.Equal(12, s.Offset("outer", 100, 1000));
        s.SelectWeapon("g", "a"); Assert.Equal(80, s.Offset("ammo", 100, 1000));
        s.SelectPage(CombatPanelSection.Summary); Assert.Equal(50, s.Offset("primary", 100, 1000));
    }
    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 768)]
    public void ResponsiveDocumentsRetainEverySectionAndRelationalBounds(int pixels, int height)
    {
        var p = Projection(); var s = Present(p); var stacked = CombatLayoutPolicy.Stack(pixels);
        var shell = RetainedVisualLayoutPolicy.Create(RetainedReferenceTransformPolicy.Create(pixels, height, 1));
        var frame = CombatLayoutPolicy.Frame(shell, height);
        Assert.Equal(shell.Header.Top + shell.Header.Height + 40 * frame.Scale, frame.Top, 3);
        Assert.Equal(height - 30 * frame.Scale, frame.Top + frame.Height * frame.Scale, 3);
        var widths = CombatLayoutPolicy.Widths(frame.Width, stacked); var d = Document(); d.Summary(s, widths.Page, stacked);
        Assert.True(d.Height > 0); Assert.True(frame.Height > 0);
        Assert.Equal(stacked ? frame.Width : frame.Width - 40, stacked ? widths.Page : widths.Selector + widths.Page, 3);
        Assert.Equal(4, d.Rows.Count(row => row.Kind == CombatRowKind.Card));
        Assert.Equal(12, d.Rows.Count(row => row.Kind == CombatRowKind.Metric));
        Assert.Equal(4, d.Rows.Count(row => row.Kind == CombatRowKind.Heading));
        Assert.All(d.Rows, row => { Assert.True(row.X >= 30); Assert.True(row.Width > 0); Assert.True(row.X + row.Width <= widths.Page - 29); });
    }
    [Theory]
    [InlineData(600, 600, 0, false, false)]
    [InlineData(600, 1400, 0, false, true)]
    [InlineData(600, 1400, 200, true, true)]
    [InlineData(600, 1400, 800, true, false)]
    public void OverflowEdgesHaveIndependentEndpointsAndRoundedContours(float viewport, float content, float offset, bool top, bool bottom)
    {
        var cues = OverflowCuePolicy.Resolve(viewport, content, offset);
        Assert.Equal(top, cues.ShowLeading); Assert.Equal(bottom, cues.ShowTrailing);
        var points = RunsRoundedEdgePolicy.Points(400, viewport, 20, true);
        Assert.Equal(20, points[0].Y, 3); Assert.Equal(0, points[8].Y, 3); Assert.Equal(400, points[^1].X, 3);
    }
    [Theory]
    [InlineData(CombatPanelSection.Enemies)]
    [InlineData(CombatPanelSection.WeaponsAndAmmunition)]
    [InlineData(CombatPanelSection.IncomingDamage)]
    public void TenThousandRowsKeepNativeControlDemandBounded(object pageValue)
    {
        var page = (CombatPanelSection)pageValue;
        var p = Projection(); const int count = 10000;
        if (page == CombatPanelSection.Enemies) Enemies(p, Enumerable.Range(0, count).Select(i => Row("e" + i, "Enemy " + i)).ToArray());
        else if (page == CombatPanelSection.IncomingDamage) Attackers(p, Enumerable.Range(0, count).Select(i => Row("a" + i, "Attacker " + i, incoming: 1)).ToArray());
        else Weapons(p, Enumerable.Range(0, count).Select(i => Weapon("w" + i, 1)).ToArray());
        var s = new CombatSelection(); s.Refresh(Present(p)); s.SelectPage(page); var d = Document();
        if (page == CombatPanelSection.WeaponsAndAmmunition) d.Items(s, 700, false);
        else d.Table(page == CombatPanelSection.Enemies ? s.Snapshot!.Enemies : s.Snapshot!.Attackers, "", 1500, null, page == CombatPanelSection.IncomingDamage, s.Snapshot!, false);
        Assert.True(d.Rows.Count >= count);
        foreach (var fraction in new[] { 0f, .5f, 1f })
        {
            var offset = CombatLayoutPolicy.Clamp(d.Height * fraction, 700, d.Height);
            Assert.InRange(CombatLayoutPolicy.Visible(d.Rows, offset, 700).Count, 1, 32);
        }
    }
    [Fact]
    public void LongNamesAndAmmunitionCollectionsReflowWithoutLosingRowsOrNotices()
    {
        var p = Projection(); var name = new string('界', 300); Weapons(p, Weapon("mod:weapon", 1000, name, 1, true,
            Enumerable.Range(0, 1000).Select(i => ("mod:ammo" + i, 1L, .1d)).ToArray()));
        var s = new CombatSelection(); s.Refresh(Present(p)); var d = Document(); d.Items(s, 800, true);
        Assert.Equal(1000, d.Rows.Count(r => r.Kind == CombatRowKind.Item)); Assert.Contains(d.Rows, r => r.Kind == CombatRowKind.Notice);
        Assert.InRange(CombatLayoutPolicy.Visible(d.Rows, d.Height / 2, 700).Count, 1, 20);
        Assert.Equal(name, s.Weapon!.Row.Name);
        Assert.Null(NativeItemTypeIdPolicy.Resolve<object>("mod:weapon", _ => throw new InvalidOperationException(), null));
    }
    [Fact]
    public void SelectorNavigationExitsAtBothEndsAndFocusTracksIdentityAcrossVirtualization()
    {
        Assert.Equal(-1, CombatLayoutPolicy.Move(0, 4, -1)); Assert.Equal(4, CombatLayoutPolicy.Move(3, 4, 1));
        for (var i = 0; i < 3; i++) Assert.Equal(i + 1, CombatLayoutPolicy.Move(i, 4, 1));
        var d = Document();
        for (var i = 0; i < 20; i++) d.Add(new CombatRenderRow { Id = "e" + i, Actionable = true, Kind = CombatRowKind.Selector, Cells = new[] { "Enemy " + i } }, 30, i * 100, 700);
        var visible = CombatLayoutPolicy.Visible(d.Rows, 400, 300);
        Assert.True(CombatLayoutPolicy.FocusSlot(d.Rows, visible, "e5") >= 0); Assert.Equal(-1, CombatLayoutPolicy.FocusSlot(d.Rows, visible, "e0"));
        Assert.Equal(400, RunsLayoutPolicy.Reveal(400, 300, d.Height, 500, 64));
    }
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 200, false)]
    [InlineData(false, 0, false)]
    [InlineData(false, 200, false)]
    public void NonActionablePagesScrollInBothDirectionsUntilTheLeadingEndpoint(bool up, float offset, bool returns)
    { Assert.Equal(returns, CombatLayoutPolicy.ReturnFromScroll(up, offset)); }
    [Fact]
    public void PooledNativeButtonCancelsPressedIdentityOnRebindAndAllowsCurrentSubmit()
    {
        var b = new RunsHistoryButton(); var graphic = new Graphic(); b.Configure(graphic);
        Assert.True(graphic.raycastTarget); Assert.True(b.interactable); Assert.Equal(Navigation.Mode.None, b.navigation.mode);
        var clicked = 0; b.Clicked += () => clicked++;
        b.Binding.Bind("g", "enemy"); b.OnPointerDown(new PointerEventData()); b.Binding.Bind("g", "replacement"); b.OnPointerClick(new PointerEventData()); Assert.Equal(0, clicked);
        b.OnSubmit(new BaseEventData()); Assert.Equal(1, clicked);
        b.OnPointerDown(new PointerEventData()); b.Disable(); b.OnPointerClick(new PointerEventData()); Assert.Equal(1, clicked);
    }
    [Fact]
    public void PoolResourcesAreReleasedExactlyOnceAcrossRepeatedRefreshAndDisposal()
    {
        var created = 0; var released = 0;
        var pool = new CombatControlPool<RetainedListenerLease>(() => { created++; var lease = new RetainedListenerLease(); lease.Register(() => released++); return lease; });
        for (var i = 0; i < 100; i++) pool.Ensure(i % 20);
        Assert.Equal(19, created); pool.Dispose(); pool.Dispose(); pool.Ensure(500);
        Assert.Equal(19, released); Assert.Empty(pool.Items);
    }
    [Fact]
    public void LongCountersRemainExactBeyondDoubleIntegerPrecision()
    {
        var p = Projection(); p.Combat.Lifetime.Totals.KillsByYou = 9007199254740993L;
        p.Weapons.Lifetime.Totals.FiringActions = 9007199254740993L;
        Assert.Equal("9,007,199,254,740,993", Present(p).Overall[2].Value.Text);
        Assert.Equal("9,007,199,254,740,993", Present(p).Ranged[0].Value.Text);
    }
    [Fact]
    public void IdentityLossQualifiesScopedRowsWhileIndependentOverallTotalsSurvive()
    {
        var p = Projection(); Enemies(p, Row("e", "Enemy", kills: 2)); Attackers(p, Row("e", "Enemy", incoming: 12));
        p.Combat.Lifetime.Totals.KillsByYou = 2; p.Combat.Lifetime.Totals.DamageReceived = 12;
        p.Combat.Capabilities.EnemyIdentity.State = AdapterCapabilityState.DisabledIncompatible;
        var r = Present(p);
        Assert.Equal(CombatEvidence.Supported, r.Overall[2].Value.Evidence); Assert.Equal(CombatEvidence.Supported, r.IncomingTotal.Values[0].Evidence);
        Assert.Equal(CombatEvidence.Partial, r.Enemies[0].Values[1].Evidence); Assert.Equal(CombatEvidence.Partial, r.Attackers[0].Values[0].Evidence);
    }
    [Fact]
    public void AmmunitionIdentityLossQualifiesPairsWithoutInventingConsumptionOrProjectiles()
    {
        var p = Projection(); p.Weapons.Lifetime.Totals.FiringActions = 10;
        p.Weapons.Lifetime.Totals.AmmunitionUnitsConsumed = 9999; p.Weapons.Lifetime.Totals.Projectiles = 8888;
        Weapons(p, Weapon("w", 10, pairs: new[] { ("ammo", 10L, 100d) }));
        p.Weapons.Capabilities.AmmunitionIdentity.State = AdapterCapabilityState.DisabledIncompatible;
        var r = Present(p); Assert.Equal("10", r.Ranged[0].Value.Text);
        Assert.Equal(CombatEvidence.Partial, r.Weapons[0].Ammunition[0].Actions.Evidence);
        Assert.Equal(CombatEvidence.Unavailable, r.Weapons[0].Ammunition[0].Percentage.Evidence);
    }
    [Theory]
    [InlineData("duckov:ammo:123", "duckov:item:123")]
    [InlineData("duckov:weapon:123", "duckov:weapon:123")]
    [InlineData("mod:ammo", "mod:ammo")]
    public void CapturedAmmunitionTypeIdUsesNativeMetadataWithoutChangingItsIdentity(string captured, string expected)
    {
        var icon = new object(); string? requested = null;
        Assert.Same(icon, CombatItemIconPolicy.Resolve(captured, id => { requested = id; return icon; }));
        Assert.Equal(expected, requested);
        Assert.Null(CombatItemIconPolicy.Resolve<object>(captured, _ => throw new InvalidOperationException()));
    }
    [Fact]
    public void SubpageChangesPreserveSelectorScrollAndDesktopFooterDoesNotShortenItsPanel()
    {
        var state = new CombatSelection(); state.Refresh(Present(Projection())); state.Capture("selector", 150);
        state.SelectPage(CombatPanelSection.WeaponsAndAmmunition);
        Assert.Equal(150, state.Offset("selector", 400, 600));
        Assert.Equal(900, CombatLayoutPolicy.OuterViewport(false, 900, 45));
        Assert.Equal(855, CombatLayoutPolicy.OuterViewport(true, 900, 45));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuickGlanceValuesPrecedeTheirMutedLabels(bool incoming)
    {
        var p = Present(Projection()); var d = Document();
        if (incoming) d.Table(p.Attackers, "", 1500, null, true, p, false); else d.Summary(p, 1500, false);
        var cards = d.Rows.Where(r => r.Kind == CombatRowKind.Card).ToArray();
        var metrics = incoming ? p.IncomingCards : p.Overall;
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(metrics[i].Value.Text, cards[i].Cells[0]);
            Assert.Equal(RunsViewStyle.Uppercase(metrics[i].Label), cards[i].Cells[1]);
            Assert.False(CombatLayoutPolicy.Muted(cards[i], 0)); Assert.True(CombatLayoutPolicy.Muted(cards[i], 1));
        }
    }
    [Fact]
    public void MissingOwnershipDisablesExpansionAndRealDetailsAreSeparateWhiteReadOnlyRows()
    {
        var projection = Projection(); Enemies(projection, Row("e", "Enemy", world: 4)); var p = Present(projection);
        var state = new CombatSelection(); state.Refresh(p); Assert.False(state.ToggleEnemy("g", "e"));
        var d = Document(); d.Table(p.Enemies, "", 1500, "e", false, p, false);
        Assert.All(d.Rows, r => { Assert.False(r.Actionable); Assert.False(r.Selected); Assert.False(r.Expandable); });
        var owner = new CombatMetric("Other NPC", new CombatValue("4", CombatEvidence.Supported));
        var available = new CombatTableRow("e", "Enemy", p.Enemies[0].Values, ownership: new[] { owner });
        var closed = Document(); closed.Table(new[] { available }, "", 1500, null, false, p, false);
        var opened = Document(); opened.Table(new[] { available }, "", 1500, "e", false, p, false);
        var button = Assert.Single(opened.Rows, r => r.Actionable);
        Assert.True(button.Selected); Assert.True(button.Expandable);
        Assert.Equal(Assert.Single(closed.Rows, r => r.Actionable).Height, button.Height);
        var detail = Assert.Single(opened.Rows, r => r.Kind == CombatRowKind.Metric);
        Assert.True(detail.Y >= button.Y + button.Height); Assert.False(detail.Actionable); Assert.False(detail.Selected);
        Assert.False(CombatLayoutPolicy.Muted(detail, 0)); Assert.False(CombatLayoutPolicy.Muted(detail, 1));
    }
    [Fact]
    public void IncomingSortUsesNumbersRetainsTotalAndSwitchesColumnDirections()
    {
        var projection = Projection(); projection.Combat.Lifetime.Totals.DamageReceived = 110;
        Attackers(projection, Row("a", "Alpha", incoming: 10, deaths: 2), Row("z", "Zulu", incoming: 100, deaths: 1));
        var p = Present(projection); var sort = new CombatIncomingSort();
        Assert.Equal(new[] { "z", "a" }, sort.Apply(p.Attackers).Select(r => r.Id));
        sort.Toggle(1); Assert.False(sort.Descending); Assert.Equal(new[] { "a", "z" }, sort.Apply(p.Attackers).Select(r => r.Id));
        sort.Toggle(3); Assert.True(sort.Descending); Assert.Equal(new[] { "a", "z" }, sort.Apply(p.Attackers).Select(r => r.Id));
        sort.Toggle(3); Assert.Equal(new[] { "z", "a" }, sort.Apply(p.Attackers).Select(r => r.Id));
        sort.Toggle(0); Assert.Equal(new[] { "z", "a" }, sort.Apply(p.Attackers).Select(r => r.Id));
        sort.Toggle(0); Assert.Equal(new[] { "a", "z" }, sort.Apply(p.Attackers).Select(r => r.Id));
        sort.Toggle(2); Assert.Equal(new[] { "z", "a" }, sort.Apply(p.Attackers).Select(r => r.Id));
        var d = Document(); d.Table(p.Attackers, "", 1500, null, true, p, false, sort);
        Assert.Equal(new[] { "total", "z", "a" }, d.Rows.Where(r => r.Kind == CombatRowKind.Table).Select(r => r.Id));
        Assert.Equal(4, d.Rows.Count(r => r.Kind == CombatRowKind.TableHeader && r.Actionable));
        Assert.StartsWith("↓ ", d.Rows.Single(r => r.Id == "sort:2").Cells[0], StringComparison.Ordinal);
    }
    [Fact]
    public void IncomingSortKeepsUnavailableLastAndLargeDeathCountsExact()
    {
        var rows = new[] { new CombatTableRow("missing", "A", Array.Empty<CombatValue>()),
            new CombatTableRow("low", "B", Array.Empty<CombatValue>(), deaths: 9007199254740992),
            new CombatTableRow("high", "C", Array.Empty<CombatValue>(), deaths: 9007199254740993) };
        var sort = new CombatIncomingSort(); sort.Toggle(3);
        Assert.Equal(new[] { "high", "low", "missing" }, sort.Apply(rows).Select(r => r.Id));
        sort.Toggle(3); Assert.Equal(new[] { "low", "high", "missing" }, sort.Apply(rows).Select(r => r.Id));
        var state = new CombatSelection(); state.Refresh(Present(Projection()));
        Assert.True(state.SortIncoming("g", 3)); state.SortIncoming("g", 3); state.Refresh(Present(Projection()));
        Assert.Equal(3, state.IncomingSort.Column); Assert.False(state.IncomingSort.Descending);
        Assert.False(state.SortIncoming("stale", 0)); Assert.False(state.SortIncoming("g", 9));
        state.Refresh(Present(Projection("new"))); Assert.Equal(1, state.IncomingSort.Column); Assert.True(state.IncomingSort.Descending);
    }
    [Fact]
    public void MeasuredHeadersFitOneLineAndShareColumnOriginsWithValues()
    {
        var p = Present(Projection()); var d = Document(); d.Table(p.Attackers, "", 1500, null, true, p, false);
        var headers = d.Rows.Where(r => r.Kind == CombatRowKind.TableHeader).ToArray();
        var total = d.Rows.Single(r => r.Kind == CombatRowKind.Table);
        Assert.False(total.Stacked); Assert.NotNull(total.Columns);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(headers[i].Cells[0].Length * CombatLayoutPolicy.TableHeaderSize * .5 <= headers[i].Width - 30);
            Assert.Equal(total.X + total.Columns.Take(i).Sum(), headers[i].X, 3);
            Assert.Equal(CombatLayoutPolicy.TableHeaderSize + 24, headers[i].Height);
        }
        var narrow = Document(); narrow.Table(p.Attackers, "", 600, null, true, p, true);
        Assert.True(narrow.Rows.Single(r => r.Kind == CombatRowKind.Table).Stacked);
        Assert.Equal(4, narrow.Rows.Count(r => r.Actionable));
    }
    [Fact]
    public void GlyphBottomAlignmentAccountsForDifferentFontDescendersAndSelectedItemsStayWhite()
    {
        var suffixY = CombatLayoutPolicy.AlignGlyphBottom(0, -38, -18);
        Assert.Equal(-38, suffixY - 18);
        foreach (var selected in new[] { false, true })
            for (var i = 0; i < 3; i++) Assert.Equal(!selected && i > 0, CombatLayoutPolicy.Muted(new CombatRenderRow { Kind = CombatRowKind.Item, Selected = selected }, i));
    }
}
