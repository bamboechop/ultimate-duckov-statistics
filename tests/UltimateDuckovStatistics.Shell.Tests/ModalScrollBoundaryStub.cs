using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    // Only the scroll viewport is isolated. The modal's focus, blocking, buttons,
    // operation dispatch and restoration all execute RetainedPanelModal.cs.
    private sealed class ScrollRegion : IDisposable
    {
        public UnityEngine.UI.ScrollRect Scroll { get; }
        public RectTransform Rect { get; }
        public RectTransform Content { get; }
        public float Offset => Math.Max(0, Content.anchoredPosition.y);
        public ScrollRegion(RectTransform parent, string name, float radius = 10)
        {
            Rect = (RectTransform)new GameObject(name).transform;
            Rect.SetParent(parent);
            Rect.gameObject.AddComponent<RunsFocusHandler>();
            Rect.gameObject.AddComponent<UnityEngine.UI.Selectable>();
            Scroll = Rect.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            Scroll.scrollSensitivity = 40;
            Scroll.viewport = (RectTransform)new GameObject("Viewport").transform;
            Scroll.viewport.SetParent(Rect);
            Content = (RectTransform)new GameObject("Content").transform;
            Content.SetParent(Scroll.viewport); Scroll.content = Content;
        }
        public void Size(float x, float y, float width, float height, float contentHeight)
        {
            Rect.anchoredPosition = new Vector2(x, -y); Rect.sizeDelta = new Vector2(width, height);
            Content.sizeDelta = new Vector2(width, Math.Max(height, contentHeight)); SetOffset(Offset);
        }
        public void SetOffset(float offset) => Content.anchoredPosition = new Vector2(0, Math.Clamp(offset, 0, Math.Max(0, Content.rect.height - Rect.rect.height)));
        public void Cues() { }
        public void Dispose() { }
    }
}
