// Native signatures only: production UI ownership is source-linked.
namespace UnityEngine { public enum TextAnchor { MiddleCenter } }
namespace UnityEngine.UI
{
    public class HorizontalLayoutGroup : LayoutGroup
    {
        public float spacing;
        public bool childControlWidth, childControlHeight, childForceExpandWidth, childForceExpandHeight;
    }
    public class VerticalLayoutGroup : HorizontalLayoutGroup { public UnityEngine.TextAnchor childAlignment; }
}
namespace Duckov.UI
{
    public class ManagedUIElement : UnityEngine.MonoBehaviour
    {
        public static event Action<ManagedUIElement>? onOpen, onClose;
        public static int Subscribers => (onOpen?.GetInvocationList().Length ?? 0) + (onClose?.GetInvocationList().Length ?? 0);
        public bool open { get; private set; }
        public void Open() { open = true; onOpen?.Invoke(this); }
        public void Close() { open = false; onClose?.Invoke(this); }
    }
    public class ClosureView : ManagedUIElement { }
}
public static class RaidUtilities
{
    public struct RaidInfo { public bool valid; public int ID; }
    public static RaidInfo CurrentRaid { get; set; }
}
namespace UltimateDuckovStatistics.Adapters
{
    internal static class NativeExtractionValuation { public static decimal? Read(Action<string> diagnostic) => null; }
}
