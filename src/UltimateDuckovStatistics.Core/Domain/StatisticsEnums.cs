using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public enum GameplayContext
{
    [EnumMember] Unknown = 0,
    [EnumMember] Base = 1,
    [EnumMember] Raid = 2,
    [EnumMember] Paused = 3
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
