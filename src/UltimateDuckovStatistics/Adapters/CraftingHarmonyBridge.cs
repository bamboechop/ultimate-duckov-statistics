using System.Reflection;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Adapters;

internal static class CraftingHarmonyBridge
{
    private static NativeCraftingAdapter? adapter;

    public static void Attach(NativeCraftingAdapter value) =>
        adapter = value ?? throw new ArgumentNullException(nameof(value));

    public static void Detach(NativeCraftingAdapter value)
    {
        if (ReferenceEquals(adapter, value)) adapter = null;
    }

    public static UniTask<List<Item>> Wrap(CraftingFormula formula, UniTask<List<Item>> source)
    {
        try { return adapter?.WrapNativeCraft(formula, source) ?? source; }
        catch { return source; }
    }
}

internal static class CraftingHarmonyCallbacks
{
    private static void CraftPostfix(CraftingFormula __0, ref UniTask<List<Item>> __result) =>
        __result = CraftingHarmonyBridge.Wrap(__0, __result);

    public static MethodInfo CraftPostfixMethod => typeof(CraftingHarmonyCallbacks).GetMethod(
        nameof(CraftPostfix),
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(CraftingHarmonyCallbacks).FullName, nameof(CraftPostfix));
}
