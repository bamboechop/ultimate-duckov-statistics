using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

internal sealed class NativeShellTemplates
{
    public NativeTextTemplateSnapshot? HeadingTypography { get; set; }

    public NativeTextTemplateSnapshot? NavigationTypography { get; set; }

    public Button? BackControl { get; set; }

    public Button? TabButton { get; set; }

    public Graphic? Surface { get; set; }

    public Graphic? NavigationRail { get; set; }

    public string Describe() =>
        $"heading={HeadingTypography?.SourceDescription ?? "public text fallback"}; " +
        $"navigation={NavigationTypography?.SourceDescription ?? "public text fallback"}; " +
        $"back={Describe(BackControl)}; tab={Describe(TabButton)}; " +
        $"surface={Describe(Surface)}; rail={Describe(NavigationRail)}";

    private static string Describe(Component? component) =>
        component == null ? "restrained retained-mode fallback" : NativeShellTemplateResolver.HierarchyPath(component.transform);
}

internal static class NativeShellTemplateResolver
{
    public static NativeShellTemplates Resolve(Canvas canvas, NativeTextTemplateSnapshot? navigationTypography)
    {
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));
        var roots = canvas.gameObject.scene.IsValid()
            ? canvas.gameObject.scene.GetRootGameObjects()
            : new[] { canvas.gameObject };

        var headings = roots
            .SelectMany(root => root.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true))
            .Where(value => value != null && value.font != null && !IsUdsObject(value.transform))
            .Select(value => new
            {
                Value = value,
                Path = HierarchyPath(value.transform),
                Score = NativeShellTemplatePolicy.ScoreHeading(HierarchyPath(value.transform), value.fontSize)
            })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Path, StringComparer.Ordinal)
            .ToArray();
        NativeTextTemplateSnapshot? heading = null;
        var headingCandidate = headings.FirstOrDefault();
        if (headingCandidate != null)
        {
            NativeTextTemplateSnapshot.TryCapture(
                headingCandidate.Value,
                $"loaded native heading {headingCandidate.Path}",
                out heading);
        }

        var buttons = roots
            .SelectMany(root => root.GetComponentsInChildren<Button>(includeInactive: true))
            .Where(value => value != null && !IsUdsObject(value.transform))
            .Select(value => new { Value = value, Path = HierarchyPath(value.transform) })
            .ToArray();
        var back = buttons
            .Select(value => new
            {
                value.Value,
                value.Path,
                Score = NativeShellTemplatePolicy.ScoreBack(value.Path, HasIcon(value.Value))
            })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Path, StringComparer.Ordinal)
            .FirstOrDefault()?.Value;
        var tab = buttons
            .Select(value => new
            {
                value.Value,
                value.Path,
                Score = NativeShellTemplatePolicy.ScoreTab(value.Path)
            })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Path, StringComparer.Ordinal)
            .FirstOrDefault()?.Value;

        var graphics = roots
            .SelectMany(root => root.GetComponentsInChildren<Graphic>(includeInactive: true))
            .Where(value => value != null && !IsUdsObject(value.transform))
            .Select(value => new { Value = value, Path = HierarchyPath(value.transform) })
            .ToArray();
        var surface = graphics
            .Select(value => new
            {
                value.Value,
                value.Path,
                Score = NativeShellTemplatePolicy.ScoreSurface(value.Path)
            })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Path, StringComparer.Ordinal)
            .FirstOrDefault()?.Value;
        var rail = graphics
            .Select(value => new
            {
                value.Value,
                value.Path,
                Score = NativeShellTemplatePolicy.ScoreRail(value.Path)
            })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Path, StringComparer.Ordinal)
            .FirstOrDefault()?.Value;

        return new NativeShellTemplates
        {
            HeadingTypography = heading,
            NavigationTypography = navigationTypography,
            BackControl = back,
            TabButton = tab,
            Surface = surface,
            NavigationRail = rail
        };
    }

    public static string HierarchyPath(Transform? transform)
    {
        if (transform == null) return "<unavailable>";
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent) names.Push(current.gameObject.name);
        return string.Join("/", names);
    }

    private static bool HasIcon(Button button) => button.GetComponentsInChildren<Image>(includeInactive: true)
        .Any(image => image != null && image != button.targetGraphic && image.sprite != null);

    private static bool IsUdsObject(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.gameObject.name.StartsWith("UltimateDuckovStatistics", StringComparison.Ordinal)) return true;
        return false;
    }
}
