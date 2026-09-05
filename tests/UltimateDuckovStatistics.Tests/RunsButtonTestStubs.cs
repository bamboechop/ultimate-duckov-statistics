// Minimal event boundary for the linked production RunsHistoryButton. Rendering, raycast sorting,
// audio and native GameObject lifecycle still require the manual Unity protocol.
#pragma warning disable CA1716, CA1720, CA1051, CA1708
namespace UnityEngine.EventSystems
{
    public class BaseEventData;
    public sealed class PointerEventData : BaseEventData
    {
        public enum InputButton { Left, Right, Middle }
        public InputButton button;
        public bool dragging;
    }
}

namespace UnityEngine.UI
{
    public sealed class Graphic
    {
        public bool raycastTarget { get; set; }
    }
    public struct Navigation
    {
        public enum Mode { None, Automatic }
        public Mode mode;
    }
    public class Button
    {
        public enum Transition { None, ColorTint }
        public Graphic? targetGraphic { get; set; }
        public bool interactable { get; set; }
        public bool Active { get; set; } = true;
        public Transition transition { get; set; }
        public Navigation navigation { get; set; }
        public event Action? Clicked;
        public bool IsActive() => Active;
        public bool IsInteractable() => interactable;
        public virtual void OnPointerDown(EventSystems.PointerEventData data) { }
        public virtual void OnPointerClick(EventSystems.PointerEventData data)
        {
            if (data.button == EventSystems.PointerEventData.InputButton.Left && IsActive() && IsInteractable()) Clicked?.Invoke();
        }
        public void OnSubmit(EventSystems.BaseEventData data)
        {
            if (IsActive() && IsInteractable()) Clicked?.Invoke();
        }
        protected virtual void OnDisable() { }
        public void Disable() { Active = false; OnDisable(); }
    }
}
