using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class WeaponNativeContractPolicy
{
    public const string FiringActionProvenance = "ItemAgent_Gun.OnMainCharacterShootEvent";
    public const string AmmunitionUnavailableDetail =
        "The public firing callback does not prove that ItemSetting_Gun.UseABullet consumed an item.";
    public const string ProjectilesUnavailableDetail =
        "ShotCount is configured intent; the public firing callback does not prove completed projectile initialization.";
    public const string WeaponIdentityProvenance = "ItemAgent_Gun.Item.TypeID at firing time";
    public const string AmmunitionIdentityProvenance = "ItemSetting_Gun.TargetBulletID at firing time";

    public static WeaponMetricCapabilities CreateMetricCapabilities() => new()
    {
        FiringActions = Availability(AdapterCapabilityState.Supported, FiringActionProvenance),
        AmmunitionConsumption = Availability(
            AdapterCapabilityState.DisabledIncompatible,
            AmmunitionUnavailableDetail),
        Projectiles = Availability(
            AdapterCapabilityState.DisabledIncompatible,
            ProjectilesUnavailableDetail),
        WeaponIdentity = Availability(AdapterCapabilityState.Supported, WeaponIdentityProvenance),
        AmmunitionIdentity = Availability(AdapterCapabilityState.Supported, AmmunitionIdentityProvenance)
    };

    private static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    {
        State = state,
        Provenance = provenance
    };
}
