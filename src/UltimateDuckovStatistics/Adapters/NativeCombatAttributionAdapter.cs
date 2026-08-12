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
    private readonly Func<EquipmentEventAssociation> equipmentAssociationProvider;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly RetryableHarmonyPatcherLease patcherLease = new();
    private readonly Dictionary<int, ProjectileSnapshot> projectiles = new();
    private readonly Queue<(int RuntimeId, string ProjectileId)> projectileOrder = new();
    private PatchRegistration[] patchRegistrations = Array.Empty<PatchRegistration>();
    private CombatHookSupport hookSupport = new();
    private CombatMetricCapabilities metricCapabilities = new();
    private CharacterMainControl? subscribedMainCharacter;
    private bool cleanupPending;
    private DateTime nextCleanupAttemptUtc;
    private DateTime nextConflictCheckUtc;
    private bool retryInitialization;
    private DateTime nextInitializationAttemptUtc;
    private bool initialized;
    private string projectileGenerationId = string.Empty;
    private string projectileRunId = string.Empty;
    private string projectileMapId = string.Empty;
    private bool disposed;

    public NativeCombatAttributionAdapter(
        Func<string> saveGenerationIdProvider,
        Func<string?> runIdProvider,
        Func<string?> mapIdProvider,
        Func<CombatRecorded, bool> combatHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler,
        Func<EquipmentEventAssociation>? equipmentAssociationProvider = null)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.runIdProvider = runIdProvider ?? throw new ArgumentNullException(nameof(runIdProvider));
        this.mapIdProvider = mapIdProvider ?? throw new ArgumentNullException(nameof(mapIdProvider));
        this.combatHandler = combatHandler ?? throw new ArgumentNullException(nameof(combatHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        this.equipmentAssociationProvider = equipmentAssociationProvider ?? (() => new EquipmentEventAssociation());
        SetUnavailable("Combat attribution has not been initialized.");
    }

    public CombatMetricCapabilities MetricCapabilities => CombatStatisticsReducer.CloneCapabilities(metricCapabilities);

    public bool CanObserveHealth => IsActive && hookSupport.HealthHurt;

    public EquipmentEventAssociation CaptureEquipmentAssociation() => equipmentAssociationProvider();

    private bool IsActive => !disposed && !cleanupPending && initialized;

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeCombatAttributionAdapter));
        if (initialized && !retryInitialization)
            return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        if (!string.Equals(Application.version, SupportedGameVersion, StringComparison.Ordinal))
        {
            initialized = true;
            SetUnavailable($"Installed Duckov version '{Application.version}' does not match verified combat contract '{SupportedGameVersion}'.");
            return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        }

        var methods = ResolveContracts();
        var support = methods.CreateHookSupport();

        if (!ReflectiveHarmonyPatcher.TryCreate(HarmonyId, out var created, out var harmonyDetail) || created == null)
        {
            retryInitialization = ReflectiveHarmonyPatcher.HasPendingCleanup
                                  || !ReflectiveHarmonyPatcher.IsHarmonyLoaded;
            nextInitializationAttemptUtc = DateTime.UtcNow.AddSeconds(1);
            support.DisableHarmonyHooks();
            ActivateCapabilities(support, harmonyDetail);
            return CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        }

        patcherLease.Attach(created);
        try
        {
            var registrations = CreateRegistrations(methods);
            foreach (var registration in registrations)
            {
                if (!created.IsPatchSetTrusted(registration.Original, Array.Empty<HarmonyPatchExpectation>(), out var detail))
                {
                    support.Disable(registration.Hook);
                    diagnosticHandler(
                        $"{registration.Original.DeclaringType?.Name}.{registration.Original.Name} is unavailable for its dependent combat capabilities because its Harmony patch set is unsafe: {detail}");
                }
            }

            patchRegistrations = registrations.Where(x => support.IsEnabled(x.Hook)).ToArray();
            if (patchRegistrations.Length > 0) CombatHarmonyBridge.Attach(this);
            foreach (var registration in patchRegistrations) ApplyPatch(created, registration);
            foreach (var registration in patchRegistrations)
            {
                if (!created.IsPatchSetTrusted(registration.Original, registration.Expected, out var detail))
                {
                    throw new InvalidOperationException($"Installed combat patch set validation failed: {detail}");
                }
            }

            hookSupport = support;
            metricCapabilities = CombatNativeContractPolicy.CreateCapabilities(hookSupport);
            initialized = true;
            retryInitialization = false;
            PublishCapabilities();
            nextConflictCheckUtc = DateTime.UtcNow.AddSeconds(2);
            SynchronizeMainCharacter();
            SynchronizeProjectileContext();
            diagnosticHandler(
                $"Combat attribution active with HarmonyLib {created.Version}; {patchRegistrations.Length}/6 Harmony hooks and independent public melee/death callbacks are available.");
        }
        catch (Exception exception)
        {
            DetachRuntimeHooks();
            initialized = true;
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

        if (!IsActive) return;
        SynchronizeMainCharacter();
        SynchronizeProjectileContext();
        if (DateTime.UtcNow < nextConflictCheckUtc) return;
        nextConflictCheckUtc = DateTime.UtcNow.AddSeconds(2);
        var patcher = patcherLease.Value;
        foreach (var registration in patchRegistrations.Where(x => !x.Disabled))
        {
            var detail = patcher == null ? "The combat Harmony patcher is unavailable." : string.Empty;
            if (patcher == null || !patcher.IsPatchSetTrusted(registration.Original, registration.Expected, out detail))
            {
                registration.Disabled = true;
                hookSupport.Disable(registration.Hook);
                metricCapabilities = CombatNativeContractPolicy.CreateCapabilities(hookSupport);
                if (registration.Hook is CombatHook.ProjectileInit or CombatHook.ProjectileUpdate or CombatHook.ProjectileRelease)
                    ClearProjectileCorrelations();
                PublishCapabilities();
                diagnosticHandler(
                    $"Disabled only the combat capabilities dependent on {registration.Original.DeclaringType?.Name}.{registration.Original.Name} after patch-set drift: {detail}");
            }
        }
    }

    public void CaptureProjectile(Projectile projectile, ProjectileContext context)
    {
        if (!IsActive || !hookSupport.ProjectileInit || projectile == null) return;
        SynchronizeProjectileContext();
        var generationId = saveGenerationIdProvider();
        var runId = runIdProvider() ?? string.Empty;
        var mapId = mapIdProvider() ?? MapIdentity.UnknownId;
        if (string.IsNullOrWhiteSpace(generationId) || string.IsNullOrWhiteSpace(runId)
            || NativeRaidContext.GetGameplayContext() != GameplayContext.Raid) return;
        var physicalSource = context.realFromCharacter != null ? context.realFromCharacter : context.fromCharacter;
        var isExactPlayer = physicalSource != null && physicalSource.IsMainCharacter
                            && ReferenceEquals(physicalSource, CharacterMainControl.Main);
        var ammunitionId = context.fromGunItemSetting == null ? -1 : context.fromGunItemSetting.TargetBulletID;
        var snapshot = new ProjectileSnapshot
        {
            ProjectileId = Guid.NewGuid().ToString("N"),
            SaveGenerationId = generationId,
            RunId = runId,
            MapId = mapId,
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
                : NonEmpty(context.fromGunItemSetting.CurrentBulletName, $"Unknown ammunition {ammunitionId}"),
            EquipmentAssociation = equipmentAssociationProvider()
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
        if (!IsActive || !hookSupport.ProjectileUpdate || projectile == null) return null;
        SynchronizeProjectileContext();
        if (!projectiles.TryGetValue(projectile.GetInstanceID(), out var value)
            || !MatchesCurrentContext(value)) return null;
        return value.Scope;
    }

    public void CompleteProjectile(Projectile projectile)
    {
        if (!IsActive || !hookSupport.ProjectileRelease || projectile == null
            || !projectiles.TryGetValue(projectile.GetInstanceID(), out var value)
            || value.Completed) return;
        SynchronizeProjectileContext();
        if (!MatchesCurrentContext(value))
        {
            projectiles.Remove(projectile.GetInstanceID());
            return;
        }
        value.Completed = true;
        if (value.IsExactPlayer
            && metricCapabilities.Accuracy.State == AdapterCapabilityState.Supported)
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
        if (!CanObserveHealth || !hookSupport.MeleeCheck || weapon == null || weapon.Holder == null
            || !weapon.Holder.IsMainCharacter || !ReferenceEquals(weapon.Holder, CharacterMainControl.Main)) return null;
        var item = weapon.Item;
        return new CombatNativeScope
        {
            IsMelee = true,
            PhysicalSource = weapon.Holder,
            WeaponTypeId = item == null ? -1 : item.TypeID,
            WeaponDisplayName = item == null ? string.Empty : NonEmpty(item.DisplayName, $"Unknown weapon {item.TypeID}"),
            EquipmentAssociation = equipmentAssociationProvider()
        };
    }

    public CombatNativeScope? CreateEffectScope(EffectTriggerEventContext context)
    {
        if (!IsActive || !hookSupport.EffectTrigger || context.source == null) return null;
        return new CombatNativeScope
        {
            IsEffect = true,
            IsDamageOverTime = context.source is TickTrigger || context.source is UpdateTrigger,
            EquipmentAssociation = equipmentAssociationProvider()
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
        if (!CombatObservationPolicy.ShouldRecordHealthTransition(targetIsMain, enemyTarget, ownership)) return;
        var playerDamage = ownership == CombatOwnership.Player && enemyTarget;
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
        Emit(NewEvent(scope, ownership, state.EquipmentAssociation) with
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
            ActualDamageToTarget = enemyTarget ? actualDamage : 0,
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
        if (!IsActive || metricCapabilities.PlayerDeaths.State != AdapterCapabilityState.Supported) return;
        var scope = CombatHarmonyBridge.CurrentScope;
        var source = scope?.PhysicalSource != null ? scope.PhysicalSource : info.fromCharacter;
        var ownership = ResolveOwnership(source);
        var attacker = ReadCharacterIdentity(source, "attacker");
        var cause = ResolveCause(info, scope, ownership);
        var value = NewEvent(scope, ownership) with
        {
            CauseKind = cause.Kind,
            CauseId = cause.Id,
            CauseDisplayName = cause.Name,
            AttackerId = attacker.Id,
            AttackerDisplayName = attacker.Name,
            PlayerDeaths = 1,
            IsFinalBlow = true,
            IsDamageOverTime = scope?.IsDamageOverTime == true
        };
        if (scope?.WeaponTypeId is not > 0 && info.fromWeaponItemID > 0)
        {
            CombatObservationPolicy.ApplyOutcomeIdentity(
                value,
                value.ProjectileId,
                info.fromWeaponItemID,
                ReadItemDisplayName(info.fromWeaponItemID, "weapon"),
                scope?.AmmunitionTypeId ?? -1,
                scope?.AmmunitionDisplayName);
        }
        Emit(value, allowTerminalPause: true);
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

    private void SynchronizeProjectileContext()
    {
        var generationId = saveGenerationIdProvider();
        var runId = runIdProvider() ?? string.Empty;
        var mapId = mapIdProvider() ?? MapIdentity.UnknownId;
        if (string.Equals(projectileGenerationId, generationId, StringComparison.Ordinal)
            && string.Equals(projectileRunId, runId, StringComparison.Ordinal)
            && string.Equals(projectileMapId, mapId, StringComparison.Ordinal)) return;
        ClearProjectileCorrelations();
        projectileGenerationId = generationId;
        projectileRunId = runId;
        projectileMapId = mapId;
    }

    private bool MatchesCurrentContext(ProjectileSnapshot value) =>
        CombatObservationPolicy.MatchesOriginatingContext(
            value.SaveGenerationId,
            value.RunId,
            value.MapId,
            saveGenerationIdProvider(),
            runIdProvider() ?? string.Empty,
            mapIdProvider() ?? MapIdentity.UnknownId);

    private void ClearProjectileCorrelations()
    {
        projectiles.Clear();
        projectileOrder.Clear();
        CombatHarmonyBridge.ClearScopes();
    }

    private void OnMeleeAttack()
    {
        if (!IsActive || metricCapabilities.MeleeSwings.State != AdapterCapabilityState.Supported) return;
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

    private CombatRecorded NewEvent(
        CombatNativeScope? scope,
        CombatOwnership ownership,
        EquipmentEventAssociation? equipmentAssociation = null)
    {
        var runId = runIdProvider();
        var mapId = mapIdProvider();
        var value = new CombatRecorded
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
            Capabilities = MetricCapabilities,
            EquipmentAssociation = equipmentAssociation ?? scope?.EquipmentAssociation ?? equipmentAssociationProvider()
        };
        CombatObservationPolicy.ApplyOutcomeIdentity(
            value,
            scope?.ProjectileId,
            scope?.WeaponTypeId ?? -1,
            scope?.WeaponDisplayName,
            scope?.AmmunitionTypeId ?? -1,
            scope?.AmmunitionDisplayName);
        return value;
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
        if (!IsActive || string.IsNullOrWhiteSpace(value.RunId)
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

    private void ActivateCapabilities(CombatHookSupport support, string detail)
    {
        hookSupport = support;
        metricCapabilities = CombatNativeContractPolicy.CreateCapabilities(hookSupport);
        initialized = true;
        PublishCapabilities();
        SynchronizeMainCharacter();
        diagnosticHandler(detail);
    }

    private void PublishCapabilities() =>
        capabilityHandler(CombatNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));

    private void DetachRuntimeHooks()
    {
        if (subscribedMainCharacter?.attackAction != null) subscribedMainCharacter.attackAction.OnAttack -= OnMeleeAttack;
        subscribedMainCharacter = null;
        ClearProjectileCorrelations();
        projectileGenerationId = string.Empty;
        projectileRunId = string.Empty;
        projectileMapId = string.Empty;
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

    private static ResolvedMethods ResolveContracts()
    {
        return new ResolvedMethods
        {
            HealthHurt = Exact(typeof(Health), "Hurt", BindingFlags.Instance | BindingFlags.Public, typeof(bool), typeof(DamageInfo)),
            ProjectileInit = Exact(typeof(Projectile), "Init", BindingFlags.Instance | BindingFlags.Public, typeof(void), typeof(ProjectileContext)),
            ProjectileUpdate = Exact(typeof(Projectile), "Update", BindingFlags.Instance | BindingFlags.NonPublic, typeof(void)),
            ProjectileRelease = Exact(typeof(Projectile), "Release", BindingFlags.Instance | BindingFlags.NonPublic, typeof(void)),
            MeleeCheck = Exact(typeof(ItemAgent_MeleeWeapon), "CheckCollidersInRange", BindingFlags.Instance | BindingFlags.NonPublic, typeof(int), typeof(bool)),
            EffectTrigger = Exact(typeof(Effect), "Trigger", BindingFlags.Instance | BindingFlags.NonPublic, typeof(void), typeof(EffectTriggerEventContext))
        };
    }

    private static MethodInfo? Exact(Type type, string name, BindingFlags flags, Type returnType, params Type[] parameters) =>
        type.GetMethods(flags).SingleOrDefault(x => x.Name == name && x.ReturnType == returnType
            && x.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));

    private static PatchRegistration[] CreateRegistrations(ResolvedMethods m) =>
        new (CombatHook Hook, MethodInfo? Method, HarmonyPatchExpectation[] Expected)[]
        {
            (CombatHook.HealthHurt, m.HealthHurt, [new("Prefixes", CombatHarmonyCallbacks.HealthPrefixMethod), new("Postfixes", CombatHarmonyCallbacks.HealthPostfixMethod)]),
            (CombatHook.ProjectileInit, m.ProjectileInit, [new("Postfixes", CombatHarmonyCallbacks.ProjectileInitPostfixMethod)]),
            (CombatHook.ProjectileUpdate, m.ProjectileUpdate, [new("Prefixes", CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod), new("Finalizers", CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod)]),
            (CombatHook.ProjectileRelease, m.ProjectileRelease, [new("Prefixes", CombatHarmonyCallbacks.ProjectileReleasePrefixMethod)]),
            (CombatHook.MeleeCheck, m.MeleeCheck, [new("Prefixes", CombatHarmonyCallbacks.MeleePrefixMethod), new("Finalizers", CombatHarmonyCallbacks.MeleeFinalizerMethod)]),
            (CombatHook.EffectTrigger, m.EffectTrigger, [new("Prefixes", CombatHarmonyCallbacks.EffectPrefixMethod), new("Finalizers", CombatHarmonyCallbacks.EffectFinalizerMethod)])
        }
        .Where(x => x.Method != null)
        .Select(x => new PatchRegistration(x.Hook, x.Method!, x.Expected))
        .ToArray();

    private static void ApplyPatch(ReflectiveHarmonyPatcher patcher, PatchRegistration registration)
    {
        switch (registration.Hook)
        {
            case CombatHook.HealthHurt:
                patcher.Patch(registration.Original, CombatHarmonyCallbacks.HealthPrefixMethod, CombatHarmonyCallbacks.HealthPostfixMethod);
                break;
            case CombatHook.ProjectileInit:
                patcher.Patch(registration.Original, postfix: CombatHarmonyCallbacks.ProjectileInitPostfixMethod);
                break;
            case CombatHook.ProjectileUpdate:
                patcher.Patch(registration.Original, CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod, finalizer: CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod);
                break;
            case CombatHook.ProjectileRelease:
                patcher.Patch(registration.Original, CombatHarmonyCallbacks.ProjectileReleasePrefixMethod);
                break;
            case CombatHook.MeleeCheck:
                patcher.Patch(registration.Original, CombatHarmonyCallbacks.MeleePrefixMethod, finalizer: CombatHarmonyCallbacks.MeleeFinalizerMethod);
                break;
            case CombatHook.EffectTrigger:
                patcher.Patch(registration.Original, CombatHarmonyCallbacks.EffectPrefixMethod, finalizer: CombatHarmonyCallbacks.EffectFinalizerMethod);
                break;
        }
    }

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation ? invocation.InnerException : exception;

    private sealed class ProjectileSnapshot
    {
        public string ProjectileId { get; set; } = string.Empty;
        public string SaveGenerationId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string MapId { get; set; } = string.Empty;
        public CharacterMainControl? PhysicalSource { get; set; }
        public bool IsExactPlayer { get; set; }
        public bool HeadTargeted { get; set; }
        public int WeaponTypeId { get; set; }
        public string WeaponDisplayName { get; set; } = string.Empty;
        public int AmmunitionTypeId { get; set; }
        public string AmmunitionDisplayName { get; set; } = string.Empty;
        public EquipmentEventAssociation EquipmentAssociation { get; set; } = new();
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
            AmmunitionDisplayName = AmmunitionDisplayName,
            EquipmentAssociation = EquipmentAssociation
        };
        private CombatNativeScope? scope;
    }

    private sealed class ResolvedMethods
    {
        public MethodInfo? HealthHurt { get; set; }
        public MethodInfo? ProjectileInit { get; set; }
        public MethodInfo? ProjectileUpdate { get; set; }
        public MethodInfo? ProjectileRelease { get; set; }
        public MethodInfo? MeleeCheck { get; set; }
        public MethodInfo? EffectTrigger { get; set; }

        public CombatHookSupport CreateHookSupport() => new()
        {
            HealthHurt = HealthHurt != null,
            ProjectileInit = ProjectileInit != null,
            ProjectileUpdate = ProjectileUpdate != null,
            ProjectileRelease = ProjectileRelease != null,
            MeleeCheck = MeleeCheck != null,
            EffectTrigger = EffectTrigger != null,
            PublicMeleeSwing = true,
            PublicPlayerDeath = true
        };
    }

    private sealed class PatchRegistration
    {
        public PatchRegistration(CombatHook hook, MethodInfo original, HarmonyPatchExpectation[] expected)
        { Hook = hook; Original = original; Expected = expected; }
        public CombatHook Hook { get; }
        public MethodInfo Original { get; }
        public HarmonyPatchExpectation[] Expected { get; }
        public bool Disabled { get; set; }
    }

    internal enum CombatHook
    {
        HealthHurt,
        ProjectileInit,
        ProjectileUpdate,
        ProjectileRelease,
        MeleeCheck,
        EffectTrigger
    }
}

internal static class CombatHookSupportExtensions
{
    public static bool IsEnabled(this CombatHookSupport support, NativeCombatAttributionAdapter.CombatHook hook) => hook switch
    {
        NativeCombatAttributionAdapter.CombatHook.HealthHurt => support.HealthHurt,
        NativeCombatAttributionAdapter.CombatHook.ProjectileInit => support.ProjectileInit,
        NativeCombatAttributionAdapter.CombatHook.ProjectileUpdate => support.ProjectileUpdate,
        NativeCombatAttributionAdapter.CombatHook.ProjectileRelease => support.ProjectileRelease,
        NativeCombatAttributionAdapter.CombatHook.MeleeCheck => support.MeleeCheck,
        NativeCombatAttributionAdapter.CombatHook.EffectTrigger => support.EffectTrigger,
        _ => false
    };

    public static void Disable(this CombatHookSupport support, NativeCombatAttributionAdapter.CombatHook hook)
    {
        switch (hook)
        {
            case NativeCombatAttributionAdapter.CombatHook.HealthHurt: support.HealthHurt = false; break;
            case NativeCombatAttributionAdapter.CombatHook.ProjectileInit: support.ProjectileInit = false; break;
            case NativeCombatAttributionAdapter.CombatHook.ProjectileUpdate: support.ProjectileUpdate = false; break;
            case NativeCombatAttributionAdapter.CombatHook.ProjectileRelease: support.ProjectileRelease = false; break;
            case NativeCombatAttributionAdapter.CombatHook.MeleeCheck: support.MeleeCheck = false; break;
            case NativeCombatAttributionAdapter.CombatHook.EffectTrigger: support.EffectTrigger = false; break;
        }
    }

    public static void DisableHarmonyHooks(this CombatHookSupport support)
    {
        support.HealthHurt = false;
        support.ProjectileInit = false;
        support.ProjectileUpdate = false;
        support.ProjectileRelease = false;
        support.MeleeCheck = false;
        support.EffectTrigger = false;
    }
}
