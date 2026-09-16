using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Duckov;
using Duckov.MiniMaps;
using Duckov.Scenes;
using Newtonsoft.Json;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UnityEngine;

namespace UltimateDuckovStatistics.Encounters;

internal sealed class NativeEncounterMapObserver : IEncounterObserver
{
    public Core.Encounters.EncounterCaptureIssue? FailureIssue => !exactPlacement || sinkFailure != null
        ? Core.Encounters.EncounterCaptureIssue.MapIncomplete : null;
    private static NativeEncounterMapObserver? current;
    private readonly IEncounterObservationSink sink;
    private readonly Action<string> log;
    private readonly EncounterMapArtworkCache artwork;
    private readonly HashSet<string> calibrations = new(StringComparer.Ordinal);
    private readonly MethodInfo placement = typeof(CharacterMainControl).GetMethod(nameof(CharacterMainControl.SetPosition), new[] { typeof(Vector3) })!;
    private ReflectiveHarmonyPatcher? patcher;
    private CharacterMainControl? character;
    private PlacementCapture? activePlacement;
    private bool disposed;
    private bool observing;
    private bool exactPlacement;
    private double lastSample = double.NegativeInfinity;
    private double lastCalibration = double.NegativeInfinity;
    private string visitKey = string.Empty;
    private int visit;
    private long sequence;
    private Vector3 previousPosition;
    private string? sinkFailure;

    public NativeEncounterMapObserver(IEncounterObservationSink sink, string outputDirectory, Action<string> log)
    {
        this.sink = sink;
        this.log = log;
        if (current != null) throw new InvalidOperationException("An encounter map probe is already active.");
        artwork = new EncounterMapArtworkCache(sink, outputDirectory, log);
        current = this;
        try
        {
            if (!ReflectiveHarmonyPatcher.TryCreate("at.bamboechop.uds.encounters.map", out patcher, out var detail) || patcher == null)
                Record("map-placement-unavailable", new { reason = detail });
            else if (!patcher.IsPatchSetTrusted(placement, Array.Empty<HarmonyPatchExpectation>(), out detail))
                Record("map-placement-unavailable", new { reason = detail });
            else
            {
                patcher.Patch(placement, Method(nameof(PlacementPrefix)), Method(nameof(PlacementPostfix)), Method(nameof(PlacementFinalizer)));
                exactPlacement = CheckPatchSet();
                Record("map-placement-capability", new { exactPlacement });
            }
        }
        catch (Exception exception) { Failure("placement initialization", exception); }
    }

    public string Status => $"Map/path probe; exact placement {(exactPlacement ? "available" : "unavailable")}; {artwork.Status}" + (sinkFailure == null ? string.Empty : "; " + sinkFailure);

