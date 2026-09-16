using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    [Theory]
    [InlineData("Init")]
    [InlineData("Update")]
    [InlineData("Release")]
    public void EncounterProjectileDriftInvalidatesOldEvidenceAndPersistsCoverage(string method)
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        Assert.Null(observer.FailureIssue);
        LevelManager.Instance!.InputManager.AimingEnemyHead = true;
        EncounterHit(LaunchEncounterProjectile(), sink);
        var inFlight = LaunchEncounterProjectile();

        AddForeignPatchAndInspect(NativeMethod(typeof(Projectile), method));
        // Exercise a tap before observer.Tick: coverage and trust must already
        // apply, including to a projectile cached before patch drift.
        EncounterHit(inFlight, sink);
        EncounterHit(LaunchEncounterProjectile(), sink);
        EncounterMelee(sink);
        observer.Tick(sink.Context);

        var hits = sink.Rows.Where(row => row.Kind == "combat_hurt_complete").Select(row => row.Data).ToArray();
        Assert.Equal("projectile", (string?)hits[0]["Source"]!["Kind"]);
        Assert.True((bool)hits[0]["Headshot"]!);
        Assert.Equal("unscoped", (string?)hits[1]["Source"]!["Kind"]);
        Assert.False((bool)hits[1]["Headshot"]!);
        // Release failure invalidates old correlations, but fresh Init/Update
        // evidence remains usable for weapon, ammo and headshots.
        Assert.Equal(method == "Release" ? "projectile" : "unscoped", (string?)hits[2]["Source"]!["Kind"]);
        Assert.Equal(method == "Release", (bool)hits[2]["Headshot"]!);
        Assert.Equal("melee", (string?)hits[3]["Source"]!["Kind"]);
        Assert.Equal(EncounterCaptureIssue.CombatIncomplete, observer.FailureIssue);
        Assert.Single(sink.Rows, row => row.Kind == "combat_coverage");

        var records = sink.Flush();
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(records).CoverageNoticeKey);
        Assert.Single(records, row => row.Coverage?.Issue == EncounterCaptureIssue.CombatIncomplete);
        var fallback = records.Where(row => row.Damage?.Source.Mechanism == "unscoped").ToArray();
        Assert.NotEmpty(fallback);
        foreach (var row in fallback)
        {
            Assert.Equal(10, row.Damage!.Amount);
            Assert.Equal(42, row.Damage.Source.WeaponTypeId);
            Assert.Null(row.Damage.Source.AmmunitionTypeId);
            Assert.DoesNotContain(EncounterDamageText.Parts(row.Damage, _ => "item", key => key),
                part => part.Text.Contains("headshot", StringComparison.Ordinal));
        }
        Assert.Contains(records, row => row.Damage?.Source.Mechanism == "melee" && row.Damage.Amount == 10);
        // Exercise the actual record codec and presentation after reopening.
        var codec = new ProfileRecordCodec();
        var reopened = records.Select(row => ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(row))).ToArray();
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(reopened).CoverageNoticeKey);
    }

    [Fact]
    public void EncounterMeleeDriftFallsBackWithoutLosingTrustedProjectilesOrScopeBalance()
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        EncounterMelee(sink);
        AddForeignPatchAndInspect(NativeMethod(typeof(ItemAgent_MeleeWeapon), "CheckCollidersInRange"));
        EncounterMelee(sink);
        LevelManager.Instance!.InputManager.AimingEnemyHead = true;
        var projectile = LaunchEncounterProjectile();
        object?[] outer = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, outer);
        try
        {
            var trusted = NativeEncounterCombatObserver.ReadActiveSource();
            Assert.NotNull(trusted);
            object?[] inner = [new ItemAgent_MeleeWeapon { Holder = player, Item = new() { TypeID = 99 } }, true, null];
            CombatHarmonyCallbacks.MeleePrefixMethod.Invoke(null, inner);
            try { Assert.Null(NativeEncounterCombatObserver.ReadActiveSource()); }
            finally { CombatHarmonyCallbacks.MeleeFinalizerMethod.Invoke(null, [null, inner[2]]); }
            Assert.Same(trusted, NativeEncounterCombatObserver.ReadActiveSource());
            EncounterHurt(sink, 42);
        }
        finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, outer[1]]); }
        Assert.Null(NativeEncounterCombatObserver.ReadActiveSource());
        var hits = sink.Rows.Where(row => row.Kind == "combat_hurt_complete").Select(row => row.Data).ToArray();
        Assert.Equal("melee", (string?)hits[0]["Source"]!["Kind"]);
        Assert.Equal("unscoped", (string?)hits[1]["Source"]!["Kind"]);
        Assert.Equal("projectile", (string?)hits[2]["Source"]!["Kind"]);
        Assert.True((bool)hits[2]["Headshot"]!);
        Assert.Equal(EncounterCaptureIssue.CombatIncomplete, observer.FailureIssue);
        Assert.Single(sink.Flush(), row => row.Coverage != null);
    }

    [Theory]
    [InlineData(typeof(Health), "Hurt", true)]
    [InlineData(typeof(Effect), "Trigger", false)]
    [InlineData(typeof(ItemAgent_MeleeWeapon), "CheckCollidersInRange", false)]
    public void EncounterCoverageIsReportedAgainForEachRunAfterSharedHookFailure(Type type, string method, bool healthLost)
    {
        AddForeignPatchAndInspect(NativeMethod(type, method));
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        observer.Tick(sink.Context);
        EncounterHit(LaunchEncounterProjectile(), sink);
        Assert.Equal(!healthLost, sink.Rows.Any(row => row.Kind == "combat_hurt_complete"));
        sink.Context.RunId = "second";
        observer.Tick(sink.Context);
        observer.Tick(sink.Context);
        Assert.Equal(EncounterCaptureIssue.CombatIncomplete, observer.FailureIssue);
        var coverage = sink.Flush().Where(row => row.Coverage != null).ToArray();
        Assert.Equal(2, coverage.Length);
        Assert.Equal("r", coverage[0].RunId);
        Assert.Equal("second", coverage[1].RunId);
        Assert.All(coverage, row => Assert.False(row.Coverage!.CaptureStopped));
    }

    [Fact]
    public void EffectEquipmentHookDriftDoesNotDegradeEncounterCombat()
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        AddForeignPatchAndInspect(NativeMethod(typeof(Effect), "SetItem"));
        LevelManager.Instance!.InputManager.AimingEnemyHead = true;
        EncounterHit(LaunchEncounterProjectile(), sink);
        EncounterMelee(sink);
        observer.Tick(sink.Context);
        Assert.Null(observer.FailureIssue);
        Assert.DoesNotContain(sink.Rows, row => row.Kind == "combat_coverage");
        Assert.True((bool)sink.Rows.First(row => row.Kind == "combat_hurt_complete").Data["Headshot"]!);
        Assert.Null(StoredEncounterRun.Build(sink.Flush()).CoverageNoticeKey);
    }

    [Theory]
    [InlineData("Init")]
    [InlineData("Update")]
    [InlineData("Release")]
    public void RejectedNestedProjectileDoesNotBorrowOrEraseTrustedMeleeScope(string method)
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var projectile = LaunchEncounterProjectile();
        AddForeignPatchAndInspect(NativeMethod(typeof(Projectile), method));
        object?[] outer = [new ItemAgent_MeleeWeapon { Holder = player, Item = new() { TypeID = 99 } }, true, null];
        CombatHarmonyCallbacks.MeleePrefixMethod.Invoke(null, outer);
        try
        {
            var trusted = NativeEncounterCombatObserver.ReadActiveSource();
            Assert.NotNull(trusted);
            object?[] inner = [projectile, null];
            CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, inner);
            try { Assert.Null(NativeEncounterCombatObserver.ReadActiveSource()); }
            finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, inner[1]]); }
            Assert.Same(trusted, NativeEncounterCombatObserver.ReadActiveSource());
            EncounterHurt(sink, 99);
        }
        finally { CombatHarmonyCallbacks.MeleeFinalizerMethod.Invoke(null, [null, outer[2]]); }
        Assert.Null(NativeEncounterCombatObserver.ReadActiveSource());
        Assert.Equal("melee", (string?)Assert.Single(sink.Rows, row => row.Kind == "combat_hurt_complete").Data["Source"]!["Kind"]);
    }

    [Fact]
    public void BuffOwnerTrustLossMarksCoverageWithoutDisablingProjectileEvidence()
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var buff = new Duckov.Buffs.Buff { ID = 1, fromWho = player, fromWeaponID = 42 };
        var manager = new CharacterBuffManager(); manager.Buffs.Add(buff);
        CombatHarmonyBridge.CaptureBuffApplication(manager, buff, player, 42, newlyCreated: true);
        buffTrust.MarkUntrusted();
        object?[] effect = [new EffectTriggerEventContext { source = new TickTrigger { Parent = buff } }, null];
        CombatHarmonyCallbacks.EffectPrefixMethod.Invoke(null, effect);
        try
        {
            var source = JObject.FromObject(NativeEncounterCombatObserver.ReadActiveSource()!);
            Assert.False((bool)source["ActorCreditResolved"]!);
            Assert.Equal(-1, (int)source["WeaponId"]!);
        }
        finally { CombatHarmonyCallbacks.EffectFinalizerMethod.Invoke(null, [null, effect[1]]); }
        LevelManager.Instance!.InputManager.AimingEnemyHead = true;
        EncounterHit(LaunchEncounterProjectile(), sink);
        Assert.True((bool)Assert.Single(sink.Rows, row => row.Kind == "combat_hurt_complete").Data["Headshot"]!);
        Assert.Equal(EncounterCaptureIssue.CombatIncomplete, observer.FailureIssue);
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(sink.Flush()).CoverageNoticeKey);
    }

    private Projectile LaunchEncounterProjectile()
    {
        var context = new ProjectileContext
        {
            realFromCharacter = player, fromCharacter = player, fromWeaponItemID = 42,
            fromGunItemSetting = new() { TargetBulletID = 43, LoadedBullet = new() { TypeID = 43 } }
        };
        var projectile = new Projectile(); projectile.Init(context);
        CombatHarmonyCallbacks.ProjectileInitPostfixMethod.Invoke(null, [projectile, context]);
        return projectile;
    }

    private void EncounterHit(Projectile projectile, CombatSink sink)
    {
        Time.frameCount++;
        object?[] update = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, update);
        try { EncounterHurt(sink, 42); }
        finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, update[1]]); }
    }

    private void EncounterMelee(CombatSink sink)
    {
        object?[] melee = [new ItemAgent_MeleeWeapon { Holder = player, Item = new() { TypeID = 99 } }, true, null];
        CombatHarmonyCallbacks.MeleePrefixMethod.Invoke(null, melee);
        try { EncounterHurt(sink, 99); }
        finally { CombatHarmonyCallbacks.MeleeFinalizerMethod.Invoke(null, [null, melee[2]]); }
    }

    private void EncounterHurt(CombatSink sink, int weapon)
    {
        var target = new CharacterMainControl { characterPreset = new() { nameKey = "Scavenger" } };
        var health = new Health { CurrentHealth = 10, Character = target, team = Teams.enemy };
        object?[] hurt = [health, new DamageInfo { fromCharacter = player, fromWeaponItemID = weapon, damageValue = 10, crit = 1 }, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
        try
        {
            NativeEncounterCombatObserver.AssignHealth(health, 0);
            health.IsDead = true;
            CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [health, hurt[2]]);
        }
        finally { CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null, [health, null]); }
        sink.Context.MonotonicSeconds++;
    }

    private sealed class CombatSink : IEncounterObservationSink
    {
        private readonly Dictionary<UnityEngine.Object, int> actors = new(ReferenceEqualityComparer.Instance);
        private readonly EncounterCapturePipeline pipeline = new();
        internal readonly List<(string Kind, JObject Data)> Rows = new();
        public EncounterObservationContext Context { get; } = new()
        { Active = true, GenerationId = "g", RunId = "r", MapId = "m", SegmentId = "segment" };
        public int ActorId(UnityEngine.Object actor)
        {
            if (!actors.TryGetValue(actor, out var id)) actors.Add(actor, id = actors.Count + 1);
            return id;
        }
        public void Record(string eventKind, object payload)
        {
            Rows.Add((eventKind, JObject.FromObject(payload)));
            // Same observer-to-pipeline routing used by EncounterCaptureHost.Record.
            if (eventKind == "combat_coverage") pipeline.ReportCoverage(Context.GenerationId, Context.RunId,
                Context.MonotonicSeconds, EncounterCaptureIssue.CombatIncomplete);
            pipeline.Record(Context.GenerationId, Context.RunId, Context.MapId, Context.SegmentId,
                Context.MonotonicSeconds, eventKind, payload);
        }
        internal EncounterRecord[] Flush()
        {
            var result = new Dictionary<(string, string), EncounterRecord>();
            Assert.True(pipeline.Pump((_, row) => { result[(row.RunId, row.Id)] = row; return true; }, flush: true));
            Assert.True(pipeline.Failure == null, pipeline.Failure);
            return result.Values.ToArray();
        }
    }
}
