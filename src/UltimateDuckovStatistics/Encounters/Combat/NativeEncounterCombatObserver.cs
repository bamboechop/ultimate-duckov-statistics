using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Duckov.Buffs;
using Duckov.Scenes;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UnityEngine;

namespace UltimateDuckovStatistics.Encounters;

/// <summary>Short-session diagnostic capture. Never changes native arguments/results.</summary>
internal sealed partial class NativeEncounterCombatObserver : IEncounterObserver
{
    public Core.Encounters.EncounterCaptureIssue? FailureIssue => enabled && reportedHookLoss == EncounterCombatHookLoss.None
        ? null : Core.Encounters.EncounterCaptureIssue.CombatIncomplete;
    internal const string OwnerId = "at.bamboechop.ultimate-duckov-statistics.encounters.combat";
    private const int MaximumOrigins = 2048;
    private static NativeEncounterCombatObserver? active;
    [ThreadStatic] private static HealthFrame? healthFrame;
    [ThreadStatic] private static AttackRuntime? attack;
    [ThreadStatic] private static Stack<AttackState>? observerAttackStates;
    private static readonly FieldInfo? RelatedScene = typeof(CharacterMainControl).GetField("relatedScene", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly IEncounterObservationSink sink;
    private readonly Action<string> log;
    private readonly int owningThread = Environment.CurrentManagedThreadId;
    private readonly RetryableHarmonyPatcherLease lease = new();
    private readonly Dictionary<int, ProjectileOrigin> origins = new();
    private readonly Queue<(int Id, long Origin)> originOrder = new();
    private ConditionalWeakTable<Health, Marker> relatedHealth = new();
    private ConditionalWeakTable<Health, Marker> confirmedDeaths = new();
    private string generation = string.Empty;
    private string run = string.Empty;
    private long epoch;
    private long sequence;
    private bool playerDeathRecorded;
    private bool subscribed;
    private bool disposed;
    private bool enabled;
    private EncounterCombatHookLoss reportedHookLoss;
    private MethodInfo? setterMethod;
    private HarmonyPatchExpectation[] setterExpectations = Array.Empty<HarmonyPatchExpectation>();
    private double nextInspection;

    public NativeEncounterCombatObserver(IEncounterObservationSink sink, Action<string> log)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        try
        {
            if (active != null) throw new InvalidOperationException("Another encounter combat probe is active.");
            if (!string.Equals(Application.version, "2.3.30", StringComparison.Ordinal))
                throw new NotSupportedException("Encounter combat probe requires inspected Duckov 2.3.30.");
            if (!ReflectiveHarmonyPatcher.TryCreate(OwnerId, out var patcher, out var detail) || patcher == null)
                throw new InvalidOperationException(detail);
            lease.Attach(patcher);
            // Shared methods are observed from existing combat-owner callbacks.
            // Adding another owner there would intentionally fail aggregate trust.
            _ = Exact(typeof(Health), "Hurt", typeof(bool), typeof(DamageInfo));
            setterMethod = typeof(Health).GetProperty(nameof(Health.CurrentHealth))?.SetMethod ?? throw new MissingMethodException("Health.CurrentHealth setter");
            if (!patcher.IsPatchSetTrusted(setterMethod, Array.Empty<HarmonyPatchExpectation>(), out detail))
                throw new InvalidOperationException("Untrusted health setter: " + detail);
            setterExpectations = [new("Prefixes", Callback(nameof(HealthValuePrefix))!), new("Postfixes", Callback(nameof(HealthValuePostfix))!)];
            Install(patcher, setterMethod, nameof(HealthValuePrefix), nameof(HealthValuePostfix), null);
            if (!patcher.IsPatchSetTrusted(setterMethod, setterExpectations, out detail))
                throw new InvalidOperationException("Health setter patch readback: " + detail);
            _ = Exact(typeof(Projectile), "Init", typeof(void), typeof(ProjectileContext));
            _ = Exact(typeof(Effect), "Trigger", typeof(void), typeof(EffectTriggerEventContext));
            Health.OnDead += OnDead;
            LevelManager.OnMainCharacterDead += OnPlayerDeath;
            subscribed = true;
            active = this;
            enabled = true;
            Status = "Active diagnostic hooks; source provenance and candidate death timing require qualification.";
        }
        catch (Exception exception)
        {
            Disable(exception);
            Cleanup();
        }
    }

