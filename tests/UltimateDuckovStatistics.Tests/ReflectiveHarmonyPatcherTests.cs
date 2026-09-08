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
