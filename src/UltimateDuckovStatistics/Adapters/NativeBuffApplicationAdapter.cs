using System.Reflection;
using Duckov.Buffs;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Adapters;

// Combat buff provenance must survive an unrelated healing-source incompatibility.
internal sealed class NativeBuffApplicationAdapter : IDisposable
{
    private const string Owner = "at.bamboechop.ultimate-duckov-statistics.buffs";
    private static NativeBuffApplicationAdapter? current;
    private readonly NativeBuffApplicationObservationBoundary boundary;
    private readonly Action<string> log;
    private readonly RetryableHarmonyPatcherLease lease = new();
    private HarmonyPatchSetStamp? stamp;
    private bool active;
    private bool disposed;
    private bool retry;
    private DateTime nextAttempt;

    internal NativeBuffApplicationAdapter(NativeBuffApplicationObservationBoundary boundary, Action<string> log)
    {
        this.boundary = boundary;
        this.log = log;
    }

    internal bool Initialize()
    {
        if (disposed) return false;
        if (active) return IsTrusted();
        boundary.MarkUntrusted();
        if (!lease.TryCleanup(out _)) { retry = true; nextAttempt = DateTime.UtcNow.AddSeconds(1); return false; }
        if (!ReflectiveHarmonyPatcher.TryCreate(Owner, out var patcher, out var detail) || patcher == null)
        {
            retry = !ReflectiveHarmonyPatcher.IsHarmonyLoaded || ReflectiveHarmonyPatcher.HasPendingCleanup;
            nextAttempt = DateTime.UtcNow.AddSeconds(1);
            Report("Shared buff observation unavailable: " + detail);
            return false;
        }
        lease.Attach(patcher);
        try
        {
            if (current != null && !ReferenceEquals(current, this)) throw new InvalidOperationException("Shared buff observer already attached.");
            var target = typeof(CharacterBuffManager).GetMethod("AddBuff", BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(Buff), typeof(CharacterMainControl), typeof(int) }, null);
            if (target == null || target.ReturnType != typeof(void)) throw new MissingMethodException("CharacterBuffManager.AddBuff");
            if (!patcher.IsPatchSetTrusted(target, Array.Empty<HarmonyPatchExpectation>(), out detail)) throw new InvalidOperationException(detail);
            current = this;
            patcher.Patch(target, BuffPrefixMethod, BuffPostfixMethod);
            if (!patcher.TryCaptureValidatedPatchSetStamp(target,
                new[] { new HarmonyPatchExpectation("Prefixes", BuffPrefixMethod), new HarmonyPatchExpectation("Postfixes", BuffPostfixMethod) },
                out stamp, out detail)) throw new InvalidOperationException(detail);
            active = true;
            retry = false;
            boundary.SetTrustValidator(IsTrusted);
            boundary.MarkTrusted();
            Report("Shared buff observation active independently of healing attribution.");
            return true;
        }
        catch (Exception exception)
        {
            Invalidate(exception.GetBaseException().Message);
            lease.TryCleanup(out _);
            return false;
        }
    }

    internal void Tick()
    {
        if (disposed) return;
        if (active) { IsTrusted(); return; }
        if (DateTime.UtcNow < nextAttempt) return;
        nextAttempt = DateTime.UtcNow.AddSeconds(1);
        lease.TryCleanup(out _);
        if (retry) Initialize();
    }

    private bool IsTrusted()
    {
        if (!active || disposed) return false;
        if (lease.Value?.IsPatchSetStampCurrent(stamp, out _) == true) return true;
        Invalidate("The shared buff patch set changed after validation.");
        return false;
    }

    private void Invalidate(string detail)
    {
        active = false;
        retry = false;
        stamp = null;
        boundary.MarkUntrusted();
        boundary.SetTrustValidator(null);
        if (ReferenceEquals(current, this)) current = null;
        Report("Shared buff observation unavailable: " + detail);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            active = false;
            boundary.MarkUntrusted();
            boundary.SetTrustValidator(null);
            if (ReferenceEquals(current, this)) current = null;
        }
        // Failed cleanup stays registered in ReflectiveHarmonyPatcher's existing
        // retry owner; detached callbacks cannot observe subsequent profiles.
        if (!lease.TryCleanup(out var detail)) Report("Shared buff cleanup pending: " + detail);
    }

    private void Report(string value) { try { log(value); } catch { } }

    private static void BuffPrefix(CharacterBuffManager __instance, Buff buffPrefab, out bool __state)
    {
        __state = false;
        try
        {
            if (current?.IsTrusted() == true && __instance != null && buffPrefab != null)
                __state = !__instance.Buffs.Any(value => value != null && value.ID == buffPrefab.ID);
        }
        catch { /* An unreadable pre-call list cannot prove a new buff instance. */ }
    }

    private static void BuffPostfix(CharacterBuffManager __instance, Buff buffPrefab,
        CharacterMainControl? fromWho, int overrideWeaponID, bool __state)
    {
        if (current?.IsTrusted() != true) return;
        // Keep sibling observers independent even if one encounters bad data.
        try { CombatHarmonyBridge.CaptureBuffApplication(__instance, buffPrefab, fromWho, overrideWeaponID, __state); }
        catch { }
        try { HealingHarmonyBridge.BindBuff(__instance, buffPrefab, fromWho, overrideWeaponID, __state); }
        catch { }
    }

    internal static MethodInfo BuffPrefixMethod => typeof(NativeBuffApplicationAdapter).GetMethod(nameof(BuffPrefix), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static MethodInfo BuffPostfixMethod => typeof(NativeBuffApplicationAdapter).GetMethod(nameof(BuffPostfix), BindingFlags.Static | BindingFlags.NonPublic)!;
}
