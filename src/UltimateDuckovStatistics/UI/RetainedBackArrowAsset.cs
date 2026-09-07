using UnityEngine;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the minimal UDS-authored texture and sprite used by the Step 02-R back-arrow graphic.
/// </summary>
internal sealed class RetainedBackArrowAsset : IDisposable
{
    private Texture2D? texture;
    private Sprite? sprite;

    private RetainedBackArrowAsset(Texture2D texture, Sprite sprite)
    {
        this.texture = texture;
        this.sprite = sprite;
    }

    public Sprite Sprite => sprite ?? throw new ObjectDisposedException(nameof(RetainedBackArrowAsset));

    public static RetainedBackArrowAsset Create()
    {
        var alpha = RetainedBackArrowAssetPolicy.DecodeTopDownAlpha();
        Texture2D? texture = null;
        Sprite? sprite = null;
        try
        {
            texture = new Texture2D(
                RetainedBackArrowAssetPolicy.WidthPixels,
                RetainedBackArrowAssetPolicy.HeightPixels,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: true)
            {
                name = RetainedBackArrowAssetPolicy.TextureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[alpha.Length];
            for (var topDownY = 0; topDownY < RetainedBackArrowAssetPolicy.HeightPixels; topDownY++)
            for (var x = 0; x < RetainedBackArrowAssetPolicy.WidthPixels; x++)
            {
                var sourceIndex = topDownY * RetainedBackArrowAssetPolicy.WidthPixels + x;
                var textureIndex = (RetainedBackArrowAssetPolicy.HeightPixels - 1 - topDownY)
                                   * RetainedBackArrowAssetPolicy.WidthPixels
                                   + x;
                pixels[textureIndex] = new Color32(255, 255, 255, alpha[sourceIndex]);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            sprite = UnityEngine.Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    RetainedBackArrowAssetPolicy.WidthPixels,
                    RetainedBackArrowAssetPolicy.HeightPixels),
                new Vector2(0.5f, 0.5f),
                RetainedBackArrowAssetPolicy.PixelsPerUnit,
                extrude: 0,
                SpriteMeshType.FullRect);
            sprite.name = RetainedBackArrowAssetPolicy.SpriteName;
            sprite.hideFlags = HideFlags.DontSave;
            return new RetainedBackArrowAsset(texture, sprite);
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
