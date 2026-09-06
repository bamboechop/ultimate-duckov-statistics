using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

#pragma warning disable CA1861
public sealed class CombatWeaponDetailsTests
{
    private static StatisticsPanelProjection Projection()
    {
        var profile = new ProfileDocument { GenerationId = "g", Statistics = new ProfileStatistics { SaveGenerationId = "g" } };
        var p = new StatisticsPanelProjection { Profile = profile };
        p.Combat.Lifetime = profile.Statistics.RunTotals.CombatStatistics;
        p.Weapons.Lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        foreach (var caps in new object[] { p.Combat.Capabilities, p.Weapons.Capabilities })
            foreach (var property in caps.GetType().GetProperties())
                if (property.GetValue(caps) is MetricAvailability value) value.State = AdapterCapabilityState.Supported;
        return p;
    }
    private static void Fire(StatisticsPanelProjection p, string id, long actions = 8)
    {
        p.Weapons.Lifetime.Totals.FiringActions += actions;
        p.WeaponAmmunitionGroups = p.WeaponAmmunitionGroups.Append(new WeaponAmmunitionGroupProjection {
            WeaponId = id, DisplayName = "Same name", TotalFiringActions = actions, CorrelatedFiringActions = actions,
            Ammunition = new[] { new WeaponAmmunitionPairView { Pair = new WeaponAmmunitionPairAggregate {
                WeaponId = id, AmmunitionId = "ammo:1", AmmunitionDisplayName = "Ammo", FiringActions = actions }, PercentageWithinObservedWeaponPairs = 100 } }
        }).ToArray();
    }
    private static CombatMetricTotals Combat(StatisticsPanelProjection p, string id, bool melee = false)
    {
        var n = new CombatMetricTotals { RangedHits = melee ? 0 : 5, Headshots = melee ? 0 : 3, HeadshotFinalBlows = melee ? 0 : 1,
            MeleeSwings = melee ? 7 : 0, MeleeHits = melee ? 4 : 0, KillsByYou = 2, DamageDealt = 123.5,
            DamageCaused = 999, DamageReceived = 888, ObservedWorldDeaths = 777,
            PlayerKills = melee ? new PlayerKillPartition { Melee = 2 } : new PlayerKillPartition { Ranged = 2 } };
        p.Combat.Lifetime.Weapons[id] = new CombatBreakdownAggregate { Id = id, DisplayName = "Same name", Totals = n };
        return n;
    }
    private static CombatPresentation Present(StatisticsPanelProjection p)
    { p.CombatBinding = new CombatProjectionBinding(p); return CombatPresentationFactory.Create(p, "g")!; }
    private static CombatValue Value(CombatWeapon w, string label) => Assert.Single(w.Metrics, m => m.Label == label).Value;

    private static void Throwable(StatisticsPanelProjection p, string id = "duckov:item:67", long count = 3)
    {
        p.Profile.Statistics.Items[id] = new ItemAggregate { ItemId = id, DisplayName = "Grenade",
            EffectTags = new() { ItemEffectTag.Throwable }, Totals = new() { ActivationCount = count } };
        p.Profile.Capabilities.Add(new CapabilityRecord { AdapterId = "throwable-releases", State = AdapterCapabilityState.Supported });
    }

