using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;

// Uses the production inspector/patcher with real Harmony injection, not the
// game or the unit tests' Harmony boundary doubles. Never opens a saved profile.
var target = typeof(Fixture).GetMethod(nameof(Fixture.Hurt))!;
var prefix = typeof(Fixture).GetMethod(nameof(Fixture.Before))!;
var postfix = typeof(Fixture).GetMethod(nameof(Fixture.After))!;
var finalizer = typeof(Fixture).GetMethod(nameof(Fixture.Finally))!;
var foreign = new Harmony("probe.unfamiliar.mod");
// Surface runtime-loader failures completely; the production patcher intentionally
// returns only a concise compatibility diagnostic.
_ = Harmony.GetPatchInfo(target);
const string owner = "uds.observation.probe";
Check(ReflectiveHarmonyPatcher.TryCreate(owner, out var observer, out var detail,
    HarmonyForeignPatchPolicy.Observe) && observer != null, detail);
using (observer!)
{
    foreign.Patch(target, prefix: new HarmonyMethod(typeof(Fixture).GetMethod(nameof(Fixture.Modify))!),
        postfix: new HarmonyMethod(typeof(Fixture).GetMethod(nameof(Fixture.Notify))!),
        transpiler: new HarmonyMethod(typeof(Fixture).GetMethod(nameof(Fixture.PassThrough))!));
    Check(observer!.IsPatchSetTrusted(target, Array.Empty<HarmonyPatchExpectation>(), out detail), detail);
    observer.Patch(target, prefix, postfix, finalizer);
    HarmonyPatchExpectation[] expected = [new("Prefixes", prefix), new("Postfixes", postfix), new("Finalizers", finalizer)];
    Check(observer.TryCaptureValidatedPatchSetStamp(target, expected, out var stamp, out detail), detail);
    Fixture.Reset(); Fixture.Hurt(10);
    Check(Fixture.Health == 93 && Fixture.Recorded == 7 && Fixture.Observations == 1 && Fixture.Notifications == 1,
        "Changed arguments must measure actual seven HP loss once.");
    var inspections = NativeHotPathDiagnostics.Inspections;
    for (var i = 0; i < 1000; i++) Check(observer.IsPatchSetStampCurrent(stamp, out detail), detail);
    Check(NativeHotPathDiagnostics.Inspections == inspections, "Stable stamps must not re-enumerate metadata.");
    foreign.Patch(target, finalizer: new HarmonyMethod(typeof(Fixture).GetMethod(nameof(Fixture.ForeignFinally))!));
    Check(observer.IsPatchSetStampCurrent(stamp, out detail), detail);
    Fixture.Reset(); Fixture.Hurt(10);
    Check(Fixture.Recorded == 7 && Fixture.Observations == 1, "Runtime patch addition lost or doubled recording.");
    foreign.Patch(target, prefix: new HarmonyMethod(typeof(Fixture).GetMethod(nameof(Fixture.Skip))!) { priority = 800 });
    Fixture.Reset(); Fixture.Hurt(10);
    Check(Fixture.Health == 100 && Fixture.Recorded == 0 && Fixture.Observations == 0,
        "Skipped original without damage must not produce a damage event.");
    foreign.Unpatch(target, HarmonyPatchType.Prefix, "probe.unfamiliar.mod");
    Fixture.Reset(); Fixture.Throw = true;
    try { Fixture.Hurt(10); throw new InvalidOperationException("Native exception was swallowed."); }
    catch (FixtureException) { }
    Check(Fixture.Finalized && Fixture.Recorded == 0, "Exception cleanup invented an event or failed to finalize.");
    observer.IsPatchSetStampCurrent(stamp, out _);
    new Harmony(owner).Unpatch(target, HarmonyPatchType.Postfix, owner);
    Check(!observer.IsPatchSetStampCurrent(stamp, out detail) && detail.Contains("missing", StringComparison.Ordinal),
        "Missing observer callback must invalidate recording.");
    observer.Patch(target, postfix: postfix);
    Check(observer.IsPatchSetStampCurrent(stamp, out detail), "Restoring the required callback must allow stamp recovery: " + detail);
}
foreign.UnpatchAll("probe.unfamiliar.mod");
Console.WriteLine("PASS: real Harmony injection; unfamiliar prefix/postfix/transpiler/finalizer; actual deltas; runtime revalidation; skipped original; exception cleanup; callback removal/restoration; stable fast path.");

static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

public sealed class FixtureException : Exception;
public static class Fixture
{
    public static int Health, Recorded, Observations, Notifications;
    public static bool Throw, Finalized;
    public static void Reset() { Health = 100; Recorded = Observations = Notifications = 0; Throw = Finalized = false; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Hurt(int amount) { if (Throw) throw new FixtureException(); Health -= amount; }
    public static void Before(out int? __state) => __state = Health;
    public static void After(int? __state)
    {
        if (__state is int before && before > Health) { Recorded += before - Health; Observations++; }
    }
    public static Exception? Finally(Exception? __exception) { Finalized = true; return __exception; }
    public static void Modify(ref int amount) => amount = 7;
    public static void Notify() => Notifications++;
    public static bool Skip() => false;
    public static Exception? ForeignFinally(Exception? __exception) => __exception;
    public static IEnumerable<CodeInstruction> PassThrough(IEnumerable<CodeInstruction> instructions) => instructions;
}

namespace UltimateDuckovStatistics.Adapters
{
    internal static class NativeHotPathDiagnostics
    {
        internal static int Inspections;
        internal static void CountHarmonyPatchSetInspection() => Inspections++;
    }
}
