using UnityEngine;
using Object = UnityEngine.Object;

namespace UltimateDuckovStatistics.Encounters;

internal sealed class EncounterRouteTexture : IDisposable
{
    private EncounterRouteRaster? raster;
    private Texture2D? texture;
    private IReadOnlyList<EncounterRouteStroke>? renderedStrokes;
    private EncounterMapFrame renderedFrame;
    private double renderedProgress = -1;
    private float logicalWidth, logicalHeight;

    public void Draw(IReadOnlyList<EncounterRouteStroke> strokes, EncounterMapFrame frame, Rect localArea,
        EncounterRevealTimeline? timeline, double progress)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (raster == null || logicalWidth != localArea.width || logicalHeight != localArea.height)
        {
            Dispose();
            logicalWidth = localArea.width; logicalHeight = localArea.height;
            raster = new EncounterRouteRaster(logicalWidth, logicalHeight);
            texture = new Texture2D(raster.Width, raster.Height, TextureFormat.RGBA32, false)
            { name = "UDS encounter route layer", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }
        if (!ReferenceEquals(strokes, renderedStrokes) || renderedProgress != progress
            || renderedFrame.MinX != frame.MinX || renderedFrame.MinY != frame.MinY
            || renderedFrame.Width != frame.Width || renderedFrame.Height != frame.Height)
        {
            raster.Render(strokes, frame, timeline, progress);
            texture!.LoadRawTextureData(raster.Pixels);
            texture.Apply(false, false);
            renderedStrokes = strokes; renderedFrame = frame; renderedProgress = progress;
        }
        var priorColor = GUI.color;
        try { GUI.color = Color.white; GUI.DrawTexture(localArea, texture); }
        finally { GUI.color = priorColor; }
    }

    public void Dispose()
    {
        if (texture != null) Object.Destroy(texture);
        texture = null; raster = null; renderedStrokes = null; renderedProgress = -1;
    }
}
