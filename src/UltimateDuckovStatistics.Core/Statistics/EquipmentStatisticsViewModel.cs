using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

public sealed class EquipmentStatisticsViewModel
{
    public EquipmentStatisticsAggregate Lifetime { get; set; } = new();
    public EquipmentMetricCapabilities Capabilities { get; set; } = new();
    public List<EquipmentWeaponView> Weapons { get; set; } = new();
    public List<EquipmentCharacterSlotView> ArmorAndGearSlots { get; set; } = new();
}

public sealed class EquipmentWeaponView
{
    public string WeaponId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double TotalEquippedDurationSeconds { get; set; }
    public List<EquipmentWeaponSlotDurationView> CharacterSlots { get; set; } = new();
    public List<EquipmentNestedSlotGroupView> NestedSlotGroups { get; set; } = new();
}

public sealed class EquipmentWeaponSlotDurationView
{
    public string SlotId { get; set; } = string.Empty;
    public string SlotDisplayName { get; set; } = string.Empty;
    public double EquippedDurationSeconds { get; set; }
}

public sealed class EquipmentNestedSlotGroupView
{
    public string GroupKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<NestedSlotStateDurationAggregate> Rows { get; set; } = new();
}

public sealed class EquipmentCharacterSlotView
{
    public string SlotId { get; set; } = string.Empty;
    public string SlotDisplayName { get; set; } = string.Empty;
    public List<CharacterSlotStateDurationAggregate> Rows { get; set; } = new();
}

public static class EquipmentStatisticsViewModelFactory
{
    public static EquipmentStatisticsViewModel Create(ProfileDocument profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var aggregate = profile.Statistics.RunTotals.EquipmentStatistics;
        var capabilities = EquipmentStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        Apply(capabilities.EquipmentSlots, EquipmentCapabilityIds.EquipmentSlots);
        Apply(capabilities.SelectedWeapon, EquipmentCapabilityIds.SelectedWeapon);
        Apply(capabilities.AttachmentMetadata, EquipmentCapabilityIds.AttachmentMetadata);
        Apply(capabilities.DirectTotems, EquipmentCapabilityIds.DirectTotems);
        Apply(capabilities.ToteContents, EquipmentCapabilityIds.ToteContents);
        Apply(capabilities.ToteActivation, EquipmentCapabilityIds.ToteActivation);
        Apply(capabilities.CharacterSlotState, EquipmentCapabilityIds.CharacterSlotState);
        Apply(capabilities.NestedSlotState, EquipmentCapabilityIds.NestedSlotState);
        return new EquipmentStatisticsViewModel
        {
            Lifetime = aggregate,
            Capabilities = capabilities,
            Weapons = CreateWeaponViews(aggregate),
            ArmorAndGearSlots = CreateArmorAndGearViews(aggregate)
        };

        void Apply(MetricAvailability recorded, string id)
        {
            var current = profile.Capabilities.FirstOrDefault(value =>
                string.Equals(value.AdapterId, id, StringComparison.Ordinal));
            EquipmentStatisticsReducer.ApplyCurrentAvailability(
                aggregate,
                recorded,
                current?.State ?? AdapterCapabilityState.DisabledIncompatible,
                current?.Detail,
                allowUninitializedFallback: true);
        }
    }

