using Cysharp.Threading.Tasks;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace UltimateDuckovStatistics.Encounters;

internal sealed partial class NativeEncounterLootObserver
{
    // Observer taps used by the existing verified container owner. Never propagate an
    // observer exception into its callbacks or alter the original native exception.
    internal static object? ObserveDeathStart(CharacterMainControl actor)
    {
        var probe = current;
        if (probe == null || !probe.Active) return null;
        DeathScope? state = null;
        probe.Guard(() => state = probe.BeginDeath(actor));
        return state;
    }

    internal static void ObserveDeathEnd(object? state)
    {
        var probe = current;
        if (probe != null) probe.Guard(() => probe.EndDeath(state as DeathScope));
    }

    internal static void ObserveCreated(Item source, InteractableLootbox? result)
    {
        var probe = current;
        if (probe != null && probe.Active) probe.Guard(() => probe.Created(source, result));
    }

    private static void DetachPrefix(Item __instance, out Operation? __state)
    {
        __state = null;
        var probe = current;
        if (probe == null || !probe.Active || probe.subscriptions.Count == 0) return;
        Operation? state = null;
        probe.Guard(() => state = probe.Begin("Detach", __instance, Endpoint.Unknown));
        __state = state;
    }

    private static Exception? DetachFinalizer(Exception? __exception, Operation? __state)
    { FinishSafe(__state, null, __exception); return __exception; }

    private static void CombinePrefix(Item __instance, Item incomingItem, out Operation? __state)
    {
        __state = null;
        var probe = current;
        if (probe == null || !probe.Active || probe.subscriptions.Count == 0 || incomingItem == null) return;
        Operation? state = null;
        probe.Guard(() => state = probe.Begin("Combine", incomingItem, probe.Resolve(__instance), __instance));
        __state = state;
    }

    private static Exception? CombineFinalizer(Exception? __exception, Operation? __state)
    { FinishSafe(__state, null, __exception); return __exception; }

    private static void AddAtPrefix(Inventory __instance, Item item, int atPosition, out Operation? __state)
    {
        __state = null;
        var probe = current;
        if (probe == null || !probe.Active || probe.subscriptions.Count == 0 || item == null) return;
        Operation? state = null;
        probe.Guard(() => state = probe.Begin("AddAt", item, probe.Resolve(__instance), targetInventory: __instance, targetIndex: atPosition));
        __state = state;
    }

    private static Exception? AddAtFinalizer(bool __result, Exception? __exception, Operation? __state)
    { FinishSafe(__state, __result, __exception); return __exception; }

    private static void PlugPrefix(Slot __instance, Item otherItem, out Operation? __state)
    {
        __state = null;
        var probe = current;
        if (probe == null || !probe.Active || probe.subscriptions.Count == 0 || otherItem == null) return;
        Operation? state = null;
        probe.Guard(() => state = probe.Begin("Plug", otherItem, probe.Resolve(__instance.Master), targetSlot: __instance));
        __state = state;
    }

    private static Exception? PlugFinalizer(bool __result, Exception? __exception, Operation? __state)
    { FinishSafe(__state, __result, __exception); return __exception; }

    private static void SplitPrefix(Item __instance, int count, out SplitRequest? __state)
    {
        __state = null;
        var probe = current;
        if (probe == null || !probe.Active || probe.subscriptions.Count == 0) return;
        SplitRequest? state = null;
        probe.Guard(() => state = probe.BeginSplit(__instance, count));
        __state = state;
    }

    private static void SplitPostfix(ref UniTask<Item> __result, SplitRequest? __state)
    {
        var probe = current;
        if (probe == null || __state == null) return;
        var original = __result;
        var replacement = original;
        probe.Guard(() => replacement = probe.WrapSplit(original, __state));
        __result = replacement;
    }

    private static Exception? SplitFinalizer(Exception? __exception, SplitRequest? __state)
    {
        var probe = current;
        if (__exception != null && probe != null && __state != null)
            probe.Guard(() => probe.CompleteSplit(__state, null, __exception.GetType().Name, delayedObservation: false));
        return __exception;
    }

    private static void ViewOpenPostfix(LootView __instance)
    {
        var probe = current;
        if (probe != null && probe.Active) probe.Guard(() => probe.ViewOpened(__instance));
    }

    private static void ViewClosePrefix(LootView __instance)
    {
        var probe = current;
        if (probe != null && probe.Active) probe.Guard(() => probe.ViewClosed(__instance));
    }

    private static void WorldDropPrefix(Item item)
    {
        var probe = current;
        if (probe != null && probe.Active) probe.Guard(() => probe.ObserveWorldDrop(item));
    }

    private static void InspectedPostfix(Item __instance)
    {
        var probe = current;
        if (probe != null && probe.Active && probe.registeredItems != 0)
            probe.Guard(() => probe.Inspection(__instance));
    }

    private static void FinishSafe(Operation? state, bool? result, Exception? exception)
    {
        var probe = current;
        if (probe != null && state != null) probe.Guard(() => probe.Finish(state, result, exception));
    }
}