    public string Status { get; private set; } = "Not initialized";

    internal static void ObserveHealthBegin(Health health, DamageInfo info) => HurtPrefix(health, info, out _);

    // The aggregate adapter classifies the accepted transition from the projectile's
    // launch-time head-target evidence and deduplicates it. Native crit is unrelated.
    // Its callback runs before ObserveHealthComplete, while this Hurt frame is owned.
    internal static void ObserveHeadshot(Health health)
    {
        var frame = FindFrame(health);
        if (frame?.Capture == true) frame.Headshot = true;
    }

    internal static void ObserveHealthComplete(Health health)
    {
        // The existing aggregate callback does not take __result. A completed
        // native call plus the observed dead transition is sufficient for the
        // candidate check; early native rejection leaves HP/isDead unchanged.
        var frame = FindFrame(health);
        if (frame != null) HurtPostfix(health, true, frame);
    }

    internal static void ObserveHealthFinally(Health health, Exception? exception) => HurtFinalizer(exception, FindFrame(health));

    internal static void ObserveProjectileInit(Projectile projectile, ProjectileContext context) => ProjectileInitPostfix(projectile, context);
    internal static void ObserveProjectileRelease(Projectile projectile) => ProjectileReleasePrefix(projectile);

    internal static void ObserveProjectileBegin(Projectile projectile, CombatNativeScope? resolvedScope)
    {
        if (!HasActiveContext()) return;
        ProjectilePrefix(projectile, resolvedScope, out var state);
        PushObserverState(state);
    }

    internal static void ObserveEffectBegin(EffectTriggerEventContext context, CombatNativeScope? resolvedScope)
    {
        if (!HasActiveContext()) return;
        EffectPrefix(context, resolvedScope, out var state);
        PushObserverState(state);
    }

    internal static void ObserveMeleeBegin(ItemAgent_MeleeWeapon weapon, bool dealDamage, CombatNativeScope? resolvedScope)
    {
        if (!HasActiveContext()) return;
        MeleePrefix(weapon, dealDamage, resolvedScope, out var state);
        PushObserverState(state);
    }

    private static void PushObserverState(AttackState state)
    {
        try { (observerAttackStates ??= new()).Push(state); }
        catch (Exception exception) { state.Owner?.Disable(exception); }
    }

    private static bool HasActiveContext()
    {
        var probe = active;
        try { return probe?.CanCapture() == true; }
        catch (Exception exception) { probe?.Disable(exception); return false; }
    }

    internal static void ObserveAttackFinally(Exception? exception)
    {
        try
        {
            if (observerAttackStates?.Count > 0) AttackFinalizer(exception, observerAttackStates.Pop());
        }
        catch (Exception failure) { active?.Disable(failure); }
    }

