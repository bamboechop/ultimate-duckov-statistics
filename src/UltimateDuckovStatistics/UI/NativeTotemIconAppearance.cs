using ItemStatsSystem;
using LeTai.TrueShadow;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

// Duckov ItemDisplay keeps the sprite white and applies its DisplayQuality glow.
// Borrow the same native metadata/style; never infer a color from a name or tier.
internal static class NativeTotemIconAppearance
{
    public static void Apply(Image icon, string? stableId)
    {
        var shadow = icon.GetComponent<TrueShadow>();
        try
        {
            if (icon.sprite == null || !NativeItemTypeIdPolicy.TryParse(stableId ?? "", out var typeId))
            { if (shadow != null) shadow.enabled = false; return; }
            var metadata = ItemAssetsCollection.GetMetaData(typeId);
            if (metadata.id != typeId || metadata.icon != icon.sprite
                || metadata.tags?.Any(tag => tag != null && tag.name == "Totem") != true)
            { if (shadow != null) shadow.enabled = false; return; }
            if (shadow == null)
            {
                shadow = OwnedTotemIconShadow.TryAddTo(icon);
                if (shadow == null) return;
                // Installed ItemDisplay prefab quality shadow: size 3, spread .5,
                // independent of the sprite's color, using its alpha silhouette.
                shadow.Size = 3; shadow.Spread = .5f; shadow.UseCasterAlpha = true;
                shadow.IgnoreCasterColor = true; shadow.IgnoreExternalActive = true;
                shadow.ShadowAsSibling = false;
            }
            Duckov.Utilities.GameplayDataSettings.UIStyle.ApplyDisplayQualityShadow(metadata.displayQuality, shadow);
            shadow.enabled = true;
        }
        catch
        {
            // Missing optional visual metadata must never suppress the item or shell.
            if (shadow != null) shadow.enabled = false;
        }
    }

    public static void Clear(Image icon)
    {
        var shadow = icon.GetComponent<TrueShadow>();
        if (shadow != null) shadow.enabled = false;
    }
}
