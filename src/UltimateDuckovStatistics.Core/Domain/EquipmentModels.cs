using System.Runtime.Serialization;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public enum EquipmentItemKind
{
    [EnumMember] Unknown = 0,
    [EnumMember] Weapon = 1,
    [EnumMember] Armor = 2,
    [EnumMember] Helmet = 3,
    [EnumMember] Backpack = 4,
    [EnumMember] Face = 5,
    [EnumMember] Headset = 6,
    [EnumMember] Totem = 7,
    [EnumMember] Other = 8
}

[DataContract]
public enum TotemCarryKind
{
    [EnumMember] DirectSlot = 0,
    [EnumMember] ToteInventory = 1
}

[DataContract]
public enum TotemActivationState
{
    [EnumMember] Unknown = 0,
    [EnumMember] ProvenActive = 1,
    [EnumMember] ProvenInactive = 2
}

[DataContract]
public sealed class EquipmentMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability EquipmentSlots { get; set; } = new();
    [DataMember(Order = 2)] public MetricAvailability SelectedWeapon { get; set; } = new();
    [DataMember(Order = 3)] public MetricAvailability AttachmentMetadata { get; set; } = new();
    [DataMember(Order = 4)] public MetricAvailability DirectTotems { get; set; } = new();
    [DataMember(Order = 5)] public MetricAvailability ToteContents { get; set; } = new();
    [DataMember(Order = 6)] public MetricAvailability ToteActivation { get; set; } = new();
}

[DataContract]
public sealed class EquippedItemSnapshot
{
    [DataMember(Order = 1)] public string SlotId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string SlotDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public string ItemId { get; set; } = string.Empty;
    [DataMember(Order = 4)] public string ItemDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 5)] public EquipmentItemKind Kind { get; set; }
    [DataMember(Order = 6)] public string AttachmentSignature { get; set; } = string.Empty;
}

[DataContract]
public sealed class TotemSnapshot
{
    [DataMember(Order = 1)] public string ItemId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public TotemCarryKind CarryKind { get; set; }
    [DataMember(Order = 4)] public string ContainerId { get; set; } = string.Empty;
    [DataMember(Order = 5)] public TotemActivationState ActivationState { get; set; }
}

[DataContract]
public sealed class EquipmentSnapshot
{
    [DataMember(Order = 1)] public string SnapshotId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string LoadoutId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public List<EquippedItemSnapshot> Items { get; set; } = new();
    [DataMember(Order = 4)] public string SelectedWeaponId { get; set; } = string.Empty;
    [DataMember(Order = 5)] public string TotemSetId { get; set; } = string.Empty;
    [DataMember(Order = 6)] public List<TotemSnapshot> Totems { get; set; } = new();
    [DataMember(Order = 7)] public string SelectedWeaponSlotId { get; set; } = string.Empty;
}

[DataContract]
public sealed class EquipmentEventAssociation
{
    public const string UnavailableId = "duckov:equipment:unavailable";

    [DataMember(Order = 1)] public string LoadoutId { get; set; } = UnavailableId;
    [DataMember(Order = 2)] public string SelectedWeaponId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public string TotemSetId { get; set; } = UnavailableId;
    [DataMember(Order = 4)] public string SelectedWeaponSlotId { get; set; } = string.Empty;
}

public static class EquipmentIdentity
{
    public static string LoadoutId(IEnumerable<EquippedItemSnapshot> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        var canonical = string.Join(";", items.OrderBy(value => value.SlotId, StringComparer.Ordinal)
            .Select(value => value.SlotId + "=" + value.ItemId + "@" + value.AttachmentSignature));
        return "duckov:loadout:" + StableHash(canonical);
    }

    public static string ActiveTotemSetId(IEnumerable<TotemSnapshot> totems)
    {
        if (totems == null) throw new ArgumentNullException(nameof(totems));
        var canonical = string.Join(";", totems
            .Where(value => value.ActivationState == TotemActivationState.ProvenActive)
            .Select(TotemCanonical)
            .OrderBy(value => value, StringComparer.Ordinal));
        return "duckov:totem-set:" + StableHash(canonical);
    }

    public static string TotemPresenceSignature(IEnumerable<TotemSnapshot> totems)
    {
        if (totems == null) throw new ArgumentNullException(nameof(totems));
        var canonical = string.Join(";", totems.Select(value =>
                TotemCanonical(value) + "=" + ((int)value.ActivationState).ToString(CultureInfo.InvariantCulture))
            .OrderBy(value => value, StringComparer.Ordinal));
        return StableHash(canonical);
    }

    public static string SnapshotId(
        string loadoutId,
        string selectedWeaponSlotId,
        string selectedWeaponId,
        string totemSetId,
        string totemPresenceSignature) => "duckov:equipment-snapshot:" + StableHash(
            (loadoutId ?? string.Empty) + "|" + (selectedWeaponSlotId ?? string.Empty) + "|"
            + (selectedWeaponId ?? string.Empty) + "|" + (totemSetId ?? string.Empty) + "|"
            + (totemPresenceSignature ?? string.Empty));

    public static string StableHash(string value)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant().Substring(0, 24);
    }

    private static string TotemCanonical(TotemSnapshot value) =>
        ((int)value.CarryKind).ToString(CultureInfo.InvariantCulture) + "="
        + value.ContainerId + "=" + value.ItemId;
}
