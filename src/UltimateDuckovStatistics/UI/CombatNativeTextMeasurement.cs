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
}
