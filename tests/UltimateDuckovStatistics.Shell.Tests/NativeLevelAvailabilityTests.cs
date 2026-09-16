using UltimateDuckovStatistics.Adapters;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class NativeLevelAvailabilityTests : IDisposable
{
    public NativeLevelAvailabilityTests()
    {
        LevelManager.Instance = null;
        LevelManager.MissingInstanceSearches = 0;
    }

    [Fact]
    public void MenuPollingDoesNotInvokeTheNativeSceneSearch()
    {
        for (var frame = 0; frame < 1000; frame++)
            Assert.Null(NativeLevelAvailability.MayExist ? LevelManager.Instance : null);
        Assert.Equal(0, LevelManager.MissingInstanceSearches);
    }

    [Fact]
    public void InitializationAndReplacementAreVisibleWithoutWaitingForACache()
    {
        Assert.False(NativeLevelAvailability.MayExist);
        var first = new LevelManager();
        LevelManager.Instance = first;
        Assert.Same(first, NativeLevelAvailability.MayExist ? LevelManager.Instance : null);
        var replacement = new LevelManager();
        LevelManager.Instance = replacement;
        Assert.Same(replacement, NativeLevelAvailability.MayExist ? LevelManager.Instance : null);
        LevelManager.Instance = null;
        Assert.False(NativeLevelAvailability.MayExist);
        Assert.Equal(0, LevelManager.MissingInstanceSearches);
    }

    [Fact]
    public void DestroyedNativeReferenceIsAbsentUntilReplacement()
    {
        var level = new LevelManager();
        LevelManager.Instance = level;
        UnityEngine.Object.Destroy(level);
        Assert.False(NativeLevelAvailability.MayExist);
        LevelManager.Instance = new LevelManager();
        Assert.True(NativeLevelAvailability.MayExist);
        Assert.Equal(0, LevelManager.MissingInstanceSearches);
    }

    [Fact]
    public void UnrecognizedBackingContractLeavesPublicLookupAvailable()
    {
        Assert.Null(NativeLevelAvailability.FindInstanceField(typeof(UnityEngine.Object)));
        Assert.Null(NativeLevelAvailability.FindInstanceField(typeof(string)));
        Assert.NotNull(NativeLevelAvailability.FindInstanceField(typeof(LevelManager)));
    }

    public void Dispose() => LevelManager.Instance = null;
}
