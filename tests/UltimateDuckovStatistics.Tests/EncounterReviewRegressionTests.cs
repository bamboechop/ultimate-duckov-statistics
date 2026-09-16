using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterAttributionRegressionTests
{
    [Theory]
    [InlineData(true, true, "effect", EncounterOutcome.PlayerKill, EncounterCredit.Player, 1)]
    [InlineData(false, true, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Other, 0)]
    [InlineData(true, false, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(false, false, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(true, null, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(true, null, "unscoped-effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(true, null, "projectile", EncounterOutcome.PlayerKill, EncounterCredit.Player, 1)]
    public void EffectOwnershipControlsBothDamageAndFatalCredit(bool retainedPlayer, bool? resolved, string mechanism,
        EncounterOutcome outcome, EncounterCredit credit, int damageRecords)
    {
        var reducer = new EncounterEvidenceProjection("s");
        var data = JObject.FromObject(new
        {
            Transaction = 1, FatalSequence = 1, Target = new { Id = 2, IsMain = false },
            Source = new { Kind = mechanism, ActorCreditResolved = resolved, Credited = new { Id = 1, IsMain = retainedPlayer }, WeaponId = 42 },
            NativeCallCompleted = true, ProposedHpLossOwnedAssignments = 10
        });
        reducer.Apply("r", "m", "segment", 1, "combat_hurt_complete", data);
        reducer.Apply("r", "m", "segment", 1, "combat_fatal", data);
        var records = reducer.Flush();
        var encounter = Assert.Single(records, r => r.Encounter != null).Encounter!;
        Assert.Equal(outcome, encounter.Outcome);
        Assert.Equal(credit, encounter.FinalSource!.Credit);
        Assert.Equal(damageRecords, records.Count(r => r.Damage != null));
        if (credit == EncounterCredit.Unknown)
        {
            Assert.Null(encounter.FinalSource.CreditedActorId);
            Assert.Null(encounter.FinalSource.WeaponTypeId);
        }
    }

    [Fact]
    public void AmbiguousIncomingEffectRetainsDamageWithoutJoiningTheRetainedActor()
    {
        var reducer = new EncounterEvidenceProjection("s");
        var data = JObject.FromObject(new
        {
            Transaction = 1, Target = new { Id = 1, IsMain = true },
            Source = new { Kind = "effect", ActorCreditResolved = false,
                Physical = new { Id = 2 }, Credited = new { Id = 2 }, WeaponId = 42 },
            NativeCallCompleted = true, ProposedHpLossOwnedAssignments = 10
        });
        reducer.Apply("r", "m", "segment", 1, "combat_hurt_complete", data);
        var records = reducer.Flush();
        var damage = Assert.Single(records, r => r.Damage != null).Damage!;
        Assert.True(damage.Incoming); Assert.Equal(10, damage.Amount);
        Assert.Equal(EncounterCredit.Unknown, damage.Source.Credit);
        Assert.Null(damage.Source.PhysicalActorId); Assert.Null(damage.Source.WeaponTypeId);
        Assert.NotEqual("s/a2", Assert.Single(records, r => r.Encounter != null).Encounter!.ActorId);
    }
}

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class EncounterMapObserverRegressionTests
{
    [Theory]
    [InlineData("native-pause")]
    [InlineData("context-pause")]
    [InlineData("loading")]
    [InlineData("missing-character")]
    [InlineData("replacement-character")]
    public void InterruptedSamplingKeepsVisitAndStartsRouteGap(string interruption)
    {
        HarmonyLib.Harmony.ClearAll();
        GameManager.Paused = false; LevelManager.LevelInitializing = false;
        Duckov.Scenes.SceneLoader.IsSceneLoading = false; MultiSceneCore.Instance = null;
        Duckov.MiniMaps.MiniMapSettings.Instance = null;
        var player = new CharacterMainControl { IsMainCharacter = true };
        CharacterMainControl.Main = player;
        var sink = new MapSink();
        using var observer = new NativeEncounterMapObserver(sink, "unused", _ => { });
        try
        {
            // A native combat callback may precede the map observer's first Tick.
            sink.Record("combat_hurt_complete", new { Target = new { Id = 2 }, NativeCallCompleted = false });
            observer.Tick(sink.Context);
            sink.Context.MonotonicSeconds = 1;
            if (interruption == "native-pause") GameManager.Paused = true;
            if (interruption == "context-pause") sink.Context.Paused = true;
            if (interruption == "loading") sink.Context.Loading = true;
            if (interruption == "missing-character") CharacterMainControl.Main = null;
            if (interruption == "replacement-character") CharacterMainControl.Main = new() { IsMainCharacter = true };
            observer.Tick(sink.Context);

            GameManager.Paused = false; sink.Context.Paused = false; sink.Context.Loading = false;
            if (CharacterMainControl.Main == null) CharacterMainControl.Main = player;
            CharacterMainControl.Main.transform.position = new Vector3(10, 0, 10);
            sink.Context.MonotonicSeconds = 2;
            observer.Tick(sink.Context);
            var records = sink.Reducer.Flush();
            var map = Assert.Single(StoredEncounterMap.Build(records));
            Assert.True(Assert.Single(records, r => r.Visit != null).Visit!.HasGaps);
            Assert.Single(sink.Kinds, k => k == "path-visit");
            Assert.Contains(records.Where(r => r.Route != null).SelectMany(r => r.Route!.Points), p => p.Connection == RouteConnection.Gap);

            // Leaving and returning to this same map is a real second visit.
            sink.Context.SegmentId = "segment2"; sink.Context.MonotonicSeconds = 3;
            observer.Tick(sink.Context);
            var next = sink.Reducer.Flush();
            Assert.Contains(next, r => r.Visit != null && r.Id != map.VisitId);
            Assert.Equal(2, sink.Kinds.Count(k => k == "path-visit"));
        }
        finally
        {
            observer.Dispose(); HarmonyLib.Harmony.ClearAll();
            CharacterMainControl.ResetNativeState(); GameManager.Paused = false;
        }
    }

    private sealed class MapSink : IEncounterObservationSink
    {
        public EncounterObservationContext Context { get; } = new()
        { Active = true, GenerationId = "g", RunId = "r", MapId = "m", SegmentId = "segment" };
        public EncounterEvidenceProjection Reducer { get; } = new("s");
        public List<string> Kinds { get; } = new();
        public int ActorId(UnityEngine.Object actor) => 1;
        public void Record(string kind, object payload)
        {
            Kinds.Add(kind);
            if (EncounterEvidenceProjection.Accepts(kind))
                Reducer.Apply(Context.RunId, Context.MapId, Context.SegmentId, Context.MonotonicSeconds, kind, JObject.FromObject(payload));
        }
    }
}
