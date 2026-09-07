using System.Globalization;
using System.Reflection;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeThrowableAdapter : IRetryableCleanup
{
    private const string Version = "native-throwables/2.3.30";
    private readonly Func<string> generation;
    private readonly Func<string?> run, map, segment;
    private readonly Action<ItemUseRecorded> publish;
    private readonly Action<CapabilityRecord> capability;
    private readonly Action<string> diagnostic;
    private readonly RetryableHarmonyPatcherLease lease = new();
    private bool active;
    private static NativeThrowableAdapter? owner;
    [ThreadStatic] private static ReleaseScope? current;
    private static readonly MethodInfo Release = typeof(SkillBase).GetMethod(nameof(SkillBase.ReleaseSkill), new[] { typeof(SkillReleaseContext), typeof(CharacterMainControl) })!;
    private static readonly MethodInfo GrenadeRelease = typeof(Skill_Grenade).GetMethod(nameof(Skill_Grenade.OnRelease), Type.EmptyTypes)!;
    private static MethodInfo Callback(string name) => typeof(NativeThrowableAdapter).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly HarmonyPatchExpectation[] ReleasePatches = {
        new("Prefixes", Callback(nameof(BeginRelease))), new("Finalizers", Callback(nameof(EndRelease))) };
    private static readonly HarmonyPatchExpectation[] GrenadePatches = { new("Postfixes", Callback(nameof(Released))) };

    public NativeThrowableAdapter(Func<string> generation, Func<string?> run, Func<string?> map, Func<string?> segment,
        Action<ItemUseRecorded> publish, Action<CapabilityRecord> capability, Action<string> diagnostic)
    { this.generation = generation; this.run = run; this.map = map; this.segment = segment;
        this.publish = publish; this.capability = capability; this.diagnostic = diagnostic; }

    public void Initialize()
    {
        if (active) return;
        try
        {
            if (Application.version != "2.3.30" || Release == null || GrenadeRelease == null)
                throw new InvalidOperationException("Throwable release contract does not match the verified baseline.");
            if (!ReflectiveHarmonyPatcher.TryCreate("at.bamboechop.ultimate-duckov-statistics.throwables", out var patcher, out var detail))
                throw new InvalidOperationException(detail);
            lease.Attach(patcher!);
            patcher!.Patch(Release, prefix: Callback(nameof(BeginRelease)), finalizer: Callback(nameof(EndRelease)));
            patcher.Patch(GrenadeRelease, postfix: Callback(nameof(Released)));
            if (!Trusted()) throw new InvalidOperationException("Throwable release patches are not trusted.");
            owner = this; active = true;
            SetCapability(AdapterCapabilityState.Supported, "Successful main-player Skill_Grenade releases in raids; recorded totals begin with this adapter. Earlier throws are unavailable.");
        }
        catch (Exception ex)
        {
            TryCleanup(); SetCapability(AdapterCapabilityState.DisabledIncompatible, ex.Message);
        }
    }

    private bool Trusted() => lease.Value != null
        && lease.Value.IsPatchSetTrusted(Release, ReleasePatches, out _)
        && lease.Value.IsPatchSetTrusted(GrenadeRelease, GrenadePatches, out _);
    private void SetCapability(AdapterCapabilityState state, string detail)
    { capability(new CapabilityRecord { AdapterId = ThrowableUseObservation.CapabilityId, State = state, Version = Version, Detail = detail }); diagnostic(detail); }
    private void Fail(Exception ex)
    {
        active = false;
        try { SetCapability(AdapterCapabilityState.DisabledIncompatible, "Throwable tracking stopped: " + ex.Message); }
        catch (Exception reportError) { Debug.LogException(reportError); }
    }
    public bool TryCleanup()
    {
        active = false;
        if (ReferenceEquals(owner, this)) { owner = null; current = null; }
        return lease.TryCleanup(out _);
    }

    private sealed class ReleaseScope
    {
        public NativeThrowableAdapter Owner = null!;
        public SkillBase Skill = null!;
        public Item Item = null!;
        public ThrowableUseObservation Observation = null!;
        public ReleaseScope? Parent;
    }
    private static void BeginRelease(SkillBase __instance, CharacterMainControl from, out ReleaseScope? __state)
    {
        __state = null; var adapter = owner;
        if (adapter?.active != true || __instance is not Skill_Grenade || from == null || !from.IsMainCharacter) return;
        try
        {
            if (!adapter.Trusted()) throw new InvalidOperationException("Throwable patch ownership changed.");
            var item = __instance.fromItem;
            if (item == null || item.TypeID <= 0) return; // No guessed identity for character-only skills.
            var typeId = item.TypeID;
            var snapshot = new ItemUseSnapshot
            {
                ItemId = "duckov:item:" + typeId.ToString(CultureInfo.InvariantCulture), DisplayName = item.DisplayName,
                SaveGenerationId = adapter.generation(), RunId = adapter.run(), MapId = adapter.map(), SegmentId = adapter.segment(),
                GameplayContext = NativeRaidContext.GetGameplayContext(), IntegrityTags = NativeIntegrityProbe.Read(),
                GameVersion = Application.version, GameBuild = "24013657", AdapterVersion = Version,
                Stackable = item.Stackable, StackCount = item.StackCount
            };
            var observation = ThrowableUseObservation.Begin(snapshot, true, true);
            if (observation == null) return;
            __state = new ReleaseScope { Owner = adapter, Skill = __instance, Item = item, Observation = observation, Parent = current };
            current = __state;
        }
        catch (Exception ex) { adapter.Fail(ex); }
    }
    private static void Released(Skill_Grenade __instance, bool __runOriginal)
    {
        if (__runOriginal && current is { } scope && ReferenceEquals(scope.Skill, __instance) && scope.Owner.active)
            scope.Observation.MarkReleased();
    }
    private static Exception? EndRelease(ReleaseScope? __state, Exception? __exception)
    {
        if (__state == null) return __exception;
        var adapter = __state.Owner;
        try
        {
            if (!ReferenceEquals(current, __state) || !adapter.active || !ReferenceEquals(owner, adapter)) return __exception;
            if (!adapter.Trusted()) throw new InvalidOperationException("Throwable patch ownership changed during release.");
            // Release evidence survives a later consumption/subscriber failure; no launch is invented on failure.
            var item = __state.Item;
            var value = __state.Observation.Complete(adapter.generation(), adapter.run(), adapter.segment(),
                item != null && item.Stackable ? item.StackCount : null, item == null, DateTime.UtcNow);
            if (value != null) adapter.publish(value);
            if (__exception != null) adapter.Fail(__exception);
        }
        catch (Exception ex) { adapter.Fail(ex); }
        finally { if (ReferenceEquals(current, __state)) current = __state.Parent; }
        return __exception; // Never swallow or replace a game/foreign exception.
    }
}
