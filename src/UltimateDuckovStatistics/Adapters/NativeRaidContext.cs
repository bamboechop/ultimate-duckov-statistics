using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRaidContext : IDisposable
{
    private bool subscribed;
    private string? currentRunId;

    public string? CurrentRunId => currentRunId;

    public void Subscribe()
    {
        if (subscribed)
        {
            return;
        }

        RaidUtilities.OnNewRaid += OnNewRaid;
        RaidUtilities.OnRaidEnd += OnRaidEnd;
        subscribed = true;
    }

    public void Dispose()
    {
        if (!subscribed)
        {
            return;
        }

        RaidUtilities.OnNewRaid -= OnNewRaid;
        RaidUtilities.OnRaidEnd -= OnRaidEnd;
        currentRunId = null;
        subscribed = false;
    }

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

    private void OnNewRaid(RaidUtilities.RaidInfo raid)
    {
        currentRunId = $"raid:{raid.ID}";
    }

    private void OnRaidEnd(RaidUtilities.RaidInfo raid)
    {
        if (currentRunId == $"raid:{raid.ID}")
        {
            currentRunId = null;
        }
    }
}