    [Fact]
    public void IndependentFiringAndThrowableEvidenceNeverSubstituteEachOthersActionCounts()
    {
        var p = Projection(); Throwable(p); Fire(p, "duckov:weapon:67", 8); Combat(p, "duckov:weapon:67");
        var weapon = Assert.Single(Present(p).Weapons);
        Assert.StartsWith("3", Value(weapon, "Throws / uses").Text, StringComparison.Ordinal);
        Assert.Equal("8", Value(weapon, "Firing actions").Text);
        Assert.Equal("8", Assert.Single(weapon.Ammunition).Actions.Text);
        Assert.DoesNotContain("Not applicable to throwables", weapon.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowableUseJoinsOnlyTheSameNativeIdAndKeepsRecordedCoverageVisible()
    {
        var p = Projection(); Throwable(p);
        p.Combat.Lifetime.Weapons["duckov:weapon:67"] = new CombatBreakdownAggregate { Id = "duckov:weapon:67", DisplayName = "Grenade",
            Totals = new() { DamageDealt = 77.86, KillsByYou = 1 } };
        var weapon = Assert.Single(Present(p).Weapons);
        Assert.Equal(new[] { "Throws / uses", "Kills by you", "Damage dealt" }, weapon.Metrics.Select(m => m.Label));
        Assert.StartsWith("3", Value(weapon, "Throws / uses").Text, StringComparison.Ordinal);
        Assert.Equal(CombatEvidence.Partial, Value(weapon, "Throws / uses").Evidence);
        Assert.Equal("77.86", Value(weapon, "Damage dealt").Text); Assert.Equal("1", Value(weapon, "Kills by you").Text);
        Assert.Empty(weapon.Ammunition); Assert.False(weapon.HasRangedEvidence);
        Assert.Contains("Not applicable to throwables", weapon.Notice, StringComparison.Ordinal);
    }
    [Fact]
    public void ThrowThatHitsNothingStillEntersCombatWithoutInventingCombatEvidence()
    {
        var p = Projection(); Throwable(p);
        var weapon = Assert.Single(Present(p).Weapons);
        Assert.Equal("duckov:weapon:67", weapon.Row.Id); Assert.Equal("Grenade", weapon.Row.Name);
        Assert.Equal("Throws / uses", weapon.ActionLabel);
        Assert.Equal(CombatEvidence.Unavailable, Value(weapon, "Kills by you").Evidence);
        Assert.Equal(CombatEvidence.Unavailable, Value(weapon, "Damage dealt").Evidence);
    }
    [Fact]
    public void NativeHistoricalThrowableClassificationDoesNotInventPastThrows()
    {
        var p = Projection();
        p.Combat.Lifetime.Weapons["duckov:weapon:67"] = new CombatBreakdownAggregate { Id = "duckov:weapon:67", DisplayName = "Grenade", Totals = new() { DamageDealt = 4 } };
        p.CombatBinding = new CombatProjectionBinding(p);
        var weapon = Assert.Single(CombatPresentationFactory.Create(p, "g", isThrowable: id => id == "duckov:weapon:67")!.Weapons);
        Assert.Equal(CombatEvidence.Unavailable, Value(weapon, "Throws / uses").Evidence);
        Assert.Contains("Not applicable to throwables", weapon.Notice, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("duckov:item:unknown")]
    [InlineData("mod:item:67")]
    [InlineData("duckov:item:067")]
    public void UnknownForeignAndNoncanonicalThrowableIdsCannotSupplyNativeCounts(string id)
    {
        var p = Projection(); Throwable(p, id);
        p.Combat.Lifetime.Weapons["duckov:weapon:67"] = new CombatBreakdownAggregate { Id = "duckov:weapon:67", DisplayName = "Grenade", Totals = new() { DamageDealt = 4 } };
        p.CombatBinding = new CombatProjectionBinding(p);
        var weapon = Assert.Single(CombatPresentationFactory.Create(p, "g", isThrowable: key => key == "duckov:weapon:67")!.Weapons);
        Assert.Equal(CombatEvidence.Unavailable, Value(weapon, "Throws / uses").Evidence);
    }
    [Fact]
    public void DisabledThrowableCapabilityDoesNotTurnRecordedCountIntoSupportedZero()
    {
        var p = Projection(); Throwable(p);
        p.Profile.Capabilities.Single(c => c.AdapterId == "throwable-releases").State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal(CombatEvidence.Partial, Value(Assert.Single(Present(p).Weapons), "Throws / uses").Evidence);
        Assert.Equal(3, p.Profile.Statistics.Items["duckov:item:67"].Totals.ActivationCount);
    }

    [Fact]
    public void ExactRangedJoinKeepsPlayerCountersAndExistingAmmoActions()
    {
        var p = Projection(); Fire(p, "weapon:a"); Combat(p, "weapon:a");
        p.Weapons.Lifetime.Totals.AmmunitionUnitsConsumed = 1000; p.Weapons.Lifetime.Totals.Projectiles = 2000;
        var weapon = Assert.Single(Present(p).Weapons);
        Assert.Equal(new[] { "Firing actions", "Hits", "Headshots", "Headshot final blows", "Kills by you", "Damage dealt" }, weapon.Metrics.Select(m => m.Label));
        Assert.Equal(new[] { "8", "5", "3", "1", "2", "123.5" }, weapon.Metrics.Select(m => m.Value.Text));
        Assert.All(weapon.Metrics, m => Assert.Equal(CombatEvidence.Supported, m.Value.Evidence));
        Assert.Equal("8", Assert.Single(weapon.Ammunition).Actions.Text); Assert.Equal("100%", weapon.Ammunition[0].Percentage.Text);
    }

    [Fact]
    public void SameNamesAndCaseDifferentIdsNeverJoin()
    {
        var p = Projection(); Fire(p, "weapon:A"); Combat(p, "weapon:a");
        var weapons = Present(p).Weapons; Assert.Equal(2, weapons.Count);
        Assert.Equal(CombatEvidence.Unavailable, Value(weapons[0], "Hits").Evidence);
        Assert.Equal("5", Value(weapons[1], "Hits").Text);
        Assert.Equal(CombatEvidence.Unavailable, Value(weapons[1], "Firing actions").Evidence);
        Assert.Empty(weapons[1].Ammunition);
    }

    [Fact]
    public void MeleeOnlyWeaponAppearsAndExpandsWithoutRangedZeroes()
    {
        var p = Projection(); Combat(p, "weapon:axe", melee: true);
        p.Weapons.Capabilities.FiringActions.State = AdapterCapabilityState.DisabledIncompatible;
        var result = Present(p); var weapon = Assert.Single(result.Weapons); Assert.Empty(result.WeaponNotice);
        Assert.Equal(new[] { "Swings", "Hits", "Kills by you", "Damage dealt" }, weapon.Metrics.Select(m => m.Label));
        Assert.Equal(new[] { "7", "4", "2", "123.5" }, weapon.Metrics.Select(m => m.Value.Text));
        Assert.Empty(weapon.Ammunition); Assert.False(weapon.HasRangedEvidence); Assert.Contains("Not applicable", weapon.Notice);
        var selection = new CombatSelection(); selection.Refresh(result);
        var doc = new CombatDocument((_, _, size) => size, (s, size) => s.Length * size);
        doc.Items(selection, 800, false);
        Assert.Equal(4, doc.Rows.Count(r => r.Kind == CombatRowKind.Metric));
        Assert.Contains(doc.Rows, r => r.Kind == CombatRowKind.Item && r.Cells[1] == "Swings: 7");
        var ammoDoc = new CombatDocument((_, _, size) => size, (s, size) => s.Length * size);
        ammoDoc.Items(selection, 800, true);
        Assert.DoesNotContain(ammoDoc.Rows, r => r.Kind == CombatRowKind.Item);
        Assert.Contains(ammoDoc.Rows, r => r.Cells.Any(c => c.Contains("Not applicable", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(AdapterCapabilityState.DisabledIncompatible, 0, CombatEvidence.Unavailable)]
    [InlineData(AdapterCapabilityState.Experimental, 5, CombatEvidence.Partial)]
    [InlineData(AdapterCapabilityState.Supported, 0, CombatEvidence.Supported)]
    public void MetricAndIdentityCapabilitiesQualifyEachCounter(AdapterCapabilityState state, int hits, object expected)
    {
        var p = Projection(); Fire(p, "w"); var n = Combat(p, "w"); n.RangedHits = hits;
        p.Combat.Capabilities.RangedHits.State = state;
        var weapon = Assert.Single(Present(p).Weapons);
        Assert.Equal((CombatEvidence)expected, Value(weapon, "Hits").Evidence);
        Assert.Equal(CombatEvidence.Supported, Value(weapon, "Damage dealt").Evidence);
        p.Combat.Capabilities.WeaponIdentity.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal(CombatEvidence.Partial, Value(Assert.Single(Present(p).Weapons), "Damage dealt").Evidence);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duckov:weapon:unknown")]
    [InlineData("")]
    public void UnknownSourcesRemainSeparateAndNeverSupplyNamedWeaponMetrics(string unknown)
    {
        var p = Projection(); Fire(p, "known"); Combat(p, "known"); Fire(p, unknown); Combat(p, unknown);
        var weapons = Present(p).Weapons; Assert.Equal(3, weapons.Count);
        var known = Assert.Single(weapons, w => w.Row.Id == "known");
        Assert.Equal("5 (" + UiText.Get("ui.combat_partial") + ")", Value(known, "Hits").Text);
        Assert.Equal("123.5 (" + UiText.Get("ui.combat_partial") + ")", Value(known, "Damage dealt").Text);
        foreach (var row in weapons.Where(w => w.Row.Id != "known"))
        {
            Assert.Equal(CombatEvidence.Unavailable, Value(row, "Damage dealt").Evidence);
            Assert.Empty(row.Ammunition);
        }
    }

    [Fact]
    public void HistoricalOwnershipAndRepairedEvidenceNeverTurnIntoSupportedZero()
    {
        var p = Projection(); Fire(p, "w"); var n = Combat(p, "w");
        n.PlayerKills.HistoricalIncomplete = true;
        Assert.Equal(CombatEvidence.Partial, Value(Assert.Single(Present(p).Weapons), "Kills by you").Evidence);
        n.KillsByYou = 0;
        Assert.Equal(CombatEvidence.Unavailable, Value(Assert.Single(Present(p).Weapons), "Kills by you").Evidence);
        p.Combat.Lifetime.WasRepairedFromInvalidState = true;
        Assert.Equal(CombatEvidence.Partial, Value(Assert.Single(Present(p).Weapons), "Damage dealt").Evidence);
    }

    [Fact]
    public void MixedProvenActivityRetainsBothHitCountersAndDamageOnlyIdentityStaysUnclassified()
    {
        var p = Projection(); Fire(p, "mixed"); Combat(p, "mixed", melee: true);
        var weapon = Assert.Single(Present(p).Weapons);
        Assert.Equal("0", Value(weapon, "Ranged hits").Text); Assert.Equal("4", Value(weapon, "Melee hits").Text);
        var n = Combat(p, "unclassified"); n.RangedHits = n.Headshots = n.HeadshotFinalBlows = 0; n.PlayerKills = new(); n.KillsByYou = 0;
        weapon = Assert.Single(Present(p).Weapons, w => w.Row.Id == "unclassified");
        Assert.False(weapon.HasRangedEvidence); Assert.Equal(2, weapon.Metrics.Count); Assert.Contains("type: Unavailable", weapon.Notice);
    }

    [Fact]
    public void ReplacedCombatWeaponPublicationFailsBinding()
    {
        var p = Projection(); Combat(p, "w", melee: true); _ = Present(p);
        p.Combat.Lifetime.Weapons = new(); Assert.Null(CombatPresentationFactory.Create(p, "g"));
    }

    [Fact]
    public void MissingCombatRecordIsUnavailableAndPairingHistoryDoesNotEraseExactCombatCounters()
    {
        var p = Projection(); Fire(p, "w");
        var weapon = Assert.Single(Present(p).Weapons);
        Assert.All(weapon.Metrics.Skip(1), m => Assert.Equal(CombatEvidence.Unavailable, m.Value.Evidence));
        Combat(p, "w"); p.WeaponAmmunitionGroups[0].HistoricalPairingUnavailable = true;
        weapon = Assert.Single(Present(p).Weapons);
        Assert.Equal(CombatEvidence.Supported, Value(weapon, "Hits").Evidence);
        Assert.Contains(UiText.Get("ui.combat_pair_history"), weapon.Notice);
    }

    [Fact]
    public void MismatchedStoredRowIdentityCannotBeJoinedEvenWhenDictionaryKeyMatches()
    {
        var p = Projection(); Fire(p, "w"); Combat(p, "w"); p.Combat.Lifetime.Weapons["w"].Id = "other";
        var weapons = Present(p).Weapons;
        Assert.Equal(2, weapons.Count);
        Assert.All(weapons, weapon => Assert.Equal(CombatEvidence.Unavailable, Value(weapon, "Damage dealt").Evidence));
    }

    [Fact]
    public void ProductionProjectionPublishesClonedCombatAlongsideFiringAndMeleeOnlyRows()
    {
        var p = Projection(); var profile = p.Profile;
        var gun = Combat(p, "duckov:weapon:1"); gun.CompletedPlayerProjectiles = 8;
        Combat(p, "duckov:weapon:2", melee: true);
        var a = p.Combat.Lifetime;
        a.Capabilities = CombatNativeContractPolicy.CreateSupportedCapabilities();
        profile.Capabilities = CombatNativeContractPolicy.ToRecords(a.Capabilities, "test").ToList();
        var firing = p.Weapons.Lifetime; firing.Capabilities = WeaponNativeContractPolicy.CreateMetricCapabilities();
        foreach (var id in WeaponCapabilityIds.All)
            profile.Capabilities.Add(new CapabilityRecord { AdapterId = id, State = AdapterCapabilityState.Supported });
        firing.Totals.FiringActions = 8;
        firing.Weapons["duckov:weapon:1"] = new WeaponAggregate { WeaponId = "duckov:weapon:1", DisplayName = "Gun", Totals = new WeaponMetricTotals { FiringActions = 8 } };
        firing.UncorrelatedFiringActions = 8; firing.UncorrelatedWeaponFiringActions["duckov:weapon:1"] = 8;
        var projection = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
        Assert.NotSame(a, projection.Combat.Lifetime);
        var result = CombatPresentationFactory.Create(projection, "g")!;
        Assert.Equal(2, result.Weapons.Count);
        Assert.Equal("5", Value(result.Weapons[0], "Hits").Text);
        Assert.Equal("7", Value(result.Weapons[1], "Swings").Text);
        gun.RangedHits = 99;
        Assert.Equal("5", Value(result.Weapons[0], "Hits").Text);
    }

    [Fact]
    public void NpcAndIncomingOnlyWeaponRowsDoNotEnterPlayerWeaponList()
    {
        var p = Projection();
        foreach (var id in new[] { "duckov:weapon:258", "duckov:weapon:356", "duckov:weapon:305", "duckov:weapon:unknown" })
            p.Combat.Lifetime.Weapons[id] = new CombatBreakdownAggregate { Id = id, DisplayName = "Attacker weapon",
                Totals = new CombatMetricTotals { DamageCaused = 205, DamageReceived = 121, PlayerDeaths = 2, ObservedWorldDeaths = 3,
                    LegacyUnclassifiedDeaths = 1, PlayerKills = new PlayerKillPartition { HistoricalIncomplete = true } } };
        Assert.Empty(Present(p).Weapons);
        Combat(p, "player:axe", melee: true);
        Assert.Equal("player:axe", Assert.Single(Present(p).Weapons).Row.Id);
        Fire(p, "duckov:weapon:258");
        Assert.Equal(2, Present(p).Weapons.Count); // Accepted player firing is independently sufficient.
        Assert.Equal(4, p.Combat.Lifetime.Weapons.Count(row => row.Key.StartsWith("duckov:", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void EachRecordedPlayerCounterCanIncludeACombatOnlyWeapon(int counter)
    {
        var p = Projection(); var totals = new CombatMetricTotals();
        switch (counter)
        {
            case 0: totals.DamageDealt = 3; break;
            case 1: totals.KillsByYou = 1; break;
            case 2: totals.CompletedPlayerProjectiles = 1; break;
            case 3: totals.RangedHits = 1; break;
            case 4: totals.MeleeSwings = 1; break;
            case 5: totals.MeleeHits = 1; break;
            case 6: totals.Headshots = 1; break;
            case 7: totals.HeadshotFinalBlows = 1; break;
        }
        p.Combat.Lifetime.Weapons["w"] = new CombatBreakdownAggregate { Id = "w", DisplayName = "Weapon", Totals = totals };
        var result = Present(p); var weapon = Assert.Single(result.Weapons);
        if (counter < 2)
        {
            Assert.Equal(counter == 0 ? "Damage dealt" : "Kills by you", weapon.ActionLabel);
            Assert.Equal(counter == 0 ? "3" : "1", weapon.Row.Actions.Text);
        }
    }
}