    public void Tick(EncounterObservationContext context)
    {
        try
        {
            if (disposed || !enabled) { Cleanup(); return; }
            Synchronize(context);
            if (context.Active) ObserveSharedHookTrust();
            if (context.MonotonicSeconds >= nextInspection)
            {
                nextInspection = context.MonotonicSeconds + 2;
                if (setterMethod != null && lease.Value != null
                    && !lease.Value.IsPatchSetTrusted(setterMethod, setterExpectations, out var detail))
                    Disable(new InvalidOperationException("Health setter patch trust changed: " + detail));
            }
        }
        catch (Exception exception) { Disable(exception); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        enabled = false;
        if (ReferenceEquals(active, this)) active = null;
        Cleanup();
    }

    private bool CanCapture()
    {
        if (Environment.CurrentManagedThreadId != owningThread || disposed || !enabled || !sink.Context.Active) return false;
        Synchronize(sink.Context);
        ObserveSharedHookTrust();
        return true;
    }

    private void ObserveSharedHookTrust()
    {
        var loss = CombatHarmonyBridge.EncounterHookLoss;
        var newlyLost = loss & ~reportedHookLoss;
        if (newlyLost == EncounterCombatHookLoss.None) return;
        reportedHookLoss |= loss;
        if ((newlyLost & (EncounterCombatHookLoss.ProjectileSource | EncounterCombatHookLoss.ProjectileRelease)) != 0)
        {
            // The shared owner invalidates its in-flight correlations on these
            // changes. Do not keep a second, apparently trusted copy alive.
            origins.Clear();
            originOrder.Clear();
        }
        Status = "Partial: shared combat hooks unavailable: " + reportedHookLoss + ". Independently trusted evidence continues.";
        // Record directly: Emit calls CanCapture. The host persists this as run
        // coverage, including when trust was lost before this run began.
        sink.Record("combat_coverage", new { UntrustedHooks = reportedHookLoss.ToString(), Detail = Status });
    }

    private void Synchronize(EncounterObservationContext context)
    {
        if (string.Equals(generation, context.GenerationId, StringComparison.Ordinal)
            && string.Equals(run, context.RunId, StringComparison.Ordinal)) return;
        generation = context.GenerationId;
        run = context.RunId;
        epoch++;
        origins.Clear();
        originOrder.Clear();
        relatedHealth = new();
        confirmedDeaths = new();
        actorLabels = new();
        playerDeathRecorded = false;
        reportedHookLoss = EncounterCombatHookLoss.None;
        healthFrame = null;
        attack = null;
    }

    private void Disable(Exception exception)
    {
        enabled = false;
        if (ReferenceEquals(active, this)) active = null;
        Status = "Disabled: " + exception.GetType().Name + ": " + exception.Message;
        try { log(Status); } catch { /* Diagnostics cannot escape a native callback. */ }
    }

    private void Cleanup()
    {
        try
        {
            if (subscribed)
            {
                Health.OnDead -= OnDead;
                LevelManager.OnMainCharacterDead -= OnPlayerDeath;
                subscribed = false;
            }
            if (ReferenceEquals(healthFrame?.Owner, this)) healthFrame = null;
            if (ReferenceEquals(attack?.Owner, this)) attack = null;
            if (observerAttackStates?.Any(state => ReferenceEquals(state.Owner, this)) == true) observerAttackStates.Clear();
            origins.Clear();
            originOrder.Clear();
            if (!lease.TryCleanup(out var detail)) Status = "Disabled; owner-only cleanup pending: " + detail;
        }
        catch (Exception exception) { try { log("Combat probe cleanup: " + exception.Message); } catch { } }
    }

    private void Emit(string kind, object value)
    {
        // The harness enforces its short-session row/byte budget. Payloads below
        // are anonymous/value DTOs containing no Unity object or DamageInfo.
        if (CanCapture()) sink.Record(kind, value);
    }

    private HealthFrame Begin(Health health, DamageInfo info)
    {
        var source = ReadSource(info);
        var target = health.TryGetCharacter();
        var targetMain = target != null && ReferenceEquals(target, CharacterMainControl.Main);
        var related = targetMain || source.OriginallyPlayer || source.Credited.IsMain || source.Physical.IsMain
            || relatedHealth.TryGetValue(health, out _);
        var frame = new HealthFrame(this, healthFrame, health, epoch, ++sequence, health.CurrentHealth, health.IsDead, related, targetMain);
        healthFrame = frame;
        if (!related) return frame;
        MarkRelated(health);
        if (targetMain && info.fromCharacter != null && info.fromCharacter.Health != null) MarkRelated(info.fromCharacter.Health);
        frame.Target = target != null ? Actor(target) : new ActorSnapshot { Id = sink.ActorId(health), Kind = "health-only" };
        frame.Source = source;
        var sourceActor = CurrentSourceActor() ?? info.fromCharacter;
        if (source.Kind is "effect" or "unscoped-effect" && !source.ActorCreditResolved) sourceActor = null;
        if (sourceActor != null) frame.SourceActor = new WeakReference<CharacterMainControl>(sourceActor);
        Emit("combat_hurt_begin", new
        {
            Transaction = frame.Id, ParentTransaction = frame.Previous?.Id, Target = frame.Target,
            Source = source, frame.Before, frame.WasDead, RequestedDamage = info.damageValue,
            NativeDamageActor = Actor(info.fromCharacter),
            InputFinalDamage = info.finalDamage, NativeWeaponId = info.fromWeaponItemID,
            NativeExplosion = info.isExplosion, NativeBuffMarker = info.isFromBuffOrEffect,
            StopwatchTicks = Stopwatch.GetTimestamp(), ContextSeconds = sink.Context.MonotonicSeconds
        });
        return frame;
    }

    private void MarkRelated(Health health)
    {
        if (!relatedHealth.TryGetValue(health, out _)) relatedHealth.Add(health, new Marker());
    }

    private void Assigned(Health health, float proposed, out SetterState state)
    {
        state = default;
        var frame = FindFrame(health);
        if (frame == null || !frame.Capture || frame.Epoch != epoch) return;
        var before = health.CurrentHealth;
        var observation = ++sequence;
        var firstLethal = frame.Boundary.ObserveAssignment(before, proposed, observation);
        state = new SetterState(frame, observation, before, proposed);
        if (firstLethal)
        {
            frame.Candidate = new FatalCandidate
            {
                Sequence = observation,
                StopwatchTicks = Stopwatch.GetTimestamp(),
                TargetPosition = Position(health.TryGetCharacter()),
                PlayerPosition = Position(CharacterMainControl.Main),
                SourcePosition = Position(frame.SourceActor != null && frame.SourceActor.TryGetTarget(out var liveSource) ? liveSource : null),
                Timing = "before-nonpositive-health-assignment; not yet confirmed fatal"
            };
            Emit("combat_fatal_candidate", new { Transaction = frame.Id, Target = frame.Target, frame.Candidate, Before = before, Proposed = proposed });
        }
        Emit("combat_hp_assignment_before", new { Transaction = frame.Id, Observation = observation, Before = before, Proposed = proposed, WasDead = health.IsDead });
    }

    private void AssignedAfter(Health health, SetterState state)
    {
        if (state.Frame == null || state.Frame.Epoch != epoch || !state.Frame.Capture) return;
        Emit("combat_hp_assignment_after", new
        {
            Transaction = state.Frame.Id, state.Observation, state.Before, state.Proposed,
            AfterCallbacks = health.CurrentHealth, IsDead = health.IsDead,
            Timing = "setter returned after OnHealthChange callbacks; may include nested mutations"
        });
    }

    private void Complete(Health health, HealthFrame frame, bool result)
    {
        if (!frame.Capture || frame.Epoch != epoch) return;
        var after = health.CurrentHealth;
        var fatal = CombatProbeBoundary.IsFatal(frame.WasDead, health.IsDead, result);
        Emit("combat_hurt_complete", new
        {
            Transaction = frame.Id, Target = frame.Target, Source = frame.Source, NativeCallCompleted = result, frame.Headshot,
            frame.Before, After = after, NetHpLossAcrossCall = CombatProbeBoundary.NetLoss(frame.Before, after),
            ProposedHpLossOwnedAssignments = frame.Boundary.ProposedLoss, frame.Boundary.MutationCount,
            Fatal = fatal, CandidateConfirmed = fatal && frame.Candidate != null,
            frame.StaticDeathObserved, frame.PublicPlayerDeathObserved,
            Interpretation = "diagnostic mutation evidence; net call loss is not isolated when callbacks mutate HP"
        });
        if (fatal) RecordFatal(health, frame);
    }

    private void RecordFatal(Health health, HealthFrame frame)
    {
        if (confirmedDeaths.TryGetValue(health, out _) || (frame.TargetMain && playerDeathRecorded)) return;
        confirmedDeaths.Add(health, new Marker());
        if (frame.TargetMain) playerDeathRecorded = true;
        Emit("combat_fatal", new
        {
            Transaction = frame.Id, FatalSequence = frame.Candidate?.Sequence ?? ++sequence,
            Kind = frame.TargetMain ? "player-death" : "player-related-target-death",
            Target = frame.Target, Source = frame.Source, frame.Candidate,
            PositionsAvailableBeforeCleanup = frame.Candidate != null,
            HpLoss = frame.Boundary.ProposedLoss,
            Attribution = "source observations only; retained buff credit is not proven causal ownership",
            frame.StaticDeathObserved, frame.PublicPlayerDeathObserved
        });
    }

    private void OnDead(Health health, DamageInfo info)
    {
        try
        {
            if (!CanCapture()) return;
            var frame = FindFrame(health);
            if (frame == null || !frame.Capture) return;
            frame.StaticDeathObserved = true;
            Emit("combat_static_death_callback", new
            {
                Transaction = frame.Id, Target = frame.Target, FinalNativeDamage = info.finalDamage,
                NativeWeaponId = info.fromWeaponItemID, NativeSource = Actor(info.fromCharacter),
                Timing = "Health.OnDead after instance death handlers and possible corpse conversion"
            });
        }
        catch (Exception exception) { Disable(exception); }
    }

    private void OnPlayerDeath(DamageInfo info)
    {
        try
        {
            if (!CanCapture()) return;
            var main = CharacterMainControl.Main;
            var frame = main != null ? FindFrame(main.Health) : null;
            if (frame != null)
            {
                frame.PublicPlayerDeathObserved = true;
                Emit("combat_player_death_callback", new { Transaction = frame.Id, AlreadyRecorded = playerDeathRecorded, NativeFinalDamage = info.finalDamage });
                return;
            }
            if (playerDeathRecorded) return;
            playerDeathRecorded = true;
            Emit("combat_fatal", new
            {
                Transaction = (long?)null, FatalSequence = ++sequence, Kind = "player-death",
                Target = Actor(main), Source = ReadSource(info), PositionsAvailableBeforeCleanup = false,
                Timing = "fallback public player-death callback; no matching Hurt scope; exact fatal positions unavailable"
            });
        }
        catch (Exception exception) { Disable(exception); }
    }

    private void CaptureProjectile(Projectile projectile, ProjectileContext context)
    {
        var gun = context.fromGunItemSetting;
        var bullet = gun != null ? gun.GetCurrentLoadedBullet() : null;
        var source = new SourceSnapshot
        {
            Kind = "projectile", Provenance = "Projectile.Init input context; ammo read before native UseABullet",
            OriginId = ++sequence, Physical = Actor(context.realFromCharacter), Credited = Actor(context.fromCharacter),
            WeaponId = context.fromWeaponItemID, TargetAmmoId = gun != null ? gun.TargetBulletID : -1,
            LoadedAmmoId = bullet != null ? bullet.TypeID : -1
        };
        source.OriginallyPlayer = source.Physical.IsMain || source.Credited.IsMain;
        source.AmmoAgreement = source.LoadedAmmoId > 0 && source.LoadedAmmoId == source.TargetAmmoId;
        var origin = new ProjectileOrigin(this, projectile, source);
        var id = projectile.GetInstanceID();
        origins[id] = origin;
        originOrder.Enqueue((id, source.OriginId));
        while (originOrder.Count > MaximumOrigins)
        {
            var old = originOrder.Dequeue();
            if (origins.TryGetValue(old.Id, out var existing) && existing.Source.OriginId == old.Origin)
            {
                origins.Remove(old.Id);
                Emit("combat_origin_evicted", new { Origin = old.Origin, Reason = "bounded encounter correlation; later hits retain unknown origin" });
            }
        }
        if (source.OriginallyPlayer) Emit("combat_projectile_launch", source);
    }

    private ProjectileOrigin? FindOrigin(Projectile projectile) =>
        origins.TryGetValue(projectile.GetInstanceID(), out var origin)
        && origin.Projectile.TryGetTarget(out var captured) && ReferenceEquals(projectile, captured) ? origin : null;

    private SourceSnapshot ReadSource(DamageInfo info)
    {
        if (attack?.Owner == this && (attack.Source.Kind != "projectile"
            || (CombatHarmonyBridge.EncounterHookLoss & EncounterCombatHookLoss.ProjectileSource) == 0))
        {
            if (attack.Projectile != null && attack.Projectile.TryGetTarget(out var projectile) && projectile != null)
            {
                var origin = attack.Source;
                return new SourceSnapshot
                {
                    OriginId = origin.OriginId, Kind = origin.Kind, Provenance = origin.Provenance,
                    Physical = Actor(projectile.context.realFromCharacter), Credited = Actor(projectile.context.fromCharacter),
                    OriginalPhysical = origin.Physical, OriginalCredited = origin.Credited,
                    OriginallyPlayer = origin.OriginallyPlayer, WeaponId = origin.WeaponId,
                    TargetAmmoId = origin.TargetAmmoId, LoadedAmmoId = origin.LoadedAmmoId, AmmoAgreement = origin.AmmoAgreement,
                    NativeDamageActor = Actor(info.fromCharacter)
                };
            }
            return attack.Source;
        }
        return new SourceSnapshot
        {
            Kind = info.isFromBuffOrEffect ? "unscoped-effect" : info.isExplosion ? "unscoped-explosion" : "unscoped",
            Provenance = "DamageInfo only; no verified source/ammo scope",
            Credited = Actor(info.fromCharacter), WeaponId = info.fromWeaponItemID,
            OriginallyPlayer = info.fromCharacter != null && ReferenceEquals(info.fromCharacter, CharacterMainControl.Main)
        };
    }

    private static CharacterMainControl? CurrentSourceActor()
    {
        if (attack?.Projectile != null && attack.Projectile.TryGetTarget(out var projectile) && projectile != null)
            return projectile.context.fromCharacter;
        return attack?.LiveActor;
    }

    private static PositionSnapshot Position(CharacterMainControl? actor)
    {
        if (actor == null) return new PositionSnapshot();
        var value = actor.transform.position;
        var scene = actor.gameObject.scene;
        var related = RelatedScene?.GetValue(actor) is int build ? build : -1;
        var main = ReferenceEquals(actor, CharacterMainControl.Main);
        return new PositionSnapshot
        {
            Available = true, X = value.x, Y = value.y, Z = value.z,
            PhysicalSceneBuild = scene.buildIndex, PhysicalSceneName = scene.name,
            RelatedSceneBuild = related,
            LogicalScene = main ? MultiSceneCore.ActiveSubSceneID : related >= 0 ? SceneInfoCollection.GetSceneID(related) ?? string.Empty : string.Empty,
            SceneEvidence = main ? "main-active-subscene" : "private-relatedScene field; map alignment requires qualification"
        };
    }

    private static HealthFrame? FindFrame(Health health)
    {
        for (var frame = healthFrame; frame != null; frame = frame.Previous)
            if (ReferenceEquals(frame.Health, health)) return frame;
        return null;
    }

    private static MethodInfo Exact(Type type, string name, Type result, params Type[] parameters) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault(method => method.Name == name && method.ReturnType == result
                && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters))
        ?? throw new MissingMethodException(type.FullName, name);