    public void Tick(EncounterObservationContext context)
    {
        if (disposed) return;
        artwork.Tick();
        try
        {
            if (!CanObserve(context))
            {
                if (observing) Record("path-gap", new { visit, reason = "inactive-paused-loading-or-native-unavailable", sequence = ++sequence });
                observing = false;
                lastSample = double.NegativeInfinity;
                return;
            }
            var candidate = CharacterMainControl.Main;
            if (candidate == null || !candidate.IsMainCharacter)
            {
                if (observing) Record("path-gap", new { visit, reason = "main-character-unavailable", sequence = ++sequence });
                observing = false;
                return;
            }
            if (!ReferenceEquals(candidate, character))
            {
                DetachCharacter();
                character = candidate;
                character.OnSetPositionEvent += OnPlacement;
                observing = false;
            }
            var key = context.GenerationId + "|" + context.RunId + "|" + context.MapId + "|" + context.SegmentId;
            if (!observing || key != visitKey)
            {
                visit++;
                visitKey = key;
                calibrations.Clear(); // Publish calibration for every visit/run, even when cached artwork is reused.
                Record("path-visit", new { visit, context.GenerationId, context.RunId, context.MapId, context.SegmentId, reason = observing ? "map-or-route-change" : "begin-or-resume", sequence = ++sequence });
                observing = true;
                lastSample = double.NegativeInfinity;
                lastCalibration = double.NegativeInfinity;
            }
            if (context.MonotonicSeconds - lastSample >= 0.2)
            {
                var position = character.transform.position;
                if (double.IsFinite(lastSample) && Finite(position))
                {
                    var speed = Math.Max(character.CharacterWalkSpeed, Math.Max(character.CharacterRunSpeed, character.DashSpeed));
                    var reason = EncounterPathMath.GapReason(context.MonotonicSeconds - lastSample, Vector3.Distance(previousPosition, position), speed);
                    if (reason != null) Record("path-gap", new { visit, reason, seconds = context.MonotonicSeconds - lastSample, sequence = ++sequence });
                }
                if (Finite(position))
                    Record("path-point", new { visit, sequence = ++sequence, context.MonotonicSeconds, context.MapId, position = Coordinates(position), actorId = sink.ActorId(character) });
                else Record("path-gap", new { visit, reason = "nonfinite-position", sequence = ++sequence });
                previousPosition = position;
                lastSample = Finite(position) ? context.MonotonicSeconds : double.NegativeInfinity;
            }
            if (context.MonotonicSeconds - lastCalibration >= 1)
            {
                lastCalibration = context.MonotonicSeconds;
                CaptureCalibration(context);
            }
        }
        catch (Exception exception) { Failure("tick", exception); observing = false; }
    }

    private void CaptureCalibration(EncounterObservationContext context)
    {
        var settings = MiniMapSettings.Instance;
        var nativeSceneId = NativeSceneId(context.MapId);
        var entry = settings == null ? null : settings.maps.Find(e => e != null && e.sceneID == nativeSceneId);
        if (entry == null) { RecordOnce("missing:" + context.MapId, "map-unavailable", new { context.MapId, reason = "no-native-map-entry" }); return; }
        var sprite = settings!.combinedSprite != null ? settings.combinedSprite : entry.sprite;
        var valid = !entry.hide && !entry.noSignal && sprite != null && Finite(entry.mapWorldCenter)
            && float.IsFinite(entry.imageWorldSize) && entry.imageWorldSize > 0 && Finite(entry.Offset);
        if (settings!.combinedSprite != null) valid &= Finite(settings.combinedCenter) && float.IsFinite(settings.combinedSize) && settings.combinedSize > 0;
        var metadata = new
        {
            version = 1, nativeVersion = Application.version, cacheRecipe = "unpacked-fullcanvas-blit-v1", colorSpace = QualitySettings.activeColorSpace.ToString(), context.MapId, nativeSceneId,
            mapGroup = MultiSceneCore.Instance == null ? null : MultiSceneCore.MainSceneID,
            worldCenter = Coordinates(entry.mapWorldCenter), entry.imageWorldSize,
            offset = new[] { entry.Offset.x, entry.Offset.y }, entry.hide, entry.noSignal,
            combined = settings.combinedSprite != null,
            combinedCenter = Coordinates(settings.combinedCenter), settings.combinedSize,
            spriteName = sprite == null ? null : sprite.name,
            spriteRect = sprite == null ? null : new[] { sprite.rect.x, sprite.rect.y, sprite.rect.width, sprite.rect.height },
            spritePivot = sprite == null ? null : new[] { sprite.pivot.x, sprite.pivot.y },
            pixelsPerUnit = sprite == null ? (float?)null : sprite.pixelsPerUnit,
            available = valid, convention = "world XZ minus scene center plus entry offset; full sprite canvas; native framing not reused"
        };
        var json = JsonConvert.SerializeObject(metadata);
        var key = Hash(Encoding.UTF8.GetBytes(json));
        var newlyRecorded = RecordOnce(key, "map-calibration", new { calibrationId = key, metadata });
        if (!calibrations.Contains(key)) return;
        if (valid) artwork.TryCapture(key, json, sprite!);
        else if (newlyRecorded) Record("map-unavailable", new { context.MapId, calibrationId = key, reason = "null-hidden-nosignal-or-invalid-calibration" });
    }

