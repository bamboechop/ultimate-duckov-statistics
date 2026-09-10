using System.Globalization;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativeFiringDiagnosticTests : IDisposable
{
    public NativeFiringDiagnosticTests() => ResetNativeState();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShootingMeasuresTheCachedAssociationAndClosesWhenTheHandlerFails(bool failHandler)
    {
        var weapon = new Item { TypeID = 736, DisplayName = "SR-3M" };
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", Content = weapon });
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        CharacterMainControl.Main = main;
        using var equipment = new NativeEquipmentAdapter(
            () => true, _ => true, () => true, _ => { }, _ => { }, () => 0);
        equipment.Initialize();
        var messages = new List<string>();
        var shots = new List<ShotRecorded>();
        using var firing = new NativeWeaponFireAdapter(
            () => "generation", () => "run", () => "map",
            shot =>
            {
                shots.Add(shot);
                if (failHandler) throw new InvalidOperationException("handler failure");
                return true;
            },
            _ => { }, messages.Add, equipment.CaptureAssociation, () => "segment");
        firing.Initialize();
        messages.Clear();
        NativeHotPathDiagnostics.Reset();

        ItemAgent_Gun.RaiseMainCharacterShoot(new ItemAgent_Gun
        {
            Holder = main,
            Item = weapon,
            GunItemSetting = new ItemSetting_Gun { TargetBulletID = 594, CurrentBulletName = "Rost-Muni (S)" }
        });

        var shot = Assert.Single(shots);
        Assert.False(string.IsNullOrWhiteSpace(shot.EquipmentAssociation.LoadoutId));
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().AcceptedFiringCallbacks);
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().EquipmentAssociationRequests);
        Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        Assert.Equal(failHandler, messages.Any(message => message.Contains("handler failure", StringComparison.Ordinal)));
        var summary = Summary();
        var firingRow = Row(summary, "WeaponFireCallback");
        var associationRow = Row(summary, "EquipmentAssociation");
        Assert.Equal(1, firingRow[0]);
        Assert.Equal(1, associationRow[0]);
        Assert.True(firingRow[1] >= associationRow[1]);
        Assert.Equal(0, Row(summary, "EquipmentObservation")[0]);

        // A native tree notification remains independently visible even if the
        // rebuilt snapshot is unchanged and its publication is suppressed.
        NativeHotPathDiagnostics.Reset();
        characterItem.RaiseItemTreeChanged();
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().EquipmentUnchangedPublications);
        var observationSummary = Summary();
        Assert.Equal(1, Row(observationSummary, "EquipmentObservation")[0]);
        Assert.Equal(0, Row(observationSummary, "WeaponFireCallback")[0]);
        Assert.Equal(0, Row(observationSummary, "EquipmentAssociation")[0]);
    }

    public void Dispose() => ResetNativeState();

    private static void ResetNativeState()
    {
        ItemAgent_Gun.ResetNativeState();
        CharacterMainControl.ResetNativeState();
        LevelManager.ResetNativeState();
        GameManager.Paused = false;
        MultiSceneCore.Instance = null;
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
        NativeRaidContext.GameplayContext = GameplayContext.Raid;
        UnityEngine.Application.version = "2.3.30";
    }

    private static string Summary()
    {
        var messages = new List<string>();
        NativeHotPathDiagnostics.WriteSummary(messages.Add);
        return Assert.Single(messages);
    }

    private static long[] Row(string summary, string area) => summary.Split(' ')
        .Single(part => part.StartsWith(area + "=", StringComparison.Ordinal))[(area.Length + 1)..]
        .Split(',').Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
}
