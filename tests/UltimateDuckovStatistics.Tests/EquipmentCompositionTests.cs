using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class EquipmentCompositionTests
{
    internal static EquipmentSnapshot Snapshot()
    {
        var item = new EquippedItemSnapshot { SlotId = "duckov:slot:PrimaryWeapon", SlotDisplayName = "Primary weapon", ItemId = "duckov:weapon:1",
            ItemDisplayName = "Weapon", Kind = EquipmentItemKind.Weapon, NestedSlotStateComplete = false, AttachmentSignature = "exact",
            NestedSlots = new() { new() { Path = "5:Scope/", SlotKey = "Scope", SlotDisplayName = "Scope", State = EquipmentSlotState.Empty } } };
        var s = new EquipmentSnapshot { SnapshotId = "one", CharacterSlotStateComplete = true, Items = new() { item },
            CharacterSlots = new() {
                new() { SlotId = item.SlotId, SlotDisplayName = item.SlotDisplayName, ItemId = item.ItemId, ItemDisplayName = item.ItemDisplayName, ItemKind = item.Kind },
                new() { SlotId = "mod:slot", SlotDisplayName = "Modded slot", State = EquipmentSlotState.Empty } },
            Totems = new() { new() { ItemId = "duckov:totem:2", DisplayName = "Totem", CarryKind = TotemCarryKind.DirectSlot,
                ContainerId = "duckov:character", DirectSlotId = "duckov:slot:totem-a", ActivationState = TotemActivationState.ProvenActive } } };
        s.LoadoutId = EquipmentIdentity.LoadoutId(s.Items); s.TotemSetId = EquipmentIdentity.ActiveTotemSetId(s.Totems); return s;
    }
    private static EquipmentStatisticsAggregate Observed(EquipmentSnapshot? snapshot = null)
    {
        var a = new EquipmentStatisticsAggregate { Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities() };
        EquipmentStatisticsReducer.Observe(a, snapshot ?? Snapshot(), 0); EquipmentStatisticsReducer.Advance(a, 10); return a;
    }
    [Fact] public void CapturesExactRootsNestedEmptyAndUnknownSlotWithoutJournaling()
    {
        var s = Snapshot(); var a = Observed(s);
        for (var i = 0; i < 1000; i++) EquipmentStatisticsReducer.Observe(a, s, 10);
        var d = Assert.Single(a.Composition.Loadouts).Value;
        Assert.Equal(2, d.Roots.Count); Assert.Equal(EquipmentSlotState.Empty, d.Roots[1].State);
        Assert.Equal("mod:slot", d.Roots[1].SlotId); Assert.False(d.NestedComplete);
        Assert.Equal(EquipmentSlotState.Empty, Assert.Single(d.Items[0].NestedSlots).State);
        Assert.Single(a.Composition.ActiveTotemSets); Assert.Single(a.Composition.TotemStates); Assert.Equal(1, a.TransitionCount);
    }
    [Fact] public void UnavailableIdentityNeverRegistersReadableSubset()
    {
        var s = Snapshot(); s.LoadoutId = s.TotemSetId = EquipmentEventAssociation.UnavailableId; s.CharacterSlotStateComplete = false;
        var a = Observed(s); Assert.Empty(a.Composition.Loadouts); Assert.Empty(a.Composition.ActiveTotemSets);
        Assert.NotEmpty(a.CharacterSlotStates); Assert.NotEmpty(a.Composition.TotemStates);
    }
    [Fact] public void NamesEnrichWithoutChangingIdentityOrTime()
    {
        var s = Snapshot(); var a = Observed(s); s.Items[0].ItemDisplayName = s.CharacterSlots[0].ItemDisplayName = "Localized weapon";
        Assert.True(EquipmentStatisticsReducer.Observe(a, s, 10));
        Assert.Equal("Localized weapon", a.Composition.Loadouts[s.LoadoutId].Items[0].ItemDisplayName);
        Assert.Equal(10, a.Loadouts[s.LoadoutId].ActiveDurationSeconds); Assert.Single(a.Composition.Loadouts);
    }
    [Fact] public void IncompleteNestedEvidenceEnrichesButContradictionFailsClosed()
    {
        var s = Snapshot(); var a = Observed(s); s.Items[0].NestedSlotStateComplete = s.NestedSlotStateComplete = true;
        EquipmentStatisticsReducer.Observe(a, s, 10); Assert.True(a.Composition.Loadouts[s.LoadoutId].NestedComplete);
        s.Items[0].NestedSlots[0].State = EquipmentSlotState.Occupied; s.Items[0].NestedSlots[0].ItemId = "mod:scope";
        EquipmentStatisticsReducer.Observe(a, s, 10); Assert.True(a.Composition.Loadouts[s.LoadoutId].Conflicting);
        var clean = Observed(Snapshot()); EquipmentStatisticsReducer.Merge(clean, a); Assert.True(clean.Composition.Loadouts[s.LoadoutId].Conflicting);
    }
    [Fact] public void RootConflictNeverPicksLastDefinition()
    {
        var s = Snapshot(); var a = Observed(s); s.CharacterSlots.RemoveAt(1);
        EquipmentStatisticsReducer.Observe(a, s, 10);
        Assert.True(a.Composition.Loadouts[s.LoadoutId].Conflicting); Assert.Equal(2, a.Composition.Loadouts[s.LoadoutId].Roots.Count);
    }
    [Fact] public void SetsRetainOnlyActiveMembersAndStateRetainsDirectToteAndCopies()
    {
        var s = Snapshot();
        s.Totems.Add(new TotemSnapshot { ItemId = "duckov:totem:3", DisplayName = "Inactive", CarryKind = TotemCarryKind.DirectSlot,
            ContainerId = "duckov:character", DirectSlotId = "duckov:slot:totem-b", ActivationState = TotemActivationState.ProvenInactive });
        for (var i = 0; i < 2; i++) s.Totems.Add(new TotemSnapshot { ItemId = "duckov:totem:2", CarryKind = TotemCarryKind.ToteInventory,
            ContainerId = "duckov:tote:1255", ActivationState = TotemActivationState.Unknown });
        var a = Observed(s); Assert.Single(a.Composition.ActiveTotemSets[s.TotemSetId].Members);
        Assert.Equal(4, a.Composition.TotemStates.Count); Assert.Equal(40, a.Composition.TotemStates.Values.Sum(r => r.DurationSeconds));
        Assert.Equal(2, a.Composition.TotemStates.Values.Count(r => r.Totem.CarryKind == TotemCarryKind.ToteInventory));
        Assert.Contains(a.Composition.TotemStates.Values, r => r.Totem.DirectSlotId == "duckov:slot:totem-b" && r.Totem.ActivationState == TotemActivationState.ProvenInactive);
        s.Totems[1].ActivationState = TotemActivationState.ProvenActive; s.TotemSetId = EquipmentIdentity.ActiveTotemSetId(s.Totems);
        EquipmentStatisticsReducer.Observe(a, s, 10); Assert.Equal(2, a.Composition.ActiveTotemSets[s.TotemSetId].Members.Count);
    }
    [Fact] public void MergeAndCloneRetainDefinitionsAndIndependentCounters()
    {
        var a = Observed(); var copy = EquipmentStatisticsReducer.Clone(a); EquipmentStatisticsReducer.Merge(copy, a);
        Assert.Equal(20, copy.Composition.TotemStates.Values.Single().DurationSeconds);
        Assert.Equal(10, a.Composition.TotemStates.Values.Single().DurationSeconds);
        Assert.Single(copy.Composition.Loadouts);
    }
    [Fact] public void NativeRequiredCategoryProvesEmptySlotWithoutLocalizedNameGuess()
    {
        var character = new ItemStatsSystem.Item();
        var proven = new ItemStatsSystem.Items.Slot { Key = "modded-totem-position", DisplayName = "Localized" };
        proven.requireTags.Add(new ItemStatsSystem.Items.NativeSlotTag { name = "Totem" }); character.Slots.Add(proven);
        character.Slots.Add(new ItemStatsSystem.Items.Slot { Key = "unknown", DisplayName = "Totem slot 2" });
        var s = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), character); var a = Observed(s);
        Assert.Equal("duckov:slot:modded-totem-position", Assert.Single(a.Composition.EmptyDirectSlots).Key);
        Assert.Equal(10, a.Composition.EmptyDirectSlots.Values.Single().ActiveDurationSeconds);
    }
    [Fact] public void TypedDurationOverflowIsRejectedBeforeMutation()
    {
        var a = Observed(); var row = a.Composition.TotemStates.Values.Single(); row.DurationSeconds = double.MaxValue;
        Assert.Throws<OverflowException>(() => EquipmentCompositionReducer.Advance(a.Composition, Snapshot(), double.MaxValue));
        Assert.Equal(double.MaxValue, row.DurationSeconds);
    }
    [Fact] public void CorruptDefinitionAndTypedIdentityAreRejected()
    {
        var a = Observed(); a.Composition.Loadouts.Values.Single().Items[0].ItemId = "tampered";
        Assert.Throws<ArgumentException>(() => EquipmentStatisticsReducer.ValidateRecoveryCandidate(a, 18));
        a = Observed(); a.Composition.TotemStates.Values.Single().Totem.DirectSlotId = "tampered";
        Assert.Throws<ArgumentException>(() => EquipmentStatisticsReducer.ValidateRecoveryCandidate(a, 18));
    }
    [Fact] public void JsonAndDedicatedCsvPreserveStructuredTruthAndHistoricalGaps()
    {
        var profile = new ProfileDocument { GenerationId = "equipment-generation", Statistics = new ProfileStatistics { SaveGenerationId = "equipment-generation" } };
        profile.Statistics.RunTotals.EquipmentStatistics = Observed();
        var a = profile.Statistics.RunTotals.EquipmentStatistics;
        a.Loadouts.Add("historical", new EquipmentDurationAggregate { Id = "historical", DisplayName = "Never parse", ActiveDurationSeconds = 22 });
        var bundle = StatisticsExporter.Create(profile, DateTime.UtcNow);
        Assert.Contains("Composition", bundle.Json); Assert.Contains("DirectSlotId", bundle.Json);
        Assert.Contains("\"historical\",\"Unavailable\"", bundle.LoadoutDefinitionsCsv);
        Assert.Contains("5:Scope/", bundle.LoadoutDefinitionsCsv); Assert.Contains("ProvenActive", bundle.ActiveTotemSetDefinitionsCsv);
        Assert.Contains("duckov:slot:totem-a", bundle.TotemStateDurationsCsv);
    }
    [Fact] public void Schema17MigrationMarksCompositionUnavailableWithoutParsingOrChangingRunSchema()
    {
        var profile = new ProfileDocument { GenerationId = "equipment-generation", Statistics = new ProfileStatistics { SaveGenerationId = "equipment-generation" } };
        profile.SchemaVersion = profile.Statistics.SchemaVersion = 17;
        profile.Statistics.RunTotals.EquipmentStatistics.Loadouts.Add("old", new EquipmentDurationAggregate { Id = "old", DisplayName = "Scope;Weapon", ActiveDurationSeconds = 10 });
        Assert.True(ProfileMigrator.Migrate(profile)); Assert.Equal(ProductInfo.SchemaVersion, profile.SchemaVersion);
        Assert.True(profile.Statistics.RunTotals.EquipmentStatistics.Composition.HistoricalUnavailable);
        Assert.Empty(profile.Statistics.RunTotals.EquipmentStatistics.Composition.Loadouts);
    }
}
