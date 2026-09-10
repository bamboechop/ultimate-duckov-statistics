using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class WeaponNativeContractPolicy
{
    public const string FiringActionProvenance = "ItemAgent_Gun.OnMainCharacterShootEvent";
    public const string WeaponIdentityProvenance = "ItemAgent_Gun.Item.TypeID at firing time";
    public const string AmmunitionIdentityProvenance = "ItemSetting_Gun.TargetBulletID at firing time";
    public const string PairingProvenance =
        "One accepted ItemAgent_Gun.OnMainCharacterShootEvent callback simultaneously exposes the firing Item.TypeID and ItemSetting_Gun.TargetBulletID.";

    public static WeaponMetricCapabilities CreateMetricCapabilities() => new()
    {
        FiringActions = Availability(AdapterCapabilityState.Supported, FiringActionProvenance),
        WeaponIdentity = Availability(AdapterCapabilityState.Supported, WeaponIdentityProvenance),
        AmmunitionIdentity = Availability(AdapterCapabilityState.Supported, AmmunitionIdentityProvenance),
        WeaponAmmunitionPairing = Availability(AdapterCapabilityState.Supported, PairingProvenance)
    };

    private static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    {
        State = state,
        Provenance = provenance
    };
}
