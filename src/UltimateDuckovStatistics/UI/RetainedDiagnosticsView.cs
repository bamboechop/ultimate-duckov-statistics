using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private DiagnosticsView? diagnosticsView;
    public void RefreshDiagnostics(DiagnosticsPresentation? snapshot) => diagnosticsView?.Refresh(snapshot);

    private sealed class DiagnosticsView : IDisposable
    {
        private sealed class Element
        {
            public RectTransform Rect = null!;
            public TextMeshProUGUI? Text;
            public Button? Button;
            public bool Used;
        }
        private readonly RectTransform root;
        private readonly ScrollRegion outer, left, right;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly CombatNativeTextMeasurement measure;
        private readonly PanelOperationController operations;
        private readonly Action changeHotkey, focusTabs;
        private readonly Func<bool> copyExportPath, copyDataPath;
        private readonly RectTransform copyFeedback;
        private readonly TextMeshProUGUI copyFeedbackText;
        private float copyFeedbackUntil;
        private readonly RectTransform operationFeedback;
        private readonly TextMeshProUGUI operationFeedbackText;
        private PanelOperationNotice? shownOperationNotice;
        private float operationFeedbackUntil;
        private readonly string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private readonly DiagnosticsSelection selection = new();
        private readonly Dictionary<string, Element> elements = new(StringComparer.Ordinal);
        private readonly List<string> leftButtons = new(), rightButtons = new();
        private readonly TextMeshProUGUI unavailable;
        private float width, height, pixels;
        private bool dirty = true, disposed;
        private string? restoreFocus;
        private PanelOperationNotice? lastNotice;
        private PanelOperation lastOperation;
        private bool lastCanStart;
        private static Color Muted => new Color32(177, 177, 177, 255);
        private static Color Orange => new Color32(255, 160, 50, 255);
        private static Color Blue => new Color32(72, 195, 242, 255);
        private static Color Red => new Color32(250, 73, 100, 255);
        private static Color Green => new Color32(113, 192, 62, 255);

        public DiagnosticsView(RectTransform parent, NativeHeaderTitleTypography typography, Material material,
            PanelOperationController operations, Action changeHotkey, Func<bool> copyExportPath, Func<bool> copyDataPath, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.operations = operations;
            shownOperationNotice = operations.LastNotice;
            this.changeHotkey = changeHotkey; this.copyExportPath = copyExportPath; this.copyDataPath = copyDataPath; this.focusTabs = focusTabs;
            root = Node(parent, "DiagnosticsContentView");
            outer = new ScrollRegion(root, "DiagnosticsOuter", radius: 20); RoundedMask(outer);
            left = new ScrollRegion(outer.Content, "DataAndTechnicalColumn", radius: 20); RoundedMask(left);
            right = new ScrollRegion(outer.Content, "TrackingHealthColumn", radius: 20); RoundedMask(right);
            ConfigureScroll(outer, null); ConfigureScroll(left, leftButtons); ConfigureScroll(right, rightButtons);
            measure = new CombatNativeTextMeasurement(CreateText(root, "Measurement", 28));
            unavailable = CreateText(root, "Unavailable", 30);
            unavailable.text = UiText.Get("ui.profile_unavailable");
            copyFeedback = Node(root, "CopyFeedback");
            copyFeedback.gameObject.AddComponent<ProceduralImage>().color = new Color(0, 0, 0, .95f);
            copyFeedback.gameObject.GetComponent<ProceduralImage>().raycastTarget = false;
            copyFeedback.gameObject.AddComponent<UniformModifier>().Radius = 10;
            copyFeedbackText = CreateText(copyFeedback, "Message", 23);
            copyFeedback.gameObject.SetActive(false);
            operationFeedback = Node(root, "OperationFeedback");
            operationFeedback.gameObject.AddComponent<ProceduralImage>().raycastTarget = false;
            operationFeedback.gameObject.AddComponent<UniformModifier>().Radius = 20;
            operationFeedbackText = CreateText(operationFeedback, "Message", 28);
            operationFeedback.gameObject.SetActive(false);
        }
        private void ConfigureScroll(ScrollRegion scroll, List<string>? buttons)
        {
            scroll.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.Rect.GetComponent<RunsFocusHandler>().Move = direction =>
            {
                if (direction == MoveDirection.Left) { if (scroll == right) FocusFirst(); else focusTabs(); }
                else if (direction == MoveDirection.Right) { if (buttons?.Count > 0) Focus(buttons[0]); else FocusHealth(); }
                else if (direction == MoveDirection.Up && scroll.Offset <= 0) focusTabs();
                else if (direction == MoveDirection.Up || direction == MoveDirection.Down)
                    ((RunsScrollRect)scroll.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * scroll.Scroll.scrollSensitivity);
            };
        }
        public void Refresh(DiagnosticsPresentation? next)
        {
            if (disposed) return;
            Capture(); RememberFocus();
            if (next == null || next.GenerationId != selection.Snapshot?.GenerationId)
            { restoreFocus = null; copyFeedback.gameObject.SetActive(false); operationFeedback.gameObject.SetActive(false); }
            selection.Refresh(next);
            if (next == null) foreach (var element in elements.Values) element.Rect.gameObject.SetActive(false);
            outer.Rect.gameObject.SetActive(next != null); unavailable.gameObject.SetActive(next == null);
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            if (focused != null && focused.transform.IsChildOf(root) && !focused.activeInHierarchy) focusTabs();
            dirty = true;
        }
        public void SetVisible(bool visible) { if (!visible) { Capture(); copyFeedback.gameObject.SetActive(false); } root.gameObject.SetActive(visible); }
        private void Capture()
        {
            if (dirty || selection.Snapshot == null) return;
            selection.Capture("outer", outer.Offset); selection.Capture("left", left.Offset); selection.Capture("right", right.Offset);
        }
        private void RememberFocus()
        {
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            restoreFocus = focused == null ? null : elements.FirstOrDefault(e => e.Value.Button?.gameObject == focused).Key;
        }
        public void FocusFirst() => Focus(leftButtons.FirstOrDefault() ?? "");
        public void FocusReset() => Focus("action:reset");
        private void FocusHealth() => Focus(rightButtons.FirstOrDefault() ?? "");
        private void Focus(string id)
        {
            if (elements.TryGetValue(id, out var e) && e.Button?.IsActive() == true && e.Button.IsInteractable())
                GameManager.EventSystem?.SetSelectedGameObject(e.Button.gameObject);
            else GameManager.EventSystem?.SetSelectedGameObject(left.Rect.gameObject);
        }
        private void Move(string id, bool isRight, MoveDirection direction)
        {
            if (direction == MoveDirection.Left) { if (isRight) FocusFirst(); else focusTabs(); return; }
            if (direction == MoveDirection.Right) { if (!isRight) FocusHealth(); else GameManager.EventSystem?.SetSelectedGameObject(right.Rect.gameObject); return; }
            var list = (isRight ? rightButtons : leftButtons).Where(key => elements[key].Button?.IsInteractable() == true).ToArray();
            var index = Array.IndexOf(list, id) + (direction == MoveDirection.Up ? -1 : 1);
            if (index < 0) focusTabs();
            else if (index < list.Length) Focus(list[index]);
            else GameManager.EventSystem?.SetSelectedGameObject((isRight ? right : left).Rect.gameObject);
        }
        private void Toggle(string id)
        {
            var snapshot = selection.Snapshot; if (snapshot == null || operations.ModalVisible) return;
            Capture(); RememberFocus(); if (selection.Toggle(snapshot.GenerationId, id)) dirty = true;
        }
        public void Layout(RetainedVisualCanvasLayout shell, float viewportPixels, float canvasHeight)
        {
            if (disposed) return;
            var frame = CombatLayoutPolicy.Frame(shell, canvasHeight);
            if (width != frame.Width || height != frame.Height || pixels != viewportPixels)
            { Capture(); RememberFocus(); width = frame.Width; height = frame.Height; pixels = viewportPixels; dirty = true; }
            root.localScale = new Vector3(frame.Scale, frame.Scale, 1); Place(root, shell.Header.Left, frame.Top, width, height);
            if (lastNotice != operations.LastNotice || lastOperation != operations.Current || lastCanStart != operations.CanStart)
            { Capture(); RememberFocus(); lastNotice = operations.LastNotice; lastOperation = operations.Current; lastCanStart = operations.CanStart; dirty = true; }
            Place(unavailable.rectTransform, 30, 30, Math.Max(1, width - 60), measure.Height(unavailable.text, width - 60, 30));
            if (!dirty || !root.gameObject.activeInHierarchy) return;
            dirty = false;
            if (selection.Snapshot == null)
            {
                unavailable.text = operations.Current == PanelOperation.Reset ? UiText.Get("ui.diag_operation_pending") : UiText.Get("ui.profile_unavailable");
                return;
            }
            foreach (var e in elements.Values) e.Used = false;
            leftButtons.Clear(); rightButtons.Clear();
            var stacked = DiagnosticsLayoutPolicy.Stack(pixels);
            var column = DiagnosticsLayoutPolicy.ColumnWidth(width, stacked);
            var lh = BuildLeft(column); var rh = BuildRight(column);
            var lv = DiagnosticsLayoutPolicy.ColumnViewport(stacked, height, lh);
            var rv = DiagnosticsLayoutPolicy.ColumnViewport(stacked, height, rh);
            left.Size(0, 0, column, lv, lh);
            right.Size(stacked ? 0 : column + 40, stacked ? lv + 40 : 0, column, rv, rh);
            left.SetOffset(selection.Offset("left", lv, lh)); right.SetOffset(selection.Offset("right", rv, rh));
            outer.Size(0, 0, width, height, stacked ? lv + 40 + rv : height); outer.Scroll.vertical = stacked;
            outer.SetOffset(stacked ? selection.Offset("outer", height, outer.Content.rect.height) : 0);
            foreach (var key in elements.Where(e => !e.Value.Used).Select(e => e.Key).ToArray())
            {
                var e = elements[key]; e.Button?.onClick.RemoveAllListeners(); e.Rect.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(e.Rect.gameObject); elements.Remove(key);
            }
            if (restoreFocus != null)
            {
                var id = restoreFocus; restoreFocus = null;
                if (!operations.ModalVisible) Focus(id);
            }
        }
        private float BuildLeft(float w)
        {
            var snapshot = selection.Snapshot!;
            var settings = Panel(left.Content, "settings", 20);
            var y = 30 + Label(settings, "settings:title", UiText.Get("ui.data_settings"), 30, 30, w - 60, 46.3f) + 20;
            y += Value(settings, "profile", new DiagnosticsValue(UiText.Get("ui.diag_current_profile"), snapshot.ProfileLabel), 30, y, w - 60);
            y += Value(settings, "saved", new DiagnosticsValue(UiText.Get("ui.diag_last_saved"), snapshot.LastSaved), 30, y, w - 60);
            var copyDataText = UiText.Get("ui.diag_copy_path");
            var copyDataWidth = Math.Min((w - 80) / 2, measure.Width(copyDataText, 23) + 40);
            var copyDataHeight = Math.Max(45, measure.Height(copyDataText, copyDataWidth - 40, 23) + 16);
            var dataHeight = Value(settings, "data", new DiagnosticsValue(UiText.Get("ui.data_path"), "…/UltimateDuckovStatistics"), 30, y, w - 80 - copyDataWidth);
            Button(settings, "action:copy-data", copyDataText, w - 30 - copyDataWidth, y, copyDataWidth, copyDataHeight,
                Blue, () => ShowCopyFeedback(copyDataPath, "action:copy-data"), false, operations.CanStart, 23, true, 23);
            y += Math.Max(dataHeight, copyDataHeight) + 10;
            var hotkeyLabel = UiText.Get("ui.diag_hotkey_hint");
            var chipWidth = Math.Min(w - 60, Math.Max(68, measure.Width(snapshot.Hotkey, 28) + 40));
            var labelWidth = Math.Max(1, w - 80 - chipWidth);
            var hh = Math.Max(measure.Height(snapshot.Hotkey, chipWidth - 40, 28) + 16,
                Math.Max(50, Label(settings, "hotkey:label", hotkeyLabel, 30, y + 8, labelWidth, 30) + 16));
            Button(settings, "action:hotkey", snapshot.Hotkey, w - 30 - chipWidth, y, chipWidth, hh, Blue,
                changeHotkey, false, operations.CanStart, size: 28, centered: true, radius: 25);
            y += hh + 28;
            var exportText = UiText.Get("ui.diag_export"); var resetText = UiText.Get("ui.diag_reset");
            var ew = Math.Min(w - 60, measure.Width(exportText, 25) + 40); var rw = Math.Min(w - 60, measure.Width(resetText, 25) + 40);
            var row = RunsFlowLayout.Arrange(w - 60, new[] { (ew, 50f), (rw, 50f) }, 20);
            var exportHeight = Math.Max(50, measure.Height(exportText, ew - 40, 25) + 16);
            var resetHeight = Math.Max(50, measure.Height(resetText, rw - 40, 25) + 16);
            Button(settings, "action:export", exportText, 30, y, ew, exportHeight, Blue,
                () => { operations.RequestExport(); dirty = true; }, false, operations.CanStart, size: 25, centered: true, radius: 25);
            var resetY = row[1].Y > 0 ? y + exportHeight + 10 : y;
            Button(settings, "action:reset", resetText, row[1].Y > 0 ? 30 : w - 30 - rw, resetY, rw, resetHeight, Red,
                () => { operations.RequestResetConfirmation(); dirty = true; }, false, operations.CanStart, size: 25, centered: true, radius: 25);
            y = Math.Max(y + exportHeight, resetY + resetHeight) + 20;
            if (operations.LastNotice is PanelOperationNotice notice)
            {
                y += Label(settings, "operation:notice", OperationText(notice), 30, y, w - 60, 25,
                    notice.Outcome == PanelOperationOutcome.Failure ? Red : notice.Outcome == PanelOperationOutcome.Success ? Green : Muted) + 10;
                if (notice.Path.Length > 0)
                {
                    y += Label(settings, "operation:path", DiagnosticsPathPrivacy.ShortPath(notice.Path), 30, y, w - 60, 22) + 10;
                    var copy = UiText.Get("ui.diag_copy_path");
                    var bw = Math.Min(w - 60, measure.Width(copy, 23) + 40); var bh = Math.Max(45, measure.Height(copy, bw - 40, 23) + 16);
                    Button(settings, "action:copy", copy, 30, y, bw, bh, Blue, () => ShowCopyFeedback(copyExportPath, "action:copy"), false, operations.CanStart, 23, true, 23); y += bh + 10;
                }
            }
            Place(settings, 0, 0, w, y + 10);
            var top = y + 50;
            var issues = Panel(left.Content, "issues", 20);
            y = 30 + Label(issues, "issues:title", UiText.Get("ui.recent_issues"), 30, 30, w - 60, 46.3f) + 20;
            if (snapshot.Issues.Count == 0)
                y += Label(issues, "issues:empty", UiText.Get("ui.no_recent_issues"), 30, y, w - 60, 22, Muted, true) + 10;
            foreach (var issue in snapshot.Issues)
            {
                var key = "issue:" + issue.Id; var group = Panel(issues, key + ":panel", 10);
                var title = issue.Title + (issue.ReportCount > 1 ? " · " + string.Format(System.Globalization.CultureInfo.CurrentCulture, UiText.Get("ui.diag_report_count"), issue.ReportCount) : "")
                    + (issue.Timestamp.Length > 0 ? "   " + issue.Timestamp : "");
                var expanded = selection.Expanded(key);
                var header = Accordion(group, key, title, UiText.Get(issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? "ui.error" : "ui.diag_warning"),
                    0, 0, w - 60, 30, false, issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? Red : Color.white);
                var total = header;
                if (expanded) total += 12 + Label(group, key + ":body", issue.Detail, 20, header + 12, w - 100, 27) + 16;
                Place(group, 30, y, w - 60, total); y += total + 10;
            }
            Place(issues, 0, top, w, y + 20); top += y + 60;
            var technical = Panel(left.Content, "technical:panel", 20);
            y = Accordion(technical, "technical", UiText.Get("ui.technical_details"), "", 0, 0, w, 46.3f, false);
            if (selection.Expanded("technical"))
            {
                y += 16 + Label(technical, "versions:title", UiText.Get("ui.diag_versions"), 20, y + 16, w - 40, 34) + 16;
                foreach (var value in snapshot.Versions) y += Value(technical, "version:" + value.Label, value, 20, y, w - 40);
                y += 26;
                y = TechnicalGroup(technical, "recovery", UiText.Get("ui.diag_recovery"), snapshot.Recovery, y, w);
                y = TechnicalGroup(technical, "limitations", UiText.Get("ui.diag_limitations"), snapshot.Limitations, y, w, paragraphs: true);
                y += Accordion(technical, "log", UiText.Get("ui.diagnostic_log"), "", 20, y, w - 40, 34, false) + 10;
                if (selection.Expanded("log"))
                {
                    var labels = new[] { UiText.Get("ui.diag_log_all"), UiText.Get("ui.diag_log_warnings"), UiText.Get("ui.diag_log_errors") };
                    var sizes = labels.Select(l => {
                        var bw = Math.Min(w - 40, measure.Width(l, 23) + 40);
                        return (bw, Math.Max(38, measure.Height(l, bw - 40, 23) + 16));
                    }).ToArray();
                    var filters = RunsFlowLayout.Arrange(w - 40, sizes, 10); float bottom = 0;
                    for (var i = 0; i < labels.Length; i++)
                    {
                        var index = i; var box = filters[i];
                        Button(technical, "filter:" + i, labels[i], 20 + box.X, y + box.Y, box.Width, box.Height,
                            (int)selection.LogFilter == i ? Orange : Color.clear,
                            () => { Capture(); RememberFocus(); if (selection.Filter(snapshot.GenerationId, (DiagnosticsLogFilter)index)) dirty = true; }, false, true, 23, true);
                        bottom = Math.Max(bottom, box.Y + box.Height);
                    }
                    y += bottom + 18;
                    foreach (var entry in selection.VisibleLog)
                    {
                        var color = entry.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? Red : entry.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? Orange : Blue;
                        y += Label(technical, "loghead:" + entry.Id, entry.Timestamp + "   " + entry.Severity.ToUpperInvariant(), 30, y, w - 60, 18, color) + 4;
                        y += Label(technical, "logbody:" + entry.Id, entry.Message, 30, y, w - 60, 24) + 16;
                    }
                    if (!selection.VisibleLog.Any()) y += Label(technical, "log:empty", UiText.Get("ui.diag_log_empty"), 30, y, w - 60, 24, Muted) + 10;
                    y += Label(technical, "log:bound", UiText.Get("ui.diag_log_bound"), 30, y, w - 60, 18, Muted) + 10;
                }
            }
            Place(technical, 0, top, w, y + 20); return top + y + 30;
        }
        private float TechnicalGroup(RectTransform parent, string key, string title, IEnumerable<DiagnosticsValue> values, float y, float w, bool paragraphs = false)
        {
            y += Accordion(parent, key, title, "", 20, y, w - 40, 34, false) + 10;
            if (selection.Expanded(key))
            {
                foreach (var value in values)
                {
                    if (paragraphs)
                    {
                        y += Label(parent, key + ":" + value.Label + ":label", value.Label, 38, y, w - 76, 27) + 4;
                        y += Label(parent, key + ":" + value.Label + ":body", value.Value, 38, y, w - 76, 22, Muted) + 16;
                    }
                    else y += Value(parent, key + ":" + value.Label, value, 38, y, w - 76, 27);
                }
                if (key == "recovery") y += Label(parent, "recovery:detail", selection.Snapshot!.OpenDetail, 38, y + 8, w - 76, 19, Muted) + 20;
            }
            return y + 10;
        }
        private float BuildRight(float w)
        {
            var snapshot = selection.Snapshot!;
            var panel = Panel(right.Content, "health:panel", 20);
            var banner = Panel(panel, "health:banner", 10, snapshot.Health == DiagnosticsHealth.Error ? Red : snapshot.Health == DiagnosticsHealth.Limited ? Orange : Green);
            var bh = 18 + Label(banner, "health:title", snapshot.BannerTitle, 20, 18, w - 80, 31)
                + Label(banner, "health:detail", snapshot.BannerDetail, 20, 18 + measure.Height(snapshot.BannerTitle, w - 80, 31), w - 80, 27) + 18;
            Place(banner, 20, 30, w - 40, bh); var y = 30 + bh + 40;
            foreach (var system in snapshot.Systems)
            {
                var key = "system:" + system.Id;
                var group = Panel(panel, key + ":panel", 10);
                var h = Accordion(group, key, system.Name, UiText.Get("ui." + system.Health.ToString().ToLowerInvariant()), 0, 0, w - 40, 30, true, HealthColor(system.Health));
                if (selection.Expanded(key))
                {
                    h += 14;
                    foreach (var capability in system.Capabilities)
                        h += Value(group, "cap:" + capability.Id, new DiagnosticsValue(capability.Name, capability.Status,
                            capability.BaselineLimitation ? null : capability.Health), 20, h, w - 80, 23, 2);
                    foreach (var value in system.ExtraRows) h += Value(group, key + ":extra:" + value.Label, value, 20, h, w - 80, 23, 2);
                    h += 14;
                    var contractKey = "contracts:" + system.Id;
                    h += Accordion(group, contractKey, UiText.Get("ui.diag_contracts"), "", 20, h, w - 80, 24, true) + 12;
                    if (selection.Expanded(contractKey))
                    {
                        foreach (var cap in system.Capabilities)
                        {
                            h += Value(group, cap.Id + ":id", new DiagnosticsValue(UiText.Get("ui.diag_adapter"), cap.Id), 38, h, w - 116, 20, 3);
                            h += Value(group, cap.Id + ":version", new DiagnosticsValue(UiText.Get("ui.diag_capability_version"), cap.Version), 38, h, w - 116, 20, 3);
                            h += Value(group, cap.Id + ":state", new DiagnosticsValue(UiText.Get("ui.diag_capability_state"), cap.State), 38, h, w - 116, 20, 3);
                            h += Label(group, cap.Id + ":detail", cap.Detail, 38, h + 5, w - 116, 20, Muted) + 24;
                        }
                        if (system.Id == "menu") h += Label(group, "menu:contract", UiText.Get("ui.diag_native_menu_detail"), 38, h, w - 116, 20, Muted) + 20;
                    }
                    h += 10;
                }
                Place(group, 20, y, w - 40, h); y += h + 10;
            }
            Place(panel, 0, 0, w, y + 20); return y + 30;
        }
        private float Accordion(RectTransform parent, string id, string title, string status, float x, float y, float w, float size, bool isRight, Color? statusColor = null)
        {
            var expanded = selection.Expanded(id);
            var valueWidth = status.Length == 0 ? 0 : Math.Min(w * .36f, measure.Width(status, size) + 12);
            var textWidth = Math.Max(1, w - 70 - valueWidth - (valueWidth > 0 ? 10 : 0));
            var displayedTitle = title;
            var statusHeight = status.Length == 0 ? 0 : measure.Height(status, valueWidth, size);
            var h = Math.Max(size >= 40 ? 86 : 62, Math.Max(measure.Height(displayedTitle, textWidth, size), statusHeight) + 24);
            Button(parent, id, displayedTitle, x, y, w, h, expanded ? Orange : new Color(0, 0, 0, .5f),
                () => Toggle(id), isRight, true, size, false);
            // A separate status field reserves its own measured width; long titles wrap.
            var titleElement = elements[id + ":label"].Text!;
            Place(titleElement.rectTransform, 50, 12, textWidth, h - 24);
            Label(elements[id].Rect, id + ":chevron", "›", 20, 12, 20, size);
            var chevron = elements[id + ":chevron"].Text!;
            chevron.alignment = TextAlignmentOptions.Center;
            chevron.rectTransform.pivot = new Vector2(.5f, .5f);
            Place(chevron.rectTransform, 30, h / 2, 20, h - 24);
            chevron.rectTransform.localRotation = Quaternion.Euler(0, 0, expanded ? -90 : 0);
            if (status.Length > 0) Label(elements[id].Rect, id + ":status", status, w - 20 - valueWidth, 12, valueWidth, size,
                expanded ? Color.white : statusColor ?? Color.white, rightAligned: true);
            return h;
        }
        private float Value(RectTransform parent, string id, DiagnosticsValue value, float x, float y, float w, float size = 30, float gap = 8)
        {
            var columns = DiagnosticsLayoutPolicy.Columns(w, measure.Width(value.Label, size), measure.Width(value.Value, size));
            var lh = Label(parent, id + ":label", value.Label, x, y, columns.Label, size);
            var vh = Label(parent, id + ":value", value.Value, columns.Stacked ? x : x + columns.Label + 20,
                columns.Stacked ? y + lh + 5 : y, columns.Value, size, value.Health.HasValue ? HealthColor(value.Health.Value) : Color.white,
                rightAligned: !columns.Stacked);
            return (columns.Stacked ? lh + 5 + vh : Math.Max(lh, vh)) + gap;
        }
        private float Label(RectTransform parent, string id, string value, float x, float y, float w, float size, Color? color = null, bool centered = false, bool rightAligned = false)
        {
            value = DiagnosticsPathPrivacy.Redact(value, userProfile);
            var e = Use(parent, id); e.Text ??= InitializeText(e.Rect.gameObject.AddComponent<TextMeshProUGUI>(), size);
            e.Text.text = value; e.Text.fontSize = size; e.Text.color = color ?? Color.white;
            e.Text.alignment = centered ? TextAlignmentOptions.Top : rightAligned ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            var section = id == "settings:title" || id == "issues:title";
            var h = Math.Max(1, section ? measure.SectionHeight(value, w, size) : measure.Height(value, w, size));
            Place(e.Rect, x, y, Math.Max(1, w), h);
            if (section) CombatNativeTextMeasurement.AlignInkTop(e.Text);
            return h;
        }
        private void Button(RectTransform parent, string id, string text, float x, float y, float w, float h, Color color,
            Action click, bool isRight, bool enabled = true, float size = 28, bool centered = false, float radius = 10)
        {
            var rect = Panel(parent, id, radius, color); var e = elements[id];
            if (e.Button == null)
            {
                e.Button = rect.gameObject.AddComponent<Button>(); e.Button.navigation = new Navigation { mode = Navigation.Mode.None };
                rect.gameObject.AddComponent<ButtonAnimation>(); AddButtonFeedback(e.Button);
                rect.gameObject.AddComponent<RunsFocusHandler>().Move = direction => Move(id, isRight, direction);
            }
            e.Button.interactable = enabled; e.Button.onClick.RemoveAllListeners();
            e.Button.onClick.AddListener(() => { if (e.Button.IsInteractable() && !operations.ModalVisible) click(); });
            var th = measure.Height(text, Math.Max(1, w - 40), size);
            Label(rect, id + ":label", text, 20, Math.Max(8, (h - th) / 2), w - 40, size, centered: centered);
            Place(rect, x, y, w, h); (isRight ? rightButtons : leftButtons).Add(id);
        }
        private RectTransform Panel(RectTransform parent, string id, float radius, Color? color = null)
        {
            var e = Use(parent, id); var background = e.Rect.GetComponent<ProceduralImage>();
            if (background == null) { background = e.Rect.gameObject.AddComponent<ProceduralImage>(); e.Rect.gameObject.AddComponent<UniformModifier>(); }
            background.color = color ?? new Color(0, 0, 0, .5f); background.raycastTarget = true;
            e.Rect.GetComponent<UniformModifier>().Radius = radius; return e.Rect;
        }
        private Element Use(RectTransform parent, string id)
        {
            if (!elements.TryGetValue(id, out var e)) elements.Add(id, e = new Element { Rect = Node(parent, id) });
            e.Used = true; e.Rect.SetParent(parent, false); e.Rect.gameObject.SetActive(true); return e;
        }
        private TextMeshProUGUI CreateText(RectTransform parent, string name, float size) => InitializeText(Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>(), size);
        private TextMeshProUGUI InitializeText(TextMeshProUGUI label, float size)
        {
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size; label.fontStyle = FontStyles.Normal;
            label.fontWeight = FontWeight.Regular; label.enableWordWrapping = true; label.enableAutoSizing = false; label.richText = false;
            label.color = Color.white; label.raycastTarget = false; label.overflowMode = TextOverflowModes.Overflow; return label;
        }
        private static Color HealthColor(DiagnosticsHealth health) => health == DiagnosticsHealth.Error ? Red : health == DiagnosticsHealth.Limited ? Orange : Green;
        private static string OperationText(PanelOperationNotice notice) => UiText.Get(notice.Outcome switch
        {
            PanelOperationOutcome.Running => notice.Operation == PanelOperation.Export ? "ui.diag_export_running" : "ui.diag_reset_running",
            PanelOperationOutcome.Pending => "ui.diag_operation_pending",
            PanelOperationOutcome.Success => notice.Operation == PanelOperation.Export ? "ui.diag_export_success" : "ui.diag_reset_success",
            PanelOperationOutcome.ClipboardUnavailable => "ui.diag_export_clipboard",
            _ => "ui.diag_operation_failure"
        });
        private static void RoundedMask(ScrollRegion scroll)
        {
            var image = scroll.Scroll.viewport.gameObject.AddComponent<ProceduralImage>(); image.color = Color.white; image.raycastTarget = false;
            scroll.Scroll.viewport.gameObject.AddComponent<UniformModifier>().Radius = 20;
            scroll.Scroll.viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        }
        private static RectTransform Node(RectTransform parent, string name)
        { var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); return r; }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(Math.Max(1, w), Math.Max(1, h)); }
        private void ShowCopyFeedback(Func<bool> copy, string buttonId)
        {
            var success = copy();
            copyFeedbackText.text = UiText.Get(success ? "ui.diag_copied" : "ui.diag_clipboard_failed");
            copyFeedbackText.color = success ? Green : Orange;
            var w = Math.Min(width - 40, measure.Width(copyFeedbackText.text, 23) + 32);
            var h = measure.Height(copyFeedbackText.text, w - 32, 23) + 20;
            var position = root.InverseTransformPoint(elements[buttonId].Rect.position);
            Place(copyFeedback, Math.Clamp(position.x, 20, Math.Max(20, width - w - 20)),
                Math.Clamp(-position.y - h - 8, 10, Math.Max(10, height - h - 10)), w, h);
            Place(copyFeedbackText.rectTransform, 16, 10, w - 32, h - 20);
            copyFeedbackUntil = Time.unscaledTime + 2.5f;
            copyFeedback.SetAsLastSibling(); copyFeedback.gameObject.SetActive(true);
        }
        public void Tick()
        {
            if (!root.gameObject.activeInHierarchy) return;
            var notice = operations.LastNotice;
            if (notice != shownOperationNotice)
            {
                shownOperationNotice = notice;
                if (notice != null && (notice.Outcome == PanelOperationOutcome.Success
                    || notice.Outcome == PanelOperationOutcome.Failure || notice.Outcome == PanelOperationOutcome.ClipboardUnavailable))
                {
                    operationFeedbackText.text = notice.Outcome == PanelOperationOutcome.Failure
                        ? UiText.Get(notice.Operation == PanelOperation.Export ? "ui.diag_export_failed_toast" : "ui.diag_reset_failed_toast")
                        : OperationText(notice);
                    operationFeedback.gameObject.GetComponent<ProceduralImage>().color = notice.Outcome == PanelOperationOutcome.Failure
                        ? Red : notice.Outcome == PanelOperationOutcome.Success ? Green : Orange;
                    operationFeedbackUntil = Time.unscaledTime + 7;
                    operationFeedback.gameObject.SetActive(true);
                }
            }
            if (operationFeedback.gameObject.activeSelf)
            {
                var w = Math.Min(Math.Min(width, 960), measure.Width(operationFeedbackText.text, 28) + 56);
                var h = measure.Height(operationFeedbackText.text, w - 56, 28) + 40;
                Place(operationFeedback, 0, Math.Max(0, height - h), w, h);
                Place(operationFeedbackText.rectTransform, 28, 20, w - 56, h - 40);
                operationFeedback.SetAsLastSibling();
                if (Time.unscaledTime >= operationFeedbackUntil) operationFeedback.gameObject.SetActive(false);
            }
            if (copyFeedback.gameObject.activeSelf && Time.unscaledTime >= copyFeedbackUntil) copyFeedback.gameObject.SetActive(false);
            left.Cues(); right.Cues(); outer.Cues(); Capture();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var e in elements.Values) e.Button?.onClick.RemoveAllListeners();
            elements.Clear(); left.Dispose(); right.Dispose(); outer.Dispose();
        }
    }
}
