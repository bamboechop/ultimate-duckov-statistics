using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

// Public native event and clock member boundaries. The real adapter and bridge
// are source-linked, including completion pairing and persistence boundaries.
#pragma warning disable CA1050, CA1051, CA1815, CA1822
public sealed class GameClock : UnityEngine.Object
{
    public static GameClock? Instance { get; set; }
    public static long Day { get; set; }
    public static TimeSpan TimeOfDay { get; set; }
    public static event Action? OnGameClockStep;
    private static void Step(float seconds) { TimeOfDay += TimeSpan.FromSeconds(seconds); OnGameClockStep?.Invoke(); }
    public static void Advance(float seconds) => Step(seconds);
    public static void Reset() { Instance = new(); Day = 1; TimeOfDay = TimeSpan.FromHours(1); OnGameClockStep = null; }
}
#pragma warning restore CA1050, CA1051, CA1815, CA1822

namespace UltimateDuckovStatistics.Tests
{
    [Collection(NativeEconomyAdapterTestGroup.CollectionName)]
    public sealed class NativeSleepObservationPolicyTests : IDisposable
    {
        public NativeSleepObservationPolicyTests()
        {
            HarmonyLib.Harmony.ClearAll(); GameClock.Reset(); Duckov.UI.SleepView.OnAfterSleep = null;
            UnityEngine.Application.version = "2.3.30";
        }
        public void Dispose() { HarmonyLib.Harmony.ClearAll(); GameClock.Reset(); Duckov.UI.SleepView.OnAfterSleep = null; }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ForeignSleepPatchPreservesOnlyPairedObservedAdvancement(bool runtime)
        {
            void Patch() => new HarmonyLib.Harmony("unfamiliar.sleep").Patch(typeof(GameClock).GetMethod("Step", BindingFlags.Static | BindingFlags.NonPublic)!,
                new HarmonyLib.HarmonyMethod(typeof(NativeSleepObservationPolicyTests).GetMethod(nameof(Foreign), BindingFlags.Static | BindingFlags.NonPublic)!), null, null, null);
            if (!runtime) Patch();
            var mutations = new List<WorldTimeMutation>();
            using var adapter = new NativeWorldTimeAdapter(() => "g", value => { mutations.Add(value); return true; }, () => true, (_, _) => { }, _ => { });
            adapter.Initialize();
            if (runtime) Patch();
            Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.SleepAdvancedTime.State);
            object?[] prefix = [3600f, null];
            WorldTimeHarmonyCallbacks.SleepAdvancePrefixMethod.Invoke(null, prefix);
            GameClock.Advance(3600);
            WorldTimeHarmonyCallbacks.SleepAdvancePostfixMethod.Invoke(null, [prefix[1]]);
            Duckov.UI.SleepView.OnAfterSleep?.Invoke(); Duckov.UI.SleepView.OnAfterSleep?.Invoke();
            Assert.Equal(1, mutations.Sum(value => value.CompletedSleepSessions));
            Assert.Equal(TimeSpan.FromHours(1).Ticks, mutations.Sum(value => value.SleepAdvancedTimeTicks));
        }

        [Fact]
        public void UnpairedSleepCompletionCannotInventTimeOrSessions()
        {
            var mutations = new List<WorldTimeMutation>();
            using var adapter = new NativeWorldTimeAdapter(() => "g", value => { mutations.Add(value); return true; }, () => true, (_, _) => { }, _ => { });
            adapter.Initialize(); Duckov.UI.SleepView.OnAfterSleep?.Invoke(); adapter.FlushPending();
            Assert.Equal(0, mutations.Sum(value => value.CompletedSleepSessions));
            Assert.Equal(0, mutations.Sum(value => value.SleepAdvancedTimeTicks));
        }
        private static void Foreign() { }
    }
}
