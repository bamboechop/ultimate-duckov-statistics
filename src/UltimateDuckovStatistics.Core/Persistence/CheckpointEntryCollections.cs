using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

internal enum CheckpointEntryKind
{
    Weapons = 1, Ammunition = 2, WeaponAmmunitionPairs = 3, UncorrelatedWeapons = 4, UncorrelatedAmmunition = 5,
    CombatEnemies = 6, CombatKillers = 7, CombatFamilies = 8, CombatCauses = 9, CombatWeapons = 10, CombatAmmunition = 11, CombatOwnership = 12,
    Items = 13, EquipmentItems = 14, SelectedWeapons = 15, Loadouts = 16, TotemSets = 17, EquipmentCombat = 18,
    TotemStates = 19, Slots = 20, SlottedWeapons = 21, CharacterSlotObserved = 22, CharacterSlotStates = 23,
    NestedSlotObserved = 24, NestedSlotStates = 25, LoadoutDefinitions = 26, TotemSetDefinitions = 27,
    TypedTotemStates = 28, EmptyDirectSlots = 29, EquipmentTransitions = 30, RouteAssociations = 31, ContainerIdentities = 32
}

internal sealed class CheckpointCollectionCapture
{
    internal CheckpointCollectionCapture(CheckpointEntryKind kind, int count, EntryChangeCapture receipt, IReadOnlyList<KeyValuePair<string, byte[]?>> entries)
    { Kind = kind; Count = count; Receipt = receipt; Entries = entries; }
    internal CheckpointEntryKind Kind { get; }
    internal int Count { get; }
    internal EntryChangeCapture Receipt { get; }
    internal IReadOnlyList<KeyValuePair<string, byte[]?>> Entries { get; }
}

internal abstract class CheckpointEntryCollection
{
    internal abstract CheckpointEntryKind Kind { get; }
    internal abstract int Count { get; }
    internal abstract CheckpointCollectionCapture Capture(ProfileRecordCodec codec);
    internal abstract void Restore(string key, byte[] bytes);

    internal static IReadOnlyList<CheckpointEntryCollection> Bind(Func<WeaponStatisticsAggregate> weapon, Func<CombatStatisticsAggregate> combat,
        Func<ItemStatisticsAggregate> items, Func<EquipmentStatisticsAggregate> equipment, Action changed) => new CheckpointEntryCollection[]
    {
        new Collection<WeaponAggregate>(CheckpointEntryKind.Weapons, () => weapon().Weapons, changed),
        new Collection<AmmunitionAggregate>(CheckpointEntryKind.Ammunition, () => weapon().AmmunitionTypes, changed),
        new Collection<WeaponAmmunitionPairAggregate>(CheckpointEntryKind.WeaponAmmunitionPairs, () => weapon().WeaponAmmunitionPairs, changed),
        new Collection<long>(CheckpointEntryKind.UncorrelatedWeapons, () => weapon().UncorrelatedWeaponFiringActions, changed),
        new Collection<long>(CheckpointEntryKind.UncorrelatedAmmunition, () => weapon().UncorrelatedAmmunitionFiringActions, changed),
        new Collection<CombatBreakdownAggregate>(CheckpointEntryKind.CombatEnemies, () => combat().Enemies, changed),
        new Collection<CombatBreakdownAggregate>(CheckpointEntryKind.CombatKillers, () => combat().Killers, changed),
        new Collection<CombatBreakdownAggregate>(CheckpointEntryKind.CombatFamilies, () => combat().Families, changed),
        new Collection<CombatBreakdownAggregate>(CheckpointEntryKind.CombatCauses, () => combat().Causes, changed),
        new Collection<CombatBreakdownAggregate>(CheckpointEntryKind.CombatWeapons, () => combat().Weapons, changed),
        new Collection<CombatBreakdownAggregate>(CheckpointEntryKind.CombatAmmunition, () => combat().Ammunition, changed),
        new Collection<CombatBreakdownAggregate>(CheckpointEntryKind.CombatOwnership, () => combat().Ownership, changed),
        new Collection<ItemAggregate>(CheckpointEntryKind.Items, () => items().Items, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.EquipmentItems, () => equipment().Items, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.SelectedWeapons, () => equipment().SelectedWeapons, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.Loadouts, () => equipment().Loadouts, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.TotemSets, () => equipment().TotemSets, changed),
        new Collection<EquipmentCombatAssociationAggregate>(CheckpointEntryKind.EquipmentCombat, () => equipment().CombatAssociations, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.TotemStates, () => equipment().TotemStates, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.Slots, () => equipment().Slots, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.SlottedWeapons, () => equipment().SlottedWeapons, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.CharacterSlotObserved, () => equipment().CharacterSlotObservedDurations, changed),
        new Collection<CharacterSlotStateDurationAggregate>(CheckpointEntryKind.CharacterSlotStates, () => equipment().CharacterSlotStates, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.NestedSlotObserved, () => equipment().NestedSlotObservedDurations, changed),
        new Collection<NestedSlotStateDurationAggregate>(CheckpointEntryKind.NestedSlotStates, () => equipment().NestedSlotStates, changed),
        new Collection<LoadoutDefinition>(CheckpointEntryKind.LoadoutDefinitions, () => equipment().Composition.Loadouts, changed),
        new Collection<ActiveTotemSetDefinition>(CheckpointEntryKind.TotemSetDefinitions, () => equipment().Composition.ActiveTotemSets, changed),
        new Collection<TotemStateDuration>(CheckpointEntryKind.TypedTotemStates, () => equipment().Composition.TotemStates, changed),
        new Collection<EquipmentDurationAggregate>(CheckpointEntryKind.EmptyDirectSlots, () => equipment().Composition.EmptyDirectSlots, changed)
    };

    private sealed class Collection<T> : CheckpointEntryCollection where T : notnull
    {
        private readonly Func<Dictionary<string, T>> source;
        private readonly Action changed;
        private Dictionary<string, T> observed;
        private EntryChangeLedger ledger;
        internal Collection(CheckpointEntryKind kind, Func<Dictionary<string, T>> source, Action changed)
        { Kind = kind; this.source = source; this.changed = changed; observed = source(); ledger = EntryChanges.Track(observed, changed); }
        internal override CheckpointEntryKind Kind { get; }
        internal override int Count => source().Count;
        internal override void Restore(string key, byte[] bytes)
        {
            var value = ProfileRecordCodec.Decode<T>(bytes);
            CheckpointEntryContracts.Validate(Kind, key, value);
            source().Add(key, value);
        }
        internal override CheckpointCollectionCapture Capture(ProfileRecordCodec codec)
        {
            var values = source();
            if (!ReferenceEquals(values, observed)) { observed = values; ledger = EntryChanges.Track(values, changed); }
            var receipt = ledger.Capture();
            var entries = receipt.Entries.Select(mark =>
            {
                if (!values.TryGetValue(mark.Key, out var value)) return new KeyValuePair<string, byte[]?>(mark.Key, null);
                CheckpointEntryContracts.Validate(Kind, mark.Key, value!);
                return new KeyValuePair<string, byte[]?>(mark.Key, codec.Encode(value!));
            }).ToArray();
            return new CheckpointCollectionCapture(Kind, values.Count, receipt, entries);
        }
    }
}
