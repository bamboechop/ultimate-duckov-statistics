using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeHealingObservationPolicyTests : IDisposable
{
    public NativeHealingObservationPolicyTests() => HarmonyLib.Harmony.ClearAll();
    public void Dispose() => HarmonyLib.Harmony.ClearAll();

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(7, false)]
    public void ForeignPatchesDoNotSuppressProvenHealingOrInventFailedUseCredit(float actual, bool successful)
    {
        var original = typeof(Health).GetMethod(nameof(Health.AddHealth))!;
        var callback = new HarmonyLib.HarmonyMethod(typeof(NativeHealingObservationPolicyTests).GetMethod(nameof(Foreign), BindingFlags.Static | BindingFlags.NonPublic)!);
        new HarmonyLib.Harmony("unfamiliar.healing-before").Patch(original, callback, null, null, null);
        var boundary = new NativeBuffApplicationObservationBoundary(); boundary.MarkTrusted();
        var events = new List<HealingApplied>();
        using var adapter = new NativeHealingAttributionAdapter(events.Add, _ => { }, boundary);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.Initialize().State);
        new HarmonyLib.Harmony("unfamiliar.healing-after").Patch(original, callback, null, null, null);
        adapter.BeginUse(new ItemUseSnapshot { ItemId = "med", RuntimeItemId = 1, SaveGenerationId = "g",
            RunId = "r", MapId = "m", GameplayContext = GameplayContext.Raid, TimestampUtc = DateTime.UtcNow });
        adapter.BeginApplication(1);
        var health = new Health { IsMainCharacterHealth = true, CurrentHealth = 10, MaxHealth = 100 };
        var state = HealingHarmonyBridge.BeginHealthApplication(health, 20);
        health.CurrentHealth += actual;
        HealingHarmonyBridge.CompleteHealthApplication(health, state);
        adapter.CompleteUse(1, successful ? new ItemUseRecorded { EventId = "use", ItemId = "med", SaveGenerationId = "g",
            GameplayContext = GameplayContext.Raid, Group = CanonicalItemGroup.Healing } : null);
        Assert.Equal(successful ? actual : 0, events.Sum(value => value.ActualHealthRestored));
        Assert.Equal(AdapterCapabilityState.Supported, adapter.Capability.State);
    }

    private static void Foreign() { }
}
