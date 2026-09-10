using System.Reflection;
using LeTai.TrueShadow;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

// The installed TrueShadow allocates a separate mesh for Image casters, but its
// OnDestroy only releases the renderer and shadow texture. Own that final mesh
// only for shadows UDS adds to its own item Images, never for native TMP casters.
internal sealed class OwnedTotemIconShadow : TrueShadow
{
    private static readonly PropertyInfo? SpriteMeshProperty = ResolveSpriteMeshProperty();

    public static OwnedTotemIconShadow? TryAddTo(Image icon) => SpriteMeshProperty == null
        ? null : icon.gameObject.AddComponent<OwnedTotemIconShadow>();

#if UDS_PERFORMANCE_DIAGNOSTICS
    internal int DiagnosticMeshInstanceId => SpriteMeshProperty?.GetValue(this) is Mesh mesh && mesh != null
        ? mesh.GetInstanceID() : 0;

    internal string DiagnosticRenderState()
    {
        // F11 only: inspect native state without repairing it or retaining objects.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object? Read(object? value, string name) => value == null ? null
            : value.GetType().GetProperty(name, flags)?.GetValue(value) ?? value.GetType().GetField(name, flags)?.GetValue(value);
        object? Native(string name) => typeof(TrueShadow).GetField(name, flags)?.GetValue(this);
        string Value(object? value) => value is UnityEngine.Object unity ? (unity != null ? unity.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")
            : value is IFormattable formattable ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : value?.ToString() ?? "null";
        var icon = GetComponent<Image>();
        var renderer = Native("shadowRenderer");
        var canvasRenderer = Read(renderer, "CanvasRenderer");
        var container = Native("shadowContainer");
        var texture = Read(container, "Texture");
        var shadowGraphic = Read(renderer, "graphic");
        var alpha = canvasRenderer?.GetType().GetMethod("GetAlpha", flags)?.Invoke(canvasRenderer, null);
        var textureCreated = texture?.GetType().GetMethod("IsCreated", flags)?.Invoke(texture, null);
        var material = canvasRenderer?.GetType().GetMethod("GetMaterial", flags, null, new[] { typeof(int) }, null)?.Invoke(canvasRenderer, new object[] { 0 });
        var materialForRendering = Read(shadowGraphic, "materialForRendering");
        string Stencil(object? mat) => Value(mat?.GetType().GetMethod("GetInt", flags, null, new[] { typeof(string) }, null)?.Invoke(mat, new object[] { "_Stencil" }));
        var renderedMesh = new Mesh();
        string renderedVertices;
        try
        {
            canvasRenderer?.GetType().GetMethod("GetMesh", flags, null, new[] { typeof(Mesh) }, null)?.Invoke(canvasRenderer, new object[] { renderedMesh });
            renderedVertices = Value(Read(renderedMesh, "vertexCount"));
        }
        finally { UnityEngine.Object.Destroy(renderedMesh); }
        return "owner=" + Value(this) + ";active=" + isActiveAndEnabled + ";sprite=" + Value(icon?.sprite)
            + ";mesh=" + Value(SpriteMeshProperty?.GetValue(this)) + ";vertices=" + Value(Read(SpriteMeshProperty?.GetValue(this), "vertexCount"))
            + ";iconEnabled=" + Value(Read(icon, "isActiveAndEnabled")) + ";renderer=" + Value(renderer)
            + ";rendererActive=" + Value(Read(renderer, "isActiveAndEnabled")) + ";alpha=" + Value(alpha)
            + ";colorAlpha=" + Value(Read(Read(shadowGraphic, "color"), "a"))
            + ";casterWidth=" + Value(Read(Read(Read(icon, "rectTransform"), "rect"), "width"))
            + ";shadowWidth=" + Value(Read(Read(Read(shadowGraphic, "rectTransform"), "rect"), "width"))
            + ";casterCull=" + Value(Read(Read(icon, "canvasRenderer"), "cull")) + ";shadowCull=" + Value(Read(canvasRenderer, "cull"))
            + ";texture=" + Value(texture) + ";textureCreated=" + Value(textureCreated)
            + ";references=" + Value(Read(container, "RefCount")) + ";rawTexture=" + Value(Read(shadowGraphic, "texture"))
            + ";rawActive=" + Value(Read(shadowGraphic, "isActiveAndEnabled")) + ";renderedVertices=" + renderedVertices
            + ";materials=" + Value(Read(canvasRenderer, "materialCount")) + ";material=" + Value(material) + ";stencil=" + Stencil(material)
            + ";expectedMaterial=" + Value(materialForRendering) + ";expectedStencil=" + Stencil(materialForRendering)
            + ";layoutDirty=" + Value(Native("layoutDirty")) + ";textureDirty=" + Value(Native("textureDirty"));
    }
#endif

    private static PropertyInfo? ResolveSpriteMeshProperty()
    {
        var property = typeof(TrueShadow).GetProperty("SpriteMesh", BindingFlags.Instance | BindingFlags.NonPublic);
        var getter = property?.GetGetMethod(nonPublic: true);
        return property?.PropertyType == typeof(Mesh) && getter != null
            && !getter.IsStatic && getter.GetParameters().Length == 0 ? property : null;
    }

    protected override void OnDestroy()
    {
        try
        {
            base.OnDestroy();
        }
        finally
        {
            // OnDisable deliberately keeps this mesh: pooled rows and hidden tabs
            // reuse it when native OnEnable runs again.
            if (SpriteMeshProperty?.GetValue(this) is Mesh mesh && mesh != null)
                UnityEngine.Object.Destroy(mesh);
        }
    }
}
