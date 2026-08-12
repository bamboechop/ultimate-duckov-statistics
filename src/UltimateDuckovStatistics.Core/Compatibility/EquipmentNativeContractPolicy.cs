using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class EquipmentCapabilityIds
{
    public const string EquipmentSlots = "native-equipment-slots";
    public const string SelectedWeapon = "native-selected-weapon";
    public const string AttachmentMetadata = "native-weapon-attachments";
    public const string DirectTotems = "native-direct-totems";
    public const string ToteContents = "native-tote-contents";
    public const string ToteActivation = "native-tote-totem-activation";
    public static readonly string[] All = { EquipmentSlots, SelectedWeapon, AttachmentMetadata, DirectTotems, ToteContents, ToteActivation };
}

public static class EquipmentNativeContractPolicy
{
    public static EquipmentMetricCapabilities CreateSupportedCapabilities() => new()
    {
        EquipmentSlots = Availability(AdapterCapabilityState.Supported,
            "CharacterMainControl.CharacterItem.Slots exposes direct equipped slot contents."),
        SelectedWeapon = Availability(AdapterCapabilityState.Supported,
            "CharacterMainControl.CurrentHoldItemAgent exposes the currently held slotted weapon."),
        AttachmentMetadata = Availability(AdapterCapabilityState.Supported,
            "Each equipped Item exposes its connected public Slot tree and stable Item.TypeID values."),
        DirectTotems = Availability(AdapterCapabilityState.Supported,
            "A direct character slot plus the exact Totem tag proves direct-equipped totem presence."),
        ToteContents = Availability(AdapterCapabilityState.Supported,
            "CharacterItem.Inventory exposes top-level Item_ToteBag containers whose attached Inventory contents expose Totem tags."),
        ToteActivation = Availability(AdapterCapabilityState.DisabledIncompatible,
            "Presence in a tote does not prove that modifiers or effects are active; activation tracking is disabled pending gameplay proof.")
    };

    public static EquipmentMetricCapabilities CreateUnavailableCapabilities(string detail) => new()
    {
        EquipmentSlots = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        SelectedWeapon = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        AttachmentMetadata = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        DirectTotems = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        ToteContents = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        ToteActivation = Availability(AdapterCapabilityState.DisabledIncompatible, detail)
    };

    public static IReadOnlyList<CapabilityRecord> ToRecords(EquipmentMetricCapabilities value, string version) => new[]
    {
        Record(EquipmentCapabilityIds.EquipmentSlots, value.EquipmentSlots, version),
        Record(EquipmentCapabilityIds.SelectedWeapon, value.SelectedWeapon, version),
        Record(EquipmentCapabilityIds.AttachmentMetadata, value.AttachmentMetadata, version),
        Record(EquipmentCapabilityIds.DirectTotems, value.DirectTotems, version),
        Record(EquipmentCapabilityIds.ToteContents, value.ToteContents, version),
        Record(EquipmentCapabilityIds.ToteActivation, value.ToteActivation, version)
    };

    private static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    { State = state, Provenance = provenance };

    private static CapabilityRecord Record(string id, MetricAvailability value, string version) => new()
    { AdapterId = id, State = value.State, Version = version, Detail = value.Provenance };
}
