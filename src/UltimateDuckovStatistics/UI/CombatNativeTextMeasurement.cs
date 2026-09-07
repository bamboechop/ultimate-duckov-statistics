using TMPro;

namespace UltimateDuckovStatistics.UI;

// Keep the GameObject active so TMP's Awake runs when the initially inactive shell opens.
// Disabling only the renderer hides this scratch label without suppressing UI initialization.
internal sealed class CombatNativeTextMeasurement
{
    private readonly TextMeshProUGUI label;
    public CombatNativeTextMeasurement(TextMeshProUGUI label)
    {
        this.label = label;
        label.enabled = false;
        label.gameObject.SetActive(true);
    }
    public float Height(string text, float width, float size)
    {
        label.fontSize = size;
        return label.GetPreferredValues(text, Math.Max(1, width), float.PositiveInfinity).y;
    }
    public float Width(string text, float size)
    {
        label.fontSize = size;
        return label.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity).x;
    }
    // Section padding is measured to visible ink, not to TMP's face ascender.
    public float SectionHeight(string text, float width, float size)
    {
        var height = Height(text, width, size);
        label.text = text;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.rectTransform.sizeDelta = new UnityEngine.Vector2(Math.Max(1, width), height);
        return Math.Max(1, height - TopInset(label));
    }
    public float HeightWithSectionInk(string text, float width, float size) => size == 46.3f
        ? SectionHeight(text, width, size) : Height(text, width, size);
    public static void AlignInkTop(TextMeshProUGUI text)
    {
        var inset = TopInset(text);
        text.rectTransform.anchoredPosition += new UnityEngine.Vector2(0, inset);
    }
    private static float TopInset(TextMeshProUGUI text)
    {
        text.ForceMeshUpdate(ignoreActiveState: true);
        float? top = null;
        var info = text.textInfo;
        for (var i = 0; i < info.characterCount && i < info.characterInfo.Length; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible || c.textElement?.glyph == null) continue;
            var inkHeight = c.textElement.glyph.metrics.height * c.scale;
            var inkTop = c.topLeft.y - (c.topLeft.y - c.bottomLeft.y - inkHeight) / 2;
            if (!float.IsNaN(inkTop) && !float.IsInfinity(inkTop)) top = top.HasValue ? Math.Max(top.Value, inkTop) : inkTop;
        }
        return top.HasValue ? Math.Max(0, text.rectTransform.rect.yMax - top.Value) : 0;
    }
    public static float? GlyphBottom(TextMeshProUGUI text)
    {
        text.ForceMeshUpdate(ignoreActiveState: true);
        float? bottom = null;
        var info = text.textInfo;
        for (var i = 0; i < info.characterCount && i < info.characterInfo.Length; i++)
        {
            var c = info.characterInfo[i];
            if (!c.isVisible || c.textElement?.glyph == null) continue;
            // TMP textBounds uses face descenders. Character quads instead include the
            // individual glyph plus symmetric SDF padding; remove that padding first.
            var inkHeight = c.textElement.glyph.metrics.height * c.scale;
            var inkBottom = c.bottomLeft.y + (c.topLeft.y - c.bottomLeft.y - inkHeight) / 2;
            if (!float.IsNaN(inkBottom) && !float.IsInfinity(inkBottom)) bottom = bottom.HasValue ? Math.Min(bottom.Value, inkBottom) : inkBottom;
        }
        return bottom;
    }
}
