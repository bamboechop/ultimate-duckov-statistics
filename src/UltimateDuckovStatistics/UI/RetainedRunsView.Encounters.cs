using System.Globalization;
using Duckov.UI;
using Duckov.UI.Animations;
using UltimateDuckovStatistics.Core.Tracking;
using TMPro;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private sealed partial class RunsView
    {
        private class EncounterMarker
        {
            internal RectTransform Root = null!;
            internal TextMeshProUGUI Label = null!;
        }
        private sealed class EncounterButton : EncounterMarker
        {
            internal Button Button = null!;
            internal int Index = -1;
        }
        private sealed class RoutePixels
        {
            internal EncounterRouteRaster Raster = null!;
            internal double Progress;
            internal int Epoch;
        }
        private RectTransform encounterTabs = null!, encounterRoot = null!, mapPane = null!, mapSelectors = null!;
        private EncounterButton detailsTab = null!, mapTab = null!;
        private EncounterMarker playerPin = null!, enemyPin = null!;
        private CombatTooltip encounterTooltip = null!;
        private TextMeshProUGUI mapStatus = null!, distanceLabel = null!, encounterEmpty = null!;
        private RectTransform distanceBox = null!, encounterBody = null!;
        private string distanceEncounterId = "";
        private float distanceTextWidth;
        private TextMeshProUGUI encounterTitleMeasure = null!, encounterTimeMeasure = null!;
        private static readonly Color EncounterBlue = new Color32(39, 85, 118, 255);
        private static readonly Color EncounterRed = new Color32(247, 85, 102, 255);
        private static readonly Color EncounterMuted = new Color32(177, 177, 177, 255);
        private static readonly Color EncounterOrange = new Color32(255, 159, 46, 255);
        private float renderedEncounterOffset = float.NaN;
        private TextMeshProUGUI encounterCoverage = null!;
        private float encounterNoticeHeight;
        private RawImage mapImage = null!, pathImage = null!;
        private ScrollRegion encounterList = null!;
        private readonly List<EncounterButton> mapButtons = new(), eventPins = new();
        private readonly List<EncounterFeedRow> eventRows = new();
        private readonly List<(RectTransform Root, Image Image, TextMeshProUGUI Label)> lootTiles = new();
        private readonly EncounterMapPresentationAssets mapAssets = new(
            EncounterPaths.MapDirectory, Debug.LogWarning);
        private StatisticsPanelProjection? encounterSource;
        private Task<StoredEncounterRun>? encounterLoad;
        private EncounterRunSelection encounterSelection = new(StoredEncounterRun.Empty);
        private StoredEncounterMap[] encounterMaps => encounterSelection.Run.Visits;
        private string encounterBinding = "", loadedBinding = "";
        private int selectedMap => encounterSelection.VisitIndex;
        private int selectedEvent => encounterSelection.EventIndex;
        private int mapEpoch;
        private bool mapSelected, startRevealOnArtwork, encounterReadFailed;
        private float mapWidth, mapHeight, listWidth, listHeight;
        private float[] eventTops = Array.Empty<float>(), eventHeights = Array.Empty<float>();
        private double revealStart, displayedProgress = -1, requestedProgress = -1;
        private Task<RoutePixels>? routeRender;
        private Texture2D? routeImage;
        private EncounterMapFrame mapFrame;
        private StoredEncounterMap? CurrentMap => encounterSelection.CurrentVisit;
        private EncounterRecord? CurrentEncounter => encounterSelection.CurrentEvent;

        private EncounterButton EncounterControl(RectTransform parent, string name, float size, Action<EncounterButton> click, bool tab = false)
        {
            var rect = Node(parent, name);
            var graphic = rect.gameObject.AddComponent<ProceduralImage>(); graphic.raycastTarget = true;
            if (tab)
            {
                var shape = rect.gameObject.AddComponent<OnlyOneEdgeModifier>();
                shape.Side = OnlyOneEdgeModifier.ProceduralImageEdge.Top; shape.Radius = 20;
            }
            else rect.gameObject.AddComponent<UniformModifier>().Radius = 10;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic;
            rect.gameObject.AddComponent<ButtonAnimation>();
            if (!tab) AddButtonFeedback(button);
            var result = new EncounterButton { Root = rect, Button = button, Label = Text(rect, name + "Label", size) };
            result.Label.alignment = TextAlignmentOptions.MidlineLeft;
            button.onClick.AddListener(() => click(result));
            return result;
        }

        private void CreateEncounterView()
        {
            encounterTooltip = new CombatTooltip(root, typography.Font, material, fitContentWidth: true);
            outer.Scroll.onValueChanged.AddListener(_ => encounterTooltip.Dismiss());
            encounterTabs = Node(detailPanel, "RunDetailTabs");
            detailsTab = EncounterControl(encounterTabs, "Details", 36, _ => SelectEncounterTab(false), tab: true);
            mapTab = EncounterControl(encounterTabs, "MapAndKills", 36, _ => SelectEncounterTab(true), tab: true);
            var rule = Node(encounterTabs, "Underline").gameObject.AddComponent<Image>();
            rule.color = EncounterOrange; rule.raycastTarget = false;
            rule.rectTransform.anchorMin = new Vector2(0, 0); rule.rectTransform.anchorMax = new Vector2(1, 0);
            rule.rectTransform.sizeDelta = new Vector2(0, 3); rule.rectTransform.anchoredPosition = Vector2.zero;
            encounterRoot = Node(detailPanel, "MapAndKillsContent");
            mapSelectors = Node(encounterRoot, "MapSelector");
            mapPane = Panel(encounterRoot, "RecordedMap", 12); mapPane.gameObject.AddComponent<RectMask2D>();
            mapPane.GetComponent<ProceduralImage>().color = Color.clear;
            mapImage = Node(mapPane, "MapArtwork").gameObject.AddComponent<RawImage>(); mapImage.raycastTarget = false;
            pathImage = Node(mapPane, "Route").gameObject.AddComponent<RawImage>(); pathImage.raycastTarget = false;
            mapStatus = Text(mapPane, "SatelliteStatus", 28); mapStatus.alignment = TextAlignmentOptions.Center;
            playerPin = CreateLocationPin("Player", EncounterBlue);
            enemyPin = CreateLocationPin("Enemy", EncounterRed);
            playerPin.Label.text = "P"; enemyPin.Label.text = "E";
            distanceBox = Panel(mapPane, "DistanceBox", 3); distanceBox.GetComponent<ProceduralImage>().color = new Color(0, 0, 0, .65f);
            distanceLabel = Text(distanceBox, "Distance", 16); distanceLabel.alignment = TextAlignmentOptions.Center;
            encounterList = new ScrollRegion(encounterRoot, "EncounterHistory");
            encounterList.Scroll.onValueChanged.AddListener(_ => encounterTooltip.Dismiss());
            encounterBody = Node(encounterList.Content, "EncounterDetails");
            encounterTitleMeasure = Text(encounterRoot, "EncounterTitleMeasure", 20); encounterTitleMeasure.gameObject.SetActive(false);
            encounterTimeMeasure = Text(encounterRoot, "EncounterTimeMeasure", 16); encounterTimeMeasure.gameObject.SetActive(false);
            encounterEmpty = Text(encounterList.Content, "EncounterEmpty", 26);
            encounterCoverage = Text(encounterList.Content, "EncounterCoverage", 24);
            encounterCoverage.color = new Color(1, .75f, .4f);
            encounterRoot.gameObject.SetActive(false);
        }

        public void SetEncounterSource(StatisticsPanelProjection? source) { encounterSource = source; }

        private void BindEncounterRun()
        {
            detailsTab.Label.text = UiText.Get("ui.encounters_details"); mapTab.Label.text = UiText.Get("ui.encounters_tab");
            playerPin.Root.GetComponent<CombatTooltipTrigger>().Bind(encounterTooltip, UiText.Get("ui.encounters_player"));
            enemyPin.Root.GetComponent<CombatTooltipTrigger>().Bind(encounterTooltip, UiText.Get("ui.encounters_enemy"));
            mapStatus.text = UiText.Get("ui.encounters_no_signal") + "\n\n" + UiText.Get("ui.encounters_no_signal_body");
            var binding = selection.Snapshot?.GenerationId + "/" + selection.SelectedId;
            if (binding != encounterBinding)
            {
                encounterBinding = binding; loadedBinding = ""; encounterSelection = new(StoredEncounterRun.Empty);
                encounterLoad = null; encounterReadFailed = false;
                encounterList.SetOffset(0);
                mapAssets.Request(""); ResetMapRendering();
            }
            if (mapSelected && loadedBinding != binding && encounterLoad == null) LoadEncounterRun();
            RebuildEncounterList();
        }

        private void LoadEncounterRun()
        {
            encounterReadFailed = false;
            var id = selection.SelectedId; var history = encounterSource?.Profile.EncounterHistory;
            loadedBinding = encounterBinding;
            if (id == null) return;
            encounterLoad = Task.Run(() => StoredEncounterRun.Build(EncounterHistoryReader.Read(history, id)));
        }

        private void SelectEncounterTab(bool map)
        {
            mapSelected = map; HideEvidence(false);
            if (map && encounterLoad == null) { loadedBinding = ""; LoadEncounterRun(); }
            if (!map) { mapAssets.Request(""); ResetMapRendering(); }
            dirty = true;
        }

        private void SelectEncounterMap(int index)
        {
            encounterSelection.SelectVisit(index);
            mapAssets.Request(CurrentMap?.ArtworkKey ?? "");
            ResetMapRendering(); RebuildEncounterList(); dirty = true;
        }

        private void SelectEncounter(int index)
        {
            encounterSelection.ToggleEvent(index);
            mapAssets.Request(CurrentMap?.ArtworkKey ?? "");
            ResetMapRendering(); revealStart = double.NegativeInfinity; // Closing returns to the completed overview.
            RebuildEncounterList();
            if (selectedEvent >= 0 && selectedEvent < eventTops.Length) encounterList.SetOffset(eventTops[selectedEvent]);
            dirty = true;
        }

        private void ResetMapRendering()
        {
            encounterTooltip.Dismiss();
            startRevealOnArtwork = true;
            distanceEncounterId = "";
            mapEpoch++; displayedProgress = requestedProgress = -1; revealStart = Time.realtimeSinceStartupAsDouble;
            pathImage.texture = null;
            if (routeImage != null) UnityEngine.Object.Destroy(routeImage);
            routeImage = null;
            foreach (var marker in eventPins) marker.Root.gameObject.SetActive(false);
            playerPin.Root.gameObject.SetActive(false); enemyPin.Root.gameObject.SetActive(false); distanceBox.gameObject.SetActive(false);
        }

        private void LayoutEncounters(float historyWidth, float historyHeight, float detailWidth, float detailY, bool stackedLayout)
        {
            Place(encounterTabs, 30, 22, detailWidth - 60, 58);
            var first = Math.Max(160, detailsTab.Label.GetPreferredValues().x + 56);
            var second = Math.Max(225, mapTab.Label.GetPreferredValues().x + 56);
            Place(detailsTab.Root, 30, 3, first, 55); Place(mapTab.Root, first + 40, 3, second, 55);
            Place(detailsTab.Label.rectTransform, 28, 0, first - 56, 55); Place(mapTab.Label.rectTransform, 28, 0, second - 56, 55);
            detailsTab.Root.GetComponent<ProceduralImage>().color = mapSelected ? Color.clear : EncounterOrange;
            mapTab.Root.GetComponent<ProceduralImage>().color = mapSelected ? EncounterOrange : Color.clear;
            fixedDetail.gameObject.SetActive(!mapSelected); encounterRoot.gameObject.SetActive(mapSelected);
            if (!mapSelected) return;
            var available = Math.Max(500, height - EncounterTabHeight - 60);
            var panelHeight = stackedLayout ? available + 130 : height;
            Place(detailPanel, stackedLayout ? 0 : historyWidth + 40, detailY, detailWidth, panelHeight);
            Place(encounterRoot, 30, 102, detailWidth - 60, panelHeight - 132);
            outer.Size(0, 0, width, height, stackedLayout ? detailY + panelHeight : height);
            var contentWidth = detailWidth - 60;
            var newWidth = (contentWidth - 50) * .46f;
            var x = 0f; var y = 0f; var selectorRowHeight = 36f;
            while (mapButtons.Count < encounterMaps.Length)
                mapButtons.Add(EncounterControl(mapSelectors, "Map" + mapButtons.Count, 20, control => SelectEncounterMap(control.Index)));
            for (var i = 0; i < mapButtons.Count; i++)
            {
                var button = mapButtons[i]; button.Root.gameObject.SetActive(i < encounterMaps.Length);
                if (i >= encounterMaps.Length) continue;
                button.Index = i;
                var visit = encounterMaps[i];
                var mapName = encounterSource?.Names.Get(visit.MapId, EncounterPathMath.NativeSceneId(visit.MapId)) ?? visit.MapId;
                button.Label.text = mapName;
                var w = Math.Min(newWidth, button.Label.GetPreferredValues().x + 20);
                if (x + w > newWidth && x > 0) { x = 0; y += selectorRowHeight + 8; selectorRowHeight = 36; }
                var buttonHeight = Math.Max(36, button.Label.GetPreferredValues(mapName, Math.Max(1, w - 20), float.PositiveInfinity).y + 10);
                selectorRowHeight = Math.Max(selectorRowHeight, buttonHeight);
                Place(button.Root, x, y, w, buttonHeight); Place(button.Label.rectTransform, 10, 0, w - 20, buttonHeight);
                button.Root.GetComponent<ProceduralImage>().color = i == selectedMap ? EncounterOrange : Color.clear;
                x += w + 10;
            }
            Place(mapSelectors, 0, 0, newWidth, y + selectorRowHeight);
            var top = y + selectorRowHeight + 22;
            var newHeight = Math.Min(newWidth, Math.Max(250, panelHeight - 132 - top));
            if (mapWidth != newWidth || mapHeight != newHeight) { mapWidth = newWidth; mapHeight = newHeight; ResetMapRendering(); }
            Place(mapPane, 0, top, mapWidth, mapHeight);
            listWidth = contentWidth - mapWidth - 50; listHeight = panelHeight - 132;
            Place(mapStatus.rectTransform, 25, 25, mapWidth - 50, mapHeight - 50);
            Place(pathImage.rectTransform, 0, 0, mapWidth, mapHeight);
            RebuildEncounterList();
            encounterList.Size(mapWidth + 50, 0, listWidth, listHeight, TotalEventHeight());
        }

        private string EnemyName(EncounterRecord record) => encounterSource?.Names.Get(
            "duckov:target:preset:" + CombatObservationPolicy.CreateStableIdentityToken(record.Encounter!.EnemyPresetKey ?? ""), record.Encounter.EnemyPresetKey) is { Length: > 0 } name ? name : UiText.Get("ui.unknown");
        private string ItemName(int? id) => id.HasValue ? encounterSource?.Names.Get("duckov:item:" + id.Value, "#" + id.Value) ?? "#" + id.Value : UiText.Get("ui.unavailable");
        private static string Number(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
        private static string EventTime(double seconds) => StoredEncounterMap.FormatEventTime(seconds);
        private string EventTitle(EncounterRecord record) => string.Format(CultureInfo.CurrentCulture, UiText.Get(record.Encounter!.Outcome switch
        { EncounterOutcome.PlayerKill => "ui.encounters_kill_title", EncounterOutcome.PlayerDeath => "ui.encounters_death_title", _ => "ui.encounters_other_title" }), EnemyName(record));

        private void RebuildEncounterList()
        {
            if (encounterList == null || listWidth <= 0) return;
            var events = encounterSelection.Run.Events; var count = events.Length;
            var notice = encounterSelection.Run.CoverageNoticeKey;
            encounterCoverage.gameObject.SetActive(notice != null);
            encounterCoverage.text = notice == null ? "" : UiText.Get(notice);
            encounterNoticeHeight = notice == null ? 0 : encounterCoverage.GetPreferredValues(encounterCoverage.text, listWidth - 24, float.PositiveInfinity).y + 24;
            Put(encounterCoverage, 12, 12, listWidth - 24);
            eventTops = new float[count]; eventHeights = new float[count];
            eventHeaderHeights = new float[count]; eventTimeWidths = new float[count];
            eventTitles = new string[count]; eventTimes = new string[count];
            var bodyHeight = LayoutEncounterBody();
            var top = encounterNoticeHeight;
            for (var i = 0; i < count; i++)
            {
                eventTops[i] = top; eventTitles[i] = EventTitle(events[i]);
                eventTimes[i] = EventTime(events[i].Encounter!.EndedSeconds ?? 0);
                eventTimeWidths[i] = encounterTimeMeasure.GetPreferredValues(eventTimes[i]).x + 4;
                var titleWidth = Math.Max(40, listWidth - 88 - (listWidth < 310 ? 0 : eventTimeWidths[i] + 20));
                var titleHeight = encounterTitleMeasure.GetPreferredValues(eventTitles[i], titleWidth, float.PositiveInfinity).y;
                eventHeaderHeights[i] = Math.Max(52, titleHeight + 20 + (listWidth < 310 ? 22 : 0));
                eventHeights[i] = eventHeaderHeights[i];
                if (i == selectedEvent)
                {
                    Place(encounterBody, 20, top + eventHeaderHeights[i] + 16, listWidth - 40, bodyHeight);
                    eventHeights[i] += bodyHeight + 32;
                }
                top += eventHeights[i] + 10;
            }
            encounterEmpty.gameObject.SetActive(count == 0);
            encounterEmpty.text = UiText.Get(encounterLoad != null ? "ui.encounters_loading" : encounterReadFailed ? "ui.encounters_read_failed"
                : notice != null ? "ui.encounters_no_recorded_kills" : encounterMaps.Length == 0 ? "ui.encounters_no_history" : "ui.encounters_no_kills");
            Put(encounterEmpty, 12, encounterNoticeHeight + 12, listWidth - 24);
            renderedEncounterOffset = float.NaN;
            RenderEncounterRows();
        }

        private float TotalEventHeight() => eventTops.Length == 0 ? encounterNoticeHeight + 180 : eventTops[eventTops.Length - 1] + eventHeights[eventHeights.Length - 1] + 12;

        private void RenderEncounterRows()
        {
            if (renderedEncounterOffset == encounterList.Offset) return;
            renderedEncounterOffset = encounterList.Offset;
            var events = encounterSelection.Run.Events; var active = 0;
            for (var i = 0; i < events.Length; i++)
            {
                var top = eventTops[i]; if (top + eventHeaderHeights[i] < encounterList.Offset || top > encounterList.Offset + listHeight) continue;
                if (active == eventRows.Count) eventRows.Add(CreateEncounterFeedRow());
                var row = eventRows[active++]; row.Control.Index = i; row.Control.Root.gameObject.SetActive(true);
                row.Control.Label.text = eventTitles[i]; row.Time.text = eventTimes[i];
                row.Time.color = selectedEvent == i ? Color.white : EncounterMuted;
                row.Number.text = (i + 1).ToString(CultureInfo.InvariantCulture);
                row.Number.fontSize = i >= 999 ? 13 : i >= 99 ? 16 : 20;
                var h = eventHeaderHeights[i]; var clockWidth = eventTimeWidths[i]; var narrow = listWidth < 310;
                Place(row.Control.Root, 0, top, listWidth, h);
                Place(row.Badge, 20, (h - 32) * .5f, 32, 32);
                Place(row.Number.rectTransform, 0, 0, 32, 32);
                Place(row.Control.Label.rectTransform, 70, 10, Math.Max(40, listWidth - 88 - (narrow ? 0 : clockWidth + 20)), h - 20 - (narrow ? 22 : 0));
                Place(row.Time.rectTransform, listWidth - clockWidth - 18, narrow ? h - 27 : 0, clockWidth, narrow ? 22 : h);
                row.Control.Root.GetComponent<ProceduralImage>().color = selectedEvent == i ? EncounterOrange : Color.clear;
            }
            for (var i = active; i < eventRows.Count; i++) eventRows[i].Control.Root.gameObject.SetActive(false);
        }

        private void TickEncounters()
        {
            if (!mapSelected) return;
            if (encounterLoad?.IsCompleted == true)
            {
                try { encounterSelection = new(encounterLoad.GetAwaiter().GetResult()); }
                catch (Exception exception) { Debug.LogWarning("UDS encounter history read: " + exception.Message); encounterSelection = new(StoredEncounterRun.Empty); encounterReadFailed = true; }
                encounterLoad = null; SelectEncounterMap(0);
            }
            mapAssets.Tick(); RenderEncounterRows(); encounterList.Cues();
            var map = CurrentMap;
            var available = mapAssets.Texture != null && map?.Calibration != null;
            mapImage.gameObject.SetActive(available); pathImage.gameObject.SetActive(available && pathImage.texture != null);
            mapStatus.gameObject.SetActive(!available);
            if (!available || mapWidth <= 0 || mapHeight <= 0)
            {
                foreach (var pin in eventPins) pin.Root.gameObject.SetActive(false);
                playerPin.Root.gameObject.SetActive(false); enemyPin.Root.gameObject.SetActive(false); distanceBox.gameObject.SetActive(false);
                return;
            }
            if (startRevealOnArtwork)
            { if (double.IsFinite(revealStart) && selectedEvent < 0) revealStart = Time.realtimeSinceStartupAsDouble; startRevealOnArtwork = false; }
            var record = CurrentEncounter;
            var focus = EncounterMapFocus.Create(record?.Encounter, map!, mapWidth, mapHeight);
            var p = focus.Player; var e = focus.Enemy;
            mapFrame = focus.Frame;
            mapImage.texture = mapAssets.Texture;
            Place(mapImage.rectTransform, (float)(-mapFrame.MinX / mapFrame.Width * mapWidth),
                (float)((mapFrame.MinY + mapFrame.Height - 1) / mapFrame.Height * mapHeight),
                (float)(mapWidth / mapFrame.Width), (float)(mapHeight / mapFrame.Height));
            var progress = selectedEvent >= 0 ? 1 : Math.Min(1, Math.Floor((Time.realtimeSinceStartupAsDouble - revealStart) / 5 * 100) / 100);
            if (routeRender?.IsCompleted == true)
            {
                try
                {
                    var rendered = routeRender.GetAwaiter().GetResult();
                    if (rendered.Epoch == mapEpoch)
                    {
                        if (routeImage == null) routeImage = new Texture2D(rendered.Raster.Width, rendered.Raster.Height, TextureFormat.RGBA32, false)
                        { name = "UDS retained encounter route", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                        routeImage.LoadRawTextureData(rendered.Raster.Pixels); routeImage.Apply(false, false);
                        pathImage.texture = routeImage; displayedProgress = rendered.Progress;
                    }
                }
                catch (Exception exception) { Debug.LogWarning("UDS route rendering: " + exception.Message); }
                routeRender = null;
            }
            if (routeRender == null && requestedProgress != progress)
            {
                requestedProgress = progress;
                var strokes = record == null ? map!.Strokes : focus.ConnectorAvailable
                    ? new[] { new EncounterRouteStroke(p, e, false) } : Array.Empty<EncounterRouteStroke>();
                var frame = mapFrame; var w = mapWidth; var h = mapHeight; var epoch = mapEpoch;
                var timeline = record != null ? null : map!.Timeline;
                routeRender = Task.Run(() => { var raster = new EncounterRouteRaster(w, h); raster.Render(strokes, frame, timeline, progress);
                    return new RoutePixels { Raster = raster, Progress = progress, Epoch = epoch }; });
            }
            while (eventPins.Count < map!.Events.Length)
            {
                var marker = EncounterControl(mapPane, "EncounterMarker" + eventPins.Count, 16,
                    control => SelectEncounter(control.Index));
                marker.Root.GetComponent<UniformModifier>().Radius = 12;
                marker.Label.alignment = TextAlignmentOptions.Center; eventPins.Add(marker);
            }
            for (var i = 0; i < eventPins.Count; i++)
            {
                var marker = eventPins[i];
                var visible = selectedEvent < 0 && i < map.Events.Length && displayedProgress >= 0
                    && (displayedProgress >= 1 || displayedProgress > 0 && map.Timeline.MarkerProgress(map.Events[i].Encounter!.EndedSeconds ?? 0) <= displayedProgress);
                if (!visible || !EncounterMapFocus.TryProjectOverview(map.Events[i].Encounter!, map, out var point))
                { marker.Root.gameObject.SetActive(false); continue; }
                var globalIndex = encounterSelection.Run.EventIndex(map.Events[i].Id);
                if (marker.Index != globalIndex)
                {
                    marker.Index = globalIndex; marker.Label.text = (globalIndex + 1).ToString(CultureInfo.InvariantCulture);
                    marker.Label.fontSize = globalIndex >= 999 ? 10 : globalIndex >= 99 ? 12 : 16;
                }
                Pin(marker, point, mapFrame, map.Events[i].Encounter!.Outcome == EncounterOutcome.PlayerDeath);
            }
            if (focus.PlayerAvailable) Pin(playerPin, p, mapFrame, false, location: true);
            else playerPin.Root.gameObject.SetActive(false);
            if (focus.EnemyAvailable) Pin(enemyPin, e, mapFrame, true, location: true);
            else enemyPin.Root.gameObject.SetActive(false);
            distanceBox.gameObject.SetActive(focus.ConnectorAvailable);
            if (focus.ConnectorAvailable)
            {
                if (distanceEncounterId != record!.Id)
                {
                    distanceEncounterId = record.Id;
                    var a = record.Encounter!.PlayerPosition!; var b = record.Encounter.EnemyPosition!;
                    var dx = (double)b.X - a.X; var dz = (double)b.Z - a.Z;
                    distanceLabel.text = Number(Math.Sqrt(dx * dx + dz * dz)) + " m";
                    distanceTextWidth = distanceLabel.GetPreferredValues().x;
                }
                var labelWidth = Math.Min(mapWidth - 16, distanceTextWidth + 12);
                const float labelHeight = 24;
                var labelPosition = EncounterMapGeometry.DistanceLabel(EncounterMapGeometry.ToScreen(p, mapFrame, mapWidth, mapHeight),
                    EncounterMapGeometry.ToScreen(e, mapFrame, mapWidth, mapHeight), labelWidth, labelHeight, mapWidth, mapHeight);
                Place(distanceBox, (float)labelPosition.X, (float)labelPosition.Y, labelWidth, labelHeight);
                Place(distanceLabel.rectTransform, 6, 0, labelWidth - 12, labelHeight);
            }
        }

        private void Pin(EncounterMarker pin, EncounterMapPoint point, EncounterMapFrame frame, bool hostile, bool location = false)
        {
            var screen = EncounterMapGeometry.ToScreen(point, frame, mapWidth, mapHeight);
            if (screen.X < 0 || screen.Y < 0 || screen.X > mapWidth || screen.Y > mapHeight)
            { pin.Root.gameObject.SetActive(false); return; }
            pin.Root.gameObject.SetActive(true);
            if (location)
            {
                Place(pin.Root, (float)screen.X - 14, (float)screen.Y - 36, 28, 36);
                Place(pin.Label.rectTransform, 0, 0, 28, 28);
            }
            else
            {
                Place(pin.Root, (float)screen.X - 12, (float)screen.Y - 12, 24, 24);
                Place(pin.Label.rectTransform, 0, 0, 24, 24);
                pin.Root.GetComponent<ProceduralImage>().color = hostile ? EncounterRed : EncounterBlue;
            }
        }
        private void DisposeEncounters()
        {
            encounterTooltip.Dispose();
            mapAssets.Dispose(); encounterList.Dispose();
            if (routeImage != null) UnityEngine.Object.Destroy(routeImage);
            encounterLoad = null; routeRender = null;
        }
    }
}
