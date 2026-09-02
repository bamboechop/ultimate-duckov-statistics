using TMPro;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed class NativeHeaderTitleTypography
{
    public TMP_FontAsset Font { get; set; } = null!;
    public Material Material { get; set; } = null!;
    public string SourceDescription { get; set; } = string.Empty;
}

internal static class NativeHeaderTitleTypographyResolver
{
    public static bool TryResolve(
        Canvas canvas,
        out NativeHeaderTitleTypography? typography,
        out string? error)
    {
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));

        typography = null;
        error = null;
        var roots = canvas.gameObject.scene.IsValid()
            ? canvas.gameObject.scene.GetRootGameObjects()
            : new[] { canvas.gameObject };
        var sources = roots
            .SelectMany(root => root.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true))
            .Where(candidate => candidate != null && !IsUdsObject(candidate.transform))
            .Select(candidate => new { Text = candidate, Path = HierarchyPath(candidate.transform) })
            .Where(candidate => string.Equals(
                candidate.Path,
                RetainedHeaderTitlePolicy.NativeSourcePath,
                StringComparison.Ordinal))
            .ToArray();
        if (sources.Length != 1)
        {
            error = sources.Length == 0
                ? $"Duckov's native major-heading source '{RetainedHeaderTitlePolicy.NativeSourcePath}' was not found."
                : $"Duckov exposed {sources.Length} native major-heading sources at '{RetainedHeaderTitlePolicy.NativeSourcePath}'.";
            return false;
        }

        var source = sources[0].Text;
        var font = source.font;
        var material = source.fontSharedMaterial;
        if (font == null
            || material == null
            || !string.Equals(font.name, RetainedHeaderTitlePolicy.FontAssetName, StringComparison.Ordinal)
            || !string.Equals(material.name, RetainedHeaderTitlePolicy.MaterialName, StringComparison.Ordinal)
            || source.fontStyle != FontStyles.Normal
            || source.fontWeight != FontWeight.Regular
            || !Approximately(source.fontSize, RetainedHeaderTitlePolicy.ReferenceFontSize)
            || !Approximately(source.characterSpacing, 0f)
            || !Approximately(source.wordSpacing, 0f)
            || !Approximately(source.lineSpacing, 0f)
            || !Approximately(source.paragraphSpacing, 0f)
            || source.alignment != TextAlignmentOptions.Left
            || source.enableWordWrapping != RetainedHeaderTitlePolicy.WordWrapping
            || source.enableAutoSizing != RetainedHeaderTitlePolicy.AutoSizing
            || !Approximately(source.color.r, RetainedHeaderTitlePolicy.Red)
            || !Approximately(source.color.g, RetainedHeaderTitlePolicy.Green)
            || !Approximately(source.color.b, RetainedHeaderTitlePolicy.Blue)
            || !Approximately(source.color.a, RetainedHeaderTitlePolicy.Alpha)
            || source.raycastTarget != RetainedHeaderTitlePolicy.BlocksRaycasts)
        {
            error = "Duckov's native OptionsPanel/Text (TMP) no longer matches the required major-heading typography contract. "
                    + $"Observed font='{font?.name ?? "<null>"}', material='{material?.name ?? "<null>"}', "
                    + $"size={source.fontSize}, style={source.fontStyle}, weight={source.fontWeight}, "
                    + $"alignment={source.alignment}, wrapping={source.enableWordWrapping}, autoSize={source.enableAutoSizing}, "
                    + $"raycastTarget={source.raycastTarget}.";
            return false;
        }

        typography = new NativeHeaderTitleTypography
        {
            Font = font,
            Material = material,
            SourceDescription = sources[0].Path
        };
        return true;
    }

    private static string HierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
            names.Push(current.gameObject.name);
        return string.Join("/", names);
    }

    private static bool IsUdsObject(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.gameObject.name.StartsWith("UltimateDuckovStatistics", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool Approximately(float left, float right) => Math.Abs(left - right) <= 0.001f;
}
