using Duckov.Utilities;
using ItemStatsSystem;
using LeTai.TrueShadow;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class NativeAmmunitionIconAppearanceTests : IDisposable
{
    private readonly GameObject root = new("UDS ammunition icon");
    private readonly Sprite first = new(), second = new();

    [Theory]
    [InlineData("duckov:ammo:3002", 0)]
    [InlineData("duckov:ammo:3002", 2)]
    [InlineData("duckov:ammo:3002", 4)]
    [InlineData("duckov:item:3002", 0)]
    [InlineData("duckov:item:3002", 2)]
    [InlineData("duckov:item:3002", 4)]
    public void AmmunitionUsesExactNativeDisplayQualityForCombatAndOtherItemViews(string id, int quality)
    {
        var icon = Setup(quality);
        NativeItemIconAppearance.Apply(icon, id);
        var shadow = Assert.IsType<OwnedTotemIconShadow>(icon.GetComponent<TrueShadow>());
        Assert.Equal(quality, shadow.AppliedQuality);
        Assert.Equal(3, shadow.Size); Assert.Equal(.5f, shadow.Spread);
        Assert.True(shadow.UseCasterAlpha && shadow.IgnoreCasterColor && shadow.IgnoreExternalActive);
        Assert.False(shadow.ShadowAsSibling);
        Assert.Same(first, icon.sprite); Assert.Equal(Color.white, icon.color);
    }

    [Fact]
    public void RecycledAmmoIconChangesQualityAndClearsGlowWithoutAddingOwnersOrLeakingMesh()
    {
        var icon = Setup(2);
        NativeItemIconAppearance.Apply(icon, "duckov:ammo:3002");
        var shadow = Assert.IsType<OwnedTotemIconShadow>(icon.GetComponent<TrueShadow>());
        shadow.NativeEnable(); var mesh = Assert.IsType<Mesh>(shadow.SpriteMesh);
        ItemAssetsCollection.Metadata[3003] = Metadata(second, 4, 3003);
        icon.sprite = second;
        NativeItemIconAppearance.Apply(icon, "duckov:ammo:3003");
        Assert.Same(shadow, Assert.Single(icon.GetComponents<TrueShadow>())); Assert.Equal(4, shadow.AppliedQuality);
        Assert.Same(mesh, shadow.SpriteMesh);
        NativeItemIconAppearance.Clear(icon); shadow.NativeDisable();
        Assert.False(shadow.enabled); Assert.False(mesh.Destroyed);
        NativeItemIconAppearance.Apply(icon, "duckov:item:3003"); shadow.NativeEnable();
        Assert.True(shadow.enabled); Assert.Same(mesh, shadow.SpriteMesh);
        icon.sprite = null; NativeItemIconAppearance.Apply(icon, "duckov:ammo:3003");
        Assert.False(shadow.enabled);
        shadow.NativeDestroy(); Assert.True(mesh.Destroyed);
        Assert.False(first.Destroyed); Assert.False(second.Destroyed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-id")]
    [InlineData("wrong-sprite")]
    [InlineData("unrelated-tag")]
    public void MissingOrUnrelatedMetadataCannotKeepAnOldAmmoGlow(string failure)
    {
        var icon = Setup(2);
        NativeItemIconAppearance.Apply(icon, "duckov:ammo:3002");
        var shadow = icon.GetComponent<TrueShadow>();
        var metadata = ItemAssetsCollection.Metadata[3002];
        if (failure == "missing") ItemAssetsCollection.Metadata.Remove(3002);
        else if (failure == "wrong-id") metadata.id = 3003;
        else if (failure == "wrong-sprite") metadata.icon = second;
        else metadata.tags = new() { new() { name = GameplayDataSettings.Tags.Bullet.name } };
        NativeItemIconAppearance.Apply(icon, "duckov:ammo:3002");
        Assert.False(shadow.enabled); Assert.Same(first, icon.sprite);
    }

    private Image Setup(int quality)
    {
        ItemAssetsCollection.Metadata[3002] = Metadata(first, quality, 3002);
        var icon = root.AddComponent<Image>(); icon.sprite = first; icon.color = Color.white;
        return icon;
    }
    private static ItemMetaData Metadata(Sprite sprite, int quality, int id) => new()
    { id = id, icon = sprite, displayQuality = quality, tags = new() { GameplayDataSettings.Tags.Bullet } };
    public void Dispose()
    {
        ItemAssetsCollection.Metadata.Remove(3002); ItemAssetsCollection.Metadata.Remove(3003);
        UnityEngine.Object.Destroy(root);
    }
}
