using System.Reflection;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class HarmonyPatchSetInspectorTests
{
    private const string Owner = "at.bamboechop.ultimate-duckov-statistics.healing";

    [Fact]
    [Trait("Category", "Healing")]
    public void ExactOwnedPatchSetIsTrusted()
    {
        var patchInfo = CreateExactPatchSet();

        var trusted = HarmonyPatchSetInspector.TryValidate(
            patchInfo,
            Owner,
            ExpectedPatches(),
            out var detail);

        Assert.True(trusted, detail);
    }

    [Theory]
    [Trait("Category", "Healing")]
    [InlineData("Prefixes")]
    [InlineData("Postfixes")]
    [InlineData("Transpilers")]
    [InlineData("Finalizers")]
    public void EveryForeignPatchCategoryDisablesAttribution(string collectionName)
    {
        var patchInfo = CreateExactPatchSet();
        patchInfo.Collection(collectionName).Add(new FakePatch("foreign.mod", Callback(nameof(ForeignCallback))));

        var trusted = HarmonyPatchSetInspector.TryValidate(
            patchInfo,
            Owner,
            ExpectedPatches(),
            out var detail);

        Assert.False(trusted);
        Assert.Contains(collectionName, detail, StringComparison.Ordinal);
        Assert.Contains("foreign.mod", detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void RemovedRequiredCallbackDisablesAttribution()
    {
        var patchInfo = CreateExactPatchSet();
        patchInfo.Postfixes.Clear();

        var trusted = HarmonyPatchSetInspector.TryValidate(
            patchInfo,
            Owner,
            ExpectedPatches(),
            out var detail);

        Assert.False(trusted);
        Assert.Contains("Required UDS patch is missing", detail, StringComparison.Ordinal);
        Assert.Contains(nameof(HealthPostfix), detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void ReplacedOrDuplicateOwnedCallbackDisablesAttribution()
    {
        var replaced = CreateExactPatchSet();
        replaced.Prefixes[0] = new FakePatch(Owner, Callback(nameof(ForeignCallback)));
        Assert.False(HarmonyPatchSetInspector.TryValidate(
            replaced,
            Owner,
            ExpectedPatches(),
            out var replacementDetail));
        Assert.Contains("Unexpected UDS Harmony patch", replacementDetail, StringComparison.Ordinal);

        var duplicated = CreateExactPatchSet();
        duplicated.Prefixes.Add(new FakePatch(Owner, Callback(nameof(HealthPrefix))));
        Assert.False(HarmonyPatchSetInspector.TryValidate(
            duplicated,
            Owner,
            ExpectedPatches(),
            out var duplicateDetail));
        Assert.Contains("Duplicate UDS Harmony patch", duplicateDetail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void EmptyPreflightIsTrustedButCannotSatisfyInstalledExpectations()
    {
        Assert.True(HarmonyPatchSetInspector.TryValidate(
            patchInfo: null,
            Owner,
            Array.Empty<HarmonyPatchExpectation>(),
            out var preflightDetail), preflightDetail);

        Assert.False(HarmonyPatchSetInspector.TryValidate(
            patchInfo: null,
            Owner,
            ExpectedPatches(),
            out var installedDetail));
        Assert.Contains("Required UDS patch is missing", installedDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopedCompatibilityDoesNotExcuseMissingOrDuplicateOwnedCallbacks()
    {
        var info = CreateExactPatchSet();
        var allowed = new FakePatch("known.mod", Callback(nameof(ForeignCallback)));
        info.Prefixes.Add(allowed);
        bool Accept(string collection, object patch) => collection == "Prefixes" && ReferenceEquals(patch, allowed);
        Assert.True(HarmonyPatchSetInspector.TryValidate(info, Owner, ExpectedPatches(), out var detail, Accept), detail);

        info.Postfixes.Clear();
        Assert.False(HarmonyPatchSetInspector.TryValidate(info, Owner, ExpectedPatches(), out detail, Accept));
        Assert.Contains("Required UDS patch is missing", detail, StringComparison.Ordinal);
        info.Postfixes.Add(new FakePatch(Owner, Callback(nameof(HealthPostfix))));
        info.Prefixes.Add(new FakePatch(Owner, Callback(nameof(HealthPrefix))));
        Assert.False(HarmonyPatchSetInspector.TryValidate(info, Owner, ExpectedPatches(), out detail, Accept));
        Assert.Contains("Duplicate UDS", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void KnownOwnerDoesNotApproveAnotherCallbackOrCollection()
    {
        var info = CreateExactPatchSet();
        var allowed = new FakePatch("known.mod", Callback(nameof(ForeignCallback)));
        info.Prefixes.Add(allowed);
        info.Finalizers.Add(new FakePatch("known.mod", Callback(nameof(HealthPrefix))));
        Assert.False(HarmonyPatchSetInspector.TryValidate(info, Owner, ExpectedPatches(), out var detail,
            (collection, patch) => collection == "Prefixes" && ReferenceEquals(patch, allowed)));
        Assert.Contains("Foreign Harmony patch in Finalizers", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateCompatibleCallbackIsRejected()
    {
        var info = CreateExactPatchSet();
        info.Prefixes.Add(new FakePatch("known.mod", Callback(nameof(ForeignCallback))));
        info.Prefixes.Add(new FakePatch("known.mod", Callback(nameof(ForeignCallback))));
        Assert.False(HarmonyPatchSetInspector.TryValidate(info, Owner, ExpectedPatches(), out var detail, (_, _) => true));
        Assert.Contains("Duplicate compatible", detail, StringComparison.Ordinal);
    }

    private static FakePatches CreateExactPatchSet() => new()
    {
        Prefixes = { new FakePatch(Owner, Callback(nameof(HealthPrefix))) },
        Postfixes = { new FakePatch(Owner, Callback(nameof(HealthPostfix))) }
    };

    private static HarmonyPatchExpectation[] ExpectedPatches() =>
    [
        new("Prefixes", Callback(nameof(HealthPrefix))),
        new("Postfixes", Callback(nameof(HealthPostfix)))
    ];

    private static MethodInfo Callback(string name) => typeof(HarmonyPatchSetInspectorTests).GetMethod(
        name,
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(HarmonyPatchSetInspectorTests).FullName, name);

    private static void HealthPrefix()
    {
    }

    private static void HealthPostfix()
    {
    }

    private static void ForeignCallback()
    {
    }

    private sealed class FakePatches
    {
        public List<FakePatch> Prefixes { get; } = new();

        public List<FakePatch> Postfixes { get; } = new();

        public List<FakePatch> Transpilers { get; } = new();

        public List<FakePatch> Finalizers { get; } = new();

        public List<FakePatch> Collection(string name) => name switch
        {
            "Prefixes" => Prefixes,
            "Postfixes" => Postfixes,
            "Transpilers" => Transpilers,
            "Finalizers" => Finalizers,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };
    }

    private sealed class FakePatch
    {
        public FakePatch(string owner, MethodInfo patchMethod)
        {
            this.owner = owner;
            PatchMethod = patchMethod;
        }

        public readonly string owner;

        public MethodInfo PatchMethod { get; }
    }
}