    private static void Install(ReflectiveHarmonyPatcher patcher, MethodInfo original, string? prefix, string? postfix, string? finalizer) =>
        patcher.Patch(original, Callback(prefix), Callback(postfix), Callback(finalizer));

    private static MethodInfo? Callback(string? name) => name == null ? null : typeof(NativeEncounterCombatObserver)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException(name);

    private static void HurtPrefix(Health __instance, DamageInfo damageInfo, out HealthFrame? __state)
    {
        __state = null;
        var probe = active;
        try
        {
            if (probe?.CanCapture() == true && CombatHarmonyBridge.EncounterHealthHookTrusted)
                __state = probe.Begin(__instance, damageInfo);
        }
        catch (Exception exception) { probe?.Disable(exception); }
    }

    private static void HurtPostfix(Health __instance, bool __result, HealthFrame? __state)
    {
        try { if (__state?.Owner.CanCapture() == true) __state.Owner.Complete(__instance, __state, __result); }
        catch (Exception exception) { __state?.Owner.Disable(exception); }
    }

    private static Exception? HurtFinalizer(Exception? __exception, HealthFrame? __state)
    {
        try
        {
            if (__state != null)
            {
                if (__exception != null && __state.Capture)
                    __state.Owner.Emit("combat_hurt_exception", new { Transaction = __state.Id, ExceptionType = __exception.GetType().Name });
                if (ReferenceEquals(healthFrame, __state))
                    healthFrame = __state.Owner.enabled && __state.Owner.epoch == __state.Epoch ? __state.Previous : null;
            }
        }
        catch (Exception exception) { __state?.Owner.Disable(exception); }
        return __exception;
    }

