using ItemStatsSystem;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

// Small, bounded, owned alpha silhouettes. Metadata sprites/textures stay untouched.
internal sealed class KillFeedIcons : IDisposable
{
    private readonly Dictionary<int, Sprite?> weapons = new();
    private readonly List<Sprite> owned = new();
    private Sprite? headshot;
    internal Sprite Headshot => headshot != null ? headshot : headshot = CreateHeadshot();

    internal Sprite? Weapon(string id)
    {
        if (!NativeItemTypeIdPolicy.TryParse(id, out var type)) return null;
        if (weapons.TryGetValue(type, out var cached)) return cached;
        var metadata = ItemAssetsCollection.GetMetaData(type);
        var original = metadata.id == type ? metadata.icon : null;
        if (original == null) return null;
        // Once full, use the native icon without allocating more GPU resources.
        if (weapons.Count >= 64) return original;
        Sprite result = original;
        var previous = RenderTexture.active;
        RenderTexture? buffer = null;
        Texture2D? texture = null;
        try
        {
            var rect = original.textureRect;
            var size = original.texture;
            var width = Math.Max(1, (int)(128 * rect.width / Math.Max(rect.width, rect.height)));
            var height = Math.Max(1, (int)(128 * rect.height / Math.Max(rect.width, rect.height)));
            buffer = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(size, buffer, new Vector2(rect.width / size.width, rect.height / size.height), new Vector2(rect.x / size.width, rect.y / size.height));
            RenderTexture.active = buffer;
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, pixels[i].a);
            texture.SetPixels32(pixels); texture.Apply(false, true);
            result = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f));
            owned.Add(result); texture = null;
        }
        catch { /* Native color icon is a valid fallback on unsupported GPU/sprite layouts. */ }
        finally
        {
            RenderTexture.active = previous;
            if (buffer != null) RenderTexture.ReleaseTemporary(buffer);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
        weapons[type] = result;
        return result;
    }

    private Sprite CreateHeadshot()
    {
        // Independent head-and-crosshair pictogram, not an asset from another mod.
        const int size = 64;
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - 31.5f; var dy = y - 37f;
                var radius = Math.Sqrt(dx * dx + dy * dy);
                var head = radius >= 13 && radius <= 17;
                var shoulders = y >= 7 && y <= 14 && Math.Abs(dx) < 22 - Math.Abs(y - 10) * 2;
                var cross = (Math.Abs(dx) <= 1.5 && y >= 19 && y <= 60)
                    || (Math.Abs(dy) <= 1.5 && x >= 7 && x <= 56);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(head || shoulders || cross ? 255 : 0));
            }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels); texture.Apply(false, true);
        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
        owned.Add(sprite);
        return sprite;
    }

    public void Dispose()
    {
        foreach (var sprite in owned)
        {
            if (sprite == null) continue;
            UnityEngine.Object.Destroy(sprite.texture); UnityEngine.Object.Destroy(sprite);
        }
        owned.Clear(); weapons.Clear(); headshot = null;
    }
}
