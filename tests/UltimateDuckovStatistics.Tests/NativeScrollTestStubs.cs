// Only the shared scroll configuration contract is modelled here, not Unity's scrolling engine.
#pragma warning disable CA1708
namespace UnityEngine.UI
{
    public sealed class ScrollRect
    {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public MovementType movementType { get; set; }
        public float elasticity { get; set; }
        public bool inertia { get; set; }
        public float decelerationRate { get; set; }
        public float scrollSensitivity { get; set; }
    }
}
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static ScrollPrefabSettings UIPrefabs { get; } = new();
    }
    public sealed class ScrollPrefabSettings
    {
        public UnityEngine.UI.ScrollRect ScrollRect { get; } = new();
    }
}