    private static void HealthValuePrefix(Health __instance, float value, out SetterState __state)
    {
        __state = default;
        var probe = active;
        try { if (probe?.CanCapture() == true) probe.Assigned(__instance, value, out __state); }
        catch (Exception exception) { probe?.Disable(exception); }
    }

    private static void HealthValuePostfix(Health __instance, SetterState __state)
    {
        try { if (__state.Frame?.Owner.CanCapture() == true) __state.Frame.Owner.AssignedAfter(__instance, __state); }
        catch (Exception exception) { __state.Frame?.Owner.Disable(exception); }
    }

    private static void ProjectileInitPostfix(Projectile __instance, ProjectileContext _context)
    {
        var probe = active;
        try
        {
            if (probe?.CanCapture() == true
                && (CombatHarmonyBridge.EncounterHookLoss & EncounterCombatHookLoss.ProjectileSource) == 0)
                probe.CaptureProjectile(__instance, _context);
        }
        catch (Exception exception) { probe?.Disable(exception); }
    }

    private static void ProjectilePrefix(Projectile __instance, CombatNativeScope? resolvedScope, out AttackState __state)
    {
        __state = default;
        var probe = active;
        try
        {
            if (probe?.CanCapture() != true) return;
            var origin = resolvedScope?.IsRanged == true
                && (CombatHarmonyBridge.EncounterHookLoss & EncounterCombatHookLoss.ProjectileSource) == 0
                ? probe.FindOrigin(__instance) : null;
            __state = new AttackState(probe, attack);
            attack = origin?.Runtime; // Missing origin must shadow an unrelated enclosing source.
        }
        catch (Exception exception) { probe?.Disable(exception); }
    }

