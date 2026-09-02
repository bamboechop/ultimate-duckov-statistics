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
            || !string.Equals(material.name, RetainedHeaderTitlePolicy.MaterialName, StringComparison.Ordinal))
        {
            error = "Duckov's native OptionsPanel/Text (TMP) no longer exposes the required major-heading presentation references. "
                    + $"Observed font='{font?.name ?? "<null>"}', material='{material?.name ?? "<null>"}'.";
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
}