    private bool RecordOnce(string key, string kind, object data)
    {
        if (calibrations.Contains(key)) return false;
        if (calibrations.Count >= 64)
        {
            if (calibrations.Add("[capacity]")) Record("map-coverage", new { reason = "calibration-capacity-64", available = false });
            return false;
        }
        calibrations.Add(key); Record(kind, data); return true;
    }

    private static bool CanObserve(EncounterObservationContext context) => context.Active && !context.Paused && !context.Loading && !GameManager.Paused
        && !SceneLoader.IsSceneLoading && !LevelManager.LevelInitializing
        && !(MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading);

    private bool CheckPatchSet() => patcher != null && patcher.IsPatchSetTrusted(placement, new[]
    {
        new HarmonyPatchExpectation("Prefixes", Method(nameof(PlacementPrefix))),
        new HarmonyPatchExpectation("Postfixes", Method(nameof(PlacementPostfix))),
        new HarmonyPatchExpectation("Finalizers", Method(nameof(PlacementFinalizer)))
    }, out _);

    private PlacementCapture? BeginPlacement(CharacterMainControl instance, Vector3 target)
    {
        if (disposed || !exactPlacement || !ReferenceEquals(instance, character) || !CanObserve(sink.Context)) return null;
        if (!CheckPatchSet()) { exactPlacement = false; Record("map-placement-unavailable", new { reason = "patch-set-drift" }); return null; }
        var origin = instance.transform.position;
        if (!Finite(origin) || !Finite(target)) return null;
        var state = new PlacementCapture { Owner = this, Parent = activePlacement, Actor = instance, From = origin, Requested = target, Map = sink.Context.MapId, NativeMap = NativeMap(), Generation = sink.Context.GenerationId, Visit = visit, Time = sink.Context.MonotonicSeconds };
        activePlacement = state;
        return state;
    }

    private void OnPlacement(CharacterMainControl instance, Vector3 requested)
    {
        try
        {
            if (!disposed && activePlacement is { } state && ReferenceEquals(state.Actor, instance) && state.Requested.Equals(requested))
            {
                state.Witnesses++;
                state.ArrivalAtEvent = instance.transform.position;
                state.NativeMapAtEvent = NativeMap();
            }
        }
        catch (Exception exception) { Failure("placement witness", exception); }
    }

    private void CompletePlacement(PlacementCapture state)
    {
        if (disposed || !ReferenceEquals(activePlacement, state)) return;
        var arrival = state.Actor.transform.position;
        var context = sink.Context;
        var sameMap = !string.IsNullOrWhiteSpace(state.NativeMap) && state.NativeMap == NativeMap()
            && state.NativeMap == state.NativeMapAtEvent && NativeSceneId(state.Map) == state.NativeMap
            && state.Map == context.MapId && state.Generation == context.GenerationId && CanObserve(context);
        var proven = state.Witnesses == 1 && sameMap && Finite(arrival) && arrival.Equals(state.ArrivalAtEvent);
        var mapMoved = EncounterPathMath.HasMapDisplacement(state.From.x, state.From.z, arrival.x, arrival.z);
        Record(!proven ? "path-discontinuity" : mapMoved ? "path-teleport" : "path-placement", new
        {
            visit = state.Visit, sequence = ++sequence, from = Coordinates(state.From), requested = Coordinates(state.Requested),
            arrival = Finite(arrival) ? Coordinates(arrival) : null, state.Time, state.Map, state.NativeMap,
            destinationNativeMap = NativeMap(), state.Witnesses, provenSameMap = proven,
            style = !proven ? "gap" : mapMoved ? "dotted" : "none", walkedDistanceContribution = 0,
            evidence = "guarded-SetPosition-prefix-public-event-postfix"
        });
        // Force the next observation to carry a new boundary, without touching aggregate movement state.
        lastSample = double.NegativeInfinity;
        state.Completed = true;
    }

