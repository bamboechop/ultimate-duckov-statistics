using Duckov.UI;
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Encounters;

internal sealed partial class NativeEncounterLootObserver
{
    private WeakReference<LootView>? activeView;
    private Corpse? activeViewCorpse;

    private void ViewOpened(LootView view)
    {
        if (!Active) return;
        CloseView("another-view-opened");
        if (viewTargetBox.GetValue(view) is not InteractableLootbox box || box == null
            || !boxes.TryGetValue(box, out var corpse)
            || !ReferenceEquals(interactingCharacter.GetValue(box), CharacterMainControl.Main)
            || localInventory.GetValue(box) is not Inventory inventory || inventory == null) return;
        activeView = new WeakReference<LootView>(view);
        activeViewCorpse = corpse;
        corpse.Open = true;
        corpse.Observed = true;
        var rows = new List<object>();
        var count = Math.Min(inventory.Capacity, 512);
        for (var index = 0; index < count; index++)
        {
            var item = inventory.GetItemAt(index);
            if (item == null) continue;
            Track(item, corpse);
            rows.Add(Row(item, corpse, index));
        }
        Emit("inventory-open", new
        {
            corpse.ActorId, CorpseId = corpse.BoxId, corpse.InventoryId,
            Loading = inventory.Loading, Rows = rows.ToArray(),
            Boundary = "LootView.OnOpen completed",
            Coverage = inventory.Capacity > 512 || observationLimited ? "Partial" : "TopLevelObservedStateOnly"
        });
    }

    private void ViewClosed(LootView view)
    {
        if (activeView?.TryGetTarget(out var observed) == true && ReferenceEquals(observed, view))
            CloseView("LootView.OnClose-or-OnDisable");
    }

    private void CloseView(string reason)
    {
        var corpse = activeViewCorpse;
        activeView = null;
        activeViewCorpse = null;
        if (corpse == null) return;
        corpse.Open = false;
        if (Active) Emit("inventory-view-close", new { corpse.ActorId, CorpseId = corpse.BoxId, corpse.InventoryId, Reason = reason });
    }

    private void RefreshViewLifetime()
    {
        if (activeViewCorpse == null) return;
        if (activeView == null || !activeView.TryGetTarget(out var view) || view == null || !view.open || !view.isActiveAndEnabled
            || viewTargetBox.GetValue(view) is not InteractableLootbox box || box == null
            || !boxes.TryGetValue(box, out var corpse) || !ReferenceEquals(corpse, activeViewCorpse))
            CloseView("view-no-longer-displays-linked-corpse");
    }
}
