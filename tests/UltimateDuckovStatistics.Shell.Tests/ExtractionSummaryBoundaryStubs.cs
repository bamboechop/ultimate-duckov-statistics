// Native signatures: production UI ownership is source-linked. Layout defaults match UnityEngine.UI.
namespace UnityEngine
{
    public enum TextAnchor { UpperLeft = 0, MiddleCenter = 4 }
    public class RectOffset(int left = 0, int right = 0, int top = 0, int bottom = 0)
    { public int left = left, right = right, top = top, bottom = bottom; }
}
namespace UnityEngine.UI
{
    public class HorizontalLayoutGroup : LayoutGroup
    {
        public float spacing;
        public bool childControlWidth = true, childControlHeight = true, childForceExpandWidth = true, childForceExpandHeight = true;
        public bool childScaleWidth, childScaleHeight;
        public UnityEngine.TextAnchor childAlignment;
        public UnityEngine.RectOffset padding = new();
    }
    public class VerticalLayoutGroup : HorizontalLayoutGroup { }
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
