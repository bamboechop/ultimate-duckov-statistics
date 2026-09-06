using ItemStatsSystem;

namespace UltimateDuckovStatistics.UI;

internal static class NativeThrowableIdentity
{
    public static bool IsThrowable(string stableId)
    {
        if (!NativeItemTypeIdPolicy.TryParse(stableId, out var typeId)) return false;
        try
        {
            // Read the exact registered prefab; do not instantiate an item or classify by translated name.
            var prefab = ItemAssetsCollection.GetPrefab(typeId);
            return prefab != null && prefab.TypeID == typeId && prefab.GetComponent<ItemSetting_Skill>()?.Skill is Skill_Grenade;
        }
        catch { return false; }
    }
}
