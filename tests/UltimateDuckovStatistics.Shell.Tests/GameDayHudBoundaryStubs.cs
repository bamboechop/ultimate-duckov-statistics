// Installed native event/access boundary for the production game-day HUD owner.
using TMPro;
using UnityEngine;

public sealed class GameClock : MonoBehaviour
{
    public static GameClock? Instance;
    public long Days;
    public static long Day => Instance == null ? 0 : Instance.Days;
    public static event Action? OnGameClockStep;
    public static int Listeners => OnGameClockStep?.GetInvocationList().Length ?? 0;
    public static void Step() => OnGameClockStep?.Invoke();
}
public sealed class TimeOfDayDisplay : MonoBehaviour { public TextMeshProUGUI weatherText = null!; }
public sealed class SceneLoadingContext { }
public static class SceneLoader
{
    public static bool IsSceneLoading { get; private set; }
    public static event Action<SceneLoadingContext>? onStartedLoadingScene, onFinishedLoadingScene;
    public static int Listeners => (onStartedLoadingScene?.GetInvocationList().Length ?? 0) + (onFinishedLoadingScene?.GetInvocationList().Length ?? 0);
    public static void StartLoading() { IsSceneLoading = true; onStartedLoadingScene?.Invoke(new()); }
    public static void FinishLoading() { IsSceneLoading = false; onFinishedLoadingScene?.Invoke(new()); }
}
namespace Saves
{
    public static class SavesSystem
    {
        public static event Action? OnSetFile, OnSaveDeleted;
        public static int Listeners => (OnSetFile?.GetInvocationList().Length ?? 0) + (OnSaveDeleted?.GetInvocationList().Length ?? 0);
        public static void SetFile() => OnSetFile?.Invoke();
        public static void DeleteSave() => OnSaveDeleted?.Invoke();
    }
}
