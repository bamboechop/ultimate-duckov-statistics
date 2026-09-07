using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using Duckov.Modding;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests
{
    public sealed class IntegrityProbeTests
    {
        [Fact]
        public void ReadsLiveAuthoritativeModNamesAndCurrentRulesWithoutSnapshotStaleness()
        {
            var manager = new ModManager(); ModManager.Instance = manager;
            Duckov.CheatMode.Active = false;
            Duckov.Rules.GameRulesManager.SelectedRuleIndex = Duckov.Rules.RuleIndex.Normal;
            var uds = new ModBehaviour(ProductInfo.ModId);
            manager.Mods.Add("key-is-not-identity", uds);
            manager.Mods.Add("harmony", new ModBehaviour(RunIntegrityPolicy.HarmonyLoaderModId));
            manager.Mods.Add("destroyed", new ModBehaviour("foreign") { Destroyed = true });
            Assert.Equal(IntegrityTags.Normal, NativeIntegrityProbe.Read());
            uds.info.name = "foreign";
            Assert.Equal(IntegrityTags.ModdedContent, NativeIntegrityProbe.Read());
            uds.info.name = ProductInfo.ModId;
            manager.Mods.Add("foreign", new ModBehaviour("Foreign"));
            Assert.Equal(IntegrityTags.ModdedContent, NativeIntegrityProbe.Read());
            manager.Mods.Remove("foreign");
            Assert.Equal(IntegrityTags.Normal, NativeIntegrityProbe.Read());
            manager.Mods = new() { ["case"] = new ModBehaviour(ProductInfo.ModId.ToLowerInvariant()) };
            Assert.Equal(IntegrityTags.ModdedContent, NativeIntegrityProbe.Read());
            ModManager.Instance = new ModManager();
            Duckov.CheatMode.Active = true;
            Assert.Equal(IntegrityTags.CheatOrCustomDifficulty, NativeIntegrityProbe.Read());
            Duckov.CheatMode.Active = false;
            Duckov.Rules.GameRulesManager.SelectedRuleIndex = Duckov.Rules.RuleIndex.Custom;
            Assert.Equal(IntegrityTags.CheatOrCustomDifficulty, NativeIntegrityProbe.Read());
            ModManager.Instance = null;
            Assert.Equal(IntegrityTags.CheatOrCustomDifficulty, NativeIntegrityProbe.Read());
            Duckov.Rules.GameRulesManager.SelectedRuleIndex = Duckov.Rules.RuleIndex.Normal;
            Assert.Equal(IntegrityTags.Normal, NativeIntegrityProbe.Read());
        }

        [Fact]
        public void LaterNativeFailureDoesNotLeakAnEarlierGameplayClassification()
        {
            var manager = new ModManager(); ModManager.Instance = manager;
            manager.Mods.Add("valid-first", new ModBehaviour("foreign"));
            manager.Mods.Add("broken-second", new ModBehaviour("bad") { FailInfo = true });
            Assert.Equal(IntegrityTags.Unknown, NativeIntegrityProbe.Read());
            manager.Mods = null!;
            Assert.Equal(IntegrityTags.Unknown, NativeIntegrityProbe.Read());
        }

        [Fact]
        public void MissingWrongAndStaticFieldsFailClosedAndWarmedReadsAvoidCollectionAllocation()
        {
            Assert.Null(NativeIntegrityProbe.ResolveActiveModsField(typeof(object)));
            Assert.Null(NativeIntegrityProbe.ResolveActiveModsField(typeof(WrongField)));
            Assert.Null(NativeIntegrityProbe.ResolveActiveModsField(typeof(StaticField)));
            Assert.NotNull(NativeIntegrityProbe.ResolveActiveModsField(typeof(ModManager)));
            ModManager.Instance = new ModManager();
            ModManager.Instance.Mods.Add("uds", new ModBehaviour(ProductInfo.ModId));
            Duckov.CheatMode.Active = false;
            Duckov.Rules.GameRulesManager.SelectedRuleIndex = Duckov.Rules.RuleIndex.Normal;
            for (var i = 0; i < 10_000; i++) NativeIntegrityProbe.Read();
            var start = GC.GetAllocatedBytesForCurrentThread();
            var result = IntegrityTags.Unknown;
            for (var i = 0; i < 100_000; i++) result |= NativeIntegrityProbe.Read();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.Equal(IntegrityTags.Normal, result);
            Assert.InRange(allocated, 0, 128); // .NET test boundary; Unity Mono/rules getter still require native measurement.
        }
#pragma warning disable CS0169, CS0414 // Deliberately incompatible reflected contracts.
        private sealed class WrongField { private readonly List<ModBehaviour>? activeMods = null; }
        private sealed class StaticField { private static readonly Dictionary<string, ModBehaviour>? activeMods = null; }
#pragma warning restore CS0169, CS0414
    }
}
namespace Duckov { public static class CheatMode { public static bool Active; } }
namespace Duckov.Rules
{
    public enum RuleIndex { Normal, Custom }
    public static class GameRulesManager { public static RuleIndex SelectedRuleIndex; }
}
namespace Duckov.Modding
{
    public sealed class ModInfo { public string name = ""; }
    public sealed class ModBehaviour(string name) : UnityEngine.Object
    {
        private readonly ModInfo value = new() { name = name };
        public bool FailInfo;
        public ModInfo info => FailInfo ? throw new InvalidOperationException("native fixture failure") : value;
    }
    public sealed class ModManager : UnityEngine.Object
    {
        public static ModManager? Instance;
        private Dictionary<string, ModBehaviour> activeMods = new();
        public Dictionary<string, ModBehaviour> Mods { get => activeMods; set => activeMods = value; }
    }
}
