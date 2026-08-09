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

        [Fact]
        [Trait("Category", "Healing")]
        public void FailedCleanupRetainsLeaseAndCanBeRetried()
        {
            HarmonyLib.Harmony.ClearAll();
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(out var patcher, out var createDetail), createDetail);
            Assert.NotNull(patcher);

            var target = Method(nameof(Target));
            var prefix = Method(nameof(Prefix));
            patcher.Patch(target, prefix);
            var lease = new RetryableHarmonyPatcherLease();
            lease.Attach(patcher);
            HarmonyLib.Harmony.FailNextUnpatches(1);

            Assert.False(lease.TryCleanup(out var failedDetail));
            Assert.Contains("Injected UnpatchAll failure", failedDetail, StringComparison.Ordinal);
            Assert.True(lease.HasValue);
            Assert.Same(patcher, lease.Value);
            Assert.True(ReflectiveHarmonyPatcher.HasPendingCleanup);
            Assert.Single(Assert.IsType<HarmonyLib.Patches>(HarmonyLib.Harmony.GetPatchInfo(target)).Prefixes);

            Assert.True(lease.TryCleanup(out var retryDetail), retryDetail);
            Assert.False(lease.HasValue);
            Assert.Null(lease.Value);
            Assert.False(ReflectiveHarmonyPatcher.HasPendingCleanup);
            Assert.Empty(Assert.IsType<HarmonyLib.Patches>(HarmonyLib.Harmony.GetPatchInfo(target)).Prefixes);
            Assert.Equal(2, HarmonyLib.Harmony.UnpatchAttempts);
        }

        [Fact]
        [Trait("Category", "Healing")]
        public void PendingCleanupBlocksReactivationUntilARegisteredRetrySucceeds()
        {
            HarmonyLib.Harmony.ClearAll();
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(out var patcher, out var createDetail), createDetail);
            Assert.NotNull(patcher);

            var target = Method(nameof(Target));
            patcher.Patch(target, Method(nameof(Prefix)));
            HarmonyLib.Harmony.FailNextUnpatches(2);

            Assert.False(patcher.TryDispose(out var initialFailure));
            Assert.Contains("Injected UnpatchAll failure", initialFailure, StringComparison.Ordinal);
            Assert.True(ReflectiveHarmonyPatcher.HasPendingCleanup);

            Assert.False(ReflectiveHarmonyPatcher.TryCreate(out var blockedPatcher, out var blockedDetail));
            Assert.Null(blockedPatcher);
            Assert.Contains("previous UDS activation is still pending", blockedDetail, StringComparison.Ordinal);
            Assert.True(ReflectiveHarmonyPatcher.HasPendingCleanup);

            Assert.True(ReflectiveHarmonyPatcher.TryCreate(out var replacement, out var retryDetail), retryDetail);
            Assert.NotNull(replacement);
            Assert.False(ReflectiveHarmonyPatcher.HasPendingCleanup);
            Assert.Empty(Assert.IsType<HarmonyLib.Patches>(HarmonyLib.Harmony.GetPatchInfo(target)).Prefixes);
            Assert.Equal(3, HarmonyLib.Harmony.UnpatchAttempts);
            replacement.Dispose();
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
        private static int unpatchFailuresRemaining;
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
            UnpatchAttempts++;
            if (unpatchFailuresRemaining > 0)
            {
                unpatchFailuresRemaining--;
                throw new InvalidOperationException("Injected UnpatchAll failure.");
            }

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

        public static int UnpatchAttempts { get; private set; }

        public static void FailNextUnpatches(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            unpatchFailuresRemaining = count;
        }

        public static void ClearAll()
        {
            Registry.Clear();
            unpatchFailuresRemaining = 0;
            UnpatchAttempts = 0;
        }

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
