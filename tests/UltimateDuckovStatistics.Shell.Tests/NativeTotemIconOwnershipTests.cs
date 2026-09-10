using ItemStatsSystem;
using LeTai.TrueShadow;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class NativeTotemIconOwnershipTests : IDisposable
{
    private const int TotemId = 3001;
    private readonly Sprite borrowedIcon = new();
    private readonly List<GameObject> roots = new();

    public NativeTotemIconOwnershipTests()
    {
        ItemAssetsCollection.Metadata[TotemId] = new ItemMetaData
        {
            id = TotemId,
            icon = borrowedIcon,
            displayQuality = 4,
            tags = new List<ItemTag> { new() { name = "Totem" } }
        };
    }

    [Fact]
    public void RepeatedTotemCreationAndNativeDestructionReleasePrivateMeshesAndPreserveNativeStyle()
    {
        var meshes = new List<Mesh>();
        for (var cycle = 0; cycle < 25; cycle++)
        {
            var icon = CreateIcon();
            NativeItemIconAppearance.Apply(icon, "duckov:totem:3001");
            var shadow = Assert.IsType<OwnedTotemIconShadow>(icon.GetComponent<TrueShadow>());
            shadow.NativeEnable();
            var mesh = Assert.IsType<Mesh>(shadow.SpriteMesh);
            meshes.Add(mesh);
            Assert.Equal(3, shadow.Size);
            Assert.Equal(.5f, shadow.Spread);
            Assert.True(shadow.UseCasterAlpha && shadow.IgnoreCasterColor && shadow.IgnoreExternalActive);
            Assert.False(shadow.ShadowAsSibling);
            Assert.Equal(4, shadow.AppliedQuality);
            shadow.NativeDisable();
            shadow.NativeDestroy();
            UnityEngine.Object.Destroy(icon.gameObject);
            Assert.True(mesh.Destroyed);
            Assert.Equal(1, shadow.NativeCleanupCount);
            Assert.False(borrowedIcon.Destroyed);
        }
        Assert.Equal(25, meshes.Distinct().Count());
        Assert.All(meshes, mesh => Assert.True(mesh.Destroyed));
    }

    [Fact]
    public void PooledTotemShadowReusesItsMeshAcrossClearAndReenable()
    {
        var icon = CreateIcon();
        NativeItemIconAppearance.Apply(icon, "duckov:totem:3001");
        var shadow = Assert.IsType<OwnedTotemIconShadow>(icon.GetComponent<TrueShadow>());
        shadow.NativeEnable();
        var mesh = Assert.IsType<Mesh>(shadow.SpriteMesh);
        for (var cycle = 0; cycle < 25; cycle++)
        {
            NativeItemIconAppearance.Clear(icon);
            shadow.NativeDisable();
            Assert.False(shadow.enabled);
            Assert.False(mesh.Destroyed);
            NativeItemIconAppearance.Apply(icon, "duckov:item:3001");
            shadow.NativeEnable();
            Assert.Same(shadow, Assert.Single(icon.GetComponents<TrueShadow>()));
            Assert.Same(mesh, shadow.SpriteMesh);
            Assert.True(shadow.enabled);
        }
        shadow.NativeDisable();
        shadow.NativeDestroy();
        Assert.True(mesh.Destroyed);
    }

    [Fact]
    public void CreatingAnotherTotemPreservesMaterialsAndMeshesOfRetainedRows()
    {
        var retained = Enumerable.Range(0, 4).Select(_ => CreateIcon()).ToArray();
        foreach (var icon in retained)
        {
            NativeItemIconAppearance.Apply(icon, "duckov:totem:3001");
            var shadow = Assert.IsType<OwnedTotemIconShadow>(icon.GetComponent<TrueShadow>());
            shadow.NativeEnable();
        }
        var shadows = retained.Select(icon => icon.GetComponent<TrueShadow>()).ToArray();
        foreach (var shadow in shadows) shadow.NativeRebuildMaterial();
        var meshes = shadows.Select(shadow => shadow.SpriteMesh).ToArray();
        var material = Assert.IsType<UnityEngine.Object>(shadows[0].RenderedMaskMaterial);

        // Expanding a row changes its control identity: the old row is pooled,
        // a new row is created, and the other visible rows remain retained.
        NativeItemIconAppearance.Clear(retained[0]);
        shadows[0].NativeDisable();
        var expanded = CreateIcon();
        NativeItemIconAppearance.Apply(expanded, "duckov:totem:3001");
        var expandedShadow = Assert.IsType<OwnedTotemIconShadow>(expanded.GetComponent<TrueShadow>());
        expandedShadow.NativeEnable();
        expandedShadow.NativeRebuildMaterial();
        Assert.False(material.Destroyed);
        Assert.All(shadows, shadow => Assert.Same(material, shadow.RenderedMaskMaterial));
        Assert.Same(material, expandedShadow.RenderedMaskMaterial);

        NativeItemIconAppearance.Apply(retained[0], "duckov:totem:3001");
        shadows[0].NativeEnable();
        for (var i = 0; i < shadows.Length; i++)
        {
            Assert.Same(meshes[i], shadows[i].SpriteMesh);
            Assert.False(meshes[i]!.Destroyed);
            shadows[i].NativeDestroy();
            Assert.True(meshes[i]!.Destroyed);
        }
        expandedShadow.NativeDestroy();
        Assert.False(material.Destroyed);
        Assert.False(borrowedIcon.Destroyed);
    }

    [Fact]
    public void ExistingNativeShadowDoesNotAcquireUdsMeshOwnership()
    {
        var icon = CreateIcon();
        var native = icon.gameObject.AddComponent<TrueShadow>();
        native.NativeEnable();
        var mesh = Assert.IsType<Mesh>(native.SpriteMesh);
        NativeItemIconAppearance.Apply(icon, "duckov:totem:3001");
        Assert.Same(native, Assert.Single(icon.GetComponents<TrueShadow>()));
        Assert.Null(icon.GetComponent<OwnedTotemIconShadow>());
        NativeItemIconAppearance.Clear(icon);
        Assert.False(mesh.Destroyed);
        Assert.False(borrowedIcon.Destroyed);
        UnityEngine.Object.Destroy(mesh);
    }

    [Fact]
    public void NativeCleanupFailureStillReleasesTheOwnedMesh()
    {
        var icon = CreateIcon();
        NativeItemIconAppearance.Apply(icon, "duckov:totem:3001");
        var shadow = Assert.IsType<OwnedTotemIconShadow>(icon.GetComponent<TrueShadow>());
        shadow.NativeEnable();
        var mesh = Assert.IsType<Mesh>(shadow.SpriteMesh);
        shadow.ThrowDuringNativeCleanup = true;
        Assert.Throws<InvalidOperationException>(shadow.NativeDestroy);
        Assert.True(mesh.Destroyed);
        Assert.Equal(1, shadow.NativeCleanupCount);
        Assert.False(borrowedIcon.Destroyed);
    }

    [Theory]
    [InlineData("duckov:totem:unknown")]
    [InlineData("duckov:item:3002")]
    public void UnavailableMetadataDoesNotAllocateAShadow(string id)
    {
        var icon = CreateIcon();
        NativeItemIconAppearance.Apply(icon, id);
        Assert.Empty(icon.GetComponents<TrueShadow>());
    }

    private Image CreateIcon()
    {
        var root = new GameObject("UDS totem image");
        roots.Add(root);
        var icon = root.AddComponent<Image>();
        icon.sprite = borrowedIcon;
        return icon;
    }

    public void Dispose()
    {
        ItemAssetsCollection.Metadata.Remove(TotemId);
        foreach (var root in roots) UnityEngine.Object.Destroy(root);
    }
}
