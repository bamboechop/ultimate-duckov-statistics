using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    private static readonly string[] FatalPresetOrder = ["second-discovered", "first-discovered"];
    private static readonly int[] FatalMarkerOrder = [1, 2];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameFrameDeathsFollowFatalBoundaryRatherThanDiscoveryOrCallbackCompletion(bool nested)
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var firstDiscovered = new Health { CurrentHealth = 20, team = Teams.enemy,
            Character = new() { characterPreset = new() { nameKey = "first-discovered" } } };
        var secondDiscovered = new Health { CurrentHealth = 20, team = Teams.enemy,
            Character = new() { characterPreset = new() { nameKey = "second-discovered" } } };
        // Earlier nonfatal hits establish actor identity in the opposite order to the deaths.
        FinishFatalOrderHit(firstDiscovered, BeginFatalOrderHit(firstDiscovered, 10));
        FinishFatalOrderHit(secondDiscovered, BeginFatalOrderHit(secondDiscovered, 10));
        sink.Context.MonotonicSeconds = 5;

        // Synchronous explosion victims share the tracker's last-advanced elapsed time.
        var firstFatal = BeginFatalOrderHit(secondDiscovered, 0);
        if (!nested) FinishFatalOrderHit(secondDiscovered, firstFatal);
        FinishFatalOrderHit(firstDiscovered, BeginFatalOrderHit(firstDiscovered, 0));
        // A nested callback can complete the second death before the first returns.
        if (nested) FinishFatalOrderHit(secondDiscovered, firstFatal);

        var fatals = sink.Rows.Where(row => row.Kind == "combat_fatal").ToArray();
        Assert.Equal(2, fatals.Length);
        var ordered = fatals.OrderBy(row => (long)row.Data["FatalSequence"]!).ToArray();
        Assert.Equal(sink.ActorId(secondDiscovered.Character!), (int)ordered[0].Data["Target"]!["Id"]!);
        Assert.Equal(nested, (long)fatals[0].Data["FatalSequence"]! > (long)fatals[1].Data["FatalSequence"]!);

        var records = sink.Flush();
        EncounterRecordValidation.ValidateHistory(records);
        // Reload order deliberately opposes publication order.
        var codec = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord);
        var run = StoredEncounterRun.Build(records.Reverse()
            .Select(record => ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(record))).ToArray());
        Assert.Equal(FatalPresetOrder, run.Events.Select(record => record.Encounter!.EnemyPresetKey));
        Assert.All(run.Events, record =>
        {
            Assert.Equal(5, record.Encounter!.EndedSeconds);
            Assert.Equal(EncounterOutcome.PlayerKill, record.Encounter.Outcome);
        });
        Assert.Equal(ordered.Select(row => (long?)row.Data["FatalSequence"]), run.Events.Select(record => record.Encounter!.FatalSequence));
        Assert.Equal(FatalMarkerOrder, Assert.Single(run.Visits).Events.Select(record => run.EventIndex(record.Id) + 1));
    }

    private object? BeginFatalOrderHit(Health target, float remaining)
    {
        object?[] hurt = [target, new DamageInfo { fromCharacter = player, fromWeaponItemID = 67,
            damageValue = target.CurrentHealth - remaining, isExplosion = true }, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
        NativeEncounterCombatObserver.AssignHealth(target, remaining);
        target.IsDead = remaining <= 0;
        return hurt[2];
    }

    private static void FinishFatalOrderHit(Health target, object? state)
    {
        try { CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [target, state]); }
        finally { CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null, [target, null]); }
    }
}
