using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class OverviewHealingEvidenceTests
{
    [Theory]
    [InlineData(0, false, "0.00", "0")]
    [InlineData(12.5, false, "12.50", "12.5")]
    [InlineData(0, true, "Unavailable", "Unavailable")]
    [InlineData(12.5, true, "12.50 (Partial)", "12.5 (Partial)")]
    public void SupportedAndRepairedEvidenceAgreeAcrossBothViews(double value, bool repaired, string overviewExpected, string itemExpected)
    {
        var profile = new Core.Persistence.ProfileDocument { GenerationId = "g" };
        profile.Statistics.SaveGenerationId = "g";
        profile.Statistics.Overall.ActualHealthRestored = value;
        profile.Statistics.RunTotals.ItemStatistics.WasRepairedFromInvalidState = repaired;
        profile.Capabilities.Add(new() { AdapterId = NativeHealingAttributionAdapter.AdapterId, State = AdapterCapabilityState.Supported });
        var projection = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
        Assert.Equal(overviewExpected, ProfileSummaryPresentationFactory.Create(projection, UiText.Get)[(int)ProfileSummaryMetric.HealthRestored].Value);
        Assert.Equal(itemExpected, ItemUsePresentationFactory.Create(projection, "g")!.Health.Text);
    }

    [Theory]
    [InlineData(0, "Unavailable", "Unavailable", ItemUseEvidence.Unavailable)]
    [InlineData(12.5, "12.50 (Partial)", "12.5 (Partial)", ItemUseEvidence.Partial)]
    public void FailedNativeActivationPublishesUnavailableHealingToBothViews(double retained,
        string overviewExpected, string itemUseExpected, object expectedEvidence)
    {
        var originalPath = Application.persistentDataPath;
        var originalSlot = Saves.SavesSystem.CurrentSlot;
        using var directory = new TemporaryDirectory();
        HarmonyLib.Harmony.ClearAll();
        try
        {
            Application.persistentDataPath = directory.Path;
            Saves.SavesSystem.CurrentSlot = 1;
            var save = Path.Combine(directory.Path, Saves.SavesSystem.GetFilePath(1));
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllText(save, "{\"SaveTime\":{\"value\":1}}");
            using var coordinator = new NativeProfileCoordinator();
            coordinator.Initialize();
            // A foreign prefix makes real adapter activation fail before any capture is enabled.
            new HarmonyLib.Harmony("foreign-healing").Patch(typeof(Health).GetMethod(nameof(Health.AddHealth))!,
                prefix: new HarmonyLib.HarmonyMethod(typeof(OverviewHealingEvidenceTests).GetMethod(nameof(ForeignPrefix), BindingFlags.NonPublic | BindingFlags.Static)!), postfix: null, transpiler: null, finalizer: null);
            using var adapter = new NativeHealingAttributionAdapter(coordinator.HandleHealing, _ => { }, new NativeBuffApplicationObservationBoundary());
            adapter.CapabilityChanged += coordinator.SetHealingCapability;
            var failed = adapter.Initialize();
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, failed.State);
            Assert.Contains("unsafe pre-existing Harmony patch set", failed.Detail);
            var profile = coordinator.Current!;
            Assert.Equal(failed.Detail, Assert.Single(profile.Capabilities, cap => cap.AdapterId == NativeHealingAttributionAdapter.AdapterId).Detail);
            // Same-format retained observations; ordinary item-use data is independently available.
            ItemUseReducer.Apply(profile.Statistics, new ItemUseRecorded { EventId = "use", SaveGenerationId = profile.GenerationId,
                GameplayContext = GameplayContext.Raid, ItemId = "medkit", DisplayName = "Medkit", Group = CanonicalItemGroup.Healing,
                ActivationCount = 1, AmountConsumed = 1, ConsumptionUnit = ConsumptionUnit.Item });
            if (retained > 0) HealingReducer.Apply(profile.Statistics, new HealingApplied { EventId = "heal", ApplicationId = "application", SourceItemUseEventId = "use", SaveGenerationId = profile.GenerationId,
                GameplayContext = GameplayContext.Raid, ItemId = "medkit", DisplayName = "Medkit", Group = CanonicalItemGroup.Healing, ActualHealthRestored = retained });
            var projection = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
            var overview = ProfileSummaryPresentationFactory.Create(projection, UiText.Get);
            var itemUse = ItemUsePresentationFactory.Create(projection, profile.GenerationId)!;
            Assert.Equal(overviewExpected, overview[(int)ProfileSummaryMetric.HealthRestored].Value);
            Assert.Equal(itemUseExpected, itemUse.Health.Text);
            Assert.Equal((ItemUseEvidence)expectedEvidence, itemUse.Health.Evidence);
        }
        finally
        {
            HarmonyLib.Harmony.ClearAll();
            Application.persistentDataPath = originalPath;
            Saves.SavesSystem.CurrentSlot = originalSlot;
        }
    }

    private static void ForeignPrefix() { }
}
