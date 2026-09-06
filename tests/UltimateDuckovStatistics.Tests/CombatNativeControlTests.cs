using TMPro;
using UltimateDuckovStatistics.UI;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class CombatNativeControlTests
{
    [Fact]
    public void AlignmentUsesIndividualGlyphGeometryWithoutFontDescendersOrSdfPadding()
    {
        var title = new TextMeshProUGUI(); var suffix = new TextMeshProUGUI();
        // Both meshes have padding. Font descenders extend below the actual drawn glyphs.
        var large = new TMP_CharacterInfo
        {
            isVisible = true,
            scale = 2,
            descender = -48,
            bottomLeft = (0, -40),
            topLeft = (0, 4),
            textElement = new TMP_TextElement { glyph = new MeasurementGlyph { metrics = new MeasurementGlyphMetrics { height = 18 } } }
        };
        var small = new TMP_CharacterInfo
        {
            isVisible = true,
            scale = 1,
            descender = -24,
            bottomLeft = (0, -20),
            topLeft = (0, 2),
            textElement = new TMP_TextElement { glyph = new MeasurementGlyph { metrics = new MeasurementGlyphMetrics { height = 18 } } }
        };
        title.textInfo = new TMP_TextInfo { characterCount = 2, characterInfo = new[] { large, new TMP_CharacterInfo { isVisible = false, descender = -999 } } };
        suffix.textInfo = new TMP_TextInfo { characterCount = 1, characterInfo = new[] { small } };
        Assert.Equal(-36, CombatNativeTextMeasurement.GlyphBottom(title));
        Assert.Equal(-18, CombatNativeTextMeasurement.GlyphBottom(suffix));
        Assert.True(title.ForcedIgnoringActiveState); Assert.True(suffix.ForcedIgnoringActiveState);
        var y = CombatLayoutPolicy.AlignGlyphBottom(0, CombatNativeTextMeasurement.GlyphBottom(title)!.Value, CombatNativeTextMeasurement.GlyphBottom(suffix)!.Value);
        Assert.Equal(-36, y - 18);
        Assert.NotEqual(large.descender - small.descender, y);
        Assert.Null(CombatNativeTextMeasurement.GlyphBottom(new TextMeshProUGUI()));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasurementInitializesWhenShellOpensWithoutRenderingScratchText(bool initiallyOpen)
    {
        var label = new TextMeshProUGUI(); label.gameObject.SetParentActive(initiallyOpen);
        var measure = new CombatNativeTextMeasurement(label);
        label.gameObject.SetParentActive(true);
        Assert.True(label.AwakeCalled);
        Assert.False(label.enabled);
        Assert.True(label.gameObject.activeSelf);
        Assert.Equal(32, measure.Height("Weapon", 200, 32));
        Assert.Equal(96, measure.Width("Weapon", 32));
        Assert.Equal(96, measure.Height("Weapon", 40, 32));
        var document = new CombatDocument(measure.Height, measure.Width);
        var card = new CombatRenderRow { Kind = CombatRowKind.Card, Cells = new[] { "Damage", "100" } };
        document.Add(card, 0, 0, 300);
        Assert.Equal(76, card.Height);
        var item = new CombatRenderRow { Kind = CombatRowKind.Item, Cells = new[] { new string('W', 40), "100 firing actions", "50% of all firing actions" } };
        document.Add(item, 0, card.Height + 10, 300);
        Assert.True(item.Height >= 32 * 4 + 24 * 2 + 20 * 2 + 24);
        label.gameObject.SetParentActive(false); label.gameObject.SetParentActive(true);
        Assert.Equal(28, measure.Height("Enemy", 200, 28));
        Assert.False(label.enabled);
    }

    [Fact]
    public void NativeColdInactiveMeasurementReproducesUndersizedGeometry()
    {
        var label = new TextMeshProUGUI(); label.gameObject.SetActive(false);
        label.gameObject.SetParentActive(true); label.fontSize = 32;
        Assert.False(label.AwakeCalled);
        Assert.Equal(3.2f, label.GetPreferredValues("Weapon", 200, float.PositiveInfinity).y);
    }

    [Fact]
    public void PooledDecorativeRowsHideWhiteResetOverlayAndRestoreNativeInteraction()
    {
        var background = new Graphic(); var overlay = new Graphic();
        var button = new RunsHistoryButton(); button.Configure(background);
        button.targetGraphic = overlay; button.transition = Button.Transition.ColorTint;
        button.BindInteractionOverlay(background, true);
        Assert.Equal(0, overlay.TintAlpha);
        button.Binding.Bind("g", "enemy"); button.Binding.Press();
        button.BindInteractionOverlay(background, false);
        Assert.Equal(1, overlay.TintAlpha); // Native disable still happened; it cannot paint.
        Assert.False(overlay.enabled); Assert.True(background.enabled);
        Assert.False(background.raycastTarget); Assert.False(button.IsActive());
        Assert.False(button.Binding.Release(false));
        button.BindInteractionOverlay(background, true);
        Assert.True(overlay.enabled); Assert.True(background.raycastTarget);
        Assert.True(button.IsActive()); Assert.True(button.IsInteractable());
        Assert.Equal(0, overlay.TintAlpha); // No white-to-clear tween after recycling.
        button.Selected = true; button.BindInteractionOverlay(background, true);
        Assert.Equal(.13f, overlay.TintAlpha);
        button.Disable(); button.Active = true; button.BindInteractionOverlay(background, true);
        Assert.Equal(.13f, overlay.TintAlpha);
    }
}
