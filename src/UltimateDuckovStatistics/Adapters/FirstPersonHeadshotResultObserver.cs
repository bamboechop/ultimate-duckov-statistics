using System.Reflection;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Adapters;

// Observe FPC's explicit per-call result, not a binary identity or generic crit.
// The small state schema is the integration contract; FPC owns head-hit physics.
internal sealed class FirstPersonHeadshotResultObserver : IDisposable
{
    private static FirstPersonHeadshotResultObserver? active;
    private readonly ReflectiveHarmonyPatcher patcher;
    private readonly FieldInfo tracked, headHit, attacker, target;
    private HarmonyPatchSetStamp? stamp;
    private object? pendingHealth, pendingDamage, pendingState;

    internal FirstPersonHeadshotResultObserver(MethodInfo prefix)
    {
        var parameters = prefix.GetParameters();
        if (!prefix.IsStatic || prefix.ReturnType != typeof(void) || parameters.Length != 3
            || !parameters[2].IsOut || !parameters[2].ParameterType.IsByRef)
            throw new NotSupportedException("FPC headshot result signature changed.");
        var state = parameters[2].ParameterType.GetElementType()!;
        tracked = Field(state, "tracked", typeof(bool));
        headHit = Field(state, "actualHeadHit", typeof(bool));
        attacker = Field(state, "attacker", parameters[0].ParameterType.Assembly.GetType("CharacterMainControl")
            ?? throw new NotSupportedException("FPC character type unavailable."));
        target = Field(state, "target", attacker.FieldType);
        if (!ReflectiveHarmonyPatcher.TryCreate("at.bamboechop.uds.fpc-headshot-result", out var created,
                out var detail, HarmonyForeignPatchPolicy.Observe) || created == null)
            throw new NotSupportedException(detail);
        patcher = created;
        try
        {
            if (active != null) throw new InvalidOperationException("FPC observer already active.");
            active = this;
            var before = Callback(nameof(Before));
            var after = Callback(nameof(After));
            var finallyMethod = Callback(nameof(Finally));
            patcher.Patch(prefix, before, after, finallyMethod);
            HarmonyPatchExpectation[] expected = { new("Prefixes", before), new("Postfixes", after), new("Finalizers", finallyMethod) };
            if (!patcher.TryCaptureValidatedPatchSetStamp(prefix, expected, out stamp, out detail))
                throw new NotSupportedException(detail);
        }
        catch { Dispose(); throw; }
    }

    private static FieldInfo Field(Type owner, string name, Type type)
    {
        var field = owner.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.FieldType == type ? field : throw new NotSupportedException("FPC result field changed: " + name);
    }

    private static MethodInfo Callback(string name) => typeof(FirstPersonHeadshotResultObserver)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Before() => active?.Clear();
    private static void After(object __0, object __1, object __2, bool __runOriginal)
    {
        var observer = active;
        if (observer == null || !__runOriginal) return;
        observer.pendingHealth = __0;
        observer.pendingDamage = __1;
        observer.pendingState = __2;
    }
    private static Exception? Finally(Exception? __exception)
    {
        if (__exception != null) active?.Clear();
        return __exception;
    }

    internal bool TryConsume(object health, object damage, object source, object victim, out bool result)
    {
        result = false;
        var state = pendingState;
        try
        {
            if (!patcher.IsPatchSetStampCurrent(stamp, out _) || state == null
                || !ReferenceEquals(pendingHealth, health) || !Equals(pendingDamage, damage)) return false;
            result = (bool)tracked.GetValue(state)! && (bool)headHit.GetValue(state)!
                && ReferenceEquals(attacker.GetValue(state), source) && ReferenceEquals(target.GetValue(state), victim);
            return true;
        }
        finally { Clear(); }
    }

    private void Clear() { pendingHealth = pendingDamage = pendingState = null; }
    internal static void CompleteHealth(object health)
    {
        if (active != null && ReferenceEquals(active.pendingHealth, health)) active.Clear();
    }
    public void Dispose()
    {
        if (ReferenceEquals(active, this)) active = null;
        Clear();
        patcher.Dispose();
    }
}
