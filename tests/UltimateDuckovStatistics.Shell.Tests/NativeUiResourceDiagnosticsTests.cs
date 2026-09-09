#if UDS_PERFORMANCE_DIAGNOSTICS
using ItemStatsSystem;
using LeTai.TrueShadow;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class NativeUiResourceDiagnosticsTests : IDisposable
{
    private readonly NativeUiResourceDiagnostics diagnostics = new();
    private readonly List<string> messages = new();
    private readonly List<GameObject> roots = new();
    private readonly Sprite borrowedIcon = new();

    public NativeUiResourceDiagnosticsTests()
    {
        ItemAssetsCollection.Metadata[3001] = new ItemMetaData
        {
            id = 3001, icon = borrowedIcon, displayQuality = 4,
            tags = new List<ItemTag> { new() { name = "Totem" } }
        };
    }

    [Fact]
    public void OpenSnapshotTracksExactMeshAndLaterClosedSnapshotObservesItsDestruction()
    {
        var root = Root(RetainedDimmerPolicy.RootName);
        var blocker = Root("UDS native menu input owner");
        var shadow = Shadow(root);
        var mesh = Assert.IsType<Mesh>(shadow.SpriteMesh);
        var pair = Pair(shadow, mesh);
        Time.frameCount = 10;
        diagnostics.WriteSnapshot(messages.Add);
        Assert.Contains("sequence=1", messages[0]);
        Assert.Contains("frame=10", messages[0]);
        Assert.Contains("totemOwners=[" + pair + ":active]", messages[0]);
        Assert.Contains("udsRoots=[" + root.GetInstanceID() + ":active]", messages[0]);
        Assert.Contains("udsInputOwners=[" + blocker.GetInstanceID() + ":active]", messages[0]);

        shadow.NativeDisable();
        shadow.NativeDestroy();
        UnityEngine.Object.Destroy(root);
        UnityEngine.Object.Destroy(blocker);
        Time.frameCount = 12;
        diagnostics.WriteSnapshot(messages.Add);
        Assert.Contains("sequence=2", messages[1]);
        Assert.Contains("frame=12", messages[1]);
        Assert.Contains("totemOwners=[]", messages[1]);
        Assert.Contains("udsRoots=[] udsInputOwners=[]", messages[1]);
        Assert.Contains("observedMeshesNowAbsent=[" + pair + "]", messages[1]);
        Assert.Contains("destroyedOwnerMeshCandidates=[]", messages[1]);
        Assert.Contains("trackedMeshes=0 missingOwnerMeshes=0 trackingComplete=true", messages[1]);
    }

    [Fact]
    public void MeshSurvivingItsOwnerRemainsDetectableUntilNativeDestructionFinishes()
    {
        var root = Root(RetainedDimmerPolicy.RootName);
        var shadow = Shadow(root);
        var mesh = Assert.IsType<Mesh>(shadow.SpriteMesh);
        var pair = Pair(shadow, mesh);
        diagnostics.WriteSnapshot(messages.Add);
        // Model the observed native bug or a still-pending Unity Destroy: the
        // component is gone from the census while its separately allocated mesh lives.
        UnityEngine.Object.Destroy(root);
        diagnostics.WriteSnapshot(messages.Add);
        diagnostics.WriteSnapshot(messages.Add);
        Assert.All(messages.Skip(1), message =>
        {
            Assert.Contains("totemOwners=[]", message);
            Assert.Contains("destroyedOwnerMeshCandidates=[" + pair + "]", message);
            Assert.Contains("trackedMeshes=1", message);
        });
        UnityEngine.Object.Destroy(mesh);
        diagnostics.WriteSnapshot(messages.Add);
        Assert.Contains("observedMeshesNowAbsent=[" + pair + "]", messages[3]);
        Assert.Contains("destroyedOwnerMeshCandidates=[]", messages[3]);
        Assert.Contains("trackedMeshes=0", messages[3]);
    }

    [Fact]
    public void GlobalResourceGrowthIsReportedWithoutAttributingItToAnOwnedShadow()
    {
        Resources.AdditionalObjects.Add(new Mesh());
        Resources.AdditionalObjects.Add(new Material());
        Resources.AdditionalObjects.Add(new Texture());
        diagnostics.WriteSnapshot(messages.Add);
        Assert.Contains("meshes=1 materials=1 textures=1", messages[0]);
        Assert.Contains("totemOwners=[]", messages[0]);
        Assert.Contains("destroyedOwnerMeshCandidates=[]", messages[0]);
        Assert.Contains("globalCountsAreNotAttribution=true candidateIdsMayBeReused=true", messages[0]);
    }

    [Fact]
    public void HiddenOwnerRetainsTheSameMeshWithoutBeingReportedAsDestroyed()
    {
        var root = Root(RetainedDimmerPolicy.RootName);
        var shadow = Shadow(root);
        var mesh = Assert.IsType<Mesh>(shadow.SpriteMesh);
        diagnostics.WriteSnapshot(messages.Add);
        root.SetActive(false);
        shadow.NativeDisable();
        diagnostics.WriteSnapshot(messages.Add);
        Assert.Contains("totemOwners=[" + Pair(shadow, mesh) + ":inactive]", messages[1]);
        Assert.Contains("destroyedOwnerMeshCandidates=[]", messages[1]);
        Assert.Contains("observedMeshesNowAbsent=[]", messages[1]);
        shadow.NativeDestroy();
    }

    private GameObject Root(string name)
    {
        var root = new GameObject(name);
        roots.Add(root);
        return root;
    }

    private OwnedTotemIconShadow Shadow(GameObject root)
    {
        var icon = root.AddComponent<Image>();
        icon.sprite = borrowedIcon;
        NativeTotemIconAppearance.Apply(icon, "duckov:totem:3001");
        var shadow = Assert.IsType<OwnedTotemIconShadow>(root.GetComponent<TrueShadow>());
        shadow.NativeEnable();
        Resources.AdditionalObjects.Add(Assert.IsType<Mesh>(shadow.SpriteMesh));
        return shadow;
    }

    private static string Pair(OwnedTotemIconShadow shadow, Mesh mesh) => shadow.GetInstanceID() + ":" + mesh.GetInstanceID();

    public void Dispose()
    {
        foreach (var root in roots) UnityEngine.Object.Destroy(root);
        Resources.AdditionalObjects.Clear();
        ItemAssetsCollection.Metadata.Remove(3001);
    }
}
#endif
