using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

// A separate overlay blocks menu clicks until the player acknowledges the warning.
internal sealed class NativeStartupCapabilityWarning : IDisposable
{
    private readonly GameObject root;
    private readonly GameObject? priorFocus;
    private readonly TextMeshProUGUI template;
    internal static bool TryCreate(IReadOnlyList<string> disabled, out NativeStartupCapabilityWarning? warning)
    {
        warning = null;
        if (EventSystem.current == null) return false;
        var template = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>(true).FirstOrDefault(value => value.font != null);
        if (template == null) return false;
        warning = new NativeStartupCapabilityWarning(disabled, template);
        return true;
    }
    private NativeStartupCapabilityWarning(IReadOnlyList<string> disabled, TextMeshProUGUI template)
    {
        this.template = template;
        priorFocus = EventSystem.current?.currentSelectedGameObject;
        root = new GameObject("UDSStartupCapabilityWarning", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        UnityEngine.Object.DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        var shade = Node(root.transform, "Shade", new Vector2(0, 0), new Vector2(1, 1));
        var blocker = shade.gameObject.AddComponent<Image>();
        blocker.color = new Color(0, 0, 0, .88f); blocker.raycastTarget = true;
        var card = Node(shade, "Card", new Vector2(.15f, .1f), new Vector2(.85f, .9f));
        card.gameObject.AddComponent<Image>().color = new Color(.06f, .09f, .13f, 1);
        Label(card, "Title", UiText.Get("ui.startup_disabled_title"), new Vector2(.05f, .80f), new Vector2(.95f, .97f), 30);
        Label(card, "Explanation", UiText.Get("ui.startup_disabled_body"), new Vector2(.05f, .62f), new Vector2(.95f, .81f), 22);
        var viewport = Node(card, "Viewport", new Vector2(.05f, .18f), new Vector2(.95f, .61f));
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .25f);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        var names = disabled.Select(id => DiagnosticsCapabilityCatalog.All.FirstOrDefault(value => value.Id == id))
            .Select((descriptor, index) => descriptor == null ? disabled[index] : UiText.Get(descriptor.TextKey));
        var content = Label(viewport, "DisabledCapabilities", string.Join("\n", names.Select(name => "• " + name)),
            Vector2.zero, Vector2.one, 23);
        var rect = content.rectTransform;
        rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1);
        rect.sizeDelta = new Vector2(-24, Math.Max(220, disabled.Count * 32));
        scroll.viewport = viewport; scroll.content = rect; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var buttonRect = Node(card, "Acknowledge", new Vector2(.25f, .04f), new Vector2(.75f, .14f));
        buttonRect.gameObject.AddComponent<Image>().color = new Color(.1f, .45f, .65f, 1);
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonRect.GetComponent<Image>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var caption = Label(buttonRect, "Caption", UiText.Get("ui.startup_disabled_acknowledge"), Vector2.zero, Vector2.one, 23);
        caption.alignment = TextAlignmentOptions.Center;
        button.onClick.AddListener(Dispose);
        Canvas.ForceUpdateCanvases();
        rect.sizeDelta = new Vector2(-24, Math.Max(viewport.rect.height,
            content.GetPreferredValues(content.text, Math.Max(1, viewport.rect.width - 24), float.PositiveInfinity).y + 16));
        EventSystem.current?.SetSelectedGameObject(button.gameObject);
    }

    private static RectTransform Node(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var node = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        node.SetParent(parent, false); node.anchorMin = min; node.anchorMax = max;
        node.offsetMin = node.offsetMax = Vector2.zero;
        return node;
    }
    private TextMeshProUGUI Label(Transform parent, string name, string value, Vector2 min, Vector2 max, int size)
    {
        var label = Node(parent, name, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = template.font; label.fontSharedMaterial = template.fontSharedMaterial;
        label.text = value; label.fontSize = size; label.color = Color.white; label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.richText = false; label.enableWordWrapping = true;
        return label;
    }
    public void Dispose()
    {
        if (root == null) return;
        root.SetActive(false);
        UnityEngine.Object.Destroy(root);
        if (priorFocus != null) EventSystem.current?.SetSelectedGameObject(priorFocus);
    }
}
