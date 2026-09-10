using System.Globalization;
using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private AboutView? aboutView;

    // Static content owns no profile, projection, adapter or per-frame update.
    private sealed class AboutView : IDisposable
    {
        private readonly RectTransform root, card;
        private readonly ScrollRegion scroll;
        private readonly TextMeshProUGUI title, description, author, invitation, failure;
        private readonly Button support, discord;
        private readonly TextMeshProUGUI supportLabel, discordLabel;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly Action focusTabs;
        private float width, height, scale;
        private bool dirty = true, disposed;

        public AboutView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.focusTabs = focusTabs;
            root = Node(parent, "AboutContentView");
            card = CreateOverviewPanel(root, "AboutCard", out var modifier); modifier.Radius = 20;
            scroll = new ScrollRegion(card, "AboutScroll", radius: 20);
            var viewport = scroll.Scroll.viewport;
            var mask = viewport.gameObject.AddComponent<ProceduralImage>(); mask.color = Color.white; mask.raycastTarget = false;
            viewport.gameObject.AddComponent<UniformModifier>().Radius = 20;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            scroll.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            title = Text("AboutTitle", 40); description = Text("AboutDescription", 28);
            author = Text("AboutAuthor", 28); invitation = Text("AboutInvitation", 28);
            failure = Text("AboutLinkFailure", 26); failure.text = "";
            support = Link("AboutSupport", out supportLabel);
            discord = Link("AboutDiscord", out discordLabel);
            support.onClick.AddListener(() => Activate(support, CommunityLinks.SupportUrl));
            discord.onClick.AddListener(() => Activate(discord, CommunityLinks.DiscordUrl));
            support.GetComponent<RunsFocusHandler>().Move = d => Move(support, d);
            discord.GetComponent<RunsFocusHandler>().Move = d => Move(discord, d);
            scroll.Rect.GetComponent<RunsFocusHandler>().Move = d =>
            {
                if (d == MoveDirection.Left || d == MoveDirection.Up && scroll.Offset <= .5f) focusTabs();
                else if (d == MoveDirection.Right) Focus(support);
                else if (d == MoveDirection.Up || d == MoveDirection.Down)
                    scroll.SetOffset(scroll.Offset + (d == MoveDirection.Up ? -1 : 1) * 60);
            };
            RefreshText();
        }

        public void RefreshText()
        {
            if (disposed) return;
            title.text = "Ultimate Duckov Statistics";
            description.text = UiText.Get("ui.about_description");
            author.text = string.Format(CultureInfo.CurrentCulture, UiText.Get("ui.about_author"), CommunityLinks.Author);
            invitation.text = UiText.Get("ui.about_translation");
            supportLabel.text = UiText.Get("ui.about_support"); discordLabel.text = UiText.Get("ui.about_discord");
            if (failure.text.Length > 0) failure.text = UiText.Get("ui.about_link_failed");
            dirty = true;
        }

        private void Activate(Button button, string destination)
        {
            if (disposed || !root.gameObject.activeInHierarchy || !button.IsInteractable()) return;
            try { Application.OpenURL(destination); failure.text = ""; }
            catch (Exception) { failure.text = UiText.Get("ui.about_link_failed"); }
            // Unity's void API cannot confirm browser/site success. Keep focus and the
            // existing panel input owner intact for the user's return to the game.
            GameManager.EventSystem?.SetSelectedGameObject(button.gameObject);
            dirty = true;
        }

        public void SetVisible(bool visible) { if (!disposed) root.gameObject.SetActive(visible); }
        public void FocusFirst() => GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject);
        private void Focus(Button button)
        {
            if (GameManager.EventSystem?.currentSelectedGameObject != button.gameObject)
                GameManager.EventSystem?.SetSelectedGameObject(button.gameObject);
            var rect = (RectTransform)button.transform;
            var top = -rect.anchoredPosition.y;
            if (top < scroll.Offset) scroll.SetOffset(top);
            else if (top + rect.rect.height > scroll.Offset + scroll.Rect.rect.height)
                scroll.SetOffset(top + rect.rect.height - scroll.Rect.rect.height);
        }
        private void Move(Button button, MoveDirection direction)
        {
            if (direction == MoveDirection.Left) FocusFirst();
            else if (direction == MoveDirection.Up && button == support) focusTabs();
            else if (direction == MoveDirection.Up) Focus(support);
            else if (direction == MoveDirection.Down || direction == MoveDirection.Right) Focus(discord);
        }

        public void Layout(RetainedVisualCanvasLayout shell, float canvasHeight)
        {
            if (disposed) return;
            var frame = CombatLayoutPolicy.Frame(shell, canvasHeight);
            if (frame.Width != width || frame.Height != height || frame.Scale != scale) { width = frame.Width; height = frame.Height; scale = frame.Scale; dirty = true; }
            if (!dirty || !root.gameObject.activeInHierarchy) return;
            dirty = false;
            root.localScale = new Vector3(frame.Scale, frame.Scale, 1);
            Place(root, frame.Left, frame.Top, width, height); Place(card, 0, 0, width, height);
            var textWidth = Math.Max(1, width - 60); float y = 30;
            y += Put(title, 30, y, textWidth) + 20;
            y += Put(description, 30, y, textWidth) + 20;
            y += Put(author, 30, y, textWidth) + 20;
            y += PutButton(support, supportLabel, y, textWidth) + 40;
            y += Put(invitation, 30, y, textWidth) + 20;
            y += PutButton(discord, discordLabel, y, textWidth) + 20;
            if (failure.text.Length > 0) y += Put(failure, 30, y, textWidth) + 20;
            failure.gameObject.SetActive(failure.text.Length > 0);
            scroll.Size(0, 0, width, height, y + 30);
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            if (focused == support.gameObject) Focus(support);
            else if (focused == discord.gameObject) Focus(discord);
        }

        private static float PutButton(Button button, TextMeshProUGUI label, float y, float available)
        {
            var w = Math.Min(available, Math.Max(260, label.GetPreferredValues(label.text).x + 40));
            var h = Math.Max(56, label.GetPreferredValues(label.text, Math.Max(1, w - 40), float.PositiveInfinity).y + 20);
            Place((RectTransform)button.transform, 30, y, w, h); Place(label.rectTransform, 20, 10, w - 40, h - 20); return h;
        }
        private Button Link(string name, out TextMeshProUGUI label)
        {
            var rect = CreateOverviewPanel(scroll.Content, name, out var modifier); modifier.Radius = 25;
            var background = rect.GetComponent<ProceduralImage>();
            background.color = new Color32(72, 195, 242, 255); background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            rect.gameObject.AddComponent<ButtonAnimation>(); AddButtonFeedback(button);
            rect.gameObject.AddComponent<RunsFocusHandler>().Selected = () => Focus(button);
            label = Text(name + "Label", 26); label.transform.SetParent(rect, false); label.alignment = TextAlignmentOptions.Center;
            return button;
        }
        private TextMeshProUGUI Text(string name, float size)
        {
            var label = Node(scroll.Content, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular; label.enableWordWrapping = true;
            label.enableAutoSizing = false; label.richText = false; label.color = Color.white; label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft; return label;
        }
        private static float Put(TextMeshProUGUI text, float x, float y, float width)
        { var h = Math.Max(1, text.GetPreferredValues(text.text, width, float.PositiveInfinity).y); Place(text.rectTransform, x, y, width, h); return h; }
        private static RectTransform Node(RectTransform parent, string name)
        { var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); return r; }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(Math.Max(1, w), Math.Max(1, h)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var button in new[] { support, discord })
            { button.onClick.RemoveAllListeners(); var focus = button.GetComponent<RunsFocusHandler>(); focus.Move = null; focus.Selected = null; }
            scroll.Rect.GetComponent<RunsFocusHandler>().Move = null;
            scroll.Dispose(); root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
