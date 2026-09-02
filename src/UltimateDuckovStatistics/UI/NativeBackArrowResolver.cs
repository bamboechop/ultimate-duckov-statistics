using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

internal static class NativeBackArrowResolver
{
    public static bool TryResolve(
        Canvas canvas,
        out Sprite? sprite,
        out string? sourceDescription,
        out string? error)
    {
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));

        sprite = null;
        sourceDescription = null;
        error = null;
        var roots = canvas.gameObject.scene.IsValid()
            ? canvas.gameObject.scene.GetRootGameObjects()
            : new[] { canvas.gameObject };
        var auditedControls = roots
            .SelectMany(root => root.GetComponentsInChildren<Button>(includeInactive: true))
            .Where(button => button != null && !IsUdsObject(button.transform))
            .Select(button => new { Button = button, Path = HierarchyPath(button.transform) })
            .Where(candidate => NativeBackArrowPolicy.IsAuditedControlPath(candidate.Path))
            .ToArray();
        if (auditedControls.Length > 0)
        {
            var auditedSprites = auditedControls
                .SelectMany(candidate => candidate.Button.GetComponentsInChildren<Image>(includeInactive: true))
                .Where(image => image != null
                                && image.sprite != null
                                && NativeBackArrowPolicy.IsExpectedSpriteName(image.sprite.name))
                .Select(image => image.sprite)
                .Distinct()
                .ToArray();
            if (auditedControls.Length == 1 && auditedSprites.Length == 1)
            {
                sprite = auditedSprites[0];
                sourceDescription = $"{auditedControls[0].Path}/{NativeBackArrowPolicy.SpriteName}";
                return true;
            }

            error = "Duckov's audited OptionsPanel/Return control did not expose exactly one expected native back-arrow sprite.";
            return false;
        }

        var loadedSprites = Resources.FindObjectsOfTypeAll<Sprite>()
            .Where(candidate => candidate != null && NativeBackArrowPolicy.IsExpectedSpriteName(candidate.name))
            .Distinct()
            .ToArray();
        if (loadedSprites.Length == 1)
        {
            sprite = loadedSprites[0];
            sourceDescription = $"loaded native sprite {NativeBackArrowPolicy.SpriteName}";
            return true;
        }

        error = loadedSprites.Length == 0
            ? $"Duckov's native back-arrow sprite '{NativeBackArrowPolicy.SpriteName}' was not loaded."
            : $"Duckov exposed multiple loaded sprites named '{NativeBackArrowPolicy.SpriteName}', so selection was ambiguous.";
        return false;
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
