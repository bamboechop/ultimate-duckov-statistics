using Duckov.Scenes;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed partial class NativeRunLifecycleAdapter
{
    private BaseMovementCapture? baseMovement;
    private Func<bool>? baseMovementDurability;
    private Func<bool>? baseProfileTransitionPending;
    private GameObject? baseLevelHost;
    private GameObject? retiringBaseLevelHost;
    private string? baseSubSceneId;
    private bool baseAwaitingNativeLoad;
    private bool baseWasReady;

    public void ConfigureBaseMovement(
        Func<BaseMovementUpdate, bool> publish,
        Func<bool> persistBoundary,
        Func<bool> profileTransitionPending)
    {
        baseMovement = new BaseMovementCapture(publish);
        baseMovementDurability = persistBoundary;
        baseProfileTransitionPending = profileTransitionPending;
    }

    // Save-slot selection can publish a new profile before the old native level disappears.
    public void BeginBaseMovementNativeLoad(long _)
    {
        retiringBaseLevelHost = LevelManager.Instance?.gameObject;
        baseAwaitingNativeLoad = true;
        baseMovement?.ResetBaseline();
        baseWasReady = false;
    }

    public bool PublishBaseMovementForBoundary() => baseMovement?.Publish(NowMonotonic(), force: true) != false;

    private void TickBaseMovement(DateTime utcNow, double now)
    {
        if (baseMovement == null) return;
        var level = LevelManager.Instance;
        var generation = saveGenerationIdProvider();
        var ready = !tracker.IsActive && !loading && !paused && Time.timeScale > 0
            && baseProfileTransitionPending?.Invoke() != true
            && level != null && level.IsBaseLevel && !level.IsRaidMap
            && LevelManager.LevelInited && LevelManager.AfterInit
            && mainCharacter != null && mainCharacter.IsMainCharacter
            && mainCharacter.Health != null && !mainCharacter.Health.IsDead
            && !string.IsNullOrWhiteSpace(generation)
            && MovementCapability.State == AdapterCapabilityState.Supported;
        if (baseAwaitingNativeLoad)
        {
            if (level == null || ReferenceEquals(level.gameObject, retiringBaseLevelHost)) ready = false;
            else if (ready)
            {
                baseAwaitingNativeLoad = false;
                retiringBaseLevelHost = null;
            }
        }
        if (!ready)
        {
            if (baseWasReady)
            {
                baseMovement.ResetBaseline(knownGap: !loading && !paused && Time.timeScale > 0
                    && level != null && level.IsBaseLevel);
                baseMovementDurability?.Invoke();
            }
            baseWasReady = false;
            baseMovement.Publish(now);
            return;
        }

        baseMovement.Bind(generation);
        var subSceneId = MultiSceneCore.Instance == null ? null : MultiSceneCore.ActiveSubSceneID;
        if (!ReferenceEquals(baseLevelHost, level!.gameObject) || baseSubSceneId != subSceneId)
            baseMovement.ResetBaseline();
        baseLevelHost = level.gameObject;
        baseSubSceneId = subSceneId;
        baseWasReady = true;
        if (sampleCadence.IsDue(now))
        {
            try
            {
                var position = mainCharacter!.transform.position;
                baseMovement.Observe(new Position3D(position.x, position.y, position.z), now,
                    ReadMaximumPlausibleSpeed(mainCharacter), utcNow);
            }
            catch (Exception exception)
            {
                baseMovement.ResetBaseline(knownGap: true);
                DisableMovement($"Base main-duck movement sampling failed: {exception.GetType().Name}: {exception.Message}");
            }
            sampleCadence.MarkCompleted(now);
        }
        baseMovement.Publish(now);
    }

    private void ResetBaseMovementBoundary()
    {
        baseMovement?.ResetBaseline();
        baseWasReady = false;
        baseMovementDurability?.Invoke();
    }
}
