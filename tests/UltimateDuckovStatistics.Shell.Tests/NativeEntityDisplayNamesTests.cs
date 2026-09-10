using Duckov.Utilities;
using ItemStatsSystem;
using SodaCraft.Localizations;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class NativeEntityDisplayNamesTests : IDisposable
{
    public NativeEntityDisplayNamesTests()
    {
        LocalizationManager.Initialized = true; LocalizationManager.DataModel = new();
        LocalizationManager.SetLanguage(SystemLanguage.English);
    }

    [Theory]
    [InlineData("duckov:weapon:357")]
    [InlineData("duckov:item:357")]
    [InlineData("duckov:ammo:357")]
    [InlineData("duckov:totem:357")]
    [InlineData("357")]
    public void NativeItemNamesFollowEveryLanguageEventAndReopen(string id)
    {
        ItemAssetsCollection.Metadata[357] = new() { id = 357, DisplayNameKey = "BowKey" };
        LocalizationManager.Translations[(SystemLanguage.English, "BowKey")] = "Scrap Bow";
        LocalizationManager.Translations[(SystemLanguage.German, "BowKey")] = "Schrott-Bogen";
        var before = LocalizationManager.Listeners;
        using var resolver = new NativeEntityDisplayNames();
        var changes = 0; resolver.Changed += () => changes++;
        Assert.Equal(before + 1, LocalizationManager.Listeners);
        Assert.Equal("Scrap Bow", resolver.Names.Get(id, "Schrott-Bogen"));
        var reads = LocalizationManager.Reads;
        Assert.Equal("Scrap Bow", resolver.Names.Get(id, "old"));
        Assert.Equal(reads, LocalizationManager.Reads);
        LocalizationManager.SetLanguage(SystemLanguage.German);
        Assert.Equal("Schrott-Bogen", resolver.Names.Get(id, "old"));
        LocalizationManager.Translations[(SystemLanguage.German, "BowKey")] = "Reloaded";
        LocalizationManager.SetLanguage(SystemLanguage.German);
        Assert.Equal("Reloaded", resolver.Names.Get(id, "old"));
        Assert.Equal(2, changes);
        LocalizationManager.Translations[(SystemLanguage.German, "BowKey")] = "Override";
        resolver.Invalidate();
        Assert.Equal("Override", resolver.Names.Get(id, "old"));
        resolver.Dispose();
        Assert.Equal(before, LocalizationManager.Listeners);
        Assert.Equal("recorded", resolver.Names.Get(id, "recorded"));
    }

    [Fact]
    public void MissingMetadataAndUninitializedLocalizationRetainEachRecordedFallback()
    {
        using var resolver = new NativeEntityDisplayNames();
        Assert.Equal("first", resolver.Names.Get("duckov:item:357", "first"));
        Assert.Equal("second", resolver.Names.Get("duckov:item:357", "second"));
        ItemAssetsCollection.Metadata[357] = new() { id = 999, DisplayNameKey = "BowKey" };
        resolver.Invalidate();
        Assert.Equal("recorded", resolver.Names.Get("357", "recorded"));
        ItemAssetsCollection.Metadata[357].id = 357; resolver.Invalidate();
        Assert.Equal("recorded", resolver.Names.Get("357", "recorded")); // Exact *key* native missing sentinel.
        LocalizationManager.Translations[(SystemLanguage.English, "BowKey")] = "Bow";
        LocalizationManager.Initialized = false; LocalizationManager.DataModel = null;
        var reads = LocalizationManager.Reads;
        resolver.Invalidate();
        Assert.Equal("recorded", resolver.Names.Get("357", "recorded"));
        Assert.Equal(reads, LocalizationManager.Reads);
        LocalizationManager.Initialized = true; LocalizationManager.DataModel = new();
        Assert.Equal("Bow", resolver.Names.Get("357", "recorded"));
        Assert.Equal("unknown", resolver.Names.Get("duckov:weapon:unknown", "unknown"));
        Assert.Equal("foreign", resolver.Names.Get("mod:item:357", "foreign"));
    }

    [Fact]
    public void MapsAndPresetEnemiesUseOriginalNativeKeysAndRejectTokenCollisions()
    {
        SceneInfoCollection.Scenes["Scene_Zero"] = new() { ID = "Scene_Zero", DisplayNameRaw = "MapZero" };
        LocalizationManager.Translations[(SystemLanguage.English, "MapZero")] = "Ground Zero";
        GameplayDataSettings.CharacterRandomPresetData!.presets.Add(new() { nameKey = "Cname_RobSpider" });
        GameplayDataSettings.CharacterRandomPresetData.presets.Add(new() { nameKey = "Cname_RobSpider" });
        LocalizationManager.Translations[(SystemLanguage.English, "Cname_RobSpider")] = "Spider Bot";
        using var resolver = new NativeEntityDisplayNames();
        Assert.Equal("Ground Zero", resolver.Names.Get("duckov:map:Scene_Zero", "Nullpunkt"));
        Assert.Equal("saved", resolver.Names.Get("duckov:map:scene_zero", "saved"));
        Assert.Equal("Spider Bot", resolver.Names.Get("duckov:target:preset:cname-robspider", "saved"));
        Assert.Equal("Spider Bot", resolver.Names.Get("duckov:attacker:preset:cname-robspider", "saved"));
        Assert.Equal("saved", resolver.Names.Get("duckov:attacker:fallback:cname-robspider", "saved"));
        GameplayDataSettings.CharacterRandomPresetData.presets.Add(new() { nameKey = "Cname-RobSpider" });
        resolver.Invalidate();
        Assert.Equal("saved", resolver.Names.Get("duckov:target:preset:cname-robspider", "saved"));
    }

    [Fact]
    public void SlotNamesRequireExactParentAndUniqueKey()
    {
        ItemAssetsCollection.Prefabs[1] = new() { TypeID = 1, Slots = new() { new() { Key = "PrimaryWeapon", DisplayName = "Weapon 1" } } };
        ItemAssetsCollection.Prefabs[357] = new() { TypeID = 357, Slots = new() { new() { Key = "Special", DisplayName = "Special accessory" } } };
        ItemAssetsCollection.Prefabs[358] = new() { TypeID = 358, Slots = new() { new() { Key = "Special", DisplayName = "Armor gem" } } };
        using var resolver = new NativeEntityDisplayNames();
        Assert.Equal("Weapon 1", resolver.Names.Get("duckov:slot:PrimaryWeapon", "saved"));
        Assert.Equal("Special accessory", resolver.Names.Slot("duckov:weapon:357", "Special", "saved"));
        Assert.Equal("Armor gem", resolver.Names.Slot("duckov:item:358", "Special", "saved"));
        Assert.Equal("saved", resolver.Names.Slot("duckov:item:358", "special", "saved"));
        ItemAssetsCollection.Prefabs[358].Slots!.Add(new() { Key = "Special", DisplayName = "Different" });
        resolver.Invalidate();
        Assert.Equal("saved", resolver.Names.Slot("duckov:item:358", "Special", "saved"));
        Assert.Equal("saved", new EntityDisplayNames(_ => throw new InvalidOperationException()).Get("357", "saved"));
    }

    public void Dispose()
    {
        ItemAssetsCollection.Metadata.Clear(); ItemAssetsCollection.Prefabs.Clear();
        SceneInfoCollection.Scenes.Clear(); GameplayDataSettings.CharacterRandomPresetData = new();
        LocalizationManager.Translations.Clear(); LocalizationManager.Initialized = true;
        LocalizationManager.DataModel = new(); LocalizationManager.SetLanguage(SystemLanguage.English);
    }
}
