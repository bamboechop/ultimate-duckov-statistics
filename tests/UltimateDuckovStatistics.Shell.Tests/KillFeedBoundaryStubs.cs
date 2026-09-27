// Graphics/Steam boundary only; the real HUD owner and row layout are source-linked.
namespace UltimateDuckovStatistics.UI
{
    internal sealed class KillFeedIcons : IDisposable
    {
        internal UnityEngine.Sprite Headshot { get; } = new();
        internal static Dictionary<string, UnityEngine.Sprite> Weapons { get; } = new();
        internal UnityEngine.Sprite? Weapon(string id) => Weapons.GetValueOrDefault(id);
        public void Dispose() { }
    }
}
public static class SteamManager { public static bool Initialized = true; }
namespace Steamworks
{
    public static class SteamFriends { public static string GetPersonaName() => "bamboechop"; }
}

namespace Duckov.Scenes
{
    // MultiSceneCore transitions are separate from SceneLoader in the installed game.
    public sealed class MultiSceneCore : UnityEngine.MonoBehaviour
    {
        public static MultiSceneCore? Instance;
        public bool IsLoading { get; private set; }
        public static event Action<MultiSceneCore, UnityEngine.SceneManagement.Scene>? OnSubSceneWillBeUnloaded, OnSubSceneLoaded;
        public static int Listeners => (OnSubSceneWillBeUnloaded?.GetInvocationList().Length ?? 0) + (OnSubSceneLoaded?.GetInvocationList().Length ?? 0);
        public void BeginSubSceneLoad() { IsLoading = true; OnSubSceneWillBeUnloaded?.Invoke(this, new()); }
        public void FinishSubSceneLoad() { IsLoading = false; OnSubSceneLoaded?.Invoke(this, new()); }
    }
}
