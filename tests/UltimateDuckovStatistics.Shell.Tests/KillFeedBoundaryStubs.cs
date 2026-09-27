// Graphics/Steam boundary only; the real HUD owner and row layout are source-linked.
namespace UltimateDuckovStatistics.UI
{
    internal sealed class KillFeedIcons : IDisposable
    {
        internal UnityEngine.Sprite Headshot { get; } = new();
        internal UnityEngine.Sprite? Weapon(string _) => null;
        public void Dispose() { }
    }
}
public static class SteamManager { public static bool Initialized = true; }
namespace Steamworks
{
    public static class SteamFriends { public static string GetPersonaName() => "bamboechop"; }
}
