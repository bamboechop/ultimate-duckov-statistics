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
               && !instance.shaderKeywords.Contains(RetainedTabLabelShadowPolicy.RatioBypassKeyword)
               && color.r == RetainedTabLabelShadowPolicy.Red
               && color.g == RetainedTabLabelShadowPolicy.Green
               && color.b == RetainedTabLabelShadowPolicy.Blue
               && color.a == RetainedTabLabelShadowPolicy.Alpha
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty)
                   == RetainedTabLabelShadowPolicy.NativeUnderlayOffsetX
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty)
                   == RetainedTabLabelShadowPolicy.NativeUnderlayOffsetY
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlayDilateProperty)
                   == RetainedTabLabelShadowPolicy.NativeUnderlayDilate
               && instance.GetFloat(RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty)
                   == RetainedTabLabelShadowPolicy.NativeUnderlaySoftness
               && Approximately(
                   instance.GetFloat(RetainedTabLabelShadowPolicy.ScaleRatioCProperty),
                   RetainedTabLabelShadowPolicy.ExpectedNativeScaleRatioC,
                   RetainedTabLabelShadowPolicy.ScaleRatioTolerance);
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
        instance.SetColor(
            RetainedTabLabelShadowPolicy.UnderlayColorProperty,
            new Color(
                RetainedTabLabelShadowPolicy.Red,
                RetainedTabLabelShadowPolicy.Green,
                RetainedTabLabelShadowPolicy.Blue,
                RetainedTabLabelShadowPolicy.Alpha));
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
                     RetainedTabLabelShadowPolicy.ScaleRatioCProperty
                 })
        {
            if (!material.HasProperty(property))
                throw new InvalidOperationException(
                    $"Duckov's native TMP material no longer exposes '{property}'.");
        }

        if (!material.shaderKeywords.Contains(RetainedTabLabelShadowPolicy.UnderlayKeyword)
            || material.shaderKeywords.Contains(RetainedTabLabelShadowPolicy.RatioBypassKeyword)
            || material.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty)
                != RetainedTabLabelShadowPolicy.NativeUnderlayOffsetX
            || material.GetFloat(RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty)
                != RetainedTabLabelShadowPolicy.NativeUnderlayOffsetY
            || material.GetFloat(RetainedTabLabelShadowPolicy.UnderlayDilateProperty)
                != RetainedTabLabelShadowPolicy.NativeUnderlayDilate
            || material.GetFloat(RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty)
                != RetainedTabLabelShadowPolicy.NativeUnderlaySoftness
            || !Approximately(
                material.GetFloat(RetainedTabLabelShadowPolicy.ScaleRatioCProperty),
                RetainedTabLabelShadowPolicy.ExpectedNativeScaleRatioC,
                RetainedTabLabelShadowPolicy.ScaleRatioTolerance))
        {
            throw new InvalidOperationException(
                "Duckov's native TMP underlay no longer matches the audited retained-label contract.");
        }
    }

    private static bool Approximately(float actual, float expected, float tolerance)
    {
        return Math.Abs(actual - expected) <= tolerance;
    }

    public void Dispose()
    {
        owned?.Dispose();
        owned = null;
    }
}
