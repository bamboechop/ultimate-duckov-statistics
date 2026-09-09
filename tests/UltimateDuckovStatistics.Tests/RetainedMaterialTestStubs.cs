// Boundary for the linked material owner; this does not emulate Unity rendering.
#pragma warning disable CA1707, CA1708, CA1720, CA1716, CA1711
namespace UnityEngine
{
    public partial class Object
    {
        public int DestroyCount { get; private set; }
        public static void Destroy(Object value) => value.DestroyCount++;
    }

    public enum HideFlags { None, DontSave }

    public readonly record struct Color(float r, float g, float b, float a);

    public sealed class Material : Object
    {
        public Material() { }
        public Material(Material source)
        {
            name = source.name;
            UnderlaySupported = source.UnderlaySupported;
            Underlay = source.Underlay;
            Ratio = source.Ratio;
            Keywords = new HashSet<string>(source.Keywords, StringComparer.Ordinal);
            Atlas = source.Atlas;
        }
        public string name { get; set; } = "native material";
        public HideFlags hideFlags { get; set; }
        public object Atlas { get; set; } = new();
        public bool UnderlaySupported { get; set; } = true;
        public Color Underlay { get; private set; }
        public float Ratio { get; set; } = .75f;
        public HashSet<string> Keywords { get; } = new(StringComparer.Ordinal);
        public bool HasProperty(string property) => UnderlaySupported && property == "_UnderlayColor";
        public void EnableKeyword(string keyword) => Keywords.Add(keyword);
        public void SetColor(string property, Color color)
        {
            if (!HasProperty(property)) throw new InvalidOperationException("Unavailable shader property.");
            Underlay = color;
        }
    }
}

namespace TMPro
{
    public static class ShaderUtilities
    {
        public static void UpdateShaderRatios(UnityEngine.Material material) => material.Ratio = .314f;
    }
}
