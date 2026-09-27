using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.Shell.Tests;

/// <summary>
/// Narrow vertical negotiation model from installed UnityEngine.UI's LayoutUtility and
/// HorizontalOrVerticalLayoutGroup CalcAlongAxis/SetChildrenAlongAxis (Unity 2022.3.62f2).
/// LayoutElement overrides each nonnegative property at priority1; groups have priority0.
/// A missing override (-1) must propagate group flexibility, including horizontal cross-axis
/// force expansion. This executes negotiation over the production-created hierarchy, rather
/// than checking that production assigned particular properties. It is not a renderer or a
/// replacement for the native visual check. All fixture transforms use unit scale.
/// </summary>
internal static class ExtractionVerticalLayout
{
    internal readonly record struct Bounds(float Top, float Height)
    {
        public float Bottom => Top + Height;
    }

    private readonly record struct Sizes(float Min, float Preferred, float Flexible);

    internal static Dictionary<GameObject, Bounds> Rebuild(GameObject root, float height)
    {
        var bounds = new Dictionary<GameObject, Bounds>();
        Arrange(root, 0, height, bounds);
        return bounds;
    }

    private static GameObject[] Children(GameObject go) => go.transform.Children
        .Where(child => child.gameObject.activeInHierarchy).Select(child => child.gameObject).ToArray();

    private static Sizes Measure(GameObject go)
    {
        var sizes = go.GetComponent<HorizontalLayoutGroup>() is { } group
            ? GroupSizes(go, group)
            : new Sizes(0, go.GetComponent<TextMeshProUGUI>()?.preferredHeight ?? 0, 0);
        if (go.GetComponent<LayoutElement>() is { } element)
            sizes = new Sizes(element.minHeight >= 0 ? element.minHeight : sizes.Min,
                element.preferredHeight >= 0 ? element.preferredHeight : sizes.Preferred,
                element.flexibleHeight >= 0 ? element.flexibleHeight : sizes.Flexible);
        return sizes with { Preferred = Math.Max(sizes.Min, sizes.Preferred) };
    }

    private static Sizes ChildSizes(GameObject child, HorizontalLayoutGroup parent)
    {
        var sizes = parent.childControlHeight ? Measure(child)
            : new Sizes(((RectTransform)child.transform).sizeDelta.y, ((RectTransform)child.transform).sizeDelta.y, 0);
        return sizes with { Flexible = parent.childForceExpandHeight ? Math.Max(1, sizes.Flexible) : sizes.Flexible };
    }

    private static Sizes GroupSizes(GameObject go, HorizontalLayoutGroup group)
    {
        float padding = group.padding.top + group.padding.bottom;
        var sizes = new Sizes(padding, padding, 0);
        var children = Children(go);
        foreach (var child in children)
        {
            var next = ChildSizes(child, group);
            sizes = group is VerticalLayoutGroup
                ? new Sizes(sizes.Min + next.Min + group.spacing, sizes.Preferred + next.Preferred + group.spacing, sizes.Flexible + next.Flexible)
                : new Sizes(Math.Max(sizes.Min, next.Min + padding), Math.Max(sizes.Preferred, next.Preferred + padding), Math.Max(sizes.Flexible, next.Flexible));
        }
        if (group is VerticalLayoutGroup && children.Length > 0)
            sizes = sizes with { Min = sizes.Min - group.spacing, Preferred = sizes.Preferred - group.spacing };
        return sizes with { Preferred = Math.Max(sizes.Min, sizes.Preferred) };
    }

    private static void Arrange(GameObject go, float top, float height, Dictionary<GameObject, Bounds> bounds)
    {
        bounds[go] = new Bounds(top, height);
        if (go.GetComponent<HorizontalLayoutGroup>() is not { } group) return;
        var total = GroupSizes(go, group);
        var alignment = (int)group.childAlignment / 3 * .5f;
        var surplus = height - total.Preferred;
        var cursor = top + group.padding.top;
        if (surplus > 0 && total.Flexible == 0) cursor += surplus * alignment;
        var flexibility = surplus > 0 && total.Flexible > 0 ? surplus / total.Flexible : 0;
        var interpolation = total.Min == total.Preferred ? 0 : Math.Clamp((height - total.Min) / (total.Preferred - total.Min), 0, 1);
        foreach (var child in Children(go))
        {
            var sizes = ChildSizes(child, group);
            float allocated, position;
            if (group is VerticalLayoutGroup)
            {
                allocated = sizes.Min + (sizes.Preferred - sizes.Min) * interpolation + sizes.Flexible * flexibility;
                position = cursor;
                cursor += allocated + group.spacing;
            }
            else
            {
                allocated = Math.Clamp(height - group.padding.top - group.padding.bottom, sizes.Min, sizes.Flexible > 0 ? height : sizes.Preferred);
                position = top + group.padding.top + (height - group.padding.top - group.padding.bottom - allocated) * alignment;
            }
            Arrange(child, position, allocated, bounds);
        }
    }
}
