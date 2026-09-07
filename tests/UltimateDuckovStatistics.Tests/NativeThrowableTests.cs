using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

// Native signatures only. Tests invoke installed production callbacks, not a simulated Unity launch.
#pragma warning disable CA1050, CA1051, CA1711, CA1707, CA1822
public sealed class SkillReleaseContext { }
public abstract class SkillBase
{
    public ItemStatsSystem.Item fromItem = null!;
    public void ReleaseSkill(SkillReleaseContext releaseContext, CharacterMainControl from) { }
    public abstract void OnRelease();
}
public sealed class Skill_Grenade : SkillBase { public override void OnRelease() { } }
#pragma warning restore CA1050, CA1051, CA1711, CA1707, CA1822

namespace UltimateDuckovStatistics.Tests
{
    [Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
    public sealed class NativeThrowableTests : IDisposable
    {
        private readonly List<ItemUseRecorded> events = new();
        private readonly List<CapabilityRecord> capabilities = new();
        private readonly NativeThrowableAdapter adapter;
        private static MethodInfo Callback(string name) => typeof(NativeThrowableAdapter).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        public NativeThrowableTests()
        {
            HarmonyLib.Harmony.ClearAll();
            UnityEngine.Application.version = "2.3.30"; NativeRaidContext.GameplayContext = GameplayContext.Raid;
            adapter = new NativeThrowableAdapter(() => "g", () => "r", () => "m", () => "s", events.Add, capabilities.Add, _ => { });
            adapter.Initialize(); Assert.Equal(AdapterCapabilityState.Supported, capabilities[^1].State);
        }
        public void Dispose() { Assert.True(adapter.TryCleanup()); HarmonyLib.Harmony.ClearAll(); }
        private static Skill_Grenade Grenade(int id = 67) => new() { fromItem = new() { TypeID = id, DisplayName = "Grenade", StackCount = 4 } };
        private static object? Begin(SkillBase skill, bool player = true)
        { object?[] args = { skill, new CharacterMainControl { IsMainCharacter = player }, null }; Callback("BeginRelease").Invoke(null, args); return args[2]; }
        private static void Released(Skill_Grenade skill, bool ran = true) => Callback("Released").Invoke(null, new object[] { skill, ran });
        private static object? End(object? state, Exception? error = null) => Callback("EndRelease").Invoke(null, new[] { state, error });

        [Fact]
        public void NativeCallbacksRetainReleaseThroughLaterConsumptionFailureWithoutSwallowingException()
        {
            var skill = Grenade(); var state = Begin(skill); Released(skill); skill.fromItem.StackCount--;
            var error = new InvalidOperationException("Later subscriber failure");
            Assert.Same(error, End(state, error));
            var recorded = Assert.Single(events); Assert.Equal("duckov:item:67", recorded.ItemId);
            Assert.Equal(1, recorded.ActivationCount); Assert.Equal(1, recorded.AmountConsumed);
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capabilities[^1].State);
        }
        [Theory]
        [InlineData(false, true, 67)]
        [InlineData(true, false, 67)]
        [InlineData(true, true, 0)]
        public void NpcSkippedOriginalAndMissingIdentityCannotPublish(bool player, bool ran, int id)
        { var skill = Grenade(id); var state = Begin(skill, player); Released(skill, ran); End(state); Assert.Empty(events); }
        [Fact]
        public void NestedReleasesRemainSeparateAndCompletionIsIdempotent()
        {
            var first = Grenade(); var outer = Begin(first);
            var second = Grenade(68); var inner = Begin(second); Released(second); End(inner);
            Released(first); End(outer); End(outer);
            Assert.Equal<string>(["duckov:item:68", "duckov:item:67"], events.Select(e => e.ItemId));
            Assert.Equal(2, events.Select(e => e.EventId).Distinct().Count());
        }
        [Fact]
        public void CleanupDisarmsInFlightCallbacks()
        { var skill = Grenade(); var state = Begin(skill); Released(skill); Assert.True(adapter.TryCleanup()); End(state); Assert.Empty(events); }
        [Fact]
        public void ForeignPatchAddedDuringReleaseDisablesAttribution()
        {
            var skill = Grenade(); var state = Begin(skill); Released(skill);
            var foreign = new HarmonyLib.Harmony("foreign.throwable");
            foreign.Patch(typeof(SkillBase).GetMethod(nameof(SkillBase.ReleaseSkill))!,
                new HarmonyLib.HarmonyMethod(typeof(NativeThrowableTests).GetMethod(nameof(ForeignPrefix), BindingFlags.Static | BindingFlags.NonPublic)!), null, null, null);
            End(state); Assert.Empty(events); Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capabilities[^1].State);
        }
        private static void ForeignPrefix() { }
    }
}
