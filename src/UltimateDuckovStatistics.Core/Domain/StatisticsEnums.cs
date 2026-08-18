using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public enum GameplayContext
{
    [EnumMember] Unknown = 0,
    [EnumMember] Base = 1,
    [EnumMember] Raid = 2,
    [EnumMember] Paused = 3,
    [EnumMember] Shop = 4,
    [EnumMember] Reward = 5
}

[DataContract]
public enum CurrencyKind
{
    [EnumMember] Money = 0,
    [EnumMember] Cash = 1
}

[DataContract]
public enum CurrencyFlowDirection
{
    [EnumMember] Inflow = 0,
    [EnumMember] Outflow = 1
}

[DataContract]
public enum CurrencySourceCategory
{
    [EnumMember] UnknownAdjustment = 0,
    [EnumMember] Purchase = 1,
    [EnumMember] Sale = 2,
    [EnumMember] Reward = 3,
    [EnumMember] LootOrPickup = 4,
    [EnumMember] FeeOrCraftingCost = 5
}

[Flags]
[DataContract]
public enum IntegrityTags
{
    [EnumMember] Unknown = 0,
    [EnumMember] Normal = 1,
    [EnumMember] CheatOrCustomDifficulty = 2,
    [EnumMember] ModdedContent = 4
}

[DataContract]
public enum AdapterCapabilityState
{
    [EnumMember] Supported = 0,
    [EnumMember] Experimental = 1,
    [EnumMember] DisabledIncompatible = 2
}

[DataContract]
public enum CanonicalItemGroup
{
    [EnumMember] Healing = 0,
    [EnumMember] Food = 1,
    [EnumMember] Drink = 2,
    [EnumMember] StimulantBuff = 3,
    [EnumMember] RemedyDebuffRemoval = 4,
    [EnumMember] Special = 5,
    [EnumMember] OtherUnknown = 6
}

[DataContract]
public enum ItemEffectTag
{
    [EnumMember] Healing = 0,
    [EnumMember] Food = 1,
    [EnumMember] Drink = 2,
    [EnumMember] Buff = 3,
    [EnumMember] DebuffRemoval = 4,
    [EnumMember] Special = 5
}

[DataContract]
public enum ConsumptionUnit
{
    [EnumMember] Item = 0,
    [EnumMember] StackUnit = 1,
    [EnumMember] Durability = 2,
    [EnumMember] UnknownAmount = 3
}

[DataContract]
public enum RunOutcome
{
    [EnumMember] Extracted = 0,
    [EnumMember] Died = 1,
    [EnumMember] Interrupted = 2
}

[DataContract]
public enum CombatOwnership
{
    [EnumMember] Unknown = 0,
    [EnumMember] Player = 1,
    [EnumMember] PetCompanion = 2,
    [EnumMember] Environmental = 3
}

[DataContract]
public enum CombatAttackKind
{
    [EnumMember] Unknown = 0,
    [EnumMember] Ranged = 1,
    [EnumMember] Melee = 2,
    [EnumMember] Effect = 3,
    [EnumMember] Environmental = 4
}

[DataContract]
public enum CombatCauseKind
{
    [EnumMember] Unknown = 0,
    [EnumMember] Direct = 1,
    [EnumMember] DamageOverTime = 2,
    [EnumMember] Effect = 3,
    [EnumMember] Explosion = 4,
    [EnumMember] RealDamage = 5,
    [EnumMember] Environmental = 6
}
