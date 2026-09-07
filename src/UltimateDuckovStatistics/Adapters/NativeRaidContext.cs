using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeRaidContext
{
    public static GameplayContext GetGameplayContext()
    {
        try
        {
            if (GameManager.Paused)
            {
                return GameplayContext.Paused;
            }

            var level = LevelManager.Instance;
            if (level == null)
            {
                return GameplayContext.Unknown;
            }

            if (level.IsBaseLevel)
            {
                return GameplayContext.Base;
            }

            return level.IsRaidMap ? GameplayContext.Raid : GameplayContext.Unknown;
        }
        catch
        {
            return GameplayContext.Unknown;
        }
    }

    public static string? GetMapId()
    {
        try
        {
            var level = LevelManager.GetCurrentLevelInfo();
            if (string.IsNullOrWhiteSpace(level.sceneName))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(level.activeSubSceneID)
                ? level.sceneName
                : $"{level.sceneName}/{level.activeSubSceneID}";
        }
        catch
        {
            return null;
        }
    }

    public static bool IsRaidMap()
    {
        try
        {
            return LevelManager.Instance != null && LevelManager.Instance.IsRaidMap;
        }
        catch
        {
            return false;
        }
    }

}