    private static void ProjectileReleasePrefix(Projectile __instance)
    {
        var probe = active;
        try { if (probe?.CanCapture() == true) probe.origins.Remove(__instance.GetInstanceID()); }
        catch (Exception exception) { probe?.Disable(exception); }
    }

    private static void EffectPrefix(EffectTriggerEventContext context, CombatNativeScope? resolvedScope, out AttackState __state)
    {
        __state = default;
        var probe = active;
        try
        {
            if (probe?.CanCapture() != true) return;
            var buff = context.source != null ? context.source.GetComponentInParent<Buff>() : null;
            // Reuse the trusted aggregate owner resolution. Native buffs retain their
            // original actor/weapon even after another actor refreshes the same buff.
            var resolved = resolvedScope != null && !resolvedScope.ConflictingActorEvidence;
            var source = resolved ? resolvedScope!.PhysicalSource ?? resolvedScope.CreditedSource : null;
            __state = new AttackState(probe, attack);
            attack = new AttackRuntime(probe, new SourceSnapshot
            {
                Kind = "effect", Provenance = "shared combat effect ownership resolution", ActorCreditResolved = resolved,
                Physical = probe.Actor(source), Credited = probe.Actor(resolved ? resolvedScope!.CreditedSource : null),
                OriginallyPlayer = ReferenceEquals(buff?.fromWho, CharacterMainControl.Main) && CharacterMainControl.Main != null,
                WeaponId = resolved ? resolvedScope!.WeaponTypeId : -1, BuffId = buff != null ? buff.ID : -1,
                BuffLayers = buff != null ? buff.CurrentLayers : -1,
                Delayed = context.source is TickTrigger || context.source is UpdateTrigger
            }) { LiveActor = source };
        }
        catch (Exception exception) { probe?.Disable(exception); }
    }

