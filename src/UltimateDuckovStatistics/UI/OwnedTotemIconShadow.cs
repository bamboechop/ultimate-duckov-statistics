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
