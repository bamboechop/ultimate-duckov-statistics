using System.Globalization;
using System.Reflection;
using Duckov.Scenes;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeCombatAttributionAdapter : IDisposable, IRetryableCleanup
{
    internal const string HarmonyId = "at.bamboechop.ultimate-duckov-statistics.combat";
    internal const string AdapterVersion = "native-combat-attribution/2.3.30+harmony-2.4.1";
    private const string SupportedGameVersion = "2.3.30";
    private const string SupportedGameBuild = "24013657";
    private const int MaximumProjectileCorrelations = 2048;
    private readonly Func<string> saveGenerationIdProvider;
    private readonly Func<string?> runIdProvider;
    private readonly Func<string?> mapIdProvider;
    private readonly Func<CombatRecorded, bool> combatHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly RetryableHarmonyPatcherLease patcherLease = new();
    private readonly Dictionary<int, ProjectileSnapshot> projectiles = new();
    private readonly Queue<(int RuntimeId, string ProjectileId)> projectileOrder = new();
    private PatchRegistration[] patchRegistrations = Array.Empty<PatchRegistration>();
    private CombatMetricCapabilities metricCapabilities = new();
    private CharacterMainControl? subscribedMainCharacter;
    private bool cleanupPending;
    private DateTime nextCleanupAttemptUtc;
    private DateTime nextConflictCheckUtc;
    private bool retryInitialization;
    private DateTime nextInitializationAttemptUtc;
    private bool disposed;

    public NativeCombatAttributionAdapter(
        Func<string> saveGenerationIdProvider,
        Func<string?> runIdProvider,
        Func<string?> mapIdProvider,
        Func<CombatRecorded, bool> combatHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.runIdProvider = runIdProvider ?? throw new ArgumentNullException(nameof(runIdProvider));
        this.mapIdProvider = mapIdProvider ?? throw new ArgumentNullException(nameof(mapIdProvider));
        this.combatHandler = combatHandler ?? throw new ArgumentNullException(nameof(combatHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        SetUnavailable("Combat attribution has not been initialized.");
    }

    public CombatMetricCapabilities MetricCapabilities => CombatStatisticsReducer.CloneCapabilities(metricCapabilities);

    public bool CanObserveHealth => !disposed
                                    && !cleanupPending
                                    && metricCapabilities.DamageDealt.State == AdapterCapabilityState.Supported;

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeCombatAttributionAdapter));
        if (CanObserveHealth) return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        if (!string.Equals(Application.version, SupportedGameVersion, StringComparison.Ordinal))
        {
            SetUnavailable($"Installed Duckov version '{Application.version}' does not match verified combat contract '{SupportedGameVersion}'.");
            return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        }

        if (!TryResolveContracts(out var methods, out var failure))
        {
            retryInitialization = false;
            SetUnavailable(failure);
            return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        }

        if (!ReflectiveHarmonyPatcher.TryCreate(HarmonyId, out var created, out var harmonyDetail) || created == null)
        {
            retryInitialization = ReflectiveHarmonyPatcher.HasPendingCleanup
                                  || !ReflectiveHarmonyPatcher.IsHarmonyLoaded;
            nextInitializationAttemptUtc = DateTime.UtcNow.AddSeconds(1);
            SetUnavailable(harmonyDetail);
            return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        }

        patcherLease.Attach(created);
        try
        {
            foreach (var method in methods.All)
            {
                if (!created.IsPatchSetTrusted(method, Array.Empty<HarmonyPatchExpectation>(), out var detail))
                {
                    throw new InvalidOperationException(
                        $"{method.DeclaringType?.Name}.{method.Name} has an unsafe pre-existing Harmony patch set: {detail}");
                }
            }

            patchRegistrations = CreateRegistrations(methods);
            CombatHarmonyBridge.Attach(this);
            created.Patch(methods.HealthHurt, CombatHarmonyCallbacks.HealthPrefixMethod, CombatHarmonyCallbacks.HealthPostfixMethod);
            created.Patch(methods.ProjectileInit, postfix: CombatHarmonyCallbacks.ProjectileInitPostfixMethod);
            created.Patch(methods.ProjectileUpdate, CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod, finalizer: CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod);
            created.Patch(methods.ProjectileRelease, CombatHarmonyCallbacks.ProjectileReleasePrefixMethod);
            created.Patch(methods.MeleeCheck, CombatHarmonyCallbacks.MeleePrefixMethod, finalizer: CombatHarmonyCallbacks.MeleeFinalizerMethod);
            created.Patch(methods.EffectTrigger, CombatHarmonyCallbacks.EffectPrefixMethod, finalizer: CombatHarmonyCallbacks.EffectFinalizerMethod);
            foreach (var registration in patchRegistrations)
            {
                if (!created.IsPatchSetTrusted(registration.Original, registration.Expected, out var detail))
                {
                    throw new InvalidOperationException($"Installed combat patch set validation failed: {detail}");
                }
            }

            metricCapabilities = CombatNativeContractPolicy.CreateSupportedCapabilities();
            retryInitialization = false;
            PublishCapabilities();
            nextConflictCheckUtc = DateTime.UtcNow.AddSeconds(2);
            SynchronizeMainCharacter();
            diagnosticHandler(
                $"Combat attribution active with HarmonyLib {created.Version}; exact HP deltas, projectile/melee/effect scopes, and public melee action callbacks verified.");
        }
        catch (Exception exception)
        {
            DetachRuntimeHooks();
            retryInitialization = false;
            SetUnavailable($"Combat patch activation failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
            QueueCleanup();
            TryCompleteCleanup();
        }

        return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
    }

    public void Tick()
    {
        if (cleanupPending)
        {
            if (DateTime.UtcNow >= nextCleanupAttemptUtc) TryCompleteCleanup();
            return;
        }

        if (retryInitialization && DateTime.UtcNow >= nextInitializationAttemptUtc)
        {
            Initialize();
            return;
        }

        if (!CanObserveHealth) return;
        SynchronizeMainCharacter();
        if (DateTime.UtcNow < nextConflictCheckUtc) return;
        nextConflictCheckUtc = DateTime.UtcNow.AddSeconds(2);
        var patcher = patcherLease.Value;
        foreach (var registration in patchRegistrations)
        {
            var detail = patcher == null ? "The combat Harmony patcher is unavailable." : string.Empty;
            if (patcher == null || !patcher.IsPatchSetTrusted(registration.Original, registration.Expected, out detail))
            {
                DetachRuntimeHooks();
                retryInitialization = false;
                SetUnavailable($"Combat attribution disabled after patch-set drift: {detail}");
                QueueCleanup();
                TryCompleteCleanup();
                return;
            }
        }
    }

    public void CaptureProjectile(Projectile projectile, ProjectileContext context)
    {
        if (!CanObserveHealth || projectile == null) return;
        var physicalSource = context.realFromCharacter != null ? context.realFromCharacter : context.fromCharacter;
        var isExactPlayer = physicalSource != null && physicalSource.IsMainCharacter
                            && ReferenceEquals(physicalSource, CharacterMainControl.Main);
        var ammunitionId = context.fromGunItemSetting == null ? -1 : context.fromGunItemSetting.TargetBulletID;
        var snapshot = new ProjectileSnapshot
        {
            ProjectileId = Guid.NewGuid().ToString("N"),
            PhysicalSource = physicalSource,
            IsExactPlayer = isExactPlayer,
            HeadTargeted = isExactPlayer && LevelManager.Instance != null
                           && LevelManager.Instance.InputManager != null
                           && LevelManager.Instance.InputManager.AimingEnemyHead,
            WeaponTypeId = context.fromWeaponItemID,
            WeaponDisplayName = ReadItemDisplayName(context.fromWeaponItemID, "weapon"),
            AmmunitionTypeId = ammunitionId,
            AmmunitionDisplayName = context.fromGunItemSetting == null
                ? string.Empty
                : NonEmpty(context.fromGunItemSetting.CurrentBulletName, $"Unknown ammunition {ammunitionId}")
        };
        var runtimeId = projectile.GetInstanceID();
        projectiles[runtimeId] = snapshot;
        projectileOrder.Enqueue((runtimeId, snapshot.ProjectileId));
        while (projectileOrder.Count > MaximumProjectileCorrelations)
        {
            var oldest = projectileOrder.Dequeue();
            if (projectiles.TryGetValue(oldest.RuntimeId, out var current)
                && string.Equals(current.ProjectileId, oldest.ProjectileId, StringComparison.Ordinal))
            {
                projectiles.Remove(oldest.RuntimeId);
            }
        }
    }

    public CombatNativeScope? CreateProjectileScope(Projectile projectile)
    {
        if (!CanObserveHealth || projectile == null
            || !projectiles.TryGetValue(projectile.GetInstanceID(), out var value)) return null;
        return value.Scope;
    }

    public void CompleteProjectile(Projectile projectile)
    {
        if (!CanObserveHealth || projectile == null
            || !projectiles.TryGetValue(projectile.GetInstanceID(), out var value)
            || value.Completed) return;
        value.Completed = true;
        if (value.IsExactPlayer)
        {
            Emit(NewEvent(value.Scope, ownership: CombatOwnership.Player) with
            {
                CompletedPlayerProjectiles = 1,
                RangedHits = value.Scope.HitCounted ? 1 : 0
            });
        }
        projectiles.Remove(projectile.GetInstanceID());
    }

    public CombatNativeScope? CreateMeleeScope(ItemAgent_MeleeWeapon weapon)
    {
        if (!CanObserveHealth || weapon == null || weapon.Holder == null
            || !weapon.Holder.IsMainCharacter || !ReferenceEquals(weapon.Holder, CharacterMainControl.Main)) return null;
        var item = weapon.Item;
        return new CombatNativeScope
        {
            IsMelee = true,
            PhysicalSource = weapon.Holder,
            WeaponTypeId = item == null ? -1 : item.TypeID,
            WeaponDisplayName = item == null ? string.Empty : NonEmpty(item.DisplayName, $"Unknown weapon {item.TypeID}")
        };
    }

    public CombatNativeScope? CreateEffectScope(EffectTriggerEventContext context)
    {
        if (!CanObserveHealth || context.source == null) return null;
        return new CombatNativeScope
        {
            IsEffect = true,
            IsDamageOverTime = context.source is TickTrigger || context.source is UpdateTrigger
        };
    }

    public void RecordHealthTransition(Health health, CombatHealthPatchState state, double actualDamage, bool fatal)
    {
        if (!CanObserveHealth || health == null || actualDamage <= 0) return;
        var target = health.TryGetCharacter();
        var targetIsMain = health.IsMainCharacterHealth;
        var scope = state.Scope;
        var source = scope?.PhysicalSource != null ? scope.PhysicalSource : state.DamageInfo.fromCharacter;
        var ownership = ResolveOwnership(source);
        var enemyTarget = !targetIsMain && health.team != Teams.player;
        var playerDamage = ownership == CombatOwnership.Player && !targetIsMain;
        var rangedHit = CombatObservationPolicy.CountRangedHit(
            enemyTarget, ownership == CombatOwnership.Player, scope?.IsRanged == true, scope?.HitCounted == true);
        var meleeHit = CombatObservationPolicy.CountMeleeHit(
            enemyTarget, ownership == CombatOwnership.Player, scope?.IsMelee == true, scope?.HitCounted == true);
        if (rangedHit || meleeHit) scope!.HitCounted = true;
        var headshot = CombatObservationPolicy.CountHeadshot(
            scope?.HeadTargeted == true, state.DamageInfo.crit > 0, rangedHit, scope?.HeadshotCounted == true);
        if (headshot) scope!.HeadshotCounted = true;
        var headshotFinalBlow = CombatObservationPolicy.CountHeadshotFinalBlow(
            scope?.HeadTargeted == true, enemyTarget, fatal, scope?.HeadshotFinalBlowCounted == true);
        if (headshotFinalBlow) scope!.HeadshotFinalBlowCounted = true;
        var targetIdentity = ReadCharacterIdentity(target, "target");
        var attackerIdentity = ReadCharacterIdentity(source, "attacker");
        var family = ReadFamily(health);
        var cause = ResolveCause(state.DamageInfo, scope, ownership);
        var weaponTypeId = scope?.WeaponTypeId >= 0 ? scope.WeaponTypeId : state.DamageInfo.fromWeaponItemID;
        var weaponName = !string.IsNullOrWhiteSpace(scope?.WeaponDisplayName)
            ? scope!.WeaponDisplayName
            : ReadItemDisplayName(weaponTypeId, "weapon");
        var ammunitionId = scope?.AmmunitionTypeId ?? -1;
        Emit(NewEvent(scope, ownership) with
        {
            AttackKind = targetIsMain
                ? CombatAttackKind.Unknown
                : scope?.IsRanged == true ? CombatAttackKind.Ranged
                : scope?.IsMelee == true ? CombatAttackKind.Melee
                : scope?.IsEffect == true || state.DamageInfo.isFromBuffOrEffect ? CombatAttackKind.Effect
                : ownership == CombatOwnership.Environmental ? CombatAttackKind.Environmental
                : CombatAttackKind.Unknown,
            CauseKind = cause.Kind,
            CauseId = cause.Id,
            CauseDisplayName = cause.Name,
            AttackerId = attackerIdentity.Id,
            AttackerDisplayName = attackerIdentity.Name,
            TargetId = targetIdentity.Id,
            TargetDisplayName = targetIdentity.Name,
            TargetIsEnemy = enemyTarget,
            TargetFamilyId = family.Id,
            TargetFamilyDisplayName = family.Name,
            WeaponId = weaponTypeId <= 0 ? "duckov:weapon:unknown" : $"duckov:weapon:{weaponTypeId.ToString(CultureInfo.InvariantCulture)}",
            WeaponDisplayName = weaponName,
            AmmunitionId = ammunitionId < 0 ? "duckov:ammo:unknown" : $"duckov:ammo:{ammunitionId.ToString(CultureInfo.InvariantCulture)}",
            AmmunitionDisplayName = ammunitionId < 0 ? "Unknown ammunition" : scope!.AmmunitionDisplayName,
            ActualDamageToTarget = targetIsMain ? 0 : actualDamage,
            ActualDamageDealt = playerDamage ? actualDamage : 0,
            ActualDamageReceived = targetIsMain ? actualDamage : 0,
            // Compatible accuracy is committed only when this projectile
            // completes; the shared scope remembers that at least one enemy
            // suffered actual HP loss.
            RangedHits = 0,
            MeleeHits = meleeHit ? 1 : 0,
            EnemiesKilled = fatal && enemyTarget ? 1 : 0,
            Headshots = headshot ? 1 : 0,
            HeadshotFinalBlows = headshotFinalBlow ? 1 : 0,
            IsFinalBlow = fatal,
            IsDamageOverTime = scope?.IsDamageOverTime == true
        }, allowTerminalPause: targetIsMain && fatal);
    }

    public void RecordPlayerDeath(DamageInfo info)
    {
        if (!CanObserveHealth) return;
        var scope = CombatHarmonyBridge.CurrentScope;
        var source = scope?.PhysicalSource != null ? scope.PhysicalSource : info.fromCharacter;
        var ownership = ResolveOwnership(source);
        var attacker = ReadCharacterIdentity(source, "attacker");
        var cause = ResolveCause(info, scope, ownership);
        Emit(NewEvent(scope, ownership) with
        {
            CauseKind = cause.Kind,
            CauseId = cause.Id,
            CauseDisplayName = cause.Name,
            AttackerId = attacker.Id,
            AttackerDisplayName = attacker.Name,
            PlayerDeaths = 1,
            IsFinalBlow = true,
            IsDamageOverTime = scope?.IsDamageOverTime == true
        }, allowTerminalPause: true);
    }

    public bool TryCleanup()
    {
        if (!disposed)
        {
            disposed = true;
            DetachRuntimeHooks();
            QueueCleanup();
        }
        return TryCompleteCleanup();
    }

    public void Dispose() => TryCleanup();

    private void SynchronizeMainCharacter()
    {
        CharacterMainControl? observed = null;
        try { observed = CharacterMainControl.Main; } catch { }
        if (ReferenceEquals(observed, subscribedMainCharacter)) return;
        if (subscribedMainCharacter?.attackAction != null) subscribedMainCharacter.attackAction.OnAttack -= OnMeleeAttack;
        subscribedMainCharacter = observed;
        if (subscribedMainCharacter?.attackAction != null) subscribedMainCharacter.attackAction.OnAttack += OnMeleeAttack;
    }

    private void OnMeleeAttack()
    {
        var character = subscribedMainCharacter;
        var weapon = character?.GetMeleeWeapon();
        if (character == null || weapon == null || !character.IsMainCharacter) return;
        var item = weapon.Item;
        Emit(NewEvent(null, CombatOwnership.Player) with
        {
            AttackKind = CombatAttackKind.Melee,
            WeaponId = item == null ? "duckov:weapon:unknown" : $"duckov:weapon:{item.TypeID.ToString(CultureInfo.InvariantCulture)}",
            WeaponDisplayName = item == null ? "Unknown weapon" : NonEmpty(item.DisplayName, $"Unknown weapon {item.TypeID}"),
            MeleeSwings = 1
        });
    }

    private CombatRecorded NewEvent(CombatNativeScope? scope, CombatOwnership ownership)
    {
        var runId = runIdProvider();
        var mapId = mapIdProvider();
        return new CombatRecorded
        {
            EventId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            SaveGenerationId = saveGenerationIdProvider(),
            RunId = runId ?? string.Empty,
            MapId = mapId ?? MapIdentity.UnknownId,
            GameplayContext = NativeRaidContext.GetGameplayContext(),
            IntegrityTags = NativeIntegrityProbe.Read(),
            GameVersion = Application.version ?? string.Empty,
            GameBuild = SupportedGameBuild,
            AdapterVersion = AdapterVersion,
            Ownership = ownership,
            ProjectileId = scope?.ProjectileId,
            Capabilities = MetricCapabilities
        };
    }

    private bool Emit(CombatRecorded value, bool allowTerminalPause = false)
    {
        if (allowTerminalPause
            && value.GameplayContext == GameplayContext.Paused
            && NativeRaidContext.IsRaidMap())
        {
            // Duckov may enter its death pause inside Health.Hurt before the
            // richer death/postfix callbacks. The still-active raid/run proves
            // this terminal observation belongs to raid gameplay.
            value.GameplayContext = GameplayContext.Raid;
        }
        if (!CanObserveHealth || string.IsNullOrWhiteSpace(value.RunId)
            || string.IsNullOrWhiteSpace(value.SaveGenerationId)
            || value.GameplayContext != GameplayContext.Raid
            || SceneLoader.IsSceneLoading || LevelManager.LevelInitializing
            || (MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading)
            || (GameManager.Paused && !allowTerminalPause)) return false;
        return combatHandler(value);
    }

    private static CombatOwnership ResolveOwnership(CharacterMainControl? source)
    {
        if (source == null) return CombatOwnership.Environmental;
        var main = CharacterMainControl.Main;
        if (source.IsMainCharacter && ReferenceEquals(source, main)) return CombatOwnership.Player;
        try
        {
            if (ReferenceEquals(source, LevelManager.Instance?.PetCharacter)
                || ReferenceEquals(source.GetComponent<PetAI>()?.master, main)) return CombatOwnership.PetCompanion;
        }
        catch { }
        return CombatOwnership.Unknown;
    }

    private static (string Id, string Name) ReadCharacterIdentity(CharacterMainControl? character, string role)
    {
        if (character == null) return ($"duckov:{role}:environment", "Environment");
        if (character.IsMainCharacter) return ($"duckov:{role}:main-duck", "Main duck");
        var preset = character.characterPreset;
        if (preset != null)
        {
            var stable = NonEmpty(preset.nameKey, preset.name);
            var name = NonEmpty(preset.DisplayName, NonEmpty(preset.name, stable));
            if (!string.IsNullOrWhiteSpace(stable))
                return ($"duckov:{role}:preset:{CombatObservationPolicy.CreateStableIdentityToken(stable)}", name);
        }
        var fallback = NonEmpty(character.name, "Unknown character");
        return ($"duckov:{role}:fallback:{CombatObservationPolicy.CreateStableIdentityToken(fallback)}", fallback);
    }

    private static (string Id, string Name) ReadFamily(Health health) => health.isZombie
        ? ("duckov:family:zombie", "Zombie")
        : ("duckov:family:unknown", "Unknown family");

    private static (CombatCauseKind Kind, string Id, string Name) ResolveCause(
        DamageInfo info, CombatNativeScope? scope, CombatOwnership ownership)
    {
        if (scope?.IsDamageOverTime == true) return (CombatCauseKind.DamageOverTime, "duckov:cause:damage-over-time", "Damage over time");
        if (info.isFromBuffOrEffect || scope?.IsEffect == true) return (CombatCauseKind.Effect, "duckov:cause:effect", "Effect");
        if (info.isExplosion) return (CombatCauseKind.Explosion, "duckov:cause:explosion", "Explosion");
        if (info.damageType == DamageTypes.realDamage) return (CombatCauseKind.RealDamage, "duckov:cause:real-damage", "Real damage");
        if (ownership == CombatOwnership.Environmental) return (CombatCauseKind.Environmental, "duckov:cause:environmental", "Environmental");
        return (CombatCauseKind.Direct, "duckov:cause:direct", "Direct");
    }

    private static string ReadItemDisplayName(int typeId, string kind)
    {
        if (typeId < 0) return $"Unknown {kind}";
        try
        {
            var metadata = ItemAssetsCollection.GetMetaData(typeId);
            return NonEmpty(metadata.DisplayName, $"Unknown {kind} {typeId.ToString(CultureInfo.InvariantCulture)}");
        }
        catch { return $"Unknown {kind} {typeId.ToString(CultureInfo.InvariantCulture)}"; }
    }

    private static string NonEmpty(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void SetUnavailable(string detail)
    {
        metricCapabilities = CombatNativeContractPolicy.CreateUnavailableCapabilities(detail);
        PublishCapabilities();
        diagnosticHandler(detail);
    }

    private void PublishCapabilities() =>
        capabilityHandler(CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));

    private void DetachRuntimeHooks()
    {
        if (subscribedMainCharacter?.attackAction != null) subscribedMainCharacter.attackAction.OnAttack -= OnMeleeAttack;
        subscribedMainCharacter = null;
        projectiles.Clear();
        projectileOrder.Clear();
        CombatHarmonyBridge.ClearScopes();
        CombatHarmonyBridge.Detach(this);
    }

    private void QueueCleanup()
    {
        cleanupPending = patcherLease.HasValue;
        nextCleanupAttemptUtc = DateTime.MinValue;
        if (!cleanupPending) patchRegistrations = Array.Empty<PatchRegistration>();
    }

    private bool TryCompleteCleanup()
    {
        if (!cleanupPending) return true;
        if (!patcherLease.TryCleanup(out var detail))
        {
            nextCleanupAttemptUtc = DateTime.UtcNow.AddSeconds(1);
            diagnosticHandler($"Combat patch cleanup remains pending and will be retried: {detail}");
            return false;
        }
        cleanupPending = false;
        patchRegistrations = Array.Empty<PatchRegistration>();
        return true;
    }

    private static bool TryResolveContracts(out ResolvedMethods methods, out string failure)
    {
        methods = new ResolvedMethods
        {
            HealthHurt = Exact(typeof(Health), "Hurt", BindingFlags.Instance | BindingFlags.Public, typeof(bool), typeof(DamageInfo)),
            ProjectileInit = Exact(typeof(Projectile), "Init", BindingFlags.Instance | BindingFlags.Public, typeof(void), typeof(ProjectileContext)),
            ProjectileUpdate = Exact(typeof(Projectile), "Update", BindingFlags.Instance | BindingFlags.NonPublic, typeof(void)),
            ProjectileRelease = Exact(typeof(Projectile), "Release", BindingFlags.Instance | BindingFlags.NonPublic, typeof(void)),
            MeleeCheck = Exact(typeof(ItemAgent_MeleeWeapon), "CheckCollidersInRange", BindingFlags.Instance | BindingFlags.NonPublic, typeof(int), typeof(bool)),
            EffectTrigger = Exact(typeof(Effect), "Trigger", BindingFlags.Instance | BindingFlags.NonPublic, typeof(void), typeof(EffectTriggerEventContext))
        };
        if (methods.All.Any(x => x == null))
        {
            failure = "Exact Health.Hurt, Projectile Init/Update/Release, melee collision, or effect trigger contract is missing or changed.";
            return false;
        }
        failure = string.Empty;
        return true;
    }

    private static MethodInfo Exact(Type type, string name, BindingFlags flags, Type returnType, params Type[] parameters) =>
        type.GetMethods(flags).SingleOrDefault(x => x.Name == name && x.ReturnType == returnType
            && x.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters))!;

    private static PatchRegistration[] CreateRegistrations(ResolvedMethods m) =>
    [
        new(m.HealthHurt, [new("Prefixes", CombatHarmonyCallbacks.HealthPrefixMethod), new("Postfixes", CombatHarmonyCallbacks.HealthPostfixMethod)]),
        new(m.ProjectileInit, [new("Postfixes", CombatHarmonyCallbacks.ProjectileInitPostfixMethod)]),
        new(m.ProjectileUpdate, [new("Prefixes", CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod), new("Finalizers", CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod)]),
        new(m.ProjectileRelease, [new("Prefixes", CombatHarmonyCallbacks.ProjectileReleasePrefixMethod)]),
        new(m.MeleeCheck, [new("Prefixes", CombatHarmonyCallbacks.MeleePrefixMethod), new("Finalizers", CombatHarmonyCallbacks.MeleeFinalizerMethod)]),
        new(m.EffectTrigger, [new("Prefixes", CombatHarmonyCallbacks.EffectPrefixMethod), new("Finalizers", CombatHarmonyCallbacks.EffectFinalizerMethod)])
    ];

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation ? invocation.InnerException : exception;

    private sealed class ProjectileSnapshot
    {
        public string ProjectileId { get; set; } = string.Empty;
        public CharacterMainControl? PhysicalSource { get; set; }
        public bool IsExactPlayer { get; set; }
        public bool HeadTargeted { get; set; }
        public int WeaponTypeId { get; set; }
        public string WeaponDisplayName { get; set; } = string.Empty;
        public int AmmunitionTypeId { get; set; }
        public string AmmunitionDisplayName { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public CombatNativeScope Scope => scope ??= new CombatNativeScope
        {
            ProjectileId = ProjectileId,
            PhysicalSource = PhysicalSource,
            IsRanged = true,
            HeadTargeted = HeadTargeted,
            WeaponTypeId = WeaponTypeId,
            WeaponDisplayName = WeaponDisplayName,
            AmmunitionTypeId = AmmunitionTypeId,
            AmmunitionDisplayName = AmmunitionDisplayName
        };
        private CombatNativeScope? scope;
    }

    private sealed class ResolvedMethods
    {
        public MethodInfo HealthHurt { get; set; } = null!;
        public MethodInfo ProjectileInit { get; set; } = null!;
        public MethodInfo ProjectileUpdate { get; set; } = null!;
        public MethodInfo ProjectileRelease { get; set; } = null!;
        public MethodInfo MeleeCheck { get; set; } = null!;
        public MethodInfo EffectTrigger { get; set; } = null!;
        public MethodInfo[] All => [HealthHurt, ProjectileInit, ProjectileUpdate, ProjectileRelease, MeleeCheck, EffectTrigger];
    }

    private sealed class PatchRegistration
    {
        public PatchRegistration(MethodInfo original, HarmonyPatchExpectation[] expected)
        { Original = original; Expected = expected; }
        public MethodInfo Original { get; }
        public HarmonyPatchExpectation[] Expected { get; }
    }
}
