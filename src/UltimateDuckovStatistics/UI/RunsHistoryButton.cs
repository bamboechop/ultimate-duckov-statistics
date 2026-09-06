using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

internal sealed class RunsHistoryButton : Button
{
    public void Configure(Graphic fullRowBackground)
    {
        fullRowBackground.raycastTarget = true;
        targetGraphic = fullRowBackground;
        interactable = true;
        transition = Transition.None;
        navigation = new Navigation { mode = Navigation.Mode.None };
    }
    public RunsRowBinding Binding { get; } = new();
    public void BindInteractionOverlay(Graphic fullRowBackground, bool actionable)
    {
        interactable = actionable;
        enabled = actionable;
        fullRowBackground.raycastTarget = actionable;
        // Selectable.OnDisable resets ColorTint to white, not disabledColor. A decorative
        // pooled row must hide its separate overlay, while keeping its background visible.
        targetGraphic!.enabled = actionable;
        if (actionable) DoStateTransition(currentSelectionState, instant: true);
    }
    public override void OnPointerDown(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable()) Binding.Press();
        base.OnPointerDown(data);
    }
    public override void OnPointerClick(PointerEventData data)
    {
        if (Binding.Release(data.dragging)) base.OnPointerClick(data);
    }
    protected override void OnDisable() { Binding.CancelPointer(); base.OnDisable(); }
}
