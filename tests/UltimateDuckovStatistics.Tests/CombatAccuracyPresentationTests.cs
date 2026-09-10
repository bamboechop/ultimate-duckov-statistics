using System.Globalization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class CombatAccuracyPresentationTests
{
    [Theory]
    [InlineData(3, 1, 0, 0, "33.33%", "Unavailable")]
    [InlineData(0, 0, 3, 1, "Unavailable", "33.33%")]
    [InlineData(9, 1, 1, 1, "11.11%", "100%")]
    [InlineData(2, 2, 6, 2, "100%", "33.33%")]
    [InlineData(2, 0, 3, 0, "0%", "0%")]
    [InlineData(0, 0, 0, 0, "Unavailable", "Unavailable")]
    public void ProductionRunAndLifetimeUseCombinedCountsAndIndependentBreakdowns(
        int projectiles, int rangedHits, int swings, int meleeHits, string rangedText, string meleeText)
    {
        var session = new Session();
        for (var i = 0; i < projectiles; i++) session.Fire("42", "1", i < rangedHits ? 1 : 0);
        for (var i = 0; i < swings; i++) session.Swing(i < meleeHits);
        var profile = session.Complete();
        var before = StatisticsExporter.Create(profile, Session.Now);
        AssertPresentation(profile);
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(directory.Path, "combat-profile.json");
        store.Save(path, profile);
        var loaded = store.Load(path).Value!;
        AssertPresentation(loaded);
        var after = StatisticsExporter.Create(loaded, Session.Now);
        Assert.Equal(before.Json, after.Json);
        Assert.Equal(before.CombatAttributionCsv, after.CombatAttributionCsv);
        var csv = before.CombatAttributionCsv.Split('\n');
        var accuracyColumn = Array.IndexOf(csv[0].Trim().Split(','), "accuracy");
        var lifetime = csv.First(line => line.StartsWith("lifetime,", StringComparison.Ordinal)).Split(',');
        Assert.Equal(projectiles == 0 ? "" : ((double)rangedHits / projectiles).ToString(CultureInfo.InvariantCulture), lifetime[accuracyColumn]);

        void AssertPresentation(ProfileDocument data)
        {
            var p = Project(data);
            var combat = CombatPresentationFactory.Create(p, "g")!;
            var run = Assert.Single(RunsPresentationFactory.Create(p, "g")!.Runs);
            var attempts = projectiles + swings;
            var expected = attempts == 0 ? (double?)null : (double)(rangedHits + meleeHits) / attempts;
            var runExpected = expected?.ToString("P2", CultureInfo.InvariantCulture) ?? "Unavailable";
            Assert.Equal(runExpected, Assert.Single(run.Summary, row => row.Key == "Accuracy").Value);
            Assert.Equal(expected == null ? "Unavailable" : (expected.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%",
                Value(combat.Overall, "Overall accuracy").Text);
            Assert.Equal(rangedText, Value(combat.Ranged, "Ranged accuracy").Text);
            Assert.Equal(meleeText, Value(combat.Melee, "Melee accuracy").Text);
            Assert.Contains("Ranged accuracy: " + (projectiles == 0 ? "Unavailable" : ((double)rangedHits / projectiles).ToString("P2", CultureInfo.InvariantCulture)), run.Ranged, StringComparison.Ordinal);
            Assert.Contains("Melee accuracy: " + (swings == 0 ? "Unavailable" : ((double)meleeHits / swings).ToString("P2", CultureInfo.InvariantCulture)), run.Melee, StringComparison.Ordinal);
            Assert.Equal(projectiles == 0 ? (double?)null : (double)rangedHits / projectiles, p.Combat.Accuracy);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void UnusedFamilyRequiresKnownZeroAttemptsRatherThanMissingEvidence(bool meleeOnly, bool attemptsKnown)
    {
        var session = new Session(c =>
        {
            if (meleeOnly) { c.Accuracy.State = Disabled; c.RangedHits.State = Disabled; }
            else { c.MeleeHits.State = Disabled; if (!attemptsKnown) c.MeleeSwings.State = Disabled; }
        }, w => { if (meleeOnly && !attemptsKnown) w.FiringActions.State = Disabled; });
        if (meleeOnly) { session.Swing(false); session.Swing(true); session.Swing(false); }
        else { session.Fire("42", "1", 1); session.Fire("42", "1", 0); session.Fire("42", "1", 0); }
        var p = Project(session.Complete());
        var combat = CombatPresentationFactory.Create(p, "g")!;
        Assert.Equal(attemptsKnown ? "33.33%" : "Unavailable", Value(combat.Overall, "Overall accuracy").Text);
        Assert.Equal("33.33%", Value(meleeOnly ? combat.Melee : combat.Ranged, meleeOnly ? "Melee accuracy" : "Ranged accuracy").Text);
        Assert.Equal(attemptsKnown ? "33.33 %" : "Unavailable",
            Assert.Single(RunsPresentationFactory.Create(p, "g")!.Runs[0].Summary, row => row.Key == "Accuracy").Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HookLossAfterActivityDoesNotInventCompleteRatiosOrSuppressSibling(bool rangedLost)
    {
        var session = new Session(); session.Fire("42", "1", 1); session.Swing(true);
        session.Degrade(c =>
        {
            if (rangedLost) { c.Accuracy.State = Disabled; c.RangedHits.State = Disabled; }
            else c.MeleeHits.State = Disabled;
        });
        // Missing release suppresses projectile completions; a missing melee damage scope still allows public swings.
        if (rangedLost) session.FiringAction("42", "1"); else session.Swing(false);
        var p = Project(session.Complete());
        var combat = CombatPresentationFactory.Create(p, "g")!;
        Assert.Equal("Unavailable", Value(combat.Overall, "Overall accuracy").Text);
        Assert.Equal("Unavailable", Value(rangedLost ? combat.Ranged : combat.Melee, rangedLost ? "Ranged accuracy" : "Melee accuracy").Text);
        Assert.Equal("100%", Value(rangedLost ? combat.Melee : combat.Ranged, rangedLost ? "Melee accuracy" : "Ranged accuracy").Text);
        Assert.Contains("partial", Value(rangedLost ? combat.Ranged : combat.Melee, "Hits").Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Unavailable", Assert.Single(RunsPresentationFactory.Create(p, "g")!.Runs[0].Summary, row => row.Key == "Accuracy").Value);
    }

    [Fact]
    public void RepairedCombatRatiosRemainUnavailableWithRetainedCounts()
    {
        var session = new Session(); session.Fire("42", "1", 1); session.Swing(true);
        var profile = session.Complete();
        foreach (var a in new[] { profile.Statistics.RunTotals.CombatStatistics, profile.Statistics.Runs[0].CombatStatistics })
        {
            a.Totals.DamageReceived = double.NaN;
            CombatStatisticsReducer.NormalizePersisted(a);
            Assert.True(a.WasRepairedFromInvalidState);
        }
        var p = Project(profile); var combat = CombatPresentationFactory.Create(p, "g")!;
        Assert.Equal("Unavailable", Value(combat.Overall, "Overall accuracy").Text);
        Assert.Equal("Unavailable", Value(combat.Ranged, "Ranged accuracy").Text);
        Assert.Equal("Unavailable", Value(combat.Melee, "Melee accuracy").Text);
        Assert.Equal("Unavailable", Value(combat.Weapons.Single(w => w.Row.Id == "duckov:weapon:42").Metrics, "Accuracy").Text);
        Assert.Equal("Unavailable", Assert.Single(RunsPresentationFactory.Create(p, "g")!.Runs[0].Summary, row => row.Key == "Accuracy").Value);
        Assert.Equal(1, p.Combat.Lifetime.Totals.RangedHits);
    }

    [Fact]
    public void WeaponDetailsJoinExactWeaponAcrossAmmoChangesAndKeepUsageShares()
    {
        var session = new Session();
        session.Fire("42", "1", 2, 2); // One action, two projectiles, both hit.
        session.Fire("43", "1", 0);
        session.Fire("42", "2", 1);
        var p = Project(session.Complete()); var combat = CombatPresentationFactory.Create(p, "g")!;
        var first = combat.Weapons.Single(w => w.Row.Id == "duckov:weapon:42");
        var second = combat.Weapons.Single(w => w.Row.Id == "duckov:weapon:43");
        Assert.Equal(CombatEvidence.Supported, Value(first.Metrics, "Hits").Evidence);
        Assert.Equal(CombatEvidence.Supported, Value(first.Metrics, "Firing actions").Evidence);
        Assert.Equal("150%", Value(first.Metrics, "Accuracy").Text);
        Assert.Equal("0%", Value(second.Metrics, "Accuracy").Text);
        Assert.All(first.Ammunition, row => Assert.Equal("50%", row.Percentage.Text));
        Assert.Equal("100%", Assert.Single(second.Ammunition).Percentage.Text);
        Assert.Equal("75%", Value(combat.Ranged, "Ranged accuracy").Text);
        Assert.Contains("Multiple projectiles", first.Notice, StringComparison.Ordinal);
        var selection = new CombatSelection(); selection.Refresh(combat);
        Assert.True(selection.SelectWeapon("g", first.Row.Id));
        Assert.True(selection.ToggleWeaponDetails("g", first.Row.Id));
        var document = new CombatDocument((_, _, _) => 30, (text, _) => text.Length * 10, UiText.Get);
        document.Items(selection, 1000, true);
        Assert.Contains(document.Rows, row => row.Kind == CombatRowKind.Metric && row.Cells[0] == "Accuracy" && row.Cells[1] == "150%");
        Assert.True(selection.SelectWeapon("g", second.Row.Id));
        Assert.Equal("0%", Value(selection.Weapon!.Metrics, "Accuracy").Text);
        var other = new Session(generation: "next"); other.Fire("42", "1", 0);
        selection.Refresh(CombatPresentationFactory.Create(Project(other.Complete()), "next"));
        Assert.False(selection.SelectWeapon("g", first.Row.Id));
        Assert.Null(CombatPresentationFactory.Create(p, "next"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingWeaponAttributionMakesRatioUnavailable(bool missingHits)
    {
        var session = new Session();
        session.FiringAction(missingHits ? "42" : "unknown", "1");
        session.Projectile(missingHits ? "unknown" : "42", "1", true);
        var combat = CombatPresentationFactory.Create(Project(session.Complete()), "g")!;
        Assert.All(combat.Weapons.Where(weapon => weapon.HasRangedEvidence), weapon => Assert.Equal("Unavailable", Value(weapon.Metrics, "Accuracy").Text));
    }

    [Fact]
    public void EffectPartitionKeepsPlayerCreditAndOtherPartitionsVisible()
    {
        var session = new Session();
        foreach (var kind in new[] { CombatAttackKind.Ranged, CombatAttackKind.Melee, CombatAttackKind.Throwable,
                     CombatAttackKind.Effect, CombatAttackKind.Unknown }) session.Kill(kind, true);
        session.Kill(CombatAttackKind.Effect, false);
        var profile = session.Complete(); var export = StatisticsExporter.Create(profile, Session.Now);
        var combat = CombatPresentationFactory.Create(Project(profile), "g")!;
        Assert.Equal("5", Value(combat.Overall, "Kills by you").Text);
        Assert.Equal("1", combat.WorldTotal.Text);
        Assert.Equal("1", Value(combat.OtherPlayerKills, "Effects / damage-over-time kills").Text);
        Assert.DoesNotContain(combat.OtherPlayerKills, row => row.Label == "Environmental kills");
        Assert.Equal("1", Value(combat.OtherPlayerKills, "Unclassified kills").Text);
        Assert.Equal("1", Value(combat.Ranged, "Kills").Text);
        Assert.Equal("1", Value(combat.Melee, "Kills").Text);
        Assert.Equal("1", Value(combat.Throwables, "Kills").Text);
        Assert.Equal(export.Json, StatisticsExporter.Create(profile, Session.Now).Json);
        var document = new CombatDocument((_, _, _) => 30, (text, _) => text.Length * 10, UiText.Get);
        document.Summary(combat, 1500, false);
        Assert.Contains(document.Rows, row => row.Kind == CombatRowKind.Metric && row.Cells[0] == "Effects / damage-over-time kills");
    }

    private const AdapterCapabilityState Disabled = AdapterCapabilityState.DisabledIncompatible;
    private static CombatValue Value(IReadOnlyList<CombatMetric> values, string label) => Assert.Single(values, value => value.Label == label).Value;
    private static StatisticsPanelProjection Project(ProfileDocument profile) => StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());

    private sealed class Session
    {
        public static readonly DateTime Now = new(2026, 9, 10, 17, 0, 0, DateTimeKind.Utc);
        private readonly CombatMetricCapabilities combat = CombatNativeContractPolicy.CreateSupportedCapabilities();
        private readonly WeaponMetricCapabilities weapons = WeaponNativeContractPolicy.CreateMetricCapabilities();
        private readonly RunLifecycleTracker tracker = new(() => "r");
        private readonly string generation;
        private int sequence;

        public Session(Action<CombatMetricCapabilities>? configure = null, Action<WeaponMetricCapabilities>? firing = null, string generation = "g")
        {
            this.generation = generation; configure?.Invoke(combat); firing?.Invoke(weapons);
            tracker.Apply(Lifecycle(RunLifecycleEventKind.RaidInitialized));
            var ready = Lifecycle(RunLifecycleEventKind.ControlReady);
            ready.StartContext = new RunStartContext
            {
                SaveGenerationId = generation,
                Map = new MapIdentity { MapId = "m", DisplayName = "Map", IsKnown = true },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "24013657",
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                CombatCapabilities = combat,
                WeaponCapabilities = weapons
            };
            tracker.Apply(ready);
        }
        public void Degrade(Action<CombatMetricCapabilities> change)
        { change(combat); Assert.True(tracker.UpdateCombatCapabilities(combat)); }
        public void Swing(bool hit)
        {
            Record(Event("99") with { AttackKind = CombatAttackKind.Melee, MeleeSwings = 1 });
            if (hit) Record(Event("99") with { AttackKind = CombatAttackKind.Melee, MeleeHits = 1, ActualDamageDealt = 10, ActualDamageToTarget = 10 });
        }
        public void Fire(string weapon, string ammo, int hits, int projectiles = 1)
        { FiringAction(weapon, ammo); for (var i = 0; i < projectiles; i++) Projectile(weapon, ammo, i < hits); }
        public void FiringAction(string weapon, string ammo) => Assert.True(tracker.RecordShot(new ShotRecorded
        {
            EventId = (++sequence).ToString(CultureInfo.InvariantCulture),
            TimestampUtc = Now,
            SaveGenerationId = generation,
            RunId = "r",
            MapId = "m",
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            WeaponId = "duckov:weapon:" + weapon,
            WeaponDisplayName = "Same name",
            AmmunitionId = "duckov:ammo:" + ammo,
            AmmunitionDisplayName = "Ammo",
            FiringActionCount = 1,
            Capabilities = weapons
        }));
        public void Projectile(string weapon, string ammo, bool hit)
        {
            if (hit) Record(Event(weapon) with
            {
                AttackKind = CombatAttackKind.Ranged,
                AmmunitionId = "duckov:ammo:" + ammo,
                ActualDamageDealt = 10,
                ActualDamageToTarget = 10
            });
            Record(Event(weapon) with
            {
                AttackKind = CombatAttackKind.Ranged,
                AmmunitionId = "duckov:ammo:" + ammo,
                CompletedPlayerProjectiles = 1,
                RangedHits = hit ? 1 : 0
            });
        }
        public void Kill(CombatAttackKind kind, bool player) => Record(Event("42") with
        {
            AttackKind = kind,
            Ownership = player ? CombatOwnership.Player : CombatOwnership.OtherNpc,
            KillsByYou = player ? 1 : 0,
            ObservedWorldDeaths = player ? 0 : 1,
            ActualDamageToTarget = 10,
            ActualDamageDealt = player ? 10 : 0
        });
        private void Record(CombatRecorded value) => Assert.True(tracker.RecordCombat(value));
        private CombatRecorded Event(string weapon) => new()
        {
            EventId = (++sequence).ToString(CultureInfo.InvariantCulture),
            TimestampUtc = Now,
            SaveGenerationId = generation,
            RunId = "r",
            MapId = "m",
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            Ownership = CombatOwnership.Player,
            WeaponId = "duckov:weapon:" + weapon,
            WeaponDisplayName = "Same name",
            TargetIsEnemy = true,
            TargetId = "enemy",
            TargetDisplayName = "Enemy",
            Capabilities = combat
        };
        public ProfileDocument Complete()
        {
            var run = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted)).Completed!;
            var profile = new ProfileDocument
            {
                GenerationId = generation,
                Slot = 1,
                CreatedUtc = Now,
                UpdatedUtc = Now,
                Identity = new SaveIdentitySnapshot { Slot = 1 },
                Statistics = new ProfileStatistics { SaveGenerationId = generation, CreatedUtc = Now, UpdatedUtc = Now }
            };
            RunReducer.Apply(profile.Statistics, run);
            profile.Capabilities = CombatNativeContractPolicy.ToRecords(combat, "test").ToList();
            foreach (var id in WeaponCapabilityIds.All) profile.Capabilities.Add(new CapabilityRecord { AdapterId = id, State = AdapterCapabilityState.Supported });
            return profile;
        }
        private static RunLifecycleEvent Lifecycle(RunLifecycleEventKind kind) => new()
        { Kind = kind, NativeRaidId = "raid", TimestampUtc = Now, MonotonicSeconds = kind == RunLifecycleEventKind.Extracted ? 10 : 0 };
    }
}
