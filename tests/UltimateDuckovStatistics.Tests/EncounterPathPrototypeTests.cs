#if UDS_ENCOUNTER_DIAGNOSTICS
using UltimateDuckovStatistics.Encounters;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterPathPrototypeTests
{
    [Theory]
    [InlineData(455.72, 103.62, 455.72, 103.62, false)] // Recorded vertical-only spawn adjustment.
    [InlineData(430.328735, 124.108528, 234.57, 202.303986, true)]
    [InlineData(234.651062, 202.772644, 431.093, 123.92, true)]
    [InlineData(1.0, 2.0, 1.00001, 2.0, true)] // No arbitrary minimum teleport distance.
    [InlineData(double.NaN, 2.0, 3.0, 4.0, false)]
    public void OnlyDistinctFiniteMapEndpointsGetADottedConnection(double fromX, double fromZ,
        double toX, double toZ, bool expected)
        => Assert.Equal(expected, EncounterPathMath.HasMapDisplacement(fromX, fromZ, toX, toZ));

    [Theory]
    [InlineData("duckov:map:Level_GroundZero_1", "Level_GroundZero_1")]
    [InlineData("Level_GroundZero_1", "Level_GroundZero_1")]
    public void LogicalMapIdMapsToNativeSceneLookup(string logical, string expected)
        => Assert.Equal(expected, EncounterPathMath.NativeSceneId(logical));

    [Theory]
    [InlineData(0.2, 1.0, 8.0, null)]
    [InlineData(0.2, 80.0, 8.0, "unclassified-speed-discontinuity")]
    [InlineData(2.01, 1.0, 8.0, "sample-gap")]
    [InlineData(0.0, 0.0, 8.0, "nonmonotonic-sample")]
    [InlineData(0.2, 1.0, 0.0, "invalid-observation")]
    public void SamplesNeverInferAProvenTeleport(double elapsed, double distance, double speed, string? expected)
        => Assert.Equal(expected, EncounterPathMath.GapReason(elapsed, distance, speed));

}
#endif
