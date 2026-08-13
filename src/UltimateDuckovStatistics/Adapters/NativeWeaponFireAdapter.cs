using System.Globalization;
using Duckov.Scenes;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeWeaponFireAdapter : IDisposable, IRetryableCleanup
{
    internal const string AdapterVersion = "native-weapon-fire/2.3.30+public-event-v2";
    private const string SupportedGameVersion = "2.3.30";
    private const string SupportedGameBuild = "24013657";
    private readonly Func<string> saveGenerationIdProvider;
    private readonly Func<string?> runIdProvider;
    private readonly Func<string?> mapIdProvider;
    private readonly Func<string?> segmentIdProvider;
    private readonly Func<ShotRecorded, bool> shotHandler;
    private readonly Func<EquipmentEventAssociation> equipmentAssociationProvider;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private readonly WeaponFireEventIdSource eventIds = new(() => Guid.NewGuid().ToString("N"));
    private IReadOnlyList<CapabilityRecord> capabilities = DisabledCapabilities("Weapon tracking has not been initialized.");
    private WeaponMetricCapabilities metricCapabilities = new();

    public NativeWeaponFireAdapter(
        Func<string> saveGenerationIdProvider,
        Func<string?> runIdProvider,
        Func<string?> mapIdProvider,
        Func<ShotRecorded, bool> shotHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler,
        Func<EquipmentEventAssociation>? equipmentAssociationProvider = null,
        Func<string?>? segmentIdProvider = null)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider
            ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.runIdProvider = runIdProvider ?? throw new ArgumentNullException(nameof(runIdProvider));
        this.mapIdProvider = mapIdProvider ?? throw new ArgumentNullException(nameof(mapIdProvider));
        this.shotHandler = shotHandler ?? throw new ArgumentNullException(nameof(shotHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        this.equipmentAssociationProvider = equipmentAssociationProvider ?? (() => new EquipmentEventAssociation());
        this.segmentIdProvider = segmentIdProvider ?? (() => null);
    }

    public WeaponMetricCapabilities MetricCapabilities =>
        WeaponStatisticsReducer.CloneCapabilities(metricCapabilities);

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (callbackLifetime.DisposalStarted)
        {
            throw new ObjectDisposedException(nameof(NativeWeaponFireAdapter));
        }

        if (callbackLifetime.IsActive)
        {
            return capabilities;
        }

        var gameVersion = Application.version ?? string.Empty;
        if (!string.Equals(gameVersion, SupportedGameVersion, StringComparison.Ordinal))
        {
            SetDisabled(
                $"Installed Duckov version '{gameVersion}' does not match verified weapon contract version '{SupportedGameVersion}'.");
            return capabilities;
        }

        try
        {
            var guardedShot = callbackLifetime.Guard<ItemAgent_Gun>(OnMainCharacterShoot);
            callbackLifetime.Activate(new[]
            {
                new SubscriptionBinding(
                    () => ItemAgent_Gun.OnMainCharacterShootEvent += guardedShot,
                    () => ItemAgent_Gun.OnMainCharacterShootEvent -= guardedShot)
            });
            metricCapabilities = WeaponNativeContractPolicy.CreateMetricCapabilities();
            capabilities = CapabilityRecords(AdapterCapabilityState.Supported);
            capabilityHandler(capabilities);
            diagnosticHandler(
                "Native weapon hook subscribed: each public firing callback receives a unique event ID. "
                + "Loaded-ammunition consumption, projectile creation, and dry-fire trigger attempts are unavailable because the public callback does not prove those side effects.");
        }
        catch (Exception exception)
        {
            TryCleanup();
            SetDisabled($"Weapon hook activation failed: {exception.GetType().Name}: {exception.Message}");
        }

        return capabilities;
    }

    public bool TryCleanup()
    {
        var cleaned = callbackLifetime.TryCleanup(() => true, out var cleanupFailure);
        if (cleanupFailure != null)
        {
            diagnosticHandler(
                $"Weapon subscription cleanup failed; cleanup remains retryable: "
                + $"{cleanupFailure.GetType().Name}: {cleanupFailure.Message}");
        }

        if (cleaned)
        {
            diagnosticHandler("Native weapon hook unsubscribed.");
        }

        return cleaned;
    }

    public void Dispose() => TryCleanup();

    private void OnMainCharacterShoot(ItemAgent_Gun agent)
    {
        try
        {
            var runId = runIdProvider();
            var gameplayContext = NativeRaidContext.GetGameplayContext();
            var exactMainDuck = agent != null
                                && agent.Holder != null
                                && ReferenceEquals(agent.Holder, CharacterMainControl.Main)
                                && agent.Holder.IsMainCharacter;
            var loading = SceneLoader.IsSceneLoading
                          || LevelManager.LevelInitializing
                          || (MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading);
            var paused = GameManager.Paused;
            if (!WeaponFireAcceptancePolicy.ShouldRecord(
                    !string.IsNullOrWhiteSpace(runId),
                    gameplayContext,
                    exactMainDuck,
                    loading,
                    paused))
            {
                return;
            }

            if (agent is null)
            {
                return;
            }

            var generationId = saveGenerationIdProvider();
            var mapId = mapIdProvider();
            var weapon = agent.Item;
            var gun = agent.GunItemSetting;
            if (string.IsNullOrWhiteSpace(generationId)
                || string.IsNullOrWhiteSpace(mapId)
                || weapon == null)
            {
                diagnosticHandler("Firing callback lacked proven generation, run, map, or weapon identity; event ignored.");
                return;
            }

            var weaponTypeId = weapon.TypeID;
            var ammunitionTypeId = gun?.TargetBulletID ?? -1;
            var eventCapabilities = MetricCapabilities;
            if (gun == null || ammunitionTypeId < 0)
            {
                eventCapabilities.AmmunitionIdentity = Availability(
                    AdapterCapabilityState.DisabledIncompatible,
                    "The firing callback did not expose a stable ammunition type for this action.");
            }

            var shot = new ShotRecorded
            {
                EventId = eventIds.NextEventId(),
                TimestampUtc = DateTime.UtcNow,
                SaveGenerationId = generationId,
                RunId = runId!,
                MapId = mapId!,
                SegmentId = segmentIdProvider(),
                GameplayContext = gameplayContext,
                IntegrityTags = NativeIntegrityProbe.Read(),
                GameVersion = Application.version ?? string.Empty,
                GameBuild = SupportedGameBuild,
                AdapterVersion = AdapterVersion,
                WeaponId = $"duckov:weapon:{weaponTypeId.ToString(CultureInfo.InvariantCulture)}",
                WeaponDisplayName = ReadWeaponDisplayName(weapon, weaponTypeId),
                AmmunitionId = ammunitionTypeId < 0
                    ? string.Empty
                    : $"duckov:ammo:{ammunitionTypeId.ToString(CultureInfo.InvariantCulture)}",
                AmmunitionDisplayName = gun == null || ammunitionTypeId < 0
                    ? string.Empty
                    : ReadAmmunitionDisplayName(gun, ammunitionTypeId),
                FiringActionCount = 1,
                AmmunitionUnitsConsumed = null,
                ProjectileCount = null,
                Capabilities = eventCapabilities,
                EquipmentAssociation = equipmentAssociationProvider()
            };
            shotHandler(shot);
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Firing callback failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void SetDisabled(string detail)
    {
        metricCapabilities = DisabledMetricCapabilities(detail);
        capabilities = DisabledCapabilities(detail);
        capabilityHandler(capabilities);
        diagnosticHandler(detail);
    }

    private static CapabilityRecord[] DisabledCapabilities(string detail) => new[]
    {
        Capability(WeaponCapabilityIds.TriggerAttempts, AdapterCapabilityState.DisabledIncompatible, detail),
        Capability(WeaponCapabilityIds.FiringActions, AdapterCapabilityState.DisabledIncompatible, detail),
        Capability(WeaponCapabilityIds.AmmunitionConsumption, AdapterCapabilityState.DisabledIncompatible, detail),
        Capability(WeaponCapabilityIds.Projectiles, AdapterCapabilityState.DisabledIncompatible, detail),
        Capability(WeaponCapabilityIds.WeaponIdentity, AdapterCapabilityState.DisabledIncompatible, detail),
        Capability(WeaponCapabilityIds.AmmunitionIdentity, AdapterCapabilityState.DisabledIncompatible, detail)
    };

    private static CapabilityRecord[] CapabilityRecords(
        AdapterCapabilityState state,
        string? failureDetail = null) => state == AdapterCapabilityState.Supported
        ? new[]
        {
            Capability(
                WeaponCapabilityIds.TriggerAttempts,
                AdapterCapabilityState.DisabledIncompatible,
                "The verified public firing event does not emit for rejected trigger attempts or dry fire; trigger-attempt counts are unavailable."),
            Capability(
                WeaponCapabilityIds.FiringActions,
                AdapterCapabilityState.Supported,
                "Public ItemAgent_Gun.OnMainCharacterShootEvent proves one accepted firing action callback; each callback receives a unique UDS event ID."),
            Capability(
                WeaponCapabilityIds.AmmunitionConsumption,
                AdapterCapabilityState.DisabledIncompatible,
                "ItemSetting_Gun.UseABullet can return without consuming a loaded item, and the public firing callback exposes no proven pre/post result."),
            Capability(
                WeaponCapabilityIds.Projectiles,
                AdapterCapabilityState.DisabledIncompatible,
                "ItemAgent_Gun.ShootOneBullet can return before projectile acquisition, and ShotCount alone does not prove created projectiles."),
            Capability(
                WeaponCapabilityIds.WeaponIdentity,
                AdapterCapabilityState.Supported,
                "The callback supplies the firing ItemAgent_Gun and its stable Item.TypeID."),
            Capability(
                WeaponCapabilityIds.AmmunitionIdentity,
                AdapterCapabilityState.Supported,
                "The firing gun exposes ItemSetting_Gun.TargetBulletID and its localized fallback name at callback time.")
        }
        : DisabledCapabilities(failureDetail ?? "Weapon tracking is unavailable.");

    private static CapabilityRecord Capability(string id, AdapterCapabilityState state, string detail) => new()
    {
        AdapterId = id,
        State = state,
        Version = AdapterVersion,
        Detail = detail
    };

    private static WeaponMetricCapabilities DisabledMetricCapabilities(string detail) => new()
    {
        FiringActions = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        AmmunitionConsumption = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        Projectiles = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        WeaponIdentity = Availability(AdapterCapabilityState.DisabledIncompatible, detail),
        AmmunitionIdentity = Availability(AdapterCapabilityState.DisabledIncompatible, detail)
    };

    private static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    {
        State = state,
        Provenance = provenance
    };

    private static string ReadWeaponDisplayName(ItemStatsSystem.Item weapon, int typeId)
    {
        var displayName = weapon.DisplayName;
        return string.IsNullOrWhiteSpace(displayName)
            ? $"Unknown weapon {typeId.ToString(CultureInfo.InvariantCulture)}"
            : displayName;
    }

    private static string ReadAmmunitionDisplayName(ItemSetting_Gun gun, int typeId)
    {
        var displayName = gun.CurrentBulletName;
        return string.IsNullOrWhiteSpace(displayName)
            ? $"Unknown ammunition {typeId.ToString(CultureInfo.InvariantCulture)}"
            : displayName;
    }
}
