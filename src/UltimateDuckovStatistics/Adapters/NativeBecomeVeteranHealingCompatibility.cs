using System.Collections;
using System.Reflection;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

// Optional bridge for the inspected Become Veteran 1.0.2 build. Nothing from the
// mod is called by discovery, and no mod assembly is shipped or referenced.
internal sealed class NativeBecomeVeteranHealingCompatibility
{
    private static NativeBecomeVeteranHealingCompatibility? active;
    private readonly HealingAttributionTracker tracker;
    private readonly Func<bool> isTrusted;
    private readonly Action<string> failureHandler;
    private readonly IList queue;
    private readonly FieldInfo healthTarget;
    private readonly List<object> boundEntries = new();
    private int tickDepth;
    private int epoch;

    private NativeBecomeVeteranHealingCompatibility(Type medicType, Type behaviourType,
        HealingAttributionTracker tracker, Func<bool> isTrusted, Action<string> failureHandler)
    {
        this.tracker = tracker;
        this.isTrusted = isTrusted;
        this.failureHandler = failureHandler;
        var entryType = medicType.GetNestedType("ActiveHoT", BindingFlags.NonPublic)
                        ?? throw new MissingMemberException("MedicSystem.ActiveHoT");
        healthTarget = entryType.GetField("healthTarget", BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new MissingFieldException("ActiveHoT.healthTarget");
        var queueField = medicType.GetField("_activeHoTs", BindingFlags.NonPublic | BindingFlags.Static);
        if (healthTarget.FieldType != typeof(object) || queueField == null || !queueField.IsInitOnly
            || queueField.FieldType != typeof(List<>).MakeGenericType(entryType)
            || queueField.GetValue(null) is not IList actualQueue)
            throw new MissingFieldException("MedicSystem._activeHoTs contract changed.");
        queue = actualQueue;
        Patches = new[]
        {
            Patch(medicType, "RegisterHoT", typeof(void), new[] { typeof(object), typeof(float) },
                nameof(RegisterPrefix), nameof(RegisterPostfix)),
            Patch(medicType, "TickAll", typeof(void), Type.EmptyTypes, nameof(TickPrefix), finalizer: nameof(TickFinalizer)),
            Patch(medicType, "ApplyHeal", typeof(void), new[] { typeof(object), typeof(float) },
                nameof(ApplyPrefix), finalizer: nameof(ApplyFinalizer)),
            Patch(medicType, "ClearHoTs", typeof(void), Type.EmptyTypes, postfix: nameof(ClearPostfix)),
            Patch(behaviourType, "TryApplyHeal", typeof(bool), new[] { typeof(Health), typeof(float) },
                nameof(NaturalPrefix), finalizer: nameof(ApplyFinalizer))
        };
    }

    public IReadOnlyList<PatchContract> Patches { get; }

    public static bool TryDiscover(HealingAttributionTracker tracker, Func<bool> isTrusted,
        Action<string> failureHandler, out NativeBecomeVeteranHealingCompatibility? compatibility, out string detail)
    {
        compatibility = null;
        var candidates = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "BecomeVeteran").ToArray();
        if (candidates.Length == 0)
        {
            detail = "Become Veteran is not installed; native healing attribution is active.";
            return true;
        }

        if (candidates.Length != 1 || !KnownModCompatibility.IsVerifiedAssembly(candidates[0], "BecomeVeteran"))
        {
            detail = "Become Veteran is loaded but its healing implementation is not a verified compatible build.";
            return false;
        }

        try
        {
            compatibility = CreateForContract(candidates[0].GetType("BecomeVeteran.MedicSystem", true)!,
                candidates[0].GetType("BecomeVeteran.ModBehaviour", true)!, tracker, isTrusted, failureHandler);
            detail = "Become Veteran immediate and queued healing attribution is active.";
            return true;
        }
        catch (Exception exception)
        {
            detail = "Become Veteran healing contract changed: " + exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    // Contract construction is also exercised against a queue fixture; production
    // discovery always validates the installed assembly before reaching this seam.
    internal static NativeBecomeVeteranHealingCompatibility CreateForContract(Type medicType, Type behaviourType,
        HealingAttributionTracker tracker, Func<bool> isTrusted, Action<string> failureHandler) =>
        new(medicType, behaviourType, tracker, isTrusted, failureHandler);

    public void Attach() => active = this;

    public void Detach()
    {
        if (ReferenceEquals(active, this)) active = null;
        Reset();
    }

    public void Reset()
    {
        epoch++;
        tickDepth = 0;
        foreach (var entry in boundEntries) tracker.RemoveDeferredSource(entry);
        boundEntries.Clear();
    }

    internal RegistrationObservation? BeginRegistration(object health)
    {
        if (!isTrusted() || health is not Health target || !target.IsMainCharacterHealth) return null;
        return new RegistrationObservation(epoch, health, FindEntry(health), HealingHarmonyBridge.CurrentCorrelationId);
    }

    internal void CompleteRegistration(RegistrationObservation? state)
    {
        if (state == null || state.Epoch != epoch || !isTrusted()) return;
        var entry = FindEntry(state.Health);
        if (entry != null && !ReferenceEquals(entry, state.Before))
        {
            // Bind the replacement before pruning its predecessor: both can refer
            // to the same completed medicine source (for example a native buff).
            if (tracker.BindDeferredSource(entry, state.CorrelationId)) boundEntries.Add(entry);
        }
        ReconcileQueue();
    }

    internal bool BeginTick()
    {
        if (boundEntries.Count == 0) return false;
        if (queue.Count == 0) { ReconcileQueue(); return false; }
        if (!isTrusted()) return false;
        tickDepth++;
        return true;
    }

    internal void EndTick()
    {
        if (tickDepth > 0) tickDepth--;
        ReconcileQueue();
    }

    internal string? BeginQueuedApplication(object health)
    {
        string? correlation = null;
        if (tickDepth > 0 && health is Health { IsMainCharacterHealth: true } && isTrusted())
        {
            var entry = FindEntry(health);
            if (entry != null) correlation = tracker.TryGetDeferredCorrelation(entry);
        }
        // An unobserved/replaced/pre-transition entry masks any outer item scope.
        // It must never borrow provenance from an unrelated medicine.
        return correlation == null && HealingHarmonyBridge.CurrentCorrelationId == null
            ? null : HealingHarmonyBridge.PushCompatibilityApplication(correlation);
    }

    internal void ReconcileQueue()
    {
        for (var index = boundEntries.Count - 1; index >= 0; index--)
        {
            var entry = boundEntries[index];
            var present = false;
            foreach (var candidate in queue)
                if (ReferenceEquals(candidate, entry)) { present = true; break; }
            if (present && tracker.TryGetDeferredCorrelation(entry) != null) continue;
            boundEntries.RemoveAt(index);
            tracker.RemoveDeferredSource(entry);
        }
    }

    private object? FindEntry(object health)
    {
        object? found = null;
        foreach (var entry in queue)
        {
            if (entry == null || !ReferenceEquals(healthTarget.GetValue(entry), health)) continue;
            if (found != null) throw new InvalidOperationException("Multiple delayed healing entries share a target.");
            found = entry;
        }
        return found;
    }

    private void Fail(Exception exception) => failureHandler(
        "Become Veteran delayed healing observation failed: " + exception.GetType().Name + ": " + exception.Message);

    private static void RegisterPrefix(object health, out RegistrationObservation? __state)
    {
        __state = null;
        var instance = active;
        try { __state = instance?.BeginRegistration(health); }
        catch (Exception exception) { instance?.Fail(exception); }
    }

    private static void RegisterPostfix(RegistrationObservation? __state)
    {
        var instance = active;
        try { instance?.CompleteRegistration(__state); }
        catch (Exception exception) { instance?.Fail(exception); }
    }

    private static void TickPrefix(out NativeBecomeVeteranHealingCompatibility? __state)
    {
        __state = null;
        var instance = active;
        try { if (instance?.BeginTick() == true) __state = instance; }
        catch (Exception exception) { instance?.Fail(exception); }
    }

    private static Exception? TickFinalizer(Exception? __exception, NativeBecomeVeteranHealingCompatibility? __state)
    {
        try { __state?.EndTick(); }
        catch (Exception exception) { __state?.Fail(exception); }
        return __exception;
    }

    private static void ApplyPrefix(object health, out string? __state)
    {
        __state = null;
        var instance = active;
        try { __state = instance?.BeginQueuedApplication(health); }
        catch (Exception exception) { instance?.Fail(exception); }
    }

    private static void NaturalPrefix(out string? __state)
    {
        __state = active == null || HealingHarmonyBridge.CurrentCorrelationId == null
            ? null : HealingHarmonyBridge.PushCompatibilityApplication(null);
    }

    private static Exception? ApplyFinalizer(Exception? __exception, string? __state)
    {
        HealingHarmonyBridge.Pop(__state);
        return __exception;
    }

    private static void ClearPostfix()
    {
        var instance = active;
        try { instance?.ReconcileQueue(); }
        catch (Exception exception) { instance?.Fail(exception); }
    }

    private static PatchContract Patch(Type declaringType, string methodName, Type returnType, Type[] parameters,
        string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        var method = declaringType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, parameters, null);
        if (method == null || method.ReturnType != returnType || method.IsGenericMethod)
            throw new MissingMethodException(declaringType.FullName, methodName);
        return new PatchContract(method, Callback(prefix), Callback(postfix), Callback(finalizer));
    }

    private static MethodInfo? Callback(string? name) => name == null ? null :
        typeof(NativeBecomeVeteranHealingCompatibility).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(name);

    internal sealed class PatchContract
    {
        public PatchContract(MethodInfo original, MethodInfo? prefix, MethodInfo? postfix, MethodInfo? finalizer)
        {
            Original = original; Prefix = prefix; Postfix = postfix; Finalizer = finalizer;
            var expectations = new List<HarmonyPatchExpectation>();
            if (prefix != null) expectations.Add(new("Prefixes", prefix));
            if (postfix != null) expectations.Add(new("Postfixes", postfix));
            if (finalizer != null) expectations.Add(new("Finalizers", finalizer));
            Expectations = expectations.ToArray();
        }
        public MethodInfo Original { get; }
        public MethodInfo? Prefix { get; }
        public MethodInfo? Postfix { get; }
        public MethodInfo? Finalizer { get; }
        public HarmonyPatchExpectation[] Expectations { get; }
    }

    internal sealed class RegistrationObservation
    {
        public RegistrationObservation(int epoch, object health, object? before, string? correlationId)
        { Epoch = epoch; Health = health; Before = before; CorrelationId = correlationId; }
        public int Epoch { get; }
        public object Health { get; }
        public object? Before { get; }
        public string? CorrelationId { get; }
    }
}
