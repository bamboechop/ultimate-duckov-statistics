using System.Reflection;
using Duckov.Buffs;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal enum HealingPatchPoint
{
    Health,
    Effect,
    Buff
}

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
        var currentAdapter = adapter;
        if (currentAdapter == null || !currentAdapter.IsPatchPointTrusted(HealingPatchPoint.Effect))
        {
            return null;
        }

        var correlationId = currentAdapter.ResolveEffectCorrelation(effectAction);
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
        var currentAdapter = adapter;
        if (currentAdapter == null
            || string.IsNullOrWhiteSpace(correlationId)
            || !currentAdapter.IsPatchPointTrusted(HealingPatchPoint.Health))
        {
            return HealingHealthPatchState.Empty;
        }

        try
        {
            var healthBeforeCall = health.CurrentHealth;
            var maximumHealthBeforeCall = health.MaxHealth;
            if (HealingAttributionTracker.CalculateActualRestoration(
                    healthBeforeCall,
                    maximumHealthBeforeCall,
                    requestedHealth) <= 0
                || !health.IsMainCharacterHealth)
            {
                return HealingHealthPatchState.Empty;
            }

            return new HealingHealthPatchState(
                correlationId,
                Guid.NewGuid().ToString("N"),
                healthBeforeCall,
                maximumHealthBeforeCall,
                isMainPlayerTarget: true);
        }
        catch
        {
            return HealingHealthPatchState.Empty;
        }
    }

    public static void CompleteHealthApplication(Health health, HealingHealthPatchState? state)
    {
        var currentAdapter = adapter;
        if (state == null
            || !state.ShouldMeasure
            || currentAdapter == null
            || !currentAdapter.IsPatchPointTrusted(HealingPatchPoint.Health))
        {
            return;
        }

        try
        {
            var amount = HealingAttributionTracker.CalculateAppliedRestoration(
                state.HealthBeforeCall,
                health.CurrentHealth,
                state.MaximumHealthBeforeCall);
            if (amount > 0)
            {
                currentAdapter.RecordHealthApplication(state, amount);
            }
        }
        catch
        {
            // A health contract failure must never invent attribution.
        }
    }

    public static void BindBuff(CharacterBuffManager manager, Buff buffPrefab)
    {
        var currentAdapter = adapter;
        if (currentAdapter == null || !currentAdapter.IsPatchPointTrusted(HealingPatchPoint.Buff))
        {
            return;
        }

        currentAdapter.ReconcileAppliedBuff(manager, buffPrefab, CurrentCorrelationId);
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
    public static readonly HealingHealthPatchState Empty = new(null, null, 0, 0, false);

    public HealingHealthPatchState(
        string? correlationId,
        string? applicationId,
        double healthBeforeCall,
        double maximumHealthBeforeCall,
        bool isMainPlayerTarget)
    {
        CorrelationId = correlationId;
        ApplicationId = applicationId;
        HealthBeforeCall = healthBeforeCall;
        MaximumHealthBeforeCall = maximumHealthBeforeCall;
        IsMainPlayerTarget = isMainPlayerTarget;
    }

    public string? CorrelationId { get; }

    public string? ApplicationId { get; }

    public double HealthBeforeCall { get; }

    public double MaximumHealthBeforeCall { get; }

    public bool IsMainPlayerTarget { get; }

    public bool ShouldMeasure => !string.IsNullOrWhiteSpace(CorrelationId)
                                 && !string.IsNullOrWhiteSpace(ApplicationId)
                                 && MaximumHealthBeforeCall > HealthBeforeCall
                                 && IsMainPlayerTarget;
}

internal static class HealingHarmonyCallbacks
{
    private static void HealthPrefix(Health __instance, float healthValue, out HealingHealthPatchState __state)
    {
        __state = HealingHarmonyBridge.BeginHealthApplication(__instance, healthValue);
    }

    private static void HealthPostfix(Health __instance, HealingHealthPatchState __state)
    {
        HealingHarmonyBridge.CompleteHealthApplication(__instance, __state);
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
