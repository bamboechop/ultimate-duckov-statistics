using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

// One retained popup on the statistics canvas, outside the scrolling content's masks.
// Duckov's global Tooltips renderer belongs to GameplayUICanvas and is not reliable on menus.
internal sealed class CombatTooltip : IDisposable
{
    private readonly RectTransform parent, panel;
    private readonly TextMeshProUGUI label;
    private CombatTooltipTrigger? current;
    private bool disposed;

    public CombatTooltip(RectTransform parent, TMP_FontAsset font, Material material)
    {
        this.parent = parent;
        panel = Node(parent, "CombatTooltip");
        var background = panel.gameObject.AddComponent<ProceduralImage>();
        background.color = new Color(.015f, .035f, .05f, .98f); background.raycastTarget = false;
        panel.gameObject.AddComponent<UniformModifier>().Radius = 10;
        label = Node(panel, "Text").gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSharedMaterial = material; label.fontSize = 24;
        label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
        label.color = Color.white; label.alignment = TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = true; label.enableAutoSizing = false; label.richText = false;
        label.raycastTarget = false; label.overflowMode = TextOverflowModes.Overflow;
        label.rectTransform.anchoredPosition = new Vector2(16, -16);
        panel.gameObject.SetActive(false);
    }

    public void Show(CombatTooltipTrigger source, PointerEventData data)
    {
        if (disposed || !parent.gameObject.activeInHierarchy || !source.isActiveAndEnabled || source.Text.Length == 0) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, data.position, data.enterEventCamera, out var point)) return;
        // Activate before measuring so TMP also initializes when the shell was constructed hidden.
        panel.SetAsLastSibling(); panel.gameObject.SetActive(true);
        current = source; label.text = source.Text;
        var width = Math.Min(560, Math.Max(64, parent.rect.width - 20));
        var height = label.GetPreferredValues(source.Text, width - 32, float.PositiveInfinity).y + 32;
        var x = point.x + parent.rect.width * parent.pivot.x;
        var y = parent.rect.height * (1 - parent.pivot.y) - point.y;
        panel.anchoredPosition = new Vector2(Math.Clamp(x + 16, 0, Math.Max(0, parent.rect.width - width)),
            -Math.Clamp(y + 16 + height <= parent.rect.height ? y + 16 : y - height - 16, 0, Math.Max(0, parent.rect.height - height)));
        panel.sizeDelta = new Vector2(width, height);
        label.rectTransform.sizeDelta = new Vector2(width - 32, height - 32);
    }

    public void Hide(CombatTooltipTrigger source) { if (ReferenceEquals(current, source)) Dismiss(); }
    public void Dismiss() { current = null; panel.gameObject.SetActive(false); }
    public void Dispose()
    {
        if (disposed) return;
        Dismiss(); disposed = true; UnityEngine.Object.Destroy(panel.gameObject);
    }
    private static RectTransform Node(RectTransform parent, string name)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        return rect;
    }
}

internal sealed class CombatTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private CombatTooltip? owner;
    public string Text { get; private set; } = "";
    public void Bind(CombatTooltip? next, string text)
    {
        if (!ReferenceEquals(owner, next) || Text != text) owner?.Hide(this);
        owner = next; Text = text;
    }
    public void OnPointerEnter(PointerEventData data) => owner?.Show(this, data);
    public void OnPointerExit(PointerEventData data) => owner?.Hide(this);
    internal void OnDisable() => owner?.Hide(this);
}
