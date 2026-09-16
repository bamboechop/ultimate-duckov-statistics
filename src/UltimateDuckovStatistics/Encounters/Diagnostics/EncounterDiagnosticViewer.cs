#if UDS_ENCOUNTER_DIAGNOSTICS
using System.Globalization;
using SodaCraft.Localizations;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UltimateDuckovStatistics.Encounters.Diagnostics;

// Native qualification surface, isolated from the retained production Runs UI.
// Opens only over an already-open UDS panel, borrowing its input/cursor ownership.
internal sealed class EncounterDiagnosticViewer : IDisposable
{
    private const int MaximumDrawEdges = EncounterRouteRaster.MaximumEdges;
    private readonly string root;
    private readonly Action<bool> publishOpen;
    private readonly Action<string> log;
    private readonly NativeEntityDisplayNames names = new();
    private EncounterMapPresentationAssets? assets;
    private EncounterRouteTexture? routeTexture;
    private EncounterRouteStroke[]? routeStrokes;
    private EncounterMapCalibration? routeCalibration;
    private GameObject? shield;
    private Task<LoadResult>? loading;
    private EncounterReplayCapture? capture;
    private EncounterReplayGroup? group;
    private string[] files = Array.Empty<string>();
    private string[] fatalLabels = Array.Empty<string>();
    private int fileIndex, groupIndex, selected = -1, epoch;
    private Vector2 scroll;
    private EncounterRevealTimeline? timeline;
    private double[] markerProgress = Array.Empty<double>();
    private float? revealStarted;
    private string status = string.Empty;
    private string selectedDetails = string.Empty;
    private bool disposed;
    internal bool IsOpen { get; private set; }

    internal EncounterDiagnosticViewer(string root, Action<bool> publishOpen, Action<string> log)
    { this.root = root; this.publishOpen = publishOpen; this.log = log; }

    internal void Tick(bool allowed)
    {
        if (disposed) return;
        if (!allowed) Close();
        try
        {
            if (allowed && Input.GetKeyDown(KeyCode.F5))
            {
                if (IsOpen) Close(); else Open();
            }
            if (loading?.IsCompleted == true)
            {
                var result = loading.GetAwaiter().GetResult(); loading = null;
                if (IsOpen && result.Epoch == epoch)
                {
                    files = result.Files; fileIndex = result.Index; capture = result.Capture;
                    status = result.Error ?? (capture == null ? "No saved captures found. Stop F6 recording first." :
                        capture.IsPartial ? "PARTIAL evidence: " + string.Join("; ", capture.Issues.Take(3)) : "Capture transport complete; gameplay coverage remains limited to recorded evidence.");
                    SelectGroup(0);
                }
            }
            if (IsOpen) assets?.Tick();
        }
        catch (Exception exception)
        {
            log("Encounter preview unavailable: " + exception.GetType().Name + ": " + exception.Message);
            Close();
        }
    }

