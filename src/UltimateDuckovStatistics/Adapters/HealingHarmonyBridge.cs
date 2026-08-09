using System.Reflection;
using Duckov.Buffs;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal static class HealingHarmonyBridge
{
    [ThreadStatic]
    private static List<AttributionScope>? scopes;

    private static NativeHealingAttributionAdapter? adapter;

    public static void Attach(NativeHealingAttributionAdapter value)
    {
        adapter = value ?? throw new ArgumentNullException(nameof(value));
        scopes?.Clear();
    }

    public static void Detach(NativeHealingAttributionAdapter value)
    {
        if (ReferenceEquals(adapter, value))
        {
            adapter = null;
            scopes?.Clear();
        }
    }

    public static string? CurrentCorrelationId => scopes is { Count: > 0 }
        ? scopes[^1].CorrelationId
        : null;

    public static string? PushItemApplication(int runtimeItemId)
    {
        var correlationId = adapter?.TryGetUseCorrelation(runtimeItemId);
        return Push(correlationId);
    }

    public static string? PushEffect(EffectAction effectAction)
    {
        var correlationId = adapter?.ResolveEffectCorrelation(effectAction);
        return Push(correlationId);
    }

    public static void Pop(string? scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeId) || scopes == null || scopes.Count == 0)
        {
            return;
        }

        var index = scopes.FindLastIndex(scope => string.Equals(scope.ScopeId, scopeId, StringComparison.Ordinal));
        if (index >= 0)
        {
            scopes.RemoveRange(index, scopes.Count - index);
        }
    }

    public static void ClearScopes() => scopes?.Clear();

    public static HealingHealthPatchState BeginHealthApplication(Health health, float requestedHealth)
    {
        var correlationId = CurrentCorrelationId;
        if (adapter == null || string.IsNullOrWhiteSpace(correlationId))
        {
            return HealingHealthPatchState.Empty;
        }

        try
        {
            var amount = HealingAttributionTracker.CalculateActualRestoration(
                health.CurrentHealth,
                health.MaxHealth,
                requestedHealth);
            if (amount <= 0 || !health.IsMainCharacterHealth)
            {
                return HealingHealthPatchState.Empty;
            }

            return new HealingHealthPatchState(
                correlationId,
                Guid.NewGuid().ToString("N"),
                amount,
                isMainPlayerTarget: true);
        }
        catch
        {
            return HealingHealthPatchState.Empty;
        }
    }

    public static void CompleteHealthApplication(HealingHealthPatchState? state)
    {
        if (state == null || !state.ShouldRecord)
        {
            return;
        }

        adapter?.RecordHealthApplication(state);
    }

    public static void BindBuff(CharacterBuffManager manager, Buff buffPrefab)
    {
        var correlationId = CurrentCorrelationId;
        if (adapter == null || string.IsNullOrWhiteSpace(correlationId))
        {
            return;
        }

        adapter.BindAppliedBuff(manager, buffPrefab, correlationId);
    }

    private static string? Push(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return null;
        }

        scopes ??= new List<AttributionScope>();
        var scopeId = Guid.NewGuid().ToString("N");
        scopes.Add(new AttributionScope(scopeId, correlationId));
        return scopeId;
    }

    private sealed class AttributionScope
    {
        public AttributionScope(string scopeId, string correlationId)
        {
            ScopeId = scopeId;
            CorrelationId = correlationId;
        }

        public string ScopeId { get; }

        public string CorrelationId { get; }
    }
}

internal sealed class HealingHealthPatchState
{
    public static readonly HealingHealthPatchState Empty = new(null, null, 0, false);

    public HealingHealthPatchState(
        string? correlationId,
        string? applicationId,
        double actualHealthRestored,
        bool isMainPlayerTarget)
    {
        CorrelationId = correlationId;
        ApplicationId = applicationId;
        ActualHealthRestored = actualHealthRestored;
        IsMainPlayerTarget = isMainPlayerTarget;
    }

    public string? CorrelationId { get; }

    public string? ApplicationId { get; }

    public double ActualHealthRestored { get; }

    public bool IsMainPlayerTarget { get; }

    public bool ShouldRecord => !string.IsNullOrWhiteSpace(CorrelationId)
                                && !string.IsNullOrWhiteSpace(ApplicationId)
                                && ActualHealthRestored > 0
                                && IsMainPlayerTarget;
}

internal static class HealingHarmonyCallbacks
{
    private static void HealthPrefix(Health __instance, float healthValue, out HealingHealthPatchState __state)
    {
        __state = HealingHarmonyBridge.BeginHealthApplication(__instance, healthValue);
    }

    private static void HealthPostfix(HealingHealthPatchState __state)
    {
        HealingHarmonyBridge.CompleteHealthApplication(__state);
    }

    private static void EffectPrefix(EffectAction __instance, out string? __state)
    {
        __state = HealingHarmonyBridge.PushEffect(__instance);
    }

    private static Exception? EffectFinalizer(Exception? __exception, string? __state)
    {
        HealingHarmonyBridge.Pop(__state);
        return __exception;
    }

    private static void BuffPostfix(CharacterBuffManager __instance, Buff buffPrefab)
    {
        HealingHarmonyBridge.BindBuff(__instance, buffPrefab);
    }

    public static MethodInfo HealthPrefixMethod => Get(nameof(HealthPrefix));

    public static MethodInfo HealthPostfixMethod => Get(nameof(HealthPostfix));

    public static MethodInfo EffectPrefixMethod => Get(nameof(EffectPrefix));

    public static MethodInfo EffectFinalizerMethod => Get(nameof(EffectFinalizer));

    public static MethodInfo BuffPostfixMethod => Get(nameof(BuffPostfix));

    private static MethodInfo Get(string name) => typeof(HealingHarmonyCallbacks).GetMethod(
        name,
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(HealingHarmonyCallbacks).FullName, name);
}
