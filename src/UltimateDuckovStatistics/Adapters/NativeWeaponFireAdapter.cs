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
    internal const string AdapterVersion = "native-weapon-fire/2.3.30";
    private const string SupportedGameVersion = "2.3.30";
    private const string SupportedGameBuild = "24013657";
    private readonly Func<string> saveGenerationIdProvider;
    private readonly Func<string?> runIdProvider;
    private readonly Func<string?> mapIdProvider;
    private readonly Func<ShotRecorded, bool> shotHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private readonly WeaponFireSequenceTracker sequenceTracker = new(() => Guid.NewGuid().ToString("N"));
    private IReadOnlyList<CapabilityRecord> capabilities = DisabledCapabilities("Weapon tracking has not been initialized.");
    private WeaponMetricCapabilities metricCapabilities = new();
    private string? observedRunId;

    public NativeWeaponFireAdapter(
        Func<string> saveGenerationIdProvider,
        Func<string?> runIdProvider,
        Func<string?> mapIdProvider,
        Func<ShotRecorded, bool> shotHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider
            ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.runIdProvider = runIdProvider ?? throw new ArgumentNullException(nameof(runIdProvider));
        this.mapIdProvider = mapIdProvider ?? throw new ArgumentNullException(nameof(mapIdProvider));
        this.shotHandler = shotHandler ?? throw new ArgumentNullException(nameof(shotHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
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
            metricCapabilities = SupportedMetricCapabilities();
            capabilities = CapabilityRecords(AdapterCapabilityState.Supported);
            capabilityHandler(capabilities);
            diagnosticHandler(
                "Native weapon hook subscribed: successful firing actions, loaded ammunition units, and native projectile count are distinct; dry-fire trigger attempts are unavailable and are not counted.");
        }
        catch (Exception exception)
        {
            TryCleanup();
            SetDisabled($"Weapon hook activation failed: {exception.GetType().Name}: {exception.Message}");
        }

        return capabilities;
    }

    public void ResetSequence()
    {
        sequenceTracker.Clear();
        observedRunId = null;
    }

    public bool TryCleanup()
    {
        sequenceTracker.Clear();
        var cleaned = callbackLifetime.TryCleanup(() => true, out var cleanupFailure);
        if (cleanupFailure != null)
        {
            diagnosticHandler(
                $"Weapon subscription cleanup failed; cleanup remains retryable: "
                + $"{cleanupFailure.GetType().Name}: {cleanupFailure.Message}");
        }

        if (cleaned)
        {
            diagnosticHandler("Native weapon hook unsubscribed and bounded firing-correlation state cleared.");
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
                || weapon == null
                || gun == null
                || gun.TargetBulletID < 0)
            {
                diagnosticHandler("Firing callback lacked proven generation, run, map, weapon, or ammunition identity; event ignored.");
                return;
            }

            var weaponTypeId = weapon.TypeID;
            var ammunitionTypeId = gun.TargetBulletID;
            if (!string.Equals(observedRunId, runId, StringComparison.Ordinal))
            {
                sequenceTracker.Clear();
                observedRunId = runId;
            }

            var eventId = sequenceTracker.GetEventId(
                agent.GetInstanceID(),
                ammunitionTypeId,
                agent.BulletCount);
            var shot = new ShotRecorded
            {
                EventId = eventId,
                TimestampUtc = DateTime.UtcNow,
                SaveGenerationId = generationId,
                RunId = runId!,
                MapId = mapId!,
                GameplayContext = gameplayContext,
                IntegrityTags = NativeIntegrityProbe.Read(),
                GameVersion = Application.version ?? string.Empty,
                GameBuild = SupportedGameBuild,
                AdapterVersion = AdapterVersion,
                WeaponId = $"duckov:weapon:{weaponTypeId.ToString(CultureInfo.InvariantCulture)}",
                WeaponDisplayName = ReadWeaponDisplayName(weapon, weaponTypeId),
                AmmunitionId = $"duckov:ammo:{ammunitionTypeId.ToString(CultureInfo.InvariantCulture)}",
                AmmunitionDisplayName = ReadAmmunitionDisplayName(gun, ammunitionTypeId),
                FiringActionCount = 1,
                AmmunitionUnitsConsumed = 1,
                ProjectileCount = Math.Max(0, agent.ShotCount),
                Capabilities = MetricCapabilities
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

    private static CapabilityRecord[] DisabledCapabilities(string detail) =>
        CapabilityRecords(AdapterCapabilityState.DisabledIncompatible, detail);

    private static CapabilityRecord[] CapabilityRecords(
        AdapterCapabilityState state,
        string? failureDetail = null) => new[]
        {
            Capability(
                WeaponCapabilityIds.TriggerAttempts,
                AdapterCapabilityState.DisabledIncompatible,
                failureDetail
                    ?? "The verified public firing event does not emit for rejected trigger attempts or dry fire; trigger-attempt counts are unavailable."),
            Capability(WeaponCapabilityIds.FiringActions, state, failureDetail
                ?? "Public ItemAgent_Gun.OnMainCharacterShootEvent proves one accepted firing action per discharged round; it does not emit for reload or dry fire."),
            Capability(WeaponCapabilityIds.AmmunitionConsumption, state, failureDetail
                ?? "ItemSetting_Gun.UseABullet consumes exactly one loaded ammunition item before the firing callback."),
            Capability(WeaponCapabilityIds.Projectiles, state, failureDetail
                ?? "ItemAgent_Gun.TransToFire creates one projectile per native ShotCount before the firing callback."),
            Capability(WeaponCapabilityIds.WeaponIdentity, state, failureDetail
                ?? "The callback supplies the firing ItemAgent_Gun and its stable Item.TypeID."),
            Capability(WeaponCapabilityIds.AmmunitionIdentity, state, failureDetail
                ?? "The firing gun retains its loaded ItemSetting_Gun.TargetBulletID and localized fallback name after consumption.")
        };

    private static CapabilityRecord Capability(string id, AdapterCapabilityState state, string detail) => new()
    {
        AdapterId = id,
        State = state,
        Version = AdapterVersion,
        Detail = detail
    };

    private static WeaponMetricCapabilities SupportedMetricCapabilities() => new()
    {
        FiringActions = Availability(AdapterCapabilityState.Supported, "ItemAgent_Gun.OnMainCharacterShootEvent"),
        AmmunitionConsumption = Availability(AdapterCapabilityState.Supported, "ItemSetting_Gun.UseABullet: one loaded item"),
        Projectiles = Availability(AdapterCapabilityState.Supported, "ItemAgent_Gun.TransToFire: ShotCount projectile loop"),
        WeaponIdentity = Availability(AdapterCapabilityState.Supported, "ItemAgent_Gun.Item.TypeID at firing time"),
        AmmunitionIdentity = Availability(AdapterCapabilityState.Supported, "ItemSetting_Gun.TargetBulletID at firing time")
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