    private static List<EquipmentWeaponView> CreateWeaponViews(EquipmentStatisticsAggregate aggregate) =>
        aggregate.CharacterSlotStates.Values
            .Where(value => value.State == EquipmentSlotState.Occupied
                            && value.ItemKind == EquipmentItemKind.Weapon)
            .GroupBy(value => value.ItemId, StringComparer.Ordinal)
            .Select(group =>
            {
                var rows = group.ToList();
                var weaponId = group.Key;
                return new EquipmentWeaponView
                {
                    WeaponId = weaponId,
                    DisplayName = EnrichedName(rows.Select(value => value.ItemDisplayName), weaponId),
                    TotalEquippedDurationSeconds = CheckedDurationSum(rows.Select(value => value.ActiveDurationSeconds)),
                    CharacterSlots = rows.GroupBy(value => value.SlotId, StringComparer.Ordinal)
                        .Select(slot => new EquipmentWeaponSlotDurationView
                        {
                            SlotId = slot.Key,
                            SlotDisplayName = EnrichedName(slot.Select(value => value.SlotDisplayName), slot.Key),
                            EquippedDurationSeconds = CheckedDurationSum(
                                slot.Select(value => value.ActiveDurationSeconds))
                        })
                        .OrderBy(value => value.SlotId, StringComparer.Ordinal)
                        .ToList(),
                    NestedSlotGroups = aggregate.NestedSlotStates.Values
                        .Where(value => value.ParentItemKind == EquipmentItemKind.Weapon
                                        && string.Equals(value.ParentItemId, weaponId, StringComparison.Ordinal))
                        .GroupBy(value => NestedGroup(value.SlotKey), StringComparer.Ordinal)
                        .Select(nested => new EquipmentNestedSlotGroupView
                        {
                            GroupKey = nested.Key,
                            DisplayName = NestedGroupDisplayName(nested.Key, nested),
                            Rows = nested.OrderBy(value => value.ParentSlotId, StringComparer.Ordinal)
                                .ThenBy(value => value.Path, StringComparer.Ordinal)
                                .ThenBy(value => value.State)
                                .ThenBy(value => value.ItemId, StringComparer.Ordinal)
                                .ToList()
                        })
                        .OrderBy(value => NestedGroupOrder(value.GroupKey))
                        .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
                        .ToList()
                };
            })
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.WeaponId, StringComparer.Ordinal)
            .ToList();

    private static List<EquipmentCharacterSlotView> CreateArmorAndGearViews(
        EquipmentStatisticsAggregate aggregate)
    {
        var weaponSlots = aggregate.CharacterSlotStates.Values
            .Where(value => value.State == EquipmentSlotState.Occupied
                            && value.ItemKind == EquipmentItemKind.Weapon)
            .Select(value => value.SlotId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var slotId in aggregate.CharacterSlotStates.Values.Select(value => value.SlotId)
                     .Where(IsKnownWeaponSlot))
            weaponSlots.Add(slotId);
        return aggregate.CharacterSlotStates.Values
            .Where(value => !weaponSlots.Contains(value.SlotId))
            .GroupBy(value => value.SlotId, StringComparer.Ordinal)
            .Select(group => new EquipmentCharacterSlotView
            {
                SlotId = group.Key,
                SlotDisplayName = EnrichedName(group.Select(value => value.SlotDisplayName), group.Key),
                Rows = group.OrderBy(value => value.State)
                    .ThenBy(value => value.ItemId, StringComparer.Ordinal)
                    .ToList()
            })
            .OrderBy(value => value.SlotId, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsKnownWeaponSlot(string slotId)
    {
        const string nativePrefix = "duckov:slot:";
        const string legacyPrefix = "slot:";
        var key = slotId.StartsWith(nativePrefix, StringComparison.OrdinalIgnoreCase)
            ? slotId[nativePrefix.Length..]
            : slotId.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)
                ? slotId[legacyPrefix.Length..]
                : slotId;
        return key.IndexOf(':') < 0
               && (key.Equals("PrimaryWeapon", StringComparison.OrdinalIgnoreCase)
               || key.Equals("SecondaryWeapon", StringComparison.OrdinalIgnoreCase)
               || key.Equals("MeleeWeapon", StringComparison.OrdinalIgnoreCase)
               || key.Equals("primary", StringComparison.OrdinalIgnoreCase)
               || key.Equals("secondary", StringComparison.OrdinalIgnoreCase)
               || key.Equals("melee", StringComparison.OrdinalIgnoreCase));
    }

    private static string NestedGroup(string slotKey)
    {
        if (slotKey.Equals("Scope", StringComparison.OrdinalIgnoreCase)) return "scope";
        if (slotKey.Equals("Muzzle", StringComparison.OrdinalIgnoreCase)) return "muzzle";
        if (slotKey.Equals("Grip", StringComparison.OrdinalIgnoreCase)) return "grip";
        if (slotKey.Equals("Stock", StringComparison.OrdinalIgnoreCase)) return "stock";
        if (slotKey.Equals("Tactic", StringComparison.OrdinalIgnoreCase)
            || slotKey.Equals("Tactical", StringComparison.OrdinalIgnoreCase)
            || slotKey.Equals("Tactics", StringComparison.OrdinalIgnoreCase)) return "tactics";
        if (slotKey.Equals("Mag", StringComparison.OrdinalIgnoreCase)
            || slotKey.Equals("Magazine", StringComparison.OrdinalIgnoreCase)) return "magazine";
        return "native:" + slotKey;
    }

    private static string NestedGroupDisplayName(
        string groupKey,
        IEnumerable<NestedSlotStateDurationAggregate> rows) => groupKey switch
        {
            "scope" => "Scope",
            "muzzle" => "Muzzle",
            "grip" => "Grip",
            "stock" => "Stock",
            "tactics" => "Tactics",
            "magazine" => "Magazine",
            _ => EnrichedName(rows.Select(value => value.SlotDisplayName), groupKey["native:".Length..])
        };

    private static int NestedGroupOrder(string value) => value switch
    {
        "scope" => 0,
        "muzzle" => 1,
        "grip" => 2,
        "stock" => 3,
        "tactics" => 4,
        "magazine" => 5,
        _ => 6
    };

    private static string EnrichedName(IEnumerable<string> names, string fallback) =>
        names.Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault() ?? fallback;

    private static double CheckedDurationSum(IEnumerable<double> values)
    {
        var total = 0d;
        foreach (var value in values)
        {
            total += value;
            if (double.IsNaN(total) || double.IsInfinity(total))
                throw new OverflowException("Equipment view duration exceeds the representable range.");
        }
        return total;
    }
}
