using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    // Only the scroll viewport is isolated. The modal's focus, blocking, buttons,
    // operation dispatch and restoration all execute RetainedPanelModal.cs.
    private sealed class ScrollRegion : IDisposable
    {
        public RectTransform Content { get; }
        public ScrollRegion(RectTransform parent, string name, float radius)
        {
            Content = (RectTransform)new GameObject(name).transform;
            Content.SetParent(parent);
        }
        public void Size(float x, float y, float width, float height, float contentHeight) { }
        public void Cues() { }
        public void Dispose() { }
    }
}