    private void Open()
    {
        // Never launch concurrent parsers if the previous bounded read is still retiring.
        if (loading != null) return;
        shield = new GameObject("UDS encounter preview raycast shield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var canvas = shield.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = short.MaxValue;
        var blocker = new GameObject("Input shield", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        blocker.transform.SetParent(shield.transform, false);
        var rect = (RectTransform)blocker.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = blocker.GetComponent<Image>(); image.color = Color.clear; image.raycastTarget = true;
        GameManager.EventSystem?.SetSelectedGameObject(null);
        IsOpen = true; publishOpen(true);
        assets = new EncounterMapPresentationAssets(Path.Combine(root, "maps"), log);
        routeTexture = new EncounterRouteTexture();
        names.Invalidate();
        BeginLoad(null, 0);
    }

    private void BeginLoad(string[]? existingFiles, int index)
    {
        if (loading != null) return;
        var requestEpoch = ++epoch;
        capture = null; group = null; selected = -1; routeStrokes = null; status = "Reading saved evidence on a worker...";
        assets?.Request(string.Empty);
        var captureRoot = Path.Combine(root, "captures");
        loading = Task.Run(() =>
        {
            var result = new LoadResult { Epoch = requestEpoch, Index = index };
            try
            {
                result.Files = existingFiles ?? (Directory.Exists(captureRoot)
                    ? Directory.EnumerateDirectories(captureRoot).OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                        .Take(20).Select(path => Path.Combine(path, "observations.jsonl")).Where(File.Exists).ToArray()
                    : Array.Empty<string>());
                if (result.Files.Length == 0) return result;
                result.Index = Math.Max(0, Math.Min(index, result.Files.Length - 1));
                result.Capture = EncounterCaptureReplay.Load(result.Files[result.Index]);
                // A restart cache-only test commonly follows the raid capture. Skip at most
                // two such empty recordings automatically; other files remain selectable.
                if (existingFiles == null)
                    for (var attempt = 0; attempt < 2 && result.Capture.Groups.Count == 0 && result.Index + 1 < result.Files.Length; attempt++)
                        result.Capture = EncounterCaptureReplay.Load(result.Files[++result.Index]);
            }
            catch (Exception exception) { result.Error = "Capture read failed: " + exception.GetType().Name + ": " + exception.Message; }
            return result;
        });
    }

    private void SelectGroup(int index)
    {
        groupIndex = index; selected = -1; scroll = Vector2.zero; revealStarted = null;
        routeStrokes = null;
        group = capture != null && index < capture.Groups.Count ? capture.Groups[index] : null;
        timeline = null; fatalLabels = Array.Empty<string>(); markerProgress = Array.Empty<double>();
        if (group == null) return;
        fatalLabels = group.Fatals.Select((fatal, number) => (number + 1).ToString(CultureInfo.InvariantCulture) + "  " + FatalLabel(fatal)).ToArray();
        try
        {
            timeline = new EncounterRevealTimeline(group.PathEdges.Select(edge => new EncounterRevealSpan(
                edge.FromTime, edge.ToTime, Distance(edge.From.Position, edge.To.Position), edge.Style == EncounterReplayEdgeStyle.Teleport)));
            markerProgress = group.Fatals.Select(fatal => timeline.MarkerProgress(fatal.Time)).ToArray();
        }
        catch (ArgumentException) { status = "Route timing overlaps; showing completed geometry without a misleading reveal."; }
        var calibration = group.Calibrations.LastOrDefault(value => value.Available);
        assets?.Request(calibration?.CalibrationId ?? string.Empty);
    }

    internal void Draw()
    {
        if (!IsOpen || disposed) return;
        var priorMatrix = GUI.matrix; var priorColor = GUI.color; var priorDepth = GUI.depth;
        var priorBackground = GUI.backgroundColor;
        try
        {
            GUI.depth = -1000;
            var scale = Math.Max(.5f, Math.Min(Screen.width / 1600f, Screen.height / 900f));
            var width = Screen.width / scale; var height = Screen.height / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            Paint(new Rect(0, 0, width, height), new Color(.015f, .025f, .04f, 1));
            var title = new GUIStyle(GUI.skin.label) { fontSize = 26 };
            var text = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
            GUI.Label(new Rect(30, 20, width - 240, 42), "UDS • Map & Kills • Native preview", title);
            if (GUI.Button(new Rect(width - 170, 24, 140, 32), "CLOSE (F5)")) { Close(); return; }
            GUI.Label(new Rect(30, 65, width - 60, 44), status, text);
            if (capture == null) return;
            if (GUI.Button(new Rect(30, 113, 120, 30), "NEWER") && fileIndex > 0) { BeginLoad(files, fileIndex - 1); return; }
            if (GUI.Button(new Rect(158, 113, 120, 30), "OLDER") && fileIndex + 1 < files.Length) { BeginLoad(files, fileIndex + 1); return; }
            GUI.Label(new Rect(293, 115, width - 650, 30), Path.GetFileName(Path.GetDirectoryName(capture.SourcePath)), text);
            if (GUI.Button(new Rect(width - 240, 113, 210, 30), "REPLAY ROUTE")) { selected = -1; revealStarted = null; routeStrokes = null; }
            if (capture.Groups.Count == 0) { GUI.Label(new Rect(30, 170, 800, 60), "This recording has no raid map or encounter observations.", text); return; }
            if (GUI.Button(new Rect(30, 158, 50, 30), "<")) SelectGroup((groupIndex + capture.Groups.Count - 1) % capture.Groups.Count);
            if (GUI.Button(new Rect(88, 158, 50, 30), ">")) SelectGroup((groupIndex + 1) % capture.Groups.Count);
            if (group == null) return;
            GUI.Label(new Rect(152, 157, width - 182, 36), names.Names.Get(group.MapId, group.NativeMapId)
                + "  •  Run " + group.RunId.Substring(0, Math.Min(8, group.RunId.Length))
                + "  •  " + (groupIndex + 1) + "/" + capture.Groups.Count + " recorded map visits/groups", text);
            var mapRect = new Rect(30, 210, (width - 90) * .57f, height - 276);
            var listRect = new Rect(mapRect.xMax + 30, 210, width - mapRect.xMax - 60, height - 246);
            DrawMap(mapRect, text);
            DrawList(listRect, text);
            GUI.Label(new Rect(30, height - 60, mapRect.width, 54), assets?.Status ?? "No map image recorded.", text);
        }
        catch (Exception exception)
        {
            // Qualification drawing failure must not close the ordinary UDS owner or spam OnGUI.
            log("Encounter preview drawing stopped: " + exception.GetType().Name + ": " + exception.Message);
            Close();
        }
        finally { GUI.matrix = priorMatrix; GUI.color = priorColor; GUI.depth = priorDepth; GUI.backgroundColor = priorBackground; }
    }

    private void DrawMap(Rect area, GUIStyle text)
    {
        Paint(area, new Color(.035f, .055f, .075f));
        if (group == null || assets?.Texture == null || assets.Calibration == null)
        {
            GUI.Label(new Rect(area.x + 25, area.y + 40, area.width - 50, 140),
                "SATELLITE LINK LOST\n\nNo eyes in the sky. Your combat log made it through.\n\n" + (assets?.Status ?? "No recorded map artwork."), text);
            return;
        }
        if (revealStarted == null) revealStarted = Time.unscaledTime;
        var progress = selected >= 0 || timeline == null ? 1 : Math.Min(1, (Time.unscaledTime - revealStarted.Value) / 5d);
        // At most twenty raster uploads per second during the reveal. Markers
        // use the same displayed progress; completed/focused layers stay cached.
        progress = Math.Floor(progress * 100) / 100;
        var frame = EncounterMapGeometry.Overview(area.width, area.height);
        var fatal = selected >= 0 ? group.Fatals[selected] : null;
        var hasPlayer = TryPoint(fatal?.PlayerPosition, out var player);
        var hasEnemy = TryPoint(fatal?.EnemyPosition, out var enemy);
        if (fatal != null && (hasPlayer || hasEnemy)) frame = EncounterMapGeometry.Focus(hasPlayer ? player : enemy, hasEnemy ? enemy : player, area.width, area.height);
        if (routeStrokes == null || !ReferenceEquals(routeCalibration, assets.Calibration))
        {
            routeCalibration = assets.Calibration;
            if (fatal != null)
                routeStrokes = hasPlayer && hasEnemy ? new[] { new EncounterRouteStroke(player, enemy, false) } : Array.Empty<EncounterRouteStroke>();
            else if (group.PathEdges.Count > MaximumDrawEdges) routeStrokes = Array.Empty<EncounterRouteStroke>();
            else
            {
                routeStrokes = new EncounterRouteStroke[group.PathEdges.Count];
                for (var index = 0; index < routeStrokes.Length; index++)
                {
                    var edge = group.PathEdges[index];
                    if (TryProject(edge.From.Position, out var from) && TryProject(edge.To.Position, out var to))
                        routeStrokes[index] = new EncounterRouteStroke(from, to, edge.Style == EncounterReplayEdgeStyle.Teleport);
                }
            }
        }
        GUI.BeginGroup(area);
        try
        {
            var corner = EncounterMapGeometry.ToScreen(new EncounterMapPoint(0, 1), frame, area.width, area.height);
            GUI.DrawTexture(new Rect((float)corner.X, (float)corner.Y, (float)(area.width / frame.Width), (float)(area.height / frame.Height)), assets.Texture);
            routeTexture?.Draw(routeStrokes, frame, new Rect(0, 0, area.width, area.height), fatal == null ? timeline : null, progress);
            if (fatal == null)
            {
                for (var index = 0; index < group.Fatals.Count; index++)
                {
                    if (timeline != null && progress < markerProgress[index]) continue;
                    var markerFatal = group.Fatals[index];
                    var anchor = markerFatal.Kind == EncounterReplayFatalKind.PlayerDeath ? markerFatal.PlayerPosition : markerFatal.EnemyPosition;
                    if (!TryPoint(anchor, out var point)) continue;
                    if (Marker(point, frame, area, (index + 1).ToString(CultureInfo.InvariantCulture), new Color(.13f, .43f, .62f))) SelectFatal(index, true);
                }
                if (group.PathEdges.Count > MaximumDrawEdges)
                    GUI.Label(new Rect(15, 15, area.width - 30, 60), "Route exceeds this prototype's 4,096-edge drawing budget. Encounter markers remain available.", text);
            }
            else
            {
                if (hasPlayer && hasEnemy)
                {
                    var midpoint = new EncounterMapPoint((player.X + enemy.X) / 2, (player.Y + enemy.Y) / 2);
                    var screen = EncounterMapGeometry.ToScreen(midpoint, frame, area.width, area.height);
                    var metres = Distance(fatal.PlayerPosition!.Position, fatal.EnemyPosition!.Position);
                    GUI.Box(new Rect((float)screen.X - 55, (float)screen.Y + 16, 110, 26), metres.ToString("0.0", CultureInfo.InvariantCulture) + " m (map)");
                }
                if (hasPlayer) Marker(player, frame, area, "P", new Color(.1f, .45f, .7f));
                if (hasEnemy) Marker(enemy, frame, area, "E", new Color(.8f, .15f, .25f));
                if (!hasEnemy || !hasPlayer) GUI.Label(new Rect(15, 15, area.width - 30, 56), "An exact participant position is unavailable. No connecting line is inferred.", text);
            }
        }
        finally { GUI.EndGroup(); }
    }

    private void DrawList(Rect area, GUIStyle text)
    {
        if (group == null) return;
        var totalHeight = group.Fatals.Count * 58f + (selected >= 0 ? 230 : 0);
        scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, area.width - 22, Math.Max(area.height, totalHeight)));
        try
        {
            var y = 0f;
            for (var index = 0; index < group.Fatals.Count; index++)
            {
                var fatal = group.Fatals[index];
                var rowHeight = 58 + (selected == index ? 230 : 0);
                if (y + rowHeight < scroll.y) { y += rowHeight; continue; }
                if (y > scroll.y + area.height) break;
                var row = new Rect(0, y, area.width - 25, 46);
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = selected == index ? new Color(1, .58f, .12f) : new Color(.16f, .35f, .47f);
                var offset = Math.Max(0, fatal.Time - (group.StartTime ?? fatal.Time));
                if (GUI.Button(row, fatalLabels[index] + "    " + TimeSpan.FromSeconds(offset).ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture)))
                    SelectFatal(selected == index ? -1 : index, false);
                GUI.backgroundColor = previous;
                y += 58;
                if (selected != index) continue;
                GUI.Label(new Rect(10, y, area.width - 45, 220), selectedDetails, text);
                y += 230;
            }
            if (group.Fatals.Count == 0) GUI.Label(new Rect(12, 12, area.width - 46, 80), "No confirmed encounter deaths in this map's recording.", text);
        }
        finally { GUI.EndScrollView(); }
    }

    private void SelectFatal(int index, bool fromMap)
    {
        selected = index;
        routeStrokes = null;
        // Closing a selected encounter returns to the completed overview.
        revealStarted = Time.unscaledTime - 5;
        if (group == null || index < 0) return;
        if (fromMap) scroll.y = Math.Max(0, index * 58 - 58);
        var fatal = group.Fatals[index];
        selectedDetails = "Observed source: " + fatal.Source.Kind + "\nWeapon: " + ItemName(fatal.Source.WeaponId)
            + "\nAmmo: " + (fatal.Source.AmmoAgreement == true ? ItemName(fatal.Source.LoadedAmmoId) : "Unavailable / unproven")
            + "\n" + (fatal.PlayerPosition != null && fatal.EnemyPosition != null ? "Positions captured before fatal cleanup." : "Exact fatal positions partly unavailable.")
            + "\nActor ID: " + (fatal.Target.Id?.ToString(CultureInfo.InvariantCulture) ?? "Unavailable")
            + "\nDamage and loot totals are still being qualified. This evidence viewer does not infer them."
            + (fatal.Kind == EncounterReplayFatalKind.OtherPlayerRelatedDeath ? "\nPlayer final-blow credit is unproven." : string.Empty);
    }

    private static string FatalLabel(EncounterReplayFatal fatal)
    {
        var actor = fatal.Kind == EncounterReplayFatalKind.PlayerDeath ? fatal.Source.Physical : fatal.Target;
        var name = actor.PresetKey.Length == 0 ? "Unattributed" : actor.PresetKey;
        if (LocalizationManager.Initialized && LocalizationManager.DataModel != null && actor.PresetKey.Length > 0)
        {
            var translated = LocalizationManager.GetPlainText(actor.PresetKey);
            if (!string.IsNullOrWhiteSpace(translated) && translated != "*" + actor.PresetKey + "*") name = translated;
        }
        return fatal.Kind == EncounterReplayFatalKind.PlayerDeath ? "You died • " + name
            : name + (fatal.Kind == EncounterReplayFatalKind.PlayerKill ? " killed" : " died • attribution unproven");
    }

    private string ItemName(int? id) => id > 0 ? names.Names.Get("duckov:item:" + id.Value.ToString(CultureInfo.InvariantCulture), "Item " + id.Value.ToString(CultureInfo.InvariantCulture)) : "Unavailable";
    private bool TryPoint(EncounterReplayEndpoint? endpoint, out EncounterMapPoint result)
    {
        result = default;
        return endpoint != null && group != null && EncounterPathMath.NativeSceneId(endpoint.LogicalScene) == group.NativeMapId && TryProject(endpoint.Position, out result);
    }
    private bool TryProject(EncounterReplayVector point, out EncounterMapPoint result)
    {
        result = default;
        // Rendering bound only; source coordinates stay exact in the recording.
        return assets?.Calibration != null && EncounterMapGeometry.TryProject(assets.Calibration, point.X, point.Z, out result)
            && Math.Abs(result.X) < 1000 && Math.Abs(result.Y) < 1000;
    }
    private static double Distance(EncounterReplayVector from, EncounterReplayVector to) => Math.Sqrt((from.X - to.X) * (from.X - to.X) + (from.Z - to.Z) * (from.Z - to.Z));

    private static bool Marker(EncounterMapPoint point, EncounterMapFrame frame, Rect area, string label, Color color)
    {
        var screen = EncounterMapGeometry.ToScreen(point, frame, area.width, area.height);
        var prior = GUI.backgroundColor; GUI.backgroundColor = color;
        try { return GUI.Button(new Rect((float)screen.X - 14, (float)screen.Y - 14, 28, 28), label); }
        finally { GUI.backgroundColor = prior; }
    }
    private static void Paint(Rect area, Color color)
    { var prior = GUI.color; GUI.color = color; GUI.DrawTexture(area, Texture2D.whiteTexture); GUI.color = prior; }

    internal void Close()
    {
        if (!IsOpen && shield == null) return;
        IsOpen = false; epoch++; publishOpen(false);
        if (shield != null) { shield.SetActive(false); Object.Destroy(shield); shield = null; }
        assets?.Dispose(); assets = null; capture = null; group = null; timeline = null;
        routeTexture?.Dispose(); routeTexture = null; routeStrokes = null; routeCalibration = null;
        files = Array.Empty<string>(); fatalLabels = Array.Empty<string>(); markerProgress = Array.Empty<double>();
    }
    public void Dispose() { if (disposed) return; Close(); disposed = true; names.Dispose(); }
    private sealed class LoadResult
    {
        internal int Epoch, Index;
        internal string[] Files = Array.Empty<string>();
        internal EncounterReplayCapture? Capture;
        internal string? Error;
    }
}
#endif
