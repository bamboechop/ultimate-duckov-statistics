using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class CraftingNativeScope
{
    public CraftingNativeScope(
        NativeCraftingAdapter owner,
        CraftingDeliveryCorrelation correlation,
        CraftingResourcePaymentProof resourcePaymentProof)
    {
        Owner = owner;
        Correlation = correlation;
        ResourcePaymentProof = resourcePaymentProof;
    }

    public NativeCraftingAdapter Owner { get; }
    public CraftingDeliveryCorrelation Correlation { get; }
    public CraftingResourcePaymentProof ResourcePaymentProof { get; }
}

internal static class CraftingHarmonyBridge
{
    [ThreadStatic] private static ReferenceScopeStack<CraftingNativeScope>? scopes;
    [ThreadStatic] private static ReferenceScopeStack<CraftingNativeScope>? paymentScopes;
    private static NativeCraftingAdapter? adapter;

    public static void Attach(NativeCraftingAdapter value) =>
        adapter = value ?? throw new ArgumentNullException(nameof(value));

    public static void Detach(NativeCraftingAdapter value)
    {
        if (ReferenceEquals(adapter, value)) adapter = null;
    }

    public static CraftingNativeScope? Begin(CraftingFormula formula)
    {
        var current = adapter;
        try
        {
            var scope = current?.BeginNativeCraft(formula);
            if (scope != null) (scopes ??= new ReferenceScopeStack<CraftingNativeScope>()).Push(scope);
            return scope;
        }
        catch (Exception exception)
        {
            current?.FailNativeCraftBegin(exception);
            return null;
        }
    }

    public static UniTask<List<Item>> WrapCraft(
        CraftingNativeScope? scope,
        UniTask<List<Item>> source)
    {
        if (scope == null) return source;
        try { return scope.Owner.WrapNativeCraft(scope, source); }
        catch (Exception exception)
        {
            scope.Owner.FailNativeCraftWrapping(scope, exception);
            return source;
        }
    }

    public static UniTask WrapDelivery(
        bool directToBuffer,
        bool toPlayerInventory,
        int amountFactor,
        List<Item>? generatedItemsBuffer,
        UniTask source)
    {
        var scope = scopes?.Current;
        if (scope == null) return source;
        try
        {
            return scope.Owner.WrapNativeDelivery(
                scope,
                directToBuffer,
                toPlayerInventory,
                amountFactor,
                generatedItemsBuffer,
                source);
        }
        catch (Exception exception)
        {
            scope.Owner.FailNativeCraftWrapping(scope, exception);
            return source;
        }
    }

    public static void End(CraftingNativeScope? scope, Exception? exception)
    {
        scopes?.Pop(scope);
        if (scope == null || exception == null) return;
        scope.Owner.AbandonSynchronousNativeCraft(scope);
    }

    public static CraftingNativeScope? BeginPayment(Cost cost)
    {
        var scope = scopes?.Current;
        if (scope == null) return null;
        try
        {
            if (!NativeCraftingAdapter.BeginNativePayment(scope, cost)) return null;
            (paymentScopes ??= new ReferenceScopeStack<CraftingNativeScope>()).Push(scope);
            return scope;
        }
        catch (Exception exception)
        {
            scope.Owner.FailNativeCraftWrapping(scope, exception);
            return null;
        }
    }

    public static void ObserveItemCount(int itemTypeId, int count)
    {
        var scope = paymentScopes?.Current;
        if (scope == null) return;
        try { NativeCraftingAdapter.ObserveNativePaymentItemCount(scope, itemTypeId, count); }
        catch (Exception exception) { scope.Owner.FailNativeCraftWrapping(scope, exception); }
    }

    public static void CompletePayment(CraftingNativeScope? scope, bool result)
    {
        if (scope == null) return;
        try { scope.Owner.CompleteNativePayment(scope, result); }
        catch (Exception exception) { scope.Owner.FailNativeCraftWrapping(scope, exception); }
    }

    public static void EndPayment(CraftingNativeScope? scope, Exception? exception)
    {
        paymentScopes?.Pop(scope);
        if (scope == null || exception == null) return;
        NativeCraftingAdapter.AbandonNativePayment(scope);
    }
}

internal static class CraftingHarmonyCallbacks
{
    private static void CraftPrefix(CraftingFormula __0, out CraftingNativeScope? __state) =>
        __state = CraftingHarmonyBridge.Begin(__0);

    private static void CraftPostfix(CraftingNativeScope? __state, ref UniTask<List<Item>> __result) =>
        __result = CraftingHarmonyBridge.WrapCraft(__state, __result);

    private static Exception? CraftFinalizer(Exception? __exception, CraftingNativeScope? __state)
    {
        CraftingHarmonyBridge.End(__state, __exception);
        return __exception;
    }

    private static void ReturnPostfix(
        bool __0,
        bool __1,
        int __2,
        List<Item>? __3,
        ref UniTask __result) =>
        __result = CraftingHarmonyBridge.WrapDelivery(__0, __1, __2, __3, __result);

    private static void PayPrefix(Cost __0, out CraftingNativeScope? __state) =>
        __state = CraftingHarmonyBridge.BeginPayment(__0);

    private static void PayPostfix(CraftingNativeScope? __state, bool __result) =>
        CraftingHarmonyBridge.CompletePayment(__state, __result);

    private static Exception? PayFinalizer(Exception? __exception, CraftingNativeScope? __state)
    {
        CraftingHarmonyBridge.EndPayment(__state, __exception);
        return __exception;
    }

    private static void GetItemCountPostfix(int __0, int __result) =>
        CraftingHarmonyBridge.ObserveItemCount(__0, __result);

    public static MethodInfo CraftPrefixMethod => Get(nameof(CraftPrefix));
    public static MethodInfo CraftPostfixMethod => Get(nameof(CraftPostfix));
    public static MethodInfo CraftFinalizerMethod => Get(nameof(CraftFinalizer));
    public static MethodInfo ReturnPostfixMethod => Get(nameof(ReturnPostfix));
    public static MethodInfo PayPrefixMethod => Get(nameof(PayPrefix));
    public static MethodInfo PayPostfixMethod => Get(nameof(PayPostfix));
    public static MethodInfo PayFinalizerMethod => Get(nameof(PayFinalizer));
    public static MethodInfo GetItemCountPostfixMethod => Get(nameof(GetItemCountPostfix));

    private static MethodInfo Get(string name) => typeof(CraftingHarmonyCallbacks).GetMethod(
        name,
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(CraftingHarmonyCallbacks).FullName, name);
}
