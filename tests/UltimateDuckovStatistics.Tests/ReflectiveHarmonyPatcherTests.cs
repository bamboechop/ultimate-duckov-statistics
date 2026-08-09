using System.Reflection;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Adapters
{
    public sealed class ReflectiveHarmonyPatcherTests
    {
        [Fact]
        [Trait("Category", "Healing")]
        public void ProductionPatcherRequiresExactOwnedCallbacksAndRejectsForeignPrefix()
        {
            HarmonyLib.Harmony.ClearAll();
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(out var patcher, out var createDetail), createDetail);
            Assert.NotNull(patcher);
            using (patcher)
            {
                var target = Method(nameof(Target));
                var prefix = Method(nameof(Prefix));
                var postfix = Method(nameof(Postfix));
                HarmonyPatchExpectation[] expected =
                [
                    new("Prefixes", prefix),
                    new("Postfixes", postfix)
                ];

                Assert.True(patcher.IsPatchSetTrusted(
                    target,
                    Array.Empty<HarmonyPatchExpectation>(),
                    out var preflightDetail), preflightDetail);

                patcher.Patch(target, prefix, postfix);

                Assert.True(patcher.IsPatchSetTrusted(target, expected, out var installedDetail), installedDetail);

                var foreign = new HarmonyLib.Harmony("foreign.mod");
                foreign.Patch(
                    target,
                    new HarmonyLib.HarmonyMethod(Method(nameof(ForeignPrefix))),
                    postfix: null,
                    transpiler: null,
                    finalizer: null);

                Assert.False(patcher.IsPatchSetTrusted(target, expected, out var foreignDetail));
                Assert.Contains("Prefixes", foreignDetail, StringComparison.Ordinal);
                Assert.Contains("foreign.mod", foreignDetail, StringComparison.Ordinal);

                var patches = Assert.IsType<HarmonyLib.Patches>(HarmonyLib.Harmony.GetPatchInfo(target));
                patches.Prefixes.RemoveAll(candidate => candidate.owner == "foreign.mod");
                patches.Postfixes.Clear();

                Assert.False(patcher.IsPatchSetTrusted(target, expected, out var removedDetail));
                Assert.Contains("Required UDS patch is missing", removedDetail, StringComparison.Ordinal);
                Assert.Contains(nameof(Postfix), removedDetail, StringComparison.Ordinal);
            }
        }

        private static MethodInfo Method(string name) => typeof(ReflectiveHarmonyPatcherTests).GetMethod(
            name,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(ReflectiveHarmonyPatcherTests).FullName, name);

        private static void Target()
        {
        }

        private static void Prefix()
        {
        }

        private static void Postfix()
        {
        }

        private static void ForeignPrefix()
        {
        }
    }
}

#pragma warning disable CA1051, CA1822, CA1859 // Faithful public-field Harmony reflection contract.
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public HarmonyMethod(MethodInfo method)
        {
            this.method = method;
        }

        public readonly MethodInfo method;

        public int priority;
    }

    public sealed class Patch
    {
        public Patch(string owner, MethodInfo patchMethod)
        {
            this.owner = owner;
            PatchMethod = patchMethod;
        }

        public readonly string owner;

        public MethodInfo PatchMethod { get; }
    }

    public sealed class Patches
    {
        public readonly List<Patch> Prefixes = new();

        public readonly List<Patch> Postfixes = new();

        public readonly List<Patch> Transpilers = new();

        public readonly List<Patch> Finalizers = new();
    }

    public sealed class Harmony
    {
        private static readonly Dictionary<MethodBase, Patches> Registry = new();
        private readonly string owner;

        public Harmony(string owner)
        {
            this.owner = owner;
        }

        public void Patch(
            MethodBase original,
            HarmonyMethod? prefix,
            HarmonyMethod? postfix,
            HarmonyMethod? transpiler,
            HarmonyMethod? finalizer)
        {
            if (!Registry.TryGetValue(original, out var patches))
            {
                patches = new Patches();
                Registry[original] = patches;
            }

            Add(patches.Prefixes, prefix);
            Add(patches.Postfixes, postfix);
            Add(patches.Transpilers, transpiler);
            Add(patches.Finalizers, finalizer);
        }

        public void UnpatchAll(string ownerId)
        {
            foreach (var patches in Registry.Values)
            {
                patches.Prefixes.RemoveAll(patch => patch.owner == ownerId);
                patches.Postfixes.RemoveAll(patch => patch.owner == ownerId);
                patches.Transpilers.RemoveAll(patch => patch.owner == ownerId);
                patches.Finalizers.RemoveAll(patch => patch.owner == ownerId);
            }
        }

        public static Patches? GetPatchInfo(MethodBase original) =>
            Registry.TryGetValue(original, out var patches) ? patches : null;

        public static void ClearAll() => Registry.Clear();

        private void Add(ICollection<Patch> patches, HarmonyMethod? method)
        {
            if (method != null)
            {
                patches.Add(new Patch(owner, method.method));
            }
        }
    }
}
#pragma warning restore CA1051, CA1822, CA1859