    private static void MeleePrefix(ItemAgent_MeleeWeapon __instance, bool dealDamage, CombatNativeScope? resolvedScope, out AttackState __state)
    {
        __state = default;
        var probe = active;
        try
        {
            if (probe?.CanCapture() != true) return;
            __state = new AttackState(probe, attack);
            // Even a rejected nested scope must shadow an enclosing attack and
            // restore it in the finalizer. Never infer trust from the tap firing.
            attack = null;
            if (!dealDamage || resolvedScope?.IsMelee != true
                || (CombatHarmonyBridge.EncounterHookLoss & EncounterCombatHookLoss.Melee) != 0) return;
            var physical = resolvedScope.PhysicalSource;
            var credited = resolvedScope.CreditedSource;
            attack = new AttackRuntime(probe, new SourceSnapshot
            {
                Kind = "melee", Provenance = "shared combat melee ownership resolution",
                Physical = probe.Actor(physical), Credited = probe.Actor(credited),
                OriginallyPlayer = credited != null && ReferenceEquals(credited, CharacterMainControl.Main),
                WeaponId = resolvedScope.WeaponTypeId
            }) { LiveActor = credited };
        }
        catch (Exception exception) { probe?.Disable(exception); }
    }

    private static Exception? AttackFinalizer(Exception? __exception, AttackState __state)
    {
        try
        {
            if (__state.Owner != null)
                attack = __state.Owner.enabled && __state.Owner.epoch == __state.Epoch ? __state.Previous : null;
        }
        catch (Exception exception) { __state.Owner?.Disable(exception); }
        return __exception;
    }

    private sealed class Marker { }

