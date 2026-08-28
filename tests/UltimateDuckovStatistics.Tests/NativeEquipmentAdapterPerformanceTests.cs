using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativeEquipmentAdapterPerformanceTests : IDisposable
{
    public NativeEquipmentAdapterPerformanceTests()
    {
        CharacterMainControl.ResetNativeState();
        UnityEngine.Application.version = "2.3.30";
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AssociationsUseImmediateEventCacheAndWatchdogBuildsOnlyOncePerSecond()
    {
        var now = 0d;
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        var primary = new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon };
        characterItem.Slots.Add(primary);
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        CharacterMainControl.Main = main;
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () => true,
            _ => { },
            _ => { },
            () => now);

        adapter.Initialize();
        var initial = Assert.Single(published);
        NativeHotPathDiagnostics.Reset();

        for (var index = 0; index < 60; index++)
        {
            var association = adapter.CaptureAssociation();
            Assert.Equal(initial.LoadoutId, association.LoadoutId);
            Assert.Equal(initial.SelectedWeaponId, association.SelectedWeaponId);
            Assert.Equal(initial.SelectedWeaponSlotId, association.SelectedWeaponSlotId);
            Assert.Equal(initial.TotemSetId, association.TotemSetId);
        }

        Assert.Equal(60, NativeHotPathDiagnostics.Snapshot().EquipmentAssociationRequests);
        Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        now = 0.999;
        adapter.Tick();
        Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        now = 1;
        adapter.Tick();
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        Assert.Single(published);

        weapon.Slots.Add(new Slot
        {
            Key = "Muzzle",
            Content = new Item { TypeID = 701, DisplayName = "Muzzle" }
        });
        characterItem.RaiseItemTreeChanged();

        Assert.Equal(2, published.Count);
        Assert.NotEqual(initial.LoadoutId, adapter.CaptureAssociation().LoadoutId);
    }

    [Fact]
    public void UnchangedLoadoutIsRepublishedWhenTheRunSegmentContextChanges()
    {
        var now = 0d;
        var observationContext = "run-one\nsegment-one";
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () => true,
            _ => { },
            _ => { },
            () => now,
            () => observationContext);

        adapter.Initialize();
        Assert.Single(published);

        observationContext = "run-one\nsegment-two";
        var association = adapter.CaptureAssociation();

        Assert.Equal(2, published.Count);
        Assert.Equal(published[0].SnapshotId, published[1].SnapshotId);
        Assert.Equal(published[1].LoadoutId, association.LoadoutId);
    }

    [Fact]
    public void PermanentlyMissingSegmentContextKeepsOverallEquipmentAssociationCached()
    {
        var now = 0d;
        var invalidations = 0;
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () =>
            {
                invalidations++;
                return true;
            },
            _ => { },
            _ => { },
            () => now,
            () => null);

        adapter.Initialize();
        var initial = Assert.Single(published);

        for (var index = 0; index < 60; index++)
        {
            var association = adapter.CaptureAssociation();
            Assert.Equal(initial.LoadoutId, association.LoadoutId);
            Assert.Equal(initial.SelectedWeaponId, association.SelectedWeaponId);
            Assert.Equal(initial.SelectedWeaponSlotId, association.SelectedWeaponSlotId);
            Assert.Equal(initial.TotemSetId, association.TotemSetId);
        }

        now = 1;
        adapter.Tick();

        Assert.Single(published);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    public void LosingSegmentContextRepublishesOverallOnceWithoutInvalidatingAssociation()
    {
        var now = 0d;
        string? observationContext = "run-one\nsegment-one";
        var invalidations = 0;
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () =>
            {
                invalidations++;
                return true;
            },
            _ => { },
            _ => { },
            () => now,
            () => observationContext);

        adapter.Initialize();
        var initial = Assert.Single(published);
        observationContext = null;

        var association = adapter.CaptureAssociation();
        var repeatedAssociation = adapter.CaptureAssociation();

        Assert.Equal(2, published.Count);
        Assert.Equal(initial.SnapshotId, published[1].SnapshotId);
        Assert.Equal(initial.LoadoutId, association.LoadoutId);
        Assert.Equal(initial.SelectedWeaponId, association.SelectedWeaponId);
        Assert.Equal(association.LoadoutId, repeatedAssociation.LoadoutId);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "UI")]
    public void NamespacedSlotEndingInBuiltInWeaponNameRemainsVisibleAsOtherGear()
    {
        var now = 0d;
        var tracker = new RunLifecycleTracker(() => "run-modded-slot");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = "generation",
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "test",
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                RouteCapabilities = RouteStatisticsReducer.Supported("test"),
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
            }
        });

        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        characterItem.Slots.Add(new Slot
        {
            Key = "mod:PrimaryWeapon",
            DisplayName = "Mod Utility",
            Content = new Item { TypeID = 900, DisplayName = "Modded utility" }
        });
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", DisplayName = "Primary" });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem
        };
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot => tracker.ObserveEquipment(snapshot, DateTime.UnixEpoch.AddSeconds(now), now),
            () => tracker.SuspendEquipment(DateTime.UnixEpoch.AddSeconds(now), now),
            _ => { },
            _ => { },
            () => now,
            () => tracker.ActiveSegmentId);

        adapter.Initialize();
        now = 5;
        tracker.Tick(DateTime.UnixEpoch.AddSeconds(now), now);
        var run = tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.Extracted,
            TimestampUtc = DateTime.UnixEpoch.AddSeconds(now),
            MonotonicSeconds = now,
            NativeRaidId = "raid"
        }).Completed!;
        var profile = new ProfileDocument
        {
            GenerationId = "generation",
            CreatedUtc = DateTime.UnixEpoch,
            UpdatedUtc = DateTime.UnixEpoch.AddSeconds(now),
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = "generation",
                CreatedUtc = DateTime.UnixEpoch,
                UpdatedUtc = DateTime.UnixEpoch.AddSeconds(now)
            },
            Capabilities = EquipmentNativeContractPolicy.ToRecords(
                adapter.MetricCapabilities,
                NativeEquipmentAdapter.AdapterVersion).ToList()
        };
        Assert.True(RunReducer.Apply(profile.Statistics, run));

        var nativeRow = Assert.Single(run.EquipmentStatistics.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:mod:PrimaryWeapon");
        Assert.Equal(EquipmentItemKind.Other, nativeRow.ItemKind);
        Assert.Equal(5, nativeRow.ActiveDurationSeconds);

        var view = EquipmentStatisticsViewModelFactory.Create(profile);

        var moddedSlot = Assert.Single(view.ArmorAndGearSlots, value =>
            value.SlotId == "duckov:slot:mod:PrimaryWeapon");
        var moddedRow = Assert.Single(moddedSlot.Rows);
        Assert.Equal(EquipmentItemKind.Other, moddedRow.ItemKind);
        Assert.Equal(5, moddedRow.ActiveDurationSeconds);
        Assert.DoesNotContain(view.ArmorAndGearSlots, value =>
            value.SlotId == "duckov:slot:PrimaryWeapon");
        Assert.Empty(view.Weapons);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void NullCharacterRootDoesNotAbortHeldWeaponOrReadableSiblingDurations()
    {
        var now = 0d;
        var tracker = new RunLifecycleTracker(() => "run-partial-character-roots");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = "generation",
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "test",
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                RouteCapabilities = RouteStatisticsReducer.Supported("test"),
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
            }
        });

        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Held weapon" };
        weapon.Slots.Add(new Slot
        {
            Key = "Scope",
            DisplayName = "Scope",
            Content = new Item { TypeID = 701, DisplayName = "Readable scope" }
        });
        var backpack = new Item { TypeID = 800, DisplayName = "Readable backpack" };
        backpack.Slots.Add(new Slot
        {
            Key = "Cube",
            DisplayName = "Cube",
            Content = new Item { TypeID = 801, DisplayName = "Readable cube" }
        });
        characterItem.Slots.Add(null!);
        characterItem.Slots.Add(new Slot
        {
            Key = "PrimaryWeapon",
            DisplayName = "Primary",
            Content = weapon
        });
        characterItem.Slots.Add(new Slot
        {
            Key = "Backpack",
            DisplayName = "Backpack",
            Content = backpack
        });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        var invalidations = 0;
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot => tracker.ObserveEquipment(snapshot, DateTime.UnixEpoch.AddSeconds(now), now),
            () =>
            {
                invalidations++;
                return tracker.SuspendEquipment(DateTime.UnixEpoch.AddSeconds(now), now);
            },
            _ => { },
            _ => { },
            () => now,
            () => tracker.ActiveSegmentId);

        adapter.Initialize();
        var association = adapter.CaptureAssociation();
        now = 5;
        tracker.Tick(DateTime.UnixEpoch.AddSeconds(now), now);
        var equipment = tracker.CreateCheckpoint(DateTime.UnixEpoch.AddSeconds(now), now)!.EquipmentStatistics;

        Assert.Equal(0, invalidations);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.NestedSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, equipment.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported, equipment.Capabilities.NestedSlotState.State);
        Assert.Equal("duckov:slot:PrimaryWeapon", association.SelectedWeaponSlotId);
        Assert.Equal("duckov:weapon:700", association.SelectedWeaponId);
        Assert.Equal("duckov:slot:PrimaryWeapon", equipment.CurrentSnapshot!.SelectedWeaponSlotId);
        Assert.Equal("duckov:weapon:700", equipment.CurrentSnapshot.SelectedWeaponId);
        Assert.Equal(5, Assert.Single(equipment.SelectedWeapons.Values).ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:PrimaryWeapon"
            && value.ItemId == "duckov:weapon:700").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:Backpack"
            && value.ItemId == "duckov:item:800").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:PrimaryWeapon"
            && value.Path == "5:Scope/"
            && value.ItemId == "duckov:item:701").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack"
            && value.Path == "4:Cube/"
            && value.ItemId == "duckov:item:801").ActiveDurationSeconds);
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "UI")]
    [Trait("Category", "Export")]
    public void DisplayMetadataChangePublishesThroughViewAndExportWithoutEquipmentTransition()
    {
        var now = 0d;
        var tracker = new RunLifecycleTracker(() => "run-display-enrichment");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = "generation",
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "test",
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                RouteCapabilities = RouteStatisticsReducer.Supported("test"),
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
            }
        });

        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var child = new Item { TypeID = 971 };
        var nestedSlot = new Slot { Key = "Scope", Content = child };
        var weapon = new Item { TypeID = 970 };
        weapon.Slots.Add(nestedSlot);
        var rootSlot = new Slot { Key = "PrimaryWeapon", Content = weapon };
        characterItem.Slots.Add(rootSlot);
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot => tracker.ObserveEquipment(snapshot, DateTime.UnixEpoch.AddSeconds(now), now),
            () => tracker.SuspendEquipment(DateTime.UnixEpoch.AddSeconds(now), now),
            _ => { },
            _ => { },
            () => now,
            () => tracker.ActiveSegmentId);

        adapter.Initialize();
        now = 5;
        tracker.Tick(DateTime.UnixEpoch.AddSeconds(now), now);
        var before = tracker.CreateCheckpoint(DateTime.UnixEpoch.AddSeconds(now), now)!;
        var snapshotId = before.EquipmentStatistics.CurrentSnapshot!.SnapshotId;
        var transitionCount = before.EquipmentStatistics.TransitionCount;
        var mutationRevision = tracker.CheckpointMutationRevision;
        Assert.Equal("Unknown item 970", Assert.Single(before.EquipmentStatistics.CharacterSlotStates).Value.ItemDisplayName);
        Assert.Equal("Unknown item 971", Assert.Single(before.EquipmentStatistics.NestedSlotStates).Value.ItemDisplayName);

        rootSlot.DisplayName = "Enriched primary slot";
        weapon.DisplayNameRaw = "Enriched modded weapon";
        weapon.DisplayName = "Enriched modded weapon";
        nestedSlot.DisplayName = "Enriched scope slot";
        child.DisplayNameRaw = "Enriched modded optic";
        child.DisplayName = "Enriched modded optic";
        characterItem.RaiseItemTreeChanged();
        var after = tracker.CreateCheckpoint(DateTime.UnixEpoch.AddSeconds(now), now)!;

        Assert.Equal(snapshotId, after.EquipmentStatistics.CurrentSnapshot!.SnapshotId);
        Assert.Equal(transitionCount, after.EquipmentStatistics.TransitionCount);
        Assert.True(tracker.CheckpointMutationRevision > mutationRevision);
        var enrichedRoot = Assert.Single(after.EquipmentStatistics.CharacterSlotStates).Value;
        Assert.Equal("Enriched primary slot", enrichedRoot.SlotDisplayName);
        Assert.Equal("Enriched modded weapon", enrichedRoot.ItemDisplayName);
        Assert.Equal(5, enrichedRoot.ActiveDurationSeconds);
        var enrichedNested = Assert.Single(after.EquipmentStatistics.NestedSlotStates).Value;
        Assert.Equal("Enriched modded weapon", enrichedNested.ParentItemDisplayName);
        Assert.Equal("Enriched scope slot", enrichedNested.SlotDisplayName);
        Assert.Equal("Enriched modded optic", enrichedNested.ItemDisplayName);
        Assert.Equal(5, enrichedNested.ActiveDurationSeconds);

        var run = tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.Extracted,
            TimestampUtc = DateTime.UnixEpoch.AddSeconds(now),
            MonotonicSeconds = now,
            NativeRaidId = "raid"
        }).Completed!;
        var profile = new ProfileDocument
        {
            GenerationId = "generation",
            CreatedUtc = DateTime.UnixEpoch,
            UpdatedUtc = DateTime.UnixEpoch.AddSeconds(now),
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = "generation",
                CreatedUtc = DateTime.UnixEpoch,
                UpdatedUtc = DateTime.UnixEpoch.AddSeconds(now)
            },
            Capabilities = EquipmentNativeContractPolicy.ToRecords(
                adapter.MetricCapabilities,
                NativeEquipmentAdapter.AdapterVersion).ToList()
        };
        Assert.True(RunReducer.Apply(profile.Statistics, run));

        var view = EquipmentStatisticsViewModelFactory.Create(profile);
        var weaponView = Assert.Single(view.Weapons);
        Assert.Equal("Enriched modded weapon", weaponView.DisplayName);
        Assert.Equal("Enriched primary slot", Assert.Single(weaponView.CharacterSlots).SlotDisplayName);
        var scope = Assert.Single(weaponView.NestedSlotGroups);
        var scopeRow = Assert.Single(scope.Rows);
        Assert.Equal("Enriched scope slot", scopeRow.SlotDisplayName);
        Assert.Equal("Enriched modded optic", scopeRow.ItemDisplayName);

        var export = StatisticsExporter.Create(profile, DateTime.UnixEpoch.AddMinutes(1));
        Assert.Contains("\"ItemDisplayName\":\"Enriched modded weapon\"", export.Json);
        Assert.Contains("\"ItemDisplayName\":\"Enriched modded optic\"", export.Json);
        Assert.Contains("Enriched primary slot", export.CharacterEquipmentSlotsCsv);
        Assert.Contains("Enriched modded weapon", export.CharacterEquipmentSlotsCsv);
        Assert.Contains("Enriched scope slot", export.EquippedItemNestedSlotsCsv);
        Assert.Contains("Enriched modded optic", export.EquippedItemNestedSlotsCsv);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void PartialNestedSlotEvidenceRetainsReadableSiblingsThroughPersistenceAndExport()
    {
        using var directory = new TemporaryDirectory();
        var identity = new SaveIdentitySnapshot
        {
            Slot = 1,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = 100,
            ObservedWriteUtcTicks = 110,
            ObservedLength = 4096,
            GameVersion = "2.3.30",
            ContentSha256 = new string('a', 64),
            SaveTimeBinary = DateTime.UnixEpoch.ToBinary()
        };
        var ids = new Queue<string>(["generation", "session-one"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddMinutes(1),
            () => ids.Dequeue());
        repository.Open(identity);
        var now = 0d;
        var tracker = new RunLifecycleTracker(() => "run-partial-nested-evidence");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = "generation",
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "test",
                LifecycleCapability = AdapterCapabilityState.Supported,
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
            }
        });

        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var armor = new Item { TypeID = 800, DisplayName = "Armor" };
        armor.Slots.Add(new Slot
        {
            Key = "Pouch",
            DisplayName = "Pouch",
            Content = new Item { TypeID = 801, DisplayName = "Valid child" }
        });
        var backpack = new Item { TypeID = 900, DisplayName = "Backpack" };
        backpack.Slots.Add(null!);
        backpack.Slots.Add(new Slot
        {
            Key = "Cube",
            DisplayName = "Cube",
            Content = new Item { TypeID = 901, DisplayName = "Readable sibling" }
        });
        characterItem.Slots.Add(new Slot { Key = "Armor", DisplayName = "Armor", Content = armor });
        characterItem.Slots.Add(new Slot { Key = "Backpack", DisplayName = "Backpack", Content = backpack });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem
        };
        var invalidations = 0;
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot => tracker.ObserveEquipment(snapshot, DateTime.UnixEpoch.AddSeconds(now), now),
            () =>
            {
                invalidations++;
                return tracker.SuspendEquipment(DateTime.UnixEpoch.AddSeconds(now), now);
            },
            _ => { },
            _ => { },
            () => now,
            () => tracker.ActiveSegmentId);

        adapter.Initialize();
        now = 5;
        adapter.Tick();
        tracker.Tick(DateTime.UnixEpoch.AddSeconds(now), now);
        var equipment = tracker.CreateCheckpoint(DateTime.UnixEpoch.AddSeconds(now), now)!.EquipmentStatistics;

        Assert.Equal(0, invalidations);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.NestedSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported, equipment.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, equipment.Capabilities.NestedSlotState.State);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:Armor"
            && value.ItemId == "duckov:item:800").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:Backpack"
            && value.ItemId == "duckov:item:900").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Armor"
            && value.Path == "5:Pouch/"
            && value.ItemId == "duckov:item:801").ActiveDurationSeconds);
        var readableSibling = Assert.Single(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack");
        Assert.Equal("4:Cube/", readableSibling.Path);
        Assert.Equal(EquipmentSlotState.Occupied, readableSibling.State);
        Assert.Equal("duckov:item:901", readableSibling.ItemId);
        Assert.Equal(5, readableSibling.ActiveDurationSeconds);
        Assert.DoesNotContain(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack"
            && value.State == EquipmentSlotState.Empty);

        var run = tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.Extracted,
            TimestampUtc = DateTime.UnixEpoch.AddSeconds(now),
            MonotonicSeconds = now,
            NativeRaidId = "raid"
        }).Completed!;
        Assert.True(repository.CompleteRun(run));
        repository.CloseClean();

        var reopened = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddMinutes(2),
            () => "session-two");
        reopened.Open(identity);
        var persisted = reopened.Current.Statistics.RunTotals.EquipmentStatistics;
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            persisted.Capabilities.NestedSlotState.State);
        var persistedSibling = Assert.Single(persisted.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack");
        Assert.Equal("4:Cube/", persistedSibling.Path);
        Assert.Equal(EquipmentSlotState.Occupied, persistedSibling.State);
        Assert.Equal("duckov:item:901", persistedSibling.ItemId);
        Assert.Equal(5, persistedSibling.ActiveDurationSeconds);
        Assert.DoesNotContain(persisted.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack"
            && value.State == EquipmentSlotState.Empty);

        var export = StatisticsExporter.Create(reopened.Current, DateTime.UnixEpoch.AddMinutes(3));
        var csv = export.EquippedItemNestedSlotsCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.TrimEnd('\r').Split(','))
            .ToArray();
        var headers = csv[0];
        var parentSlotIndex = Array.IndexOf(headers, "parent_slot_id");
        var pathIndex = Array.IndexOf(headers, "nested_path");
        var stateIndex = Array.IndexOf(headers, "state");
        var itemIndex = Array.IndexOf(headers, "item_id");
        var durationIndex = Array.IndexOf(headers, "active_duration_seconds");
        var capabilityIndex = Array.IndexOf(headers, "capability_state");
        var backpackRows = csv.Skip(1)
            .Where(row => row[parentSlotIndex] == "duckov:slot:Backpack")
            .ToArray();
        Assert.NotEmpty(backpackRows);
        Assert.All(backpackRows, row =>
        {
            Assert.Equal("4:Cube/", row[pathIndex]);
            Assert.Equal("Occupied", row[stateIndex]);
            Assert.Equal("duckov:item:901", row[itemIndex]);
            Assert.Equal("5", row[durationIndex]);
            Assert.Equal("DisabledIncompatible", row[capabilityIndex]);
        });
        Assert.Contains("Readable sibling", export.Json);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Export")]
    public void DuplicateRootAndNestedKeysRemainUnavailableWhileUniqueSiblingsPersist()
    {
        using var directory = new TemporaryDirectory();
        var identity = new SaveIdentitySnapshot
        {
            Slot = 1,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = 100,
            ObservedWriteUtcTicks = 110,
            ObservedLength = 4096,
            GameVersion = "2.3.30",
            ContentSha256 = new string('a', 64),
            SaveTimeBinary = DateTime.UnixEpoch.ToBinary()
        };
        var ids = new Queue<string>(["generation", "session-one"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddMinutes(1),
            () => ids.Dequeue());
        repository.Open(identity);
        var now = 0d;
        var tracker = new RunLifecycleTracker(() => "run-duplicate-slot-keys");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = "generation",
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "test",
                LifecycleCapability = AdapterCapabilityState.Supported,
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
            }
        });

        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        characterItem.Slots.Add(new Slot
        {
            Key = "ModRoot",
            DisplayName = "Ambiguous root A",
            Content = new Item { TypeID = 700, DisplayName = "Ambiguous item A" }
        });
        characterItem.Slots.Add(new Slot
        {
            Key = "ModRoot",
            DisplayName = "Ambiguous root B",
            Content = new Item { TypeID = 701, DisplayName = "Ambiguous item B" }
        });
        characterItem.Slots.Add(new Slot
        {
            Key = "Armor",
            DisplayName = "Armor",
            Content = new Item { TypeID = 800, DisplayName = "Unique armor" }
        });
        var backpack = new Item { TypeID = 900, DisplayName = "Backpack" };
        backpack.Slots.Add(new Slot
        {
            Key = "Cube",
            DisplayName = "Ambiguous occupied cube",
            Content = new Item { TypeID = 901, DisplayName = "Ambiguous cube" }
        });
        backpack.Slots.Add(new Slot { Key = "Cube", DisplayName = "Ambiguous empty cube" });
        backpack.Slots.Add(new Slot
        {
            Key = "Pouch",
            DisplayName = "Unique pouch",
            Content = new Item { TypeID = 902, DisplayName = "Unique child" }
        });
        characterItem.Slots.Add(new Slot { Key = "Backpack", DisplayName = "Backpack", Content = backpack });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem
        };
        var invalidations = 0;
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot => tracker.ObserveEquipment(snapshot, DateTime.UnixEpoch.AddSeconds(now), now),
            () =>
            {
                invalidations++;
                return tracker.SuspendEquipment(DateTime.UnixEpoch.AddSeconds(now), now);
            },
            _ => { },
            _ => { },
            () => now,
            () => tracker.ActiveSegmentId);

        adapter.Initialize();
        now = 5;
        adapter.Tick();
        tracker.Tick(DateTime.UnixEpoch.AddSeconds(now), now);
        var equipment = tracker.CreateCheckpoint(DateTime.UnixEpoch.AddSeconds(now), now)!.EquipmentStatistics;

        Assert.Equal(0, invalidations);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            adapter.MetricCapabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            adapter.MetricCapabilities.NestedSlotState.State);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:Armor").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:Backpack").ActiveDurationSeconds);
        Assert.DoesNotContain(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:ModRoot");
        var uniqueNested = Assert.Single(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack");
        Assert.Equal("5:Pouch/", uniqueNested.Path);
        Assert.Equal(EquipmentSlotState.Occupied, uniqueNested.State);
        Assert.Equal("duckov:item:902", uniqueNested.ItemId);
        Assert.Equal(5, uniqueNested.ActiveDurationSeconds);
        Assert.DoesNotContain(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack"
            && value.Path == "4:Cube/");

        var run = tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.Extracted,
            TimestampUtc = DateTime.UnixEpoch.AddSeconds(now),
            MonotonicSeconds = now,
            NativeRaidId = "raid"
        }).Completed!;
        Assert.True(repository.CompleteRun(run));
        repository.CloseClean();

        var reopened = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddMinutes(2),
            () => "session-two");
        reopened.Open(identity);
        var persisted = reopened.Current.Statistics.RunTotals.EquipmentStatistics;
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            persisted.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            persisted.Capabilities.NestedSlotState.State);
        Assert.DoesNotContain(persisted.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:ModRoot");
        Assert.DoesNotContain(persisted.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack"
            && value.Path == "4:Cube/");
        Assert.Equal(5, Assert.Single(persisted.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack"
            && value.Path == "5:Pouch/").ActiveDurationSeconds);

        var export = StatisticsExporter.Create(reopened.Current, DateTime.UnixEpoch.AddMinutes(3));
        Assert.DoesNotContain(ParseCsv(export.CharacterEquipmentSlotsCsv), row =>
            row["slot_id"] == "duckov:slot:ModRoot");
        Assert.DoesNotContain(ParseCsv(export.EquippedItemNestedSlotsCsv), row =>
            row["parent_slot_id"] == "duckov:slot:Backpack"
            && row["nested_path"] == "4:Cube/");
        var exportedUnique = Assert.Single(ParseCsv(export.EquippedItemNestedSlotsCsv), row =>
            row["scope"] == "run"
            && row["parent_slot_id"] == "duckov:slot:Backpack"
            && row["nested_path"] == "5:Pouch/");
        Assert.Equal("Occupied", exportedUnique["state"]);
        Assert.Equal("duckov:item:902", exportedUnique["item_id"]);
        Assert.Equal("5", exportedUnique["active_duration_seconds"]);
        Assert.Equal("DisabledIncompatible", exportedUnique["capability_state"]);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Export")]
    public void IncompleteRootsKeepExactSetIdentitiesUnavailableAcrossProductionRaidsAndFiring()
    {
        using var directory = new TemporaryDirectory();
        var identity = new SaveIdentitySnapshot
        {
            Slot = 1,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = 100,
            ObservedWriteUtcTicks = 110,
            ObservedLength = 4096,
            GameVersion = "2.3.30",
            ContentSha256 = new string('a', 64),
            SaveTimeBinary = DateTime.UnixEpoch.ToBinary()
        };
        var ids = new Queue<string>(["generation", "session-one"]);
        var now = 0d;
        var repository = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddSeconds(now),
            () => ids.Dequeue());
        repository.Open(identity);

        var weapon = new Item { TypeID = 700, DisplayName = "Readable weapon" };
        weapon.Slots.Add(new Slot
        {
            Key = "Scope",
            DisplayName = "Scope",
            Content = new Item { TypeID = 701, DisplayName = "Readable scope" }
        });
        var omittedTotem = new Item { TypeID = 800, DisplayName = "Omitted totem" };
        omittedTotem.Tags.Add("Totem");
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        characterItem.Slots.Add(new Slot
        {
            Key = "PrimaryWeapon",
            DisplayName = "Primary weapon",
            Content = weapon
        });
        characterItem.Slots.Add(new Slot
        {
            Key = "ModRoot",
            DisplayName = "Conflicting totem root",
            Content = omittedTotem
        });
        characterItem.Slots.Add(new Slot
        {
            Key = "ModRoot",
            DisplayName = "Conflicting item root",
            Content = new Item { TypeID = 801, DisplayName = "Omitted item" }
        });
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        CharacterMainControl.Main = main;
        LevelManager.Instance = new LevelManagerInstance { MainCharacter = main };
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };

        IReadOnlyList<CapabilityRecord> runCapabilities = Array.Empty<CapabilityRecord>();
        IReadOnlyList<CapabilityRecord> equipmentCapabilities = Array.Empty<CapabilityRecord>();
        IReadOnlyList<CapabilityRecord> weaponCapabilities = Array.Empty<CapabilityRecord>();
        void PublishCapabilities() => repository.SetCapabilitySnapshot(
            runCapabilities.Concat(equipmentCapabilities).Concat(weaponCapabilities),
            new EconomyMetricCapabilities(),
            new WorldTimeMetricCapabilities(),
            new CraftingMetricCapabilities());

        NativeEquipmentAdapter? equipmentAdapter = null;
        NativeWeaponFireAdapter? weaponFireAdapter = null;
        using var lifecycleAdapter = new NativeRunLifecycleAdapter(
            () => repository.CurrentGenerationId,
            checkpoint =>
            {
                repository.SaveActiveRun(checkpoint);
                return true;
            },
            repository.CompleteRun,
            capabilities =>
            {
                runCapabilities = capabilities.ToArray();
                PublishCapabilities();
            },
            _ => { },
            weaponCapabilitiesProvider: () => weaponFireAdapter?.MetricCapabilities
                ?? new WeaponMetricCapabilities(),
            equipmentCapabilitiesProvider: () => equipmentAdapter?.CaptureCapabilitiesForRunStart()
                ?? new EquipmentMetricCapabilities(),
            monotonicSecondsProvider: () => now);
        using var nativeEquipmentAdapter = new NativeEquipmentAdapter(
            () => lifecycleAdapter.IsActive,
            lifecycleAdapter.ObserveEquipment,
            lifecycleAdapter.InvalidateEquipmentObservation,
            capabilities =>
            {
                equipmentCapabilities = capabilities.ToArray();
                PublishCapabilities();
            },
            _ => { },
            () => now,
            () => lifecycleAdapter.CurrentSegmentId);
        equipmentAdapter = nativeEquipmentAdapter;
        using var nativeWeaponFireAdapter = new NativeWeaponFireAdapter(
            () => repository.CurrentGenerationId,
            () => lifecycleAdapter.CurrentRunId,
            () => lifecycleAdapter.CurrentMapId,
            lifecycleAdapter.RecordShot,
            capabilities =>
            {
                weaponCapabilities = capabilities.ToArray();
                PublishCapabilities();
            },
            _ => { },
            nativeEquipmentAdapter.CaptureAssociation,
            () => lifecycleAdapter.CurrentSegmentId);
        weaponFireAdapter = nativeWeaponFireAdapter;

        nativeEquipmentAdapter.Initialize();
        nativeWeaponFireAdapter.Initialize();
        lifecycleAdapter.Initialize();
        lifecycleAdapter.Tick();
        Assert.True(lifecycleAdapter.IsActive);

        void PublishEquipmentAndFire()
        {
            characterItem.RaiseItemTreeChanged();
            ItemAgent_Gun.RaiseMainCharacterShoot(new ItemAgent_Gun
            {
                Holder = main,
                Item = weapon,
                GunItemSetting = new ItemSetting_Gun
                {
                    TargetBulletID = 702,
                    CurrentBulletName = "Readable ammunition"
                }
            });
        }

        PublishEquipmentAndFire();
        now = 5;
        LevelManager.RaiseEvacuated();
        Assert.False(lifecycleAdapter.IsActive);

        now = 6;
        RaidUtilities.RaiseNewRaid(new RaidUtilities.RaidInfo { ID = 2, valid = true });
        lifecycleAdapter.Tick();
        Assert.True(lifecycleAdapter.IsActive);
        PublishEquipmentAndFire();
        now = 11;
        LevelManager.RaiseEvacuated();
        Assert.False(lifecycleAdapter.IsActive);

        repository.CloseClean();
        var reopened = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddMinutes(1),
            () => "session-two");
        reopened.Open(identity);

        var runs = reopened.Current.Statistics.Runs.OrderBy(value => value.StartedUtc).ToArray();
        Assert.Equal(2, runs.Length);
        foreach (var run in runs)
        {
            var equipment = run.EquipmentStatistics;
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
                equipment.Capabilities.EquipmentSlots.State);
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
                equipment.Capabilities.DirectTotems.State);
            Assert.Empty(equipment.Loadouts);
            Assert.Empty(equipment.TotemSets);
            Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
                value.SlotId == "duckov:slot:PrimaryWeapon").ActiveDurationSeconds);
            Assert.Equal(5, Assert.Single(equipment.NestedSlotStates.Values, value =>
                value.ParentSlotId == "duckov:slot:PrimaryWeapon"
                && value.Path == "5:Scope/").ActiveDurationSeconds);
            Assert.DoesNotContain(equipment.CharacterSlotStates.Values, value =>
                value.SlotId == "duckov:slot:ModRoot");
            Assert.Empty(equipment.Transitions);
            Assert.Equal(0, equipment.TransitionCount);
            var association = Assert.Single(equipment.CombatAssociations.Values);
            Assert.Equal(1, association.FiringActions);
            Assert.Equal(EquipmentEventAssociation.UnavailableId, association.LoadoutId);
            Assert.Equal(EquipmentEventAssociation.UnavailableId, association.TotemSetId);
            Assert.Equal("duckov:weapon:700", association.SelectedWeaponId);
            Assert.Equal("duckov:slot:PrimaryWeapon", association.SelectedWeaponSlotId);
        }

        var lifetime = reopened.Current.Statistics.RunTotals.EquipmentStatistics;
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            lifetime.Capabilities.EquipmentSlots.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            lifetime.Capabilities.DirectTotems.State);
        Assert.Empty(lifetime.Loadouts);
        Assert.Empty(lifetime.TotemSets);
        Assert.Equal(10, Assert.Single(lifetime.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:PrimaryWeapon").ActiveDurationSeconds);
        Assert.Equal(10, Assert.Single(lifetime.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:PrimaryWeapon"
            && value.Path == "5:Scope/").ActiveDurationSeconds);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            Assert.Single(reopened.Current.Capabilities, value =>
                value.AdapterId == EquipmentCapabilityIds.EquipmentSlots).State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            Assert.Single(reopened.Current.Capabilities, value =>
                value.AdapterId == EquipmentCapabilityIds.DirectTotems).State);

        var export = StatisticsExporter.Create(reopened.Current, DateTime.UnixEpoch.AddMinutes(2));
        Assert.DoesNotContain("duckov:loadout:", export.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("duckov:totem-set:", export.Json, StringComparison.Ordinal);
        Assert.Empty(ParseCsv(export.RecurringLoadoutsCsv));
        Assert.DoesNotContain(ParseCsv(export.EquipmentTotalsCsv), row =>
            row["breakdown"] is "loadout" or "totem_set");
        var firingRows = ParseCsv(export.EquipmentCombatCsv)
            .Where(row => row["firing_actions"] != "0")
            .ToArray();
        Assert.NotEmpty(firingRows);
        Assert.All(firingRows, row =>
        {
            Assert.Equal(EquipmentEventAssociation.UnavailableId, row["loadout_id"]);
            Assert.Equal(EquipmentEventAssociation.UnavailableId, row["totem_set_id"]);
            Assert.Equal("duckov:weapon:700", row["selected_weapon_id"]);
            Assert.Equal("duckov:slot:PrimaryWeapon", row["selected_weapon_slot_id"]);
        });
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Recovery")]
    public void NestedCompletenessChangeWithRetainedSlotIdentityReachesRunSegmentAndCheckpointRecovery()
    {
        using var directory = new TemporaryDirectory();
        var identity = new SaveIdentitySnapshot
        {
            Slot = 1,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = 100,
            ObservedWriteUtcTicks = 110,
            ObservedLength = 4096,
            GameVersion = "2.3.30",
            ContentSha256 = new string('a', 64),
            SaveTimeBinary = DateTime.UnixEpoch.ToBinary()
        };
        var repository = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddMinutes(1),
            () => "generation");
        repository.Open(identity);
        var now = 0d;
        var tracker = new RunLifecycleTracker(() => "run-nested-completeness");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = repository.CurrentGenerationId,
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "test",
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                RouteCapabilities = RouteStatisticsReducer.Supported("test"),
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
            }
        });

        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var backpack = new Item { TypeID = 900, DisplayName = "Backpack" };
        for (var index = 0; index < 256; index++)
        {
            backpack.Slots.Add(new Slot
            {
                Key = "Slot" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                DisplayName = "Slot " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        characterItem.Slots.Add(new Slot { Key = "Backpack", DisplayName = "Backpack", Content = backpack });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem
        };
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot => tracker.ObserveEquipment(snapshot, DateTime.UnixEpoch.AddSeconds(now), now),
            () => tracker.SuspendEquipment(DateTime.UnixEpoch.AddSeconds(now), now),
            _ => { },
            _ => { },
            () => now,
            () => tracker.ActiveSegmentId);

        adapter.Initialize();
        var complete = tracker.CreateCheckpoint(DateTime.UnixEpoch, 0)!;
        var completeSnapshotId = complete.EquipmentStatistics.CurrentSnapshot!.SnapshotId;
        var completeLoadoutId = complete.EquipmentStatistics.CurrentSnapshot.LoadoutId;
        Assert.True(complete.EquipmentStatistics.CurrentSnapshot.NestedSlotStateComplete);
        Assert.True(Assert.Single(complete.EquipmentStatistics.CurrentSnapshot.Items).NestedSlotStateComplete);
        Assert.Equal(256, complete.EquipmentStatistics.CurrentSnapshot.Items[0].NestedSlots.Count);
        Assert.Equal(AdapterCapabilityState.Supported,
            complete.EquipmentStatistics.Capabilities.NestedSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported,
            Assert.Single(complete.Segments).EquipmentStatistics.Capabilities.NestedSlotState.State);
        var completeMutationRevision = tracker.CheckpointMutationRevision;

        now = 5;
        backpack.Slots.Add(new Slot { Key = "Slot256", DisplayName = "Slot 256" });
        characterItem.RaiseItemTreeChanged();
        var checkpoint = tracker.CreateCheckpoint(DateTime.UnixEpoch.AddSeconds(now), now)!;
        var current = checkpoint.EquipmentStatistics.CurrentSnapshot!;

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            adapter.MetricCapabilities.NestedSlotState.State);
        Assert.False(current.NestedSlotStateComplete);
        Assert.False(Assert.Single(current.Items).NestedSlotStateComplete);
        Assert.Equal(256, current.Items[0].NestedSlots.Count);
        Assert.Equal(completeLoadoutId, current.LoadoutId);
        Assert.NotEqual(completeSnapshotId, current.SnapshotId);
        Assert.True(tracker.CheckpointMutationRevision > completeMutationRevision);
        Assert.Equal(AdapterCapabilityState.Supported,
            checkpoint.EquipmentStatistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            checkpoint.EquipmentStatistics.Capabilities.NestedSlotState.State);
        var checkpointSegment = Assert.Single(checkpoint.Segments);
        Assert.Equal(AdapterCapabilityState.Supported,
            checkpointSegment.EquipmentStatistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            checkpointSegment.EquipmentStatistics.Capabilities.NestedSlotState.State);

        repository.SaveActiveRun(checkpoint);
        var recovery = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddMinutes(2),
            () => "unused");
        Assert.True(recovery.Open(identity).InterruptedRunRecovered);
        var recovered = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(AdapterCapabilityState.Supported,
            recovered.EquipmentStatistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            recovered.EquipmentStatistics.Capabilities.NestedSlotState.State);
        var recoveredSegment = Assert.Single(recovered.Segments);
        Assert.Equal(AdapterCapabilityState.Supported,
            recoveredSegment.EquipmentStatistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            recoveredSegment.EquipmentStatistics.Capabilities.NestedSlotState.State);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "M14")]
    [Trait("Category", "Export")]
    public void CompleteObservationRestoresCurrentCapabilitiesForTheNextProductionLifecycleRun()
    {
        using var directory = new TemporaryDirectory();
        var identity = new SaveIdentitySnapshot
        {
            Slot = 1,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = 100,
            ObservedWriteUtcTicks = 110,
            ObservedLength = 4096,
            GameVersion = "2.3.30",
            ContentSha256 = new string('a', 64),
            SaveTimeBinary = DateTime.UnixEpoch.ToBinary()
        };
        var now = 0d;
        var repository = new ProfileRepository(
            directory.Path,
            () => DateTime.UnixEpoch.AddSeconds(now),
            () => "generation");
        repository.Open(identity);

        var mainItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var backpack = new Item { TypeID = 900, DisplayName = "Backpack" };
        backpack.Slots.Add(new Slot
        {
            Key = "Cube",
            DisplayName = "Cube slot",
            Content = new Item { TypeID = 901, DisplayName = "Blue cube" }
        });
        backpack.Slots.Add(null!);
        mainItem.Slots.Add(new Slot { Key = "Backpack", DisplayName = "Backpack", Content = backpack });
        mainItem.Slots.Add(null!);
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = mainItem
        };
        CharacterMainControl.Main = main;
        LevelManager.Instance = new LevelManagerInstance { MainCharacter = main };
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };

        IReadOnlyList<CapabilityRecord> runCapabilities = Array.Empty<CapabilityRecord>();
        IReadOnlyList<CapabilityRecord> equipmentCapabilities = Array.Empty<CapabilityRecord>();
        void PublishCapabilities() => repository.SetCapabilitySnapshot(
            runCapabilities.Concat(equipmentCapabilities),
            new EconomyMetricCapabilities(),
            new WorldTimeMetricCapabilities(),
            new CraftingMetricCapabilities());

        NativeEquipmentAdapter? equipmentAdapter = null;
        using var lifecycleAdapter = new NativeRunLifecycleAdapter(
            () => repository.CurrentGenerationId,
            checkpoint =>
            {
                repository.SaveActiveRun(checkpoint);
                return true;
            },
            repository.CompleteRun,
            capabilities =>
            {
                runCapabilities = capabilities.ToArray();
                PublishCapabilities();
            },
            _ => { },
            equipmentCapabilitiesProvider: () => equipmentAdapter?.CaptureCapabilitiesForRunStart()
                ?? new EquipmentMetricCapabilities(),
            monotonicSecondsProvider: () => now);
        equipmentAdapter = new NativeEquipmentAdapter(
            () => lifecycleAdapter.IsActive,
            lifecycleAdapter.ObserveEquipment,
            lifecycleAdapter.InvalidateEquipmentObservation,
            capabilities =>
            {
                equipmentCapabilities = capabilities.ToArray();
                PublishCapabilities();
            },
            _ => { },
            () => now,
            () => lifecycleAdapter.CurrentSegmentId);
        using (equipmentAdapter)
        {
            equipmentAdapter.Initialize();
            lifecycleAdapter.Initialize();
            lifecycleAdapter.Tick();
            Assert.True(lifecycleAdapter.IsActive);
            mainItem.RaiseItemTreeChanged();

            now = 5;
            LevelManager.RaiseEvacuated();
            Assert.False(lifecycleAdapter.IsActive);

            mainItem.Slots.RemoveAll(value => value == null);
            backpack.Slots.RemoveAll(value => value == null);
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
                equipmentAdapter.MetricCapabilities.CharacterSlotState.State);
            Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
                equipmentAdapter.MetricCapabilities.NestedSlotState.State);

            now = 6;
            RaidUtilities.RaiseNewRaid(new RaidUtilities.RaidInfo { ID = 2, valid = true });
            lifecycleAdapter.Tick();
            Assert.True(lifecycleAdapter.IsActive);
            Assert.Equal(AdapterCapabilityState.Supported,
                equipmentAdapter.MetricCapabilities.CharacterSlotState.State);
            Assert.Equal(AdapterCapabilityState.Supported,
                equipmentAdapter.MetricCapabilities.NestedSlotState.State);
            mainItem.RaiseItemTreeChanged();

            now = 11;
            LevelManager.RaiseEvacuated();
            Assert.False(lifecycleAdapter.IsActive);
        }

        var runs = repository.Current.Statistics.Runs.ToArray();
        Assert.Equal(2, runs.Length);
        var first = Assert.Single(runs, value => value.NativeRaidId == "1");
        var second = Assert.Single(runs, value => value.NativeRaidId == "2");
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            first.EquipmentStatistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            first.EquipmentStatistics.Capabilities.NestedSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported,
            second.EquipmentStatistics.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported,
            second.EquipmentStatistics.Capabilities.NestedSlotState.State);
        Assert.Equal(5, Assert.Single(first.EquipmentStatistics.CharacterSlotStates.Values).ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(second.EquipmentStatistics.CharacterSlotStates.Values).ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(first.EquipmentStatistics.NestedSlotStates.Values).ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(second.EquipmentStatistics.NestedSlotStates.Values).ActiveDurationSeconds);
        Assert.Equal(AdapterCapabilityState.Supported, Assert.Single(repository.Current.Capabilities, value =>
            value.AdapterId == EquipmentCapabilityIds.CharacterSlotState).State);
        Assert.Equal(AdapterCapabilityState.Supported, Assert.Single(repository.Current.Capabilities, value =>
            value.AdapterId == EquipmentCapabilityIds.NestedSlotState).State);

        var export = StatisticsExporter.Create(repository.Current, DateTime.UnixEpoch.AddSeconds(now));
        var characterRows = ParseCsv(export.CharacterEquipmentSlotsCsv)
            .Where(row => row["scope"] == "run" && row["slot_id"] == "duckov:slot:Backpack")
            .ToDictionary(row => row["run_id"], StringComparer.Ordinal);
        Assert.Equal("5", characterRows[first.RunId]["active_duration_seconds"]);
        Assert.Equal("DisabledIncompatible", characterRows[first.RunId]["capability_state"]);
        Assert.Equal("5", characterRows[second.RunId]["active_duration_seconds"]);
        Assert.Equal("Supported", characterRows[second.RunId]["capability_state"]);
        var nestedRows = ParseCsv(export.EquippedItemNestedSlotsCsv)
            .Where(row => row["scope"] == "run" && row["nested_path"] == "4:Cube/")
            .ToDictionary(row => row["run_id"], StringComparer.Ordinal);
        Assert.Equal("5", nestedRows[first.RunId]["active_duration_seconds"]);
        Assert.Equal("DisabledIncompatible", nestedRows[first.RunId]["capability_state"]);
        Assert.Equal("5", nestedRows[second.RunId]["active_duration_seconds"]);
        Assert.Equal("Supported", nestedRows[second.RunId]["capability_state"]);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "M14")]
    public void InactiveTreeChangeRefreshesCapabilitiesWithoutPublishingRunState()
    {
        var mainItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var backpack = new Item { TypeID = 900, DisplayName = "Backpack" };
        backpack.Slots.Add(new Slot { Key = "Cube", DisplayName = "Cube slot" });
        backpack.Slots.Add(null!);
        mainItem.Slots.Add(new Slot { Key = "Backpack", DisplayName = "Backpack", Content = backpack });
        mainItem.Slots.Add(null!);
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = mainItem
        };
        var publications = 0;
        var invalidations = 0;
        using var adapter = new NativeEquipmentAdapter(
            () => false,
            _ =>
            {
                publications++;
                return true;
            },
            () =>
            {
                invalidations++;
                return true;
            },
            _ => { },
            _ => { },
            () => 0);

        adapter.Initialize();
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            adapter.MetricCapabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            adapter.MetricCapabilities.NestedSlotState.State);

        mainItem.Slots.RemoveAll(value => value == null);
        backpack.Slots.RemoveAll(value => value == null);
        mainItem.RaiseItemTreeChanged();

        Assert.Equal(AdapterCapabilityState.Supported,
            adapter.MetricCapabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported,
            adapter.MetricCapabilities.NestedSlotState.State);
        Assert.Equal(0, publications);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void CleanupDetachesGlobalAndItemTreeCallbacksAndRemainsIdempotent()
    {
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var slot = new Slot { Key = "PrimaryWeapon", DisplayName = "Primary" };
        characterItem.Slots.Add(slot);
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem
        };
        CharacterMainControl.Main = main;
        var publications = 0;
        var adapter = new NativeEquipmentAdapter(
            () => true,
            _ =>
            {
                publications++;
                return true;
            },
            () => true,
            _ => { },
            _ => { },
            () => 0);
        adapter.Initialize();
        Assert.Equal(1, publications);

        Assert.True(adapter.TryCleanup());
        Assert.True(adapter.TryCleanup());
        slot.Content = new Item { TypeID = 700, DisplayName = "Weapon" };
        CharacterMainControl.RaiseSlotChanged(main, slot);
        characterItem.RaiseItemTreeChanged();

        Assert.Equal(1, publications);
        Assert.Equal(EquipmentEventAssociation.UnavailableId, adapter.CaptureAssociation().LoadoutId);
    }

    private static List<IReadOnlyDictionary<string, string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"') quoted = false;
                else field.Append(character);
                continue;
            }

            if (character == '"') quoted = true;
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else field.Append(character);
        }

        var headers = rows[0];
        return rows.Skip(1)
            .Where(values => values.Count > 1)
            .Select(values => (IReadOnlyDictionary<string, string>)headers
                .Select((header, index) => new KeyValuePair<string, string>(header, values[index]))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
            .ToList();
    }

    public void Dispose()
    {
        ItemAgent_Gun.ResetNativeState();
        CharacterMainControl.ResetNativeState();
        LevelManager.ResetNativeState();
        RaidUtilities.ResetNativeState();
        InputManager.InputActived = true;
        NativeRaidContext.GameplayContext = GameplayContext.Raid;
    }
}
