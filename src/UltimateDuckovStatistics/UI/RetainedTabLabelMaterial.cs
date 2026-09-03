using TMPro;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the private TMP material used only by retained tab labels.
/// </summary>
internal sealed class RetainedTabLabelMaterial : IDisposable
{
    private readonly Material source;
    private readonly Texture? sourceAtlas;
    private readonly Color sourceUnderlayColor;
    private readonly float sourceOffsetX;
    private readonly float sourceOffsetY;
    private readonly float sourceDilate;
    private readonly float sourceSoftness;
    private readonly float sourceScaleRatioC;
    private readonly string[] sourceKeywords;
    private RetainedOwnedResource<Material>? owned;

    private RetainedTabLabelMaterial(
        Material source,
        RetainedOwnedResource<Material> owned)
    {
        this.source = source;
        this.owned = owned;
        sourceAtlas = source.GetTexture(RetainedTabLabelShadowPolicy.MainTextureProperty);
        sourceUnderlayColor = source.GetColor(RetainedTabLabelShadowPolicy.UnderlayColorProperty);
        sourceOffsetX = source.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty);
        sourceOffsetY = source.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty);
        sourceDilate = source.GetFloat(RetainedTabLabelShadowPolicy.UnderlayDilateProperty);
        sourceSoftness = source.GetFloat(RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty);
        sourceScaleRatioC = source.GetFloat(RetainedTabLabelShadowPolicy.ScaleRatioCProperty);
        sourceKeywords = source.shaderKeywords.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    public Material Instance => owned?.Resource
        ?? throw new ObjectDisposedException(nameof(RetainedTabLabelMaterial));

    public Material Source => source;

    public static RetainedTabLabelMaterial Create(Material source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        RequireShaderProperties(source);
        var owned = RetainedOwnedResource<Material>.CreatePrivateClone(
            source,
            original => new Material(original)
            {
                name = RetainedTabLabelShadowPolicy.OwnedMaterialName,
                hideFlags = HideFlags.DontSave
            },
            UnityEngine.Object.Destroy);
        var result = new RetainedTabLabelMaterial(source, owned);
        try
        {
            result.Configure();
            if (!result.IsConfigured() || !result.IsSourceUnchanged())
                throw new InvalidOperationException("The retained tab-label material contract was not preserved.");
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public bool IsConfigured()
    {
        var instance = Instance;
        var color = instance.GetColor(RetainedTabLabelShadowPolicy.UnderlayColorProperty);
        return !ReferenceEquals(instance, source)
               && string.Equals(
                   instance.name,
                   RetainedTabLabelShadowPolicy.OwnedMaterialName,
                   StringComparison.Ordinal)
               && ReferenceEquals(
                   instance.GetTexture(RetainedTabLabelShadowPolicy.MainTextureProperty),
                   sourceAtlas)
               && instance.shaderKeywords.Contains(RetainedTabLabelShadowPolicy.UnderlayKeyword)
               && instance.shaderKeywords.Contains(RetainedTabLabelShadowPolicy.RatiosOffKeyword)
               && color.r == RetainedTabLabelShadowPolicy.Red
               && color.g == RetainedTabLabelShadowPolicy.Green
               && color.b == RetainedTabLabelShadowPolicy.Blue
               && color.a == RetainedTabLabelShadowPolicy.Alpha
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty)
                   == RetainedTabLabelShadowPolicy.UnderlayOffsetX
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty)
                   == RetainedTabLabelShadowPolicy.UnderlayOffsetY
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlayDilateProperty)
                   == RetainedTabLabelShadowPolicy.UnderlayDilate
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty)
                   == RetainedTabLabelShadowPolicy.UnderlaySoftness
               && instance.GetFloat(RetainedTabLabelShadowPolicy.ScaleRatioCProperty)
                   == RetainedTabLabelShadowPolicy.ScaleRatioC;
    }

    public bool IsSourceUnchanged()
    {
        var color = source.GetColor(RetainedTabLabelShadowPolicy.UnderlayColorProperty);
        return ReferenceEquals(
                   source.GetTexture(RetainedTabLabelShadowPolicy.MainTextureProperty),
                   sourceAtlas)
               && color == sourceUnderlayColor
               && source.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty) == sourceOffsetX
               && source.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty) == sourceOffsetY
               && source.GetFloat(RetainedTabLabelShadowPolicy.UnderlayDilateProperty) == sourceDilate
               && source.GetFloat(RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty) == sourceSoftness
               && source.GetFloat(RetainedTabLabelShadowPolicy.ScaleRatioCProperty) == sourceScaleRatioC
               && source.shaderKeywords
                   .OrderBy(value => value, StringComparer.Ordinal)
                   .SequenceEqual(sourceKeywords, StringComparer.Ordinal);
    }

    private void Configure()
    {
        var instance = Instance;
        instance.EnableKeyword(RetainedTabLabelShadowPolicy.UnderlayKeyword);
        instance.EnableKeyword(RetainedTabLabelShadowPolicy.RatiosOffKeyword);
        instance.SetColor(
            RetainedTabLabelShadowPolicy.UnderlayColorProperty,
            new Color(
                RetainedTabLabelShadowPolicy.Red,
                RetainedTabLabelShadowPolicy.Green,
                RetainedTabLabelShadowPolicy.Blue,
                RetainedTabLabelShadowPolicy.Alpha));
        instance.SetFloat(
            RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty,
            RetainedTabLabelShadowPolicy.UnderlayOffsetX);
        instance.SetFloat(
            RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty,
            RetainedTabLabelShadowPolicy.UnderlayOffsetY);
        instance.SetFloat(
            RetainedTabLabelShadowPolicy.UnderlayDilateProperty,
            RetainedTabLabelShadowPolicy.UnderlayDilate);
        instance.SetFloat(
            RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty,
            RetainedTabLabelShadowPolicy.UnderlaySoftness);
        ShaderUtilities.UpdateShaderRatios(instance);
    }

    private static void RequireShaderProperties(Material material)
    {
        foreach (var property in new[]
                 {
                     RetainedTabLabelShadowPolicy.MainTextureProperty,
                     RetainedTabLabelShadowPolicy.UnderlayColorProperty,
                     RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty,
                     RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty,
                     RetainedTabLabelShadowPolicy.UnderlayDilateProperty,
                     RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty,
                     RetainedTabLabelShadowPolicy.ScaleRatioCProperty,
                     RetainedTabLabelShadowPolicy.GradientScaleProperty
                 })
        {
            if (!material.HasProperty(property))
                throw new InvalidOperationException(
                    $"Duckov's native TMP material no longer exposes '{property}'.");
        }

        if (material.GetFloat(RetainedTabLabelShadowPolicy.GradientScaleProperty)
            != RetainedTabLabelShadowPolicy.AuditedNativeGradientScale)
        {
            throw new InvalidOperationException(
                "Duckov's native TMP atlas gradient scale no longer matches the audited retained-label contract.");
        }
    }

    public void Dispose()
    {
        owned?.Dispose();
        owned = null;
    }
}
