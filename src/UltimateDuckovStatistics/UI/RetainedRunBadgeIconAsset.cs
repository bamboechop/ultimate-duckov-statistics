using UnityEngine;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns a minimal UDS-authored procedural fallback for one semantic run-badge icon.
/// </summary>
internal sealed class RetainedRunBadgeIconAsset : IDisposable
{
    private Texture2D? texture;
    private Sprite? sprite;

    private RetainedRunBadgeIconAsset(Texture2D texture, Sprite sprite)
    {
        this.texture = texture;
        this.sprite = sprite;
    }

    public Sprite Sprite => sprite ?? throw new ObjectDisposedException(nameof(RetainedRunBadgeIconAsset));

    public static RetainedRunBadgeIconAsset Create(RetainedRunBadgeIconKind iconKind)
    {
        var width = RetainedRunBadgeProceduralIconPolicy.GetTextureWidth(iconKind);
        var height = RetainedRunBadgeProceduralIconPolicy.GetTextureHeight(iconKind);
        var alpha = RetainedRunBadgeProceduralIconPolicy.CreateTopDownAlpha(iconKind);
        Texture2D? texture = null;
        Sprite? sprite = null;
        try
        {
            texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: true)
            {
                name = $"UltimateDuckovStatisticsRunBadge{iconKind}Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[alpha.Length];
            for (var topDownY = 0; topDownY < height; topDownY++)
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = topDownY * width + x;
                var textureIndex = (height - 1 - topDownY) * width + x;
                pixels[textureIndex] = new Color32(255, 255, 255, alpha[sourceIndex]);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            sprite = UnityEngine.Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: height,
                extrude: 0,
                SpriteMeshType.FullRect);
            sprite.name = $"UltimateDuckovStatisticsRunBadge{iconKind}";
            sprite.hideFlags = HideFlags.DontSave;
            return new RetainedRunBadgeIconAsset(texture, sprite);
        }
        catch
        {
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (texture != null) UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    public void Dispose()
    {
        if (sprite != null) UnityEngine.Object.Destroy(sprite);
        if (texture != null) UnityEngine.Object.Destroy(texture);
        sprite = null;
        texture = null;
    }
}
