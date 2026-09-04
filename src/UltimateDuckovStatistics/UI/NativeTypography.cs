using System.Globalization;
using TMPro;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed class NativeTextTemplateSnapshot
{
    private NativeTextTemplateSnapshot(
        TMP_FontAsset font,
        Material? sharedMaterial,
        FontStyles fontStyle,
        FontWeight fontWeight,
        float sourceFontSize,
        float characterSpacing,
        float wordSpacing,
        float lineSpacing,
        float paragraphSpacing,
        bool enableKerning,
        bool extraPadding,
        string sourceDescription)
    {
        Font = font;
        SharedMaterial = sharedMaterial;
        FontStyle = fontStyle;
        FontWeight = fontWeight;
        SourceFontSize = sourceFontSize;
        CharacterSpacing = characterSpacing;
        WordSpacing = wordSpacing;
        LineSpacing = lineSpacing;
        ParagraphSpacing = paragraphSpacing;
        EnableKerning = enableKerning;
        ExtraPadding = extraPadding;
        SourceDescription = sourceDescription;
    }

    public TMP_FontAsset Font { get; }

    public Material? SharedMaterial { get; }

    public FontStyles FontStyle { get; }

    public FontWeight FontWeight { get; }

    public float SourceFontSize { get; }

    public float CharacterSpacing { get; }

    public float WordSpacing { get; }

    public float LineSpacing { get; }

    public float ParagraphSpacing { get; }

    public bool EnableKerning { get; }

    public bool ExtraPadding { get; }

    public string SourceDescription { get; }

    public static bool TryCapture(
        TextMeshProUGUI? source,
        string sourceDescription,
        out NativeTextTemplateSnapshot? snapshot)
    {
        snapshot = null;
        if (source == null || source.font == null || string.IsNullOrWhiteSpace(sourceDescription)) return false;
        snapshot = new NativeTextTemplateSnapshot(
            source.font,
            source.fontSharedMaterial,
            source.fontStyle,
            source.fontWeight,
            source.fontSize,
            source.characterSpacing,
            source.wordSpacing,
            source.lineSpacing,
            source.paragraphSpacing,
            source.enableKerning,
            source.extraPadding,
            sourceDescription);
        return true;
    }

    public void Apply(TextMeshProUGUI target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        target.font = Font;
        if (SharedMaterial != null) target.fontSharedMaterial = SharedMaterial;
        target.fontStyle = FontStyle;
        target.fontWeight = FontWeight;
        target.characterSpacing = CharacterSpacing;
        target.wordSpacing = WordSpacing;
        target.lineSpacing = LineSpacing;
        target.paragraphSpacing = ParagraphSpacing;
        target.enableKerning = EnableKerning;
        target.extraPadding = ExtraPadding;
        target.enableAutoSizing = false;
    }

    public string Describe() =>
        $"{SourceDescription} [font={SafeObjectName(Font)}, material={SafeObjectName(SharedMaterial)}, " +
        $"style={FontStyle}, weight={FontWeight}, sourceSize={SourceFontSize.ToString("0.###", CultureInfo.InvariantCulture)}, " +
        $"characterSpacing={CharacterSpacing.ToString("0.###", CultureInfo.InvariantCulture)}, " +
        $"wordSpacing={WordSpacing.ToString("0.###", CultureInfo.InvariantCulture)}, " +
        $"lineSpacing={LineSpacing.ToString("0.###", CultureInfo.InvariantCulture)}, " +
        $"paragraphSpacing={ParagraphSpacing.ToString("0.###", CultureInfo.InvariantCulture)}]";

    private static string SafeObjectName(UnityEngine.Object? value) =>
        value == null || string.IsNullOrWhiteSpace(value.name) ? "<unnamed>" : value.name;
}

internal sealed class NativeTypographyRoles
{
    private readonly NativeTextTemplateSnapshot publicTemplate;
    private readonly NativeTextTemplateSnapshot? liveMenuButton;
    private readonly NativeTextTemplateSnapshot? nativeHeading;

    public NativeTypographyRoles(
        NativeTextTemplateSnapshot publicTemplate,
        NativeTextTemplateSnapshot? liveMenuButton,
        NativeTextTemplateSnapshot? nativeHeading = null)
    {
        this.publicTemplate = publicTemplate ?? throw new ArgumentNullException(nameof(publicTemplate));
        this.liveMenuButton = liveMenuButton;
        this.nativeHeading = nativeHeading;
    }

    public NativeTextTemplateSnapshot Resolve(NativeTypographyRole role) =>
        NativeTypographyRolePolicy.Resolve(role, liveMenuButton != null, nativeHeading != null) switch
        {
            NativeTypographySource.NativeHeading => nativeHeading!,
            NativeTypographySource.LiveMenuButton => liveMenuButton!,
            _ => publicTemplate
        };

    public string Describe() =>
        $"title={Resolve(NativeTypographyRole.Title).Describe()}; " +
        $"navigation={Resolve(NativeTypographyRole.Navigation).Describe()}; " +
        $"body={Resolve(NativeTypographyRole.Body).Describe()}; " +
        $"secondary={Resolve(NativeTypographyRole.Secondary).Describe()}";
}
