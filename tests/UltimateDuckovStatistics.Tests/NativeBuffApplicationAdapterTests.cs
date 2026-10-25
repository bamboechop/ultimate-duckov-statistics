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
        // A conflicting callback claiming our owner is an actual hook-integrity
        // failure, distinct from an ordinary foreign mod.
        new Harmony(ReflectiveHarmonyPatcher.HarmonyId).Patch(original,
            new HarmonyMethod(typeof(NativeBuffApplicationAdapterTests).GetMethod(nameof(ForeignPatch), BindingFlags.Static | BindingFlags.NonPublic)!), null, null, null);
        using (var healing = new NativeHealingAttributionAdapter(_ => { }, _ => { }, boundary))
        {
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, healing.Initialize().State);
            Assert.True(boundary.IsTrusted);
        }
        Assert.True(boundary.IsTrusted);
    }

    [Fact]
    public void UnfamiliarBuffPatchBeforeOrAfterActivationKeepsEvidenceAvailable()
    {
        var original = typeof(CharacterBuffManager).GetMethod("AddBuff")!;
        var prefix = typeof(NativeBuffApplicationAdapterTests).GetMethod(nameof(ForeignPatch), BindingFlags.Static | BindingFlags.NonPublic)!;
        new Harmony("unfamiliar-buff.mod").Patch(original, new HarmonyMethod(prefix), null, null, null);
        var boundary = new NativeBuffApplicationObservationBoundary();
        using var buffs = new NativeBuffApplicationAdapter(boundary, _ => { });
        Assert.True(buffs.Initialize());
        new Harmony("another-buff.mod").Patch(original, new HarmonyMethod(prefix), null, null, null);
        Assert.True(boundary.IsTrusted);
        buffs.Tick();
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
        new Harmony("at.bamboechop.ultimate-duckov-statistics.buffs").Patch(
            typeof(CharacterBuffManager).GetMethod("AddBuff")!,
            new HarmonyMethod(NativeBuffApplicationAdapter.BuffPrefixMethod),
            new HarmonyMethod(NativeBuffApplicationAdapter.BuffPostfixMethod), null, null);
        buffs.Tick();
        Assert.True(boundary.IsTrusted);
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