    private static string NativeMap()
    {
        // Match NativeRunLifecycleAdapter.ReadMapIdentity: no active subscene can legitimately
        // fall back to the main scene, and a single-scene level can have no MultiSceneCore.
        if (MultiSceneCore.Instance != null)
            return !string.IsNullOrWhiteSpace(MultiSceneCore.ActiveSubSceneID)
                ? MultiSceneCore.ActiveSubSceneID
                : MultiSceneCore.MainSceneID ?? string.Empty;
        var level = LevelManager.Instance;
        return level == null ? string.Empty : SceneInfoCollection.GetSceneID(level.gameObject.scene.buildIndex) ?? string.Empty;
    }
    private static string NativeSceneId(string mapId) => EncounterPathMath.NativeSceneId(mapId);
    private static MethodInfo Method(string name) => typeof(NativeEncounterMapObserver).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void PlacementPrefix(CharacterMainControl __instance, Vector3 pos, out PlacementCapture? __state)
    {
        __state = null;
        try { __state = current?.BeginPlacement(__instance, pos); }
        catch (Exception exception) { current?.Failure("placement prefix", exception); }
    }
    private static void PlacementPostfix(PlacementCapture? __state)
    {
        if (__state == null) return;
        try { __state.Owner.CompletePlacement(__state); }
        catch (Exception exception) { __state.Owner.Failure("placement postfix", exception); }
    }
    private static Exception? PlacementFinalizer(Exception? __exception, PlacementCapture? __state)
    {
        if (__state != null)
        {
            try
            {
                if (!__state.Completed) __state.Owner.Record("path-gap", new { visit = __state.Visit, reason = "placement-incomplete-or-failed", failed = __exception != null });
                __state.Owner.lastSample = double.NegativeInfinity;
                if (ReferenceEquals(__state.Owner.activePlacement, __state)) __state.Owner.activePlacement = __state.Parent;
            }
            catch { /* Preserve the original exception/result even if diagnostics fail. */ }
        }
        return __exception;
    }
    internal static float[] Coordinates(Vector3 value) => new[] { value.x, value.y, value.z };
    private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
    internal static string Hash(byte[] data) { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant(); }
    private void Record(string kind, object value)
    {
        if (disposed) return;
        try { sink.Record(kind, value); }
        catch (Exception exception) { sinkFailure = "map diagnostic sink failed: " + exception.GetType().Name; }
    }
    private void Failure(string area, Exception exception) { try { Record("map-probe-error", new { area, error = exception.GetType().Name + ": " + exception.Message }); } catch { /* A probe failure must never reach gameplay. */ } }
    private void DetachCharacter() { if (character != null) character.OnSetPositionEvent -= OnPlacement; character = null; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(current, this)) current = null;
        try { DetachCharacter(); } catch (Exception exception) { SafeLog("Map probe detach: " + exception.Message); }
        activePlacement = null;
        artwork.Dispose();
        if (patcher != null && !patcher.TryDispose(out var detail)) SafeLog("Map probe cleanup remains retryable: " + detail);
    }

    private void SafeLog(string message) { try { log(message); } catch { /* Logging cannot prevent cleanup. */ } }

    private sealed class PlacementCapture
    {
        public NativeEncounterMapObserver Owner = null!;
        public PlacementCapture? Parent;
        public CharacterMainControl Actor = null!;
        public Vector3 From, Requested, ArrivalAtEvent;
        public string Map = string.Empty, NativeMap = string.Empty, NativeMapAtEvent = string.Empty, Generation = string.Empty;
        public int Visit, Witnesses;
        public double Time;
        public bool Completed;
    }
}
