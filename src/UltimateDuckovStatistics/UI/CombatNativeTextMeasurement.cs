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
