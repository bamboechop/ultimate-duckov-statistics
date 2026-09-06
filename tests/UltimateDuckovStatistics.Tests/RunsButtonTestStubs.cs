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
        public bool enabled { get; set; } = true;
        public float TintAlpha { get; set; } = 1;
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
        private bool isEnabled = true;
        public bool enabled
        {
            get => isEnabled;
            set { if (isEnabled == value) return; isEnabled = value; if (!value) OnDisable(); }
        }
        public Transition transition { get; set; }
        public Navigation navigation { get; set; }
        public event Action? Clicked;
        public bool IsActive() => Active && enabled;
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
        // Installed UnityEngine.UI.Selectable.InstantClearState uses white on disable,
        // even when disabledColor is transparent. Model that boundary, not rendering.
        protected virtual void OnDisable()
        { if (transition == Transition.ColorTint && targetGraphic != null) targetGraphic.TintAlpha = 1; }
        protected enum SelectionState { Normal, Selected, Disabled }
        public bool Selected { get; set; }
        protected SelectionState currentSelectionState => !interactable ? SelectionState.Disabled : Selected ? SelectionState.Selected : SelectionState.Normal;
        protected void DoStateTransition(SelectionState state, bool instant)
        {
            if (transition == Transition.ColorTint && targetGraphic != null && instant)
                targetGraphic.TintAlpha = state == SelectionState.Selected ? .13f : 0;
        }
        public void Disable() { Active = false; OnDisable(); }
    }
}
