using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class RunCapabilityAndIntegrityPolicyTests
{
    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Capability")]
    public void CapabilitiesDegradeIndependentlyForEveryUnavailableCondition()
    {
        Assert.Equal(AdapterCapabilityState.Supported, RunCapabilityPolicy.GetState(RunCapabilityCondition.Available));
        foreach (var condition in Enum.GetValues<RunCapabilityCondition>().Where(value => value != RunCapabilityCondition.Available))
        {
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, RunCapabilityPolicy.GetState(condition));
        }

        Assert.True(RunCapabilityPolicy.IsSupportedGameVersion("2.3.30", "2.3.30"));
        Assert.False(RunCapabilityPolicy.IsSupportedGameVersion("2.3.31", "2.3.30"));
        Assert.False(RunCapabilityPolicy.IsSupportedGameVersion(null, "2.3.30"));
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Integrity")]
    public void RequiredHarmonyLoaderDoesNotDisqualifyAnOtherwiseNormalRun()
    {
        var result = RunIntegrityPolicy.Evaluate(
            cheatOrCustomDifficulty: false,
            new[] { RunIntegrityPolicy.HarmonyLoaderModId, ProductInfo.ModId });

        Assert.Equal(IntegrityTags.Normal, result);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Integrity")]
    public void GameplayModsAndCheatRulesRemainExplicitlyDisqualifying()
    {
        Assert.Equal(
            IntegrityTags.ModdedContent,
            RunIntegrityPolicy.Evaluate(false, new[] { ProductInfo.ModId, "GameplayOverhaul" }));
        Assert.Equal(
            IntegrityTags.CheatOrCustomDifficulty,
            RunIntegrityPolicy.Evaluate(true, new[] { ProductInfo.ModId }));
        Assert.Equal(IntegrityTags.Unknown, RunIntegrityPolicy.Evaluate(false, null));
    }
}
