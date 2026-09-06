using Duckov.UI;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

// Reuse Duckov's text-only tooltip, including its disable cleanup. No live Item is required.
internal sealed class RunsTooltipFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    public void OnSelect(BaseEventData data) => GetComponent<TooltipsProvider>()?.OnPointerEnter(null!);
    public void OnDeselect(BaseEventData data) => GetComponent<TooltipsProvider>()?.OnPointerExit(null!);
}

internal static class RunsNativeScrollConfiguration
{
    public static void Apply(ScrollRect target)
    {
        // Read the installed shared UI prefab, never instantiate its hierarchy or its listeners.
        // The baseline probe verifies this metadata path. No guessed sensitivity is called native.
        var source = GameplayDataSettings.UIPrefabs.ScrollRect;
        target.movementType = source.movementType;
        target.elasticity = source.elasticity;
        target.inertia = source.inertia;
        target.decelerationRate = source.decelerationRate;
        target.scrollSensitivity = source.scrollSensitivity;
    }
}

internal static class RunsHistoryClipping
{
    public static void Attach(RectTransform viewport)
    {
        // Keep RectMask2D's culling, with a stencil boundary that also clips newly rebuilt
        // TMP glyph/fallback submeshes before their deferred rectangular culling settles.
        var stencil = viewport.gameObject.AddComponent<Image>();
        stencil.color = Color.white;
        stencil.raycastTarget = false;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
    }
}

// Only the horizontal contour and its two quarter-circle corners are drawn, never a full frame.
internal sealed class RunsOverflowEdge : MaskableGraphic
{
    public bool Top { get; set; }
    public float Radius { get; set; } = 10;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = rectTransform.rect;
        var points = RunsRoundedEdgePolicy.Points(rect.width, rect.height, Radius, Top);
        for (var i = 1; i < points.Count; i++)
        {
            var a = new Vector2(rect.xMin + points[i - 1].X, rect.yMax - points[i - 1].Y);
            var b = new Vector2(rect.xMin + points[i].X, rect.yMax - points[i].Y);
            var direction = b - a;
            var normal = new Vector2(-direction.y, direction.x).normalized * .5f;
            var start = mesh.currentVertCount;
            mesh.AddVert(a - normal, color, Vector2.zero); mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero); mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
