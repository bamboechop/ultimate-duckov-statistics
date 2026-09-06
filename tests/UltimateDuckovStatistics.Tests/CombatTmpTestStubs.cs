// Narrow boundary for the linked production measurement adapter, not a Unity renderer.
// The installed TMP sets m_isOrthographic in TextMeshProUGUI.Awake; preferred metrics
// otherwise use the 0.1 world-text scale. Awake waits for an active GameObject hierarchy,
// but does not require the TextMeshProUGUI behaviour to be enabled.
#pragma warning disable CA1708, CA1720, CA1716, CA1707 // Native TMP API names.
namespace TMPro;

public sealed class TextMeshProUGUI
{
    public MeasurementObject gameObject { get; }
    public bool enabled { get; set; } = true;
    public bool AwakeCalled { get; private set; }
    public float fontSize { get; set; }
    public TMP_TextInfo textInfo { get; set; } = new();
    public bool ForcedIgnoringActiveState { get; private set; }
    public void ForceMeshUpdate(bool ignoreActiveState = false) => ForcedIgnoringActiveState = ignoreActiveState;
    public TextMeshProUGUI() => gameObject = new MeasurementObject(() => AwakeCalled = true);
    public (float x, float y) GetPreferredValues(string text, float width, float height)
    {
        _ = height;
        var em = fontSize * (AwakeCalled ? 1 : .1f);
        var textWidth = text.Length * em / 2;
        return (textWidth, Math.Max(1, MathF.Ceiling(textWidth / width)) * em);
    }
}

public sealed class TMP_TextInfo
{
    public int characterCount { get; set; }
    public TMP_CharacterInfo[] characterInfo { get; set; } = Array.Empty<TMP_CharacterInfo>();
}
public sealed class TMP_CharacterInfo
{
    public bool isVisible { get; set; }
    public float scale { get; set; }
    public float descender { get; set; }
    public (float x, float y) bottomLeft { get; set; }
    public (float x, float y) topLeft { get; set; }
    public TMP_TextElement? textElement { get; set; }
}
public sealed class TMP_TextElement { public MeasurementGlyph glyph { get; set; } = new(); }
public sealed class MeasurementGlyph { public MeasurementGlyphMetrics metrics { get; set; } = new(); }
public sealed class MeasurementGlyphMetrics { public float height { get; set; } }

public sealed class MeasurementObject
{
    private readonly Action awake;
    private bool parentActive;
    public bool activeSelf { get; private set; } = true;
    public MeasurementObject(Action awake) => this.awake = awake;
    public void SetActive(bool value) { activeSelf = value; Activate(); }
    public void SetParentActive(bool value) { parentActive = value; Activate(); }
    private void Activate() { if (activeSelf && parentActive) awake(); }
}
