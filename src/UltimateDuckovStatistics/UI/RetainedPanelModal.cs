using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private PanelModal? modal;
    public bool ModalVisible => modal?.Visible == true;
    public void SyncModal(bool reset, bool hotkey, string profileLabel, string warning) => modal?.Sync(reset, hotkey, profileLabel, warning);
    public void MoveModalFocus(bool reverse) => modal?.MoveFocus(reverse);

    private sealed class PanelModal : IDisposable
    {
        private readonly RectTransform root, card;
        private readonly ScrollRegion scroll;
        private readonly TextMeshProUGUI title, body, warning;
        private readonly Button cancel, confirm;
        private readonly TextMeshProUGUI cancelLabel, confirmLabel;
        private readonly PanelOperationController operations;
        private readonly Action cancelHotkey, focusFallback;
        private readonly Dictionary<CanvasGroup, bool> blocked = new();
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private GameObject? priorFocus;
        private bool resetMode, hotkeyMode, needsFocus, disposed;
        public bool Visible => root.gameObject.activeSelf;

        public PanelModal(RectTransform parent, NativeHeaderTitleTypography typography, Material material,
            PanelOperationController operations, Action cancelHotkey, Action focusFallback)
        {
            this.typography = typography; this.material = material; this.operations = operations;
            this.cancelHotkey = cancelHotkey; this.focusFallback = focusFallback;
            root = Node(parent, "UDSOperationModal"); Stretch(root);
            var blocker = root.gameObject.AddComponent<Image>(); blocker.color = new Color(0, 0, 0, .72f); blocker.raycastTarget = true;
            card = CreateOverviewPanel(root, "Confirmation", out var modifier); modifier.Radius = 20;
            card.GetComponent<ProceduralImage>().color = new Color(0, 0, 0, .95f);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
            scroll = new ScrollRegion(card, "ConfirmationScroll", radius: 20);
            title = Text(scroll.Content, "Title", 46); body = Text(scroll.Content, "Body", 34); warning = Text(scroll.Content, "Warning", 32);
            warning.color = new Color32(250, 73, 100, 255);
            cancel = Button(scroll.Content, "Cancel", new Color32(72, 195, 242, 255), out cancelLabel);
            confirm = Button(scroll.Content, "ConfirmReset", new Color32(250, 73, 100, 255), out confirmLabel);
            cancel.onClick.AddListener(() => { if (resetMode) operations.CancelConfirmation(); else cancelHotkey(); });
            confirm.onClick.AddListener(() => { if (resetMode) operations.ConfirmReset(); });
            root.gameObject.SetActive(false);
        }
        public void Sync(bool reset, bool hotkey, string profileLabel, string warningText)
        {
            if (disposed) return;
            var visible = reset || hotkey;
            if (visible && !Visible)
            {
                priorFocus = GameManager.EventSystem?.currentSelectedGameObject;
                needsFocus = true;
                root.gameObject.SetActive(true);
            }
            if (!visible && Visible)
            {
                root.gameObject.SetActive(false); RestoreBlocked();
                if (priorFocus != null && priorFocus.activeInHierarchy) GameManager.EventSystem?.SetSelectedGameObject(priorFocus);
                else focusFallback();
                priorFocus = null;
            }
            resetMode = reset; hotkeyMode = hotkey;
            if (!visible) return;
            root.SetAsLastSibling();
            for (var i = 0; i < root.parent.childCount; i++)
            {
                var child = root.parent.GetChild(i);
                if (child == root) continue;
                var group = child.GetComponent<CanvasGroup>() ?? child.gameObject.AddComponent<CanvasGroup>();
                if (!blocked.ContainsKey(group)) blocked.Add(group, group.interactable);
                group.interactable = false;
            }
            title.text = UiText.Get(reset ? "ui.diag_reset_title" : "ui.diag_hotkey_title");
            cancelLabel.text = UiText.Get("ui.diag_cancel"); confirmLabel.text = UiText.Get("ui.diag_reset_confirm");
            body.text = reset ? string.Format(System.Globalization.CultureInfo.CurrentCulture, UiText.Get("ui.diag_reset_body"), profileLabel) : UiText.Get("ui.diag_hotkey_body");
            warning.text = reset ? UiText.Get("ui.diag_reset_irreversible") : warningText;
            confirm.gameObject.SetActive(reset); confirm.interactable = reset && operations.Current == PanelOperation.None;
            var selected = GameManager.EventSystem?.currentSelectedGameObject;
            if (needsFocus || selected == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(root))
            { GameManager.EventSystem?.SetSelectedGameObject(cancel.gameObject); needsFocus = false; }
        }
        public void MoveFocus(bool reverse)
        {
            if (!Visible) return;
            var selected = GameManager.EventSystem?.currentSelectedGameObject;
            // Two reset choices wrap in either direction. A hotkey capture has only Cancel.
            var next = resetMode && confirm.interactable && selected == cancel.gameObject ? confirm : cancel;
            GameManager.EventSystem?.SetSelectedGameObject(next.gameObject);
        }
        public void Layout(RetainedVisualCanvasLayout shell, float canvasWidth, float canvasHeight)
        {
            if (!Visible) return;
            var scale = shell.ReferenceTransform.CanvasLength(1);
            var size = DiagnosticsLayoutPolicy.Modal(canvasWidth / scale, canvasHeight / scale);
            var inner = Math.Max(1, size.Width - 60);
            var y = Put(title, 0, 0, inner) + 24;
            y += Put(body, 0, y, inner) + 24;
            if (warning.text.Length > 0) y += Put(warning, 0, y, inner) + 16;
            var cw = Math.Min(inner, cancelLabel.GetPreferredValues(cancelLabel.text).x + 36);
            var ch = Math.Max(50, cancelLabel.GetPreferredValues(cancelLabel.text, Math.Max(1, cw - 36), float.PositiveInfinity).y + 16);
            Place((RectTransform)cancel.transform, 0, y, cw, ch); Place(cancelLabel.rectTransform, 18, 8, cw - 36, ch - 16);
            var bottom = y + ch;
            if (resetMode)
            {
                var rw = Math.Min(inner, confirmLabel.GetPreferredValues(confirmLabel.text).x + 36);
                var rh = Math.Max(50, confirmLabel.GetPreferredValues(confirmLabel.text, Math.Max(1, rw - 36), float.PositiveInfinity).y + 16);
                var stack = cw + rw + 20 > inner;
                var ry = stack ? bottom + 12 : y;
                Place((RectTransform)confirm.transform, stack ? 0 : cw + 20, ry, rw, rh);
                Place(confirmLabel.rectTransform, 18, 8, rw - 36, rh - 16); bottom = Math.Max(bottom, ry + rh);
            }
            var h = Math.Min(size.Height, bottom + 60);
            card.localScale = new Vector3(scale, scale, 1); card.anchoredPosition = Vector2.zero; card.sizeDelta = new Vector2(size.Width, h);
            scroll.Size(30, 30, inner, Math.Max(1, h - 60), bottom);
            scroll.Cues();
        }
        private Button Button(RectTransform parent, string name, Color color, out TextMeshProUGUI label)
        {
            var rect = CreateOverviewPanel(parent, name, out var modifier); modifier.Radius = 25;
            var background = rect.GetComponent<ProceduralImage>(); background.color = color; background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.targetGraphic = background;
            rect.gameObject.AddComponent<ButtonAnimation>(); AddButtonFeedback(button);
            rect.gameObject.AddComponent<RunsFocusHandler>().Move = direction => MoveFocus(direction == MoveDirection.Left || direction == MoveDirection.Up);
            label = Text(rect, name + "Label", 26); label.alignment = TextAlignmentOptions.Center;
            return button;
        }
        private TextMeshProUGUI Text(RectTransform parent, string name, float size)
        {
            var label = Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular; label.enableWordWrapping = true;
            label.enableAutoSizing = false; label.richText = false; label.color = Color.white; label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft; return label;
        }
        private static float Put(TextMeshProUGUI text, float x, float y, float width)
        { var h = Math.Max(1, text.GetPreferredValues(text.text, Math.Max(1, width), float.PositiveInfinity).y); Place(text.rectTransform, x, y, width, h); return h; }
        private static RectTransform Node(RectTransform parent, string name)
        { var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); return r; }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(Math.Max(1, w), Math.Max(1, h)); }
        private void RestoreBlocked()
        {
            foreach (var pair in blocked) if (pair.Key != null) pair.Key.interactable = pair.Value;
            blocked.Clear();
        }
        public void Dispose()
        { if (disposed) return; disposed = true; RestoreBlocked(); cancel.onClick.RemoveAllListeners(); confirm.onClick.RemoveAllListeners(); scroll.Dispose(); priorFocus = null; }
    }
}
