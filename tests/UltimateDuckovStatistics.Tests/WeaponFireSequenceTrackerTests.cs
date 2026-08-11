using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class WeaponFireSequenceTrackerTests
{
    [Fact]
    [Trait("Category", "Weapon")]
    public void DuplicateNativeCallbacksReuseTheSameEventIdWhileRealDischargesAdvance()
    {
        var next = 0;
        var tracker = new WeaponFireSequenceTracker(() => $"event-{++next}");

        var first = tracker.GetEventId(runtimeWeaponId: 10, ammunitionTypeId: 20, remainingAmmunition: 7);
        var duplicate = tracker.GetEventId(10, 20, 7);
        var automaticFollowUp = tracker.GetEventId(10, 20, 6);
        var afterReload = tracker.GetEventId(10, 20, 7);
        var switchedAmmunition = tracker.GetEventId(10, 21, 6);

        Assert.Equal(first, duplicate);
        Assert.NotEqual(first, automaticFollowUp);
        Assert.NotEqual(automaticFollowUp, afterReload);
        Assert.NotEqual(afterReload, switchedAmmunition);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Performance")]
    public void RuntimeWeaponCacheIsBoundedAndProfileTransitionsClearIt()
    {
        var next = 0;
        var tracker = new WeaponFireSequenceTracker(() => $"event-{++next}", capacity: 3);
        for (var weapon = 1; weapon <= 20; weapon++)
        {
            tracker.GetEventId(weapon, 100 + weapon, 5);
        }

        Assert.Equal(3, tracker.Count);
        tracker.Clear();
        Assert.Equal(0, tracker.Count);
    }
}
