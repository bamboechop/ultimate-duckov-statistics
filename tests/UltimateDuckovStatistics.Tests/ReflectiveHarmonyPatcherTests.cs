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
        [Trait("Category", "Performance")]
        public void PatchStateStampChangesWithoutDeserializingPatchMetadata()
        {
            HarmonyLib.Harmony.ClearAll();
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(out var patcher, out var createDetail), createDetail);
            Assert.NotNull(patcher);
            using (patcher)
            {
                var target = Method(nameof(Target));
                var prefix = Method(nameof(Prefix));
                HarmonyPatchExpectation[] expected = [new("Prefixes", prefix)];
                patcher.Patch(target, prefix);
                Assert.True(patcher.IsPatchSetTrusted(target, expected, out var installedDetail), installedDetail);
                Assert.True(patcher.TryCapturePatchSetStamp(target, out var stamp, out var captureDetail), captureDetail);
                Assert.NotNull(stamp);
                Assert.True(patcher.IsPatchSetStampCurrent(stamp, out var currentDetail), currentDetail);

                var foreign = new HarmonyLib.Harmony("foreign.mod");
                foreign.Patch(
                    target,
                    new HarmonyLib.HarmonyMethod(Method(nameof(ForeignPrefix))),
                    postfix: null,
                    transpiler: null,
                    finalizer: null);

                Assert.False(patcher.IsPatchSetStampCurrent(stamp, out var changedDetail));
                Assert.Contains("changed", changedDetail, StringComparison.Ordinal);
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        [Trait("Category", "Compatibility")]
        public void ValidatedPatchStateStampRejectsPatchArrivingAfterMetadataSnapshot()
        {
            HarmonyLib.Harmony.ClearAll();
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(out var patcher, out var createDetail), createDetail);
            Assert.NotNull(patcher);
            using (patcher)
            {
                var target = Method(nameof(Target));
                var prefix = Method(nameof(Prefix));
                HarmonyPatchExpectation[] expected = [new("Prefixes", prefix)];
                patcher.Patch(target, prefix);
                var foreign = new HarmonyLib.Harmony("foreign.mod");
                HarmonyLib.Harmony.AfterGetPatchInfo = () => foreign.Patch(
                    target,
                    new HarmonyLib.HarmonyMethod(Method(nameof(ForeignPrefix))),
                    postfix: null,
                    transpiler: null,
                    finalizer: null);

                Assert.False(patcher.TryCaptureValidatedPatchSetStamp(
                    target,
                    expected,
                    out var stamp,
                    out var detail));
                Assert.Null(stamp);
                Assert.Contains("changed", detail, StringComparison.Ordinal);
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void PatchStateStampRejectsAnotherPatcherAndMissingSharedState()
        {
            HarmonyLib.Harmony.ClearAll();
            Assert.True(ReflectiveHarmonyPatcher.TryCreate("uds.stamp.first", out var first, out var firstDetail), firstDetail);
            Assert.True(ReflectiveHarmonyPatcher.TryCreate("uds.stamp.second", out var second, out var secondDetail), secondDetail);
            Assert.NotNull(first);
            Assert.NotNull(second);
            using (first)
            using (second)
            {
                var target = Method(nameof(Target));
                first.Patch(target, Method(nameof(Prefix)));
                Assert.True(first.TryCapturePatchSetStamp(target, out var stamp, out var captureDetail), captureDetail);
                Assert.NotNull(stamp);

                Assert.False(second.IsPatchSetStampCurrent(stamp, out var ownerDetail));
                Assert.Contains("another patcher", ownerDetail, StringComparison.Ordinal);

                HarmonyLib.HarmonySharedState.Clear();
                Assert.False(first.IsPatchSetStampCurrent(stamp, out var missingDetail));
                Assert.Contains("changed", missingDetail, StringComparison.Ordinal);
            }
        }

        [Fact]
        [Trait("Category", "Performance")]
        public void TrustedPatchStateStampCheckDoesNotAllocate()
        {
            HarmonyLib.Harmony.ClearAll();
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(out var patcher, out var createDetail), createDetail);
            Assert.NotNull(patcher);
            using (patcher)
            {
                var target = Method(nameof(Target));
                patcher.Patch(target, Method(nameof(Prefix)));
                Assert.True(patcher.TryCapturePatchSetStamp(target, out var stamp, out var captureDetail), captureDetail);
                Assert.NotNull(stamp);

                for (var index = 0; index < 64; index++)
                {
                    Assert.True(patcher.IsPatchSetStampCurrent(stamp, out _));
                }

                var before = GC.GetAllocatedBytesForCurrentThread();
                var allCurrent = true;
                for (var index = 0; index < 10_000; index++)
                {
                    allCurrent &= patcher.IsPatchSetStampCurrent(stamp, out _);
                }
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.True(allCurrent);
                Assert.Equal(0, allocated);
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

        [Fact]
        [Trait("Category", "Combat")]
        public void DistinctOwnersKeepCleanupAndReactivationIsolated()
        {
            HarmonyLib.Harmony.ClearAll();
            const string firstOwner = "uds.test.first";
            const string secondOwner = "uds.test.second";
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(firstOwner, out var first, out var firstDetail), firstDetail);
            Assert.NotNull(first);
            first.Patch(Method(nameof(Target)), Method(nameof(Prefix)));
            HarmonyLib.Harmony.FailNextUnpatches(1);

            Assert.False(first.TryDispose(out var failedDetail));
            Assert.Contains("Injected UnpatchAll failure", failedDetail, StringComparison.Ordinal);
            Assert.True(ReflectiveHarmonyPatcher.TryCreate(secondOwner, out var second, out var secondDetail), secondDetail);
            Assert.NotNull(second);
            second.Patch(Method(nameof(TargetTwo)), Method(nameof(Postfix)));
            second.Dispose();

            Assert.True(ReflectiveHarmonyPatcher.TryCreate(firstOwner, out var replacement, out var retryDetail), retryDetail);
            Assert.NotNull(replacement);
            Assert.Empty(Assert.IsType<HarmonyLib.Patches>(HarmonyLib.Harmony.GetPatchInfo(Method(nameof(Target)))).Prefixes);
            replacement.Dispose();
            Assert.False(ReflectiveHarmonyPatcher.HasPendingCleanup);
        }

        private static MethodInfo Method(string name) => typeof(ReflectiveHarmonyPatcherTests).GetMethod(
            name,
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(ReflectiveHarmonyPatcherTests).FullName, name);

        private static void Target()
        {
        }

        private static void TargetTwo()
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

    internal static class HarmonySharedState
    {
        private static readonly Dictionary<MethodBase, byte[]> state = new();

        public static void Update(MethodBase original) => state[original] = new byte[1];

        public static void Clear() => state.Clear();
    }

    public sealed class Harmony
    {
        private static readonly Dictionary<MethodBase, Patches> Registry = new();
        private static int unpatchFailuresRemaining;
        private readonly string owner;

        internal static Action? AfterGetPatchInfo { get; set; }

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
            HarmonySharedState.Update(original);
        }

        public void UnpatchAll(string ownerId)
        {
            UnpatchAttempts++;
            if (unpatchFailuresRemaining > 0)
            {
                unpatchFailuresRemaining--;
                throw new InvalidOperationException("Injected UnpatchAll failure.");
            }

            foreach (var entry in Registry)
            {
                var patches = entry.Value;
                var previousCount = patches.Prefixes.Count
                                    + patches.Postfixes.Count
                                    + patches.Transpilers.Count
                                    + patches.Finalizers.Count;
                patches.Prefixes.RemoveAll(patch => patch.owner == ownerId);
                patches.Postfixes.RemoveAll(patch => patch.owner == ownerId);
                patches.Transpilers.RemoveAll(patch => patch.owner == ownerId);
                patches.Finalizers.RemoveAll(patch => patch.owner == ownerId);
                var currentCount = patches.Prefixes.Count
                                   + patches.Postfixes.Count
                                   + patches.Transpilers.Count
                                   + patches.Finalizers.Count;
                if (currentCount != previousCount) HarmonySharedState.Update(entry.Key);
            }
        }

        public static Patches? GetPatchInfo(MethodBase original)
        {
            if (!Registry.TryGetValue(original, out var patches)) return null;
            var afterGetPatchInfo = AfterGetPatchInfo;
            if (afterGetPatchInfo == null) return patches;
            var snapshot = Clone(patches);
            AfterGetPatchInfo = null;
            afterGetPatchInfo();
            return snapshot;
        }

        public static int UnpatchAttempts { get; private set; }

        public static void FailNextUnpatches(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            unpatchFailuresRemaining = count;
        }

        public static void ClearAll()
        {
            Registry.Clear();
            HarmonySharedState.Clear();
            AfterGetPatchInfo = null;
            unpatchFailuresRemaining = 0;
            UnpatchAttempts = 0;
        }

        private static Patches Clone(Patches source)
        {
            var result = new Patches();
            result.Prefixes.AddRange(source.Prefixes);
            result.Postfixes.AddRange(source.Postfixes);
            result.Transpilers.AddRange(source.Transpilers);
            result.Finalizers.AddRange(source.Finalizers);
            return result;
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
