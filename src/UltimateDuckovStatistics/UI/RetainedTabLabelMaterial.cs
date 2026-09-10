using TMPro;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

/// <summary>Owns the private TMP material used by retained tab labels.</summary>
internal sealed class RetainedTabLabelMaterial : IDisposable
{
    private RetainedOwnedResource<Material>? owned;

    private RetainedTabLabelMaterial(RetainedOwnedResource<Material> owned) => this.owned = owned;

    public Material Instance => owned?.Resource
        ?? throw new ObjectDisposedException(nameof(RetainedTabLabelMaterial));

    public static RetainedTabLabelMaterial Create(Material source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var owned = RetainedOwnedResource<Material>.CreatePrivateClone(
            source,
            original => new Material(original)
            {
                name = RetainedTabLabelShadowPolicy.OwnedMaterialName,
                hideFlags = HideFlags.DontSave
            },
            UnityEngine.Object.Destroy);
        var result = new RetainedTabLabelMaterial(owned);
        try
        {
            var instance = result.Instance;
            if (instance.HasProperty(RetainedTabLabelShadowPolicy.UnderlayColorProperty))
            {
                instance.EnableKeyword(RetainedTabLabelShadowPolicy.UnderlayKeyword);
                instance.SetColor(RetainedTabLabelShadowPolicy.UnderlayColorProperty,
                    new Color(RetainedTabLabelShadowPolicy.Red, RetainedTabLabelShadowPolicy.Green,
                        RetainedTabLabelShadowPolicy.Blue, RetainedTabLabelShadowPolicy.Alpha));
                ShaderUtilities.UpdateShaderRatios(instance);
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        owned?.Dispose();
        owned = null;
    }
}