    private sealed class HealthFrame
    {
        public HealthFrame(NativeEncounterCombatObserver owner, HealthFrame? previous, Health health, long epoch, long id, double before, bool wasDead, bool capture, bool targetMain)
        { Owner = owner; Previous = previous; Health = health; Epoch = epoch; Id = id; Before = before; WasDead = wasDead; Capture = capture; TargetMain = targetMain; }
        public NativeEncounterCombatObserver Owner { get; }
        public HealthFrame? Previous { get; }
        public Health Health { get; }
        public long Epoch { get; }
        public long Id { get; }
        public double Before { get; }
        public bool WasDead { get; }
        public bool Capture { get; }
        public bool TargetMain { get; }
        public bool Headshot { get; set; }
        public CombatProbeBoundary Boundary { get; } = new();
        public ActorSnapshot Target { get; set; } = new();
        public SourceSnapshot Source { get; set; } = new();
        public WeakReference<CharacterMainControl>? SourceActor { get; set; }
        public FatalCandidate? Candidate { get; set; }
        public bool StaticDeathObserved { get; set; }
        public bool PublicPlayerDeathObserved { get; set; }
    }

    private readonly struct SetterState
    {
        public SetterState(HealthFrame frame, long observation, double before, double proposed)
        { Frame = frame; Observation = observation; Before = before; Proposed = proposed; }
        public HealthFrame? Frame { get; }
        public long Observation { get; }
        public double Before { get; }
        public double Proposed { get; }
    }

    private readonly struct AttackState
    {
        public AttackState(NativeEncounterCombatObserver owner, AttackRuntime? previous) { Owner = owner; Previous = previous; Epoch = owner.epoch; }
        public NativeEncounterCombatObserver? Owner { get; }
        public AttackRuntime? Previous { get; }
        public long Epoch { get; }
    }

    private sealed class AttackRuntime
    {
        public AttackRuntime(NativeEncounterCombatObserver owner, SourceSnapshot source) { Owner = owner; Source = source; }
        public NativeEncounterCombatObserver Owner { get; }
        public SourceSnapshot Source { get; }
        public WeakReference<Projectile>? Projectile { get; set; }
        public CharacterMainControl? LiveActor { get; set; }
    }

    private sealed class ProjectileOrigin
    {
        public ProjectileOrigin(NativeEncounterCombatObserver owner, Projectile projectile, SourceSnapshot source)
        {
            Projectile = new WeakReference<Projectile>(projectile); Source = source;
            Runtime = new AttackRuntime(owner, source) { Projectile = Projectile };
        }
        public WeakReference<Projectile> Projectile { get; }
        public SourceSnapshot Source { get; }
        public AttackRuntime Runtime { get; }
    }

    private sealed class SourceSnapshot
    {
        public long OriginId { get; set; }
        public string Kind { get; set; } = "unknown";
        public string Provenance { get; set; } = string.Empty;
        public ActorSnapshot Physical { get; set; } = new();
        public ActorSnapshot Credited { get; set; } = new();
        public ActorSnapshot? OriginalPhysical { get; set; }
        public ActorSnapshot? OriginalCredited { get; set; }
        public ActorSnapshot? NativeDamageActor { get; set; }
        public bool OriginallyPlayer { get; set; }
        public bool ActorCreditResolved { get; set; }
        public int WeaponId { get; set; } = -1;
        public int TargetAmmoId { get; set; } = -1;
        public int LoadedAmmoId { get; set; } = -1;
        public bool AmmoAgreement { get; set; }
        public int BuffId { get; set; } = -1;
        public int BuffLayers { get; set; } = -1;
        public bool Delayed { get; set; }
    }

    private sealed class PositionSnapshot
    {
        public bool Available { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int PhysicalSceneBuild { get; set; } = -1;
        public string PhysicalSceneName { get; set; } = string.Empty;
        public int RelatedSceneBuild { get; set; } = -1;
        public string LogicalScene { get; set; } = string.Empty;
        public string SceneEvidence { get; set; } = string.Empty;
    }

    private sealed class FatalCandidate
    {
        public long Sequence { get; set; }
        public long StopwatchTicks { get; set; }
        public string Timing { get; set; } = string.Empty;
        public PositionSnapshot TargetPosition { get; set; } = new();
        public PositionSnapshot PlayerPosition { get; set; } = new();
        public PositionSnapshot SourcePosition { get; set; } = new();
    }
}
