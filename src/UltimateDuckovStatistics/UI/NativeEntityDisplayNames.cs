using System.Globalization;
using Duckov.Utilities;
using ItemStatsSystem;
using SodaCraft.Localizations;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

// Reads native metadata on the UI thread. Never creates items or edits captured history.
internal sealed class NativeEntityDisplayNames : IDisposable
{
    private readonly Dictionary<string, string?> names = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Parent, string Key), string?> slots = new();
    private Dictionary<string, string?>? presetKeys;
    private bool disposed;
    internal EntityDisplayNames Names { get; }
    internal event Action? Changed;

    internal NativeEntityDisplayNames()
    {
        Names = new EntityDisplayNames(Resolve, ResolveSlot);
        LocalizationManager.OnSetLanguage += LanguageChanged;
    }

    internal void Invalidate()
    {
        names.Clear(); slots.Clear(); presetKeys = null;
    }

    private void LanguageChanged(SystemLanguage requestedLanguage)
    {
        // Even the same effective language may have a newly loaded data model.
        Invalidate();
        Changed?.Invoke();
    }

    private string? Resolve(string id)
    {
        // GetPlainText would initialize localization and write PlayerPrefs otherwise.
        if (disposed || !LocalizationManager.Initialized || LocalizationManager.DataModel == null) return null;
        if (names.TryGetValue(id, out var cached)) return cached;
        var value = ResolveUncached(id);
        if (names.Count >= 8192) names.Clear();
        names[id] = value;
        return value;
    }

    private string? ResolveUncached(string id)
    {
        if (TryItemId(id, out var typeId))
        {
            var metadata = ItemAssetsCollection.GetMetaData(typeId);
            return metadata.id == typeId ? Localized(metadata.DisplayNameKey) : null;
        }
        const string mapPrefix = "duckov:map:";
        if (id.StartsWith(mapPrefix, StringComparison.Ordinal))
        {
            var nativeId = id.Substring(mapPrefix.Length);
            var scene = SceneInfoCollection.GetSceneInfo(nativeId);
            return scene != null && scene.ID == nativeId && scene.DisplayNameRaw != nativeId
                ? Localized(scene.DisplayNameRaw) : null;
        }
        const string rootPrefix = "duckov:slot:";
        if (id.StartsWith(rootPrefix, StringComparison.Ordinal))
            return SlotName(GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID, id.Substring(rootPrefix.Length));
        var parts = id.Split(':');
        if (parts.Length != 4 || parts[0] != "duckov" || parts[1] is not ("target" or "attacker") || parts[2] != "preset") return null;
        if (presetKeys == null)
        {
            var keys = new Dictionary<string, string?>(StringComparer.Ordinal);
            var presets = GameplayDataSettings.CharacterRandomPresetData?.presets;
            if (presets == null) return null;
            foreach (var preset in presets)
            {
                if (preset == null) continue;
                var key = preset.nameKey;
                var token = CombatObservationPolicy.CreateStableIdentityToken(string.IsNullOrWhiteSpace(key) ? preset.name : key);
                if (!keys.TryGetValue(token, out var existing)) keys[token] = key;
                else if (existing != key) keys[token] = null; // Tokenization is lossy; conflicting identities stay recorded.
            }
            presetKeys = keys;
        }
        return presetKeys.TryGetValue(parts[3], out var nameKey) ? Localized(nameKey) : null;
    }

    private string? ResolveSlot(string parentItemId, string key) =>
        !disposed && LocalizationManager.Initialized && LocalizationManager.DataModel != null
        && TryItemId(parentItemId, out var typeId) ? SlotName(typeId, key) : null;

    private string? SlotName(int parentTypeId, string key)
    {
        if (slots.TryGetValue((parentTypeId, key), out var cached)) return cached;
        var prefab = ItemAssetsCollection.GetPrefab(parentTypeId);
        var matches = prefab != null && prefab.TypeID == parentTypeId
            ? prefab.Slots?.Where(slot => slot != null && slot.Key == key).Take(2).ToArray() : null;
        var value = matches?.Length == 1 ? matches[0].DisplayName : null;
        if (string.IsNullOrWhiteSpace(value) || value == "?"
            || value!.StartsWith('*') && value.EndsWith('*')) value = null;
        if (slots.Count >= 8192) slots.Clear();
        slots[(parentTypeId, key)] = value;
        return value;
    }

    private static string? Localized(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var value = LocalizationManager.GetPlainText(key!);
        return string.IsNullOrWhiteSpace(value) || value == "*" + key!.Trim() + "*" ? null : value;
    }

    private static bool TryItemId(string id, out int typeId)
    {
        var parts = id.Split(':');
        var numeric = parts.Length == 1 ? id : parts.Length == 3 && parts[0] == "duckov"
            && parts[1] is "item" or "weapon" or "ammo" or "totem" ? parts[2] : "";
        return int.TryParse(numeric, NumberStyles.None, CultureInfo.InvariantCulture, out typeId) && typeId > 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        LocalizationManager.OnSetLanguage -= LanguageChanged;
        Changed = null;
        Invalidate();
    }
}
