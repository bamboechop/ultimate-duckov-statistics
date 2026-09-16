using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.Encounters;

// Small retained vector marker; its tip is the recorded location. Geometry is
// rebuilt only when the Graphic becomes dirty, never rasterized each frame.
internal sealed class EncounterPinGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        Shape(mesh, new Rect(rect.x + 1, rect.y - 2, rect.width, rect.height), new Color(0, 0, 0, .4f));
        Shape(mesh, rect, new Color(0, 0, 0, .8f));
        Shape(mesh, new Rect(rect.x + 1, rect.y + 1.5f, rect.width - 2, rect.height - 2.5f), color);
    }

    private static void Shape(VertexHelper mesh, Rect rect, Color tint)
    {
        const int segments = 30;
        var first = mesh.currentVertCount;
        var radius = rect.width * .5f;
        var center = new Vector2(rect.center.x, rect.yMax - radius);
        void Vertex(Vector2 point) => mesh.AddVert(new Vector3(point.x, point.y), tint, Vector2.zero);
        Vertex(center);
        for (var i = 0; i <= segments; i++)
        {
            var angle = (-45 + 270f * i / segments) * Mathf.Deg2Rad;
            Vertex(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
        Vertex(new Vector2(rect.center.x, rect.yMin));
        for (var i = 0; i < segments + 2; i++)
            mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % (segments + 2));
    }
}
