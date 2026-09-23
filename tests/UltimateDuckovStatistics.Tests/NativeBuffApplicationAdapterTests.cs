using System.Reflection;
using HarmonyLib;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeBuffApplicationAdapterTests : IDisposable
{
    public NativeBuffApplicationAdapterTests() => Harmony.ClearAll();
    public void Dispose() => Harmony.ClearAll();

    [Fact]
    public void RejectedHealingDoesNotDisableSharedCombatBuffObservation()
    {
        var boundary = new NativeBuffApplicationObservationBoundary();
        using var buffs = new NativeBuffApplicationAdapter(boundary, _ => { });
        Assert.True(buffs.Initialize());
        var original = typeof(Health).GetMethod(nameof(Health.AddHealth))!;
        new Harmony("unqualified-healing.mod").Patch(original,
            new HarmonyMethod(typeof(NativeBuffApplicationAdapterTests).GetMethod(nameof(ForeignPatch), BindingFlags.Static | BindingFlags.NonPublic)!), null, null, null);
        using (var healing = new NativeHealingAttributionAdapter(_ => { }, _ => { }, boundary))
        {
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, healing.Initialize().State);
            Assert.True(boundary.IsTrusted);
        }
        Assert.True(boundary.IsTrusted);
    }

    [Fact]
    public void PatchRemovalInvalidatesSharedEvidenceBeforeNextTick()
    {
        var boundary = new NativeBuffApplicationObservationBoundary();
        using var buffs = new NativeBuffApplicationAdapter(boundary, _ => { });
        Assert.True(buffs.Initialize());
        new Harmony("test").UnpatchAll("at.bamboechop.ultimate-duckov-statistics.buffs");
        Assert.False(boundary.IsTrusted);
    }

    [Fact]
    public void FailedCleanupDetachesEvidenceAndNextActivationRetriesOwnerCleanup()
    {
        var boundary = new NativeBuffApplicationObservationBoundary();
        var buffs = new NativeBuffApplicationAdapter(boundary, _ => { });
        Assert.True(buffs.Initialize());
        Harmony.FailNextUnpatches(1);
        buffs.Dispose();
        Assert.False(boundary.IsTrusted);
        using var replacement = new NativeBuffApplicationAdapter(new NativeBuffApplicationObservationBoundary(), _ => { });
        Assert.True(replacement.Initialize());
    }

    private static void ForeignPatch() { }
}
