// Native/map-art boundaries only. The observer Tick, reducer and presentation are source-linked.
#pragma warning disable CA1050, CA1051, CA1707, CA1711, CA1720, CA1822, CA2211
using UnityEngine;

public sealed partial class CharacterMainControl : UnityEngine.Object { }

namespace UnityEngine
{
    public readonly struct Rect
    {
        public readonly float x, y, width, height;
    }
    public sealed class Sprite
    {
        public string name = "test";
        public Rect rect;
        public Vector2 pivot = new(0, 0);
        public float pixelsPerUnit = 1;
    }
    public static class QualitySettings { public static string activeColorSpace => "Linear"; }
}

namespace Duckov.MiniMaps
{
    public sealed class MiniMapSettings
    {
        public static MiniMapSettings? Instance { get; set; }
        public List<MapEntry> maps = new();
        public Sprite? combinedSprite;
        public Vector3 combinedCenter;
        public float combinedSize;
    }
    public sealed class MapEntry
    {
        public string sceneID = "";
        public Sprite? sprite;
        public bool hide, noSignal;
        public Vector3 mapWorldCenter;
        public float imageWorldSize;
        public Vector2 Offset = new(0, 0);
    }
}

namespace UltimateDuckovStatistics.Encounters
{
    internal sealed class EncounterMapArtworkCache : IDisposable
    {
        public EncounterMapArtworkCache(IEncounterObservationSink sink, string directory, Action<string> log) { }
        public string Status => "Artwork not simulated";
        public void Tick() { }
        public void TryCapture(string key, string json, Sprite sprite) { }
        public void Dispose() { }
    }
}
