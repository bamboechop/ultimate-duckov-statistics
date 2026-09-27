using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    [Fact]
    public void KillFeedCopiesFatalDistanceBeforeCorpseCleanupAndDoesNotNeedStorageSuccess()
    {
        var sink = new CombatSink(); sink.Context.MapId = mapId = "test-map";
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var captured = new List<(CombatRecorded Event, double? Distance)>();
        killFeedObservation = (value, distance) => captured.Add((value, distance));
        recordEvent = _ => false;
        player.transform.position = new Vector3(0, 99, 0);
        var enemy = new Health { CurrentHealth = 20, team = Teams.enemy, Character = new() };
        enemy.Character.transform.position = new Vector3(3, 0, 4);
        var state = BeginFatalOrderHit(enemy, 0);
        enemy.Character.transform.position = new Vector3(999, -300, 999);
        player.transform.position = new Vector3(50, 0, 50);
        FinishFatalOrderHit(enemy, state);
        var result = Assert.Single(captured);
        Assert.Equal(5, result.Distance); Assert.Equal(1, result.Event.KillsByYou);
        Assert.Equal(20, Assert.Single(events).ActualDamageDealt);
        Assert.Null(NativeEncounterCombatObserver.FatalDistance(enemy, false, mapId));
    }

    [Fact]
    public void FailingHudObserverCannotAbortDamageOrLaterStatisticsCallbacks()
    {
        var calls = 0;
        killFeedObservation = (_, _) => { calls++; throw new InvalidOperationException("HUD failed"); };
        for (var i = 0; i < 2; i++)
        {
            var enemy = new Health { CurrentHealth = 20, team = Teams.enemy, Character = new() };
            FinishFatalOrderHit(enemy, BeginFatalOrderHit(enemy, 0));
        }
        Assert.Equal(1, calls); Assert.Equal(2, events.Sum(value => value.KillsByYou));
        Assert.Equal(40, events.Sum(value => value.ActualDamageDealt));
        Assert.Contains(diagnostics, value => value.Contains("HUD failed", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingFatalPositionIsOmittedInsteadOfReadingMovedActorAtPostfix()
    {
        double? captured = 0;
        killFeedObservation = (_, distance) => captured = distance;
        var enemy = new Health { CurrentHealth = 20, team = Teams.enemy, Character = new() };
        FinishFatalOrderHit(enemy, BeginFatalOrderHit(enemy, 0));
        Assert.Null(captured); Assert.Equal(1, Assert.Single(events).KillsByYou);
    }

    [Fact]
    public void PublicPlayerDeathAndHealthPostfixProduceOneIncomingFeedEvent()
    {
        var sink = new CombatSink(); sink.Context.MapId = mapId = "test-map";
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var captured = new List<(CombatRecorded Event, double? Distance)>();
        killFeedObservation = (value, distance) => captured.Add((value, distance));
        var health = player.Health;
        health.IsMainCharacterHealth = true; health.Character = player; health.team = Teams.player; health.CurrentHealth = 20;
        player.transform.position = new Vector3(0, 0, 0);
        var enemy = new CharacterMainControl(); enemy.transform.position = new Vector3(0, 30, 12);
        var info = new DamageInfo { fromCharacter = enemy, fromWeaponItemID = 67, damageValue = 20 };
        object?[] hurt = [health, info, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
        NativeEncounterCombatObserver.AssignHealth(health, 0); health.IsDead = true;
        GameManager.Paused = true;
        try
        {
            adapter.RecordPlayerDeath(info);
            FinishFatalOrderHit(health, hurt[2]);
        }
        finally { GameManager.Paused = false; }
        var result = Assert.Single(captured);
        Assert.Equal(1, result.Event.PlayerDeaths); Assert.Equal(12, result.Distance);
    }
}
