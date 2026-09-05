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
