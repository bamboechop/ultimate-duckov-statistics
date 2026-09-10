// Test-only Unity hierarchy and TMP measurement boundary. This exercises production
// construction/access/scroll/disposal code; it does not simulate GPU rendering or glyph assets.
namespace UnityEngine
{
    public class Object
    {
        public string name = "";
        public HideFlags hideFlags;
        public bool Destroyed;
        public int GetInstanceID() => GetHashCode();
        public static T Instantiate<T>(T source, Transform parent, bool worldPositionStays) where T : Component
        {
            var clone = source.gameObject.CloneTree();
            clone.transform.SetParent(parent, worldPositionStays);
            return clone.GetComponent<T>();
        }
        public static void Destroy(Object? value) { if (value == null) return; value.Destroyed = true; if (value is GameObject go) go.DestroyTree(); }
        public static bool operator ==(Object? a, Object? b) => ReferenceEquals(a, b) || (a is null && b?.Destroyed == true) || (b is null && a?.Destroyed == true);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public enum HideFlags { None, DontSave, HideAndDontSave }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : class => gameObject.GetComponents<T>();
        public T[] GetComponentsInParent<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInParent<T>(includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
        public T GetComponentInParent<T>() where T : class => GetComponent<T>() ?? transform.parent?.GetComponentInParent<T>()!;
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour { }
    public class Camera : Object { }
    // Pointer coordinates are already local in tooltip tests; Unity owns the real canvas/camera conversion.
    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screen, Camera? camera, out Vector2 local)
        { local = screen; return true; }
    }
    public class GameObject : Object
    {
        public static readonly List<GameObject> Live = new();
        private readonly List<Component> components = new();
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public Transform transform { get; }
        public UnityEngine.SceneManagement.Scene scene => new();
        public GameObject(string name, params Type[] types) { this.name = name; transform = new RectTransform { gameObject = this }; components.Add(transform); Live.Add(this); foreach (var type in types) if (type != typeof(RectTransform)) AddComponent(type); }
        public void SetActive(bool value) { activeSelf = value; NotifyTextActivation(); }
        internal void NotifyTextActivation()
        {
            if (!activeInHierarchy) return;
            foreach (var text in GetComponents<TMPro.TextMeshProUGUI>()) text.AwakeForActiveHierarchy();
            foreach (var child in transform.Children) child.gameObject.NotifyTextActivation();
        }
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type) { var value = (Component)Activator.CreateInstance(type)!; value.gameObject = this; components.Add(value); NotifyTextActivation(); return value; }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault()!;
        public T[] GetComponents<T>() where T : class => components.Where(c => !c.Destroyed).OfType<T>().ToArray();
        public T[] GetComponentsInParent<T>(bool includeInactive = false) where T : class =>
            (includeInactive || activeInHierarchy ? GetComponents<T>() : Array.Empty<T>())
            .Concat(transform.parent?.gameObject.GetComponentsInParent<T>(includeInactive) ?? Array.Empty<T>()).ToArray();
        internal GameObject CloneTree()
        {
            var clone = new GameObject(name);
            clone.activeSelf = activeSelf;
            foreach (var component in components.Where(c => c is not Transform && !c.Destroyed))
            {
                var copied = clone.AddComponent(component.GetType());
                // This fixture clones the component types and serialized localization
                // anchor, not Unity event internals or rendering assets.
                if (component is TextLocalizor text && copied is TextLocalizor copy) copy.Key = text.Key;
            }
            foreach (var child in transform.Children) child.gameObject.CloneTree().transform.SetParent(clone.transform);
            return clone;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => components.OfType<T>().Concat(transform.Children.Where(t => includeInactive || t.gameObject.activeInHierarchy).SelectMany(t => t.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        internal void DestroyTree() { activeSelf = false; foreach (var child in transform.Children.ToArray()) Destroy(child.gameObject); foreach (var component in components) component.Destroyed = true; transform.SetParent(null); Live.Remove(this); }
    }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public Transform parent = null!;
        public Vector3 localScale = Vector3.one;
        public int childCount => Children.Count;
        public void SetParent(Transform? value, bool worldPositionStays = false) { parent?.Children.Remove(this); parent = value!; parent?.Children.Add(this); gameObject.NotifyTextActivation(); }
        public Transform GetChild(int index) => Children[index];
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public void SetSiblingIndex(int index) { if (parent == null) return; parent.Children.Remove(this); parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), this); }
        public void SetAsLastSibling() { var owner = parent; SetParent(owner); }
        public void SetAsFirstSibling() { parent?.Children.Remove(this); parent?.Children.Insert(0, this); }
        public bool IsChildOf(Transform value) => parent == value || parent?.IsChildOf(value) == true;
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, offsetMin, offsetMax;
        public Vector2 sizeDelta = new(100, 100);
        public Rect rect => new(0, 0, sizeDelta.x + (parent is RectTransform p ? p.rect.width * (anchorMax.x - anchorMin.x) : 0), sizeDelta.y + (parent is RectTransform q ? q.rect.height * (anchorMax.y - anchorMin.y) : 0));
        public void ForceUpdateRectTransforms() { }
        public enum Axis { Horizontal, Vertical }
        public void SetSizeWithCurrentAnchors(Axis axis, float value) { if (axis == Axis.Horizontal) sizeDelta.x = value; else sizeDelta.y = value; }
    }
    public struct Vector2(float x, float y) { public float x = x, y = y; public static Vector2 zero => new(0, 0); public static Vector2 one => new(1, 1); }
    public struct Vector3(float x, float y, float z) { public float x = x, y = y, z = z; public static Vector3 one => new(1, 1, 1); public static Vector3 zero => new(0, 0, 0); }
    public struct Vector4(float x, float y, float z, float w) { public float x = x, y = y, z = z, w = w; public static Vector4 zero => new(0, 0, 0, 0); }
    public struct Rect(float x, float y, float width, float height) { public float x = x, y = y, width = width, height = height; }
    public struct Bounds { public Vector3 min, max, size, center; }
    public struct Color(float r, float g, float b, float a = 1) { public float r = r, g = g, b = b, a = a; public static Color clear => new(0, 0, 0, 0); public static Color white => new(1, 1, 1); public static Color black => new(0, 0, 0); }
    public struct Color32(byte r, byte g, byte b, byte a) { public byte r = r, g = g, b = b, a = a; public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f); }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public class Canvas : Behaviour { public float scaleFactor = 1; public int sortingOrder; public RenderMode renderMode; public bool isRootCanvas => transform.parent?.GetComponentInParent<Canvas>() == null; public Rect pixelRect => ((RectTransform)transform).rect; public static void ForceUpdateCanvases() { } }
    public class CanvasGroup : Behaviour { public bool interactable = true, blocksRaycasts = true; public float alpha = 1; }
    public class Material : Object { public Material() { } public Material(Material source) { name = source.name; } public bool HasProperty(string key) => true; public void EnableKeyword(string key) { } public void SetColor(string key, Color value) { } }
    public class Sprite : Object { public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) => new(); }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Clamp }
    public enum FilterMode { Bilinear }
    public class Texture : Object { }
    public class Texture2D : Texture
    { public Texture2D(int width, int height, TextureFormat format, bool mipChain) { } public TextureWrapMode wrapMode; public FilterMode filterMode; public void SetPixels32(Color32[] pixels) { } public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { } }
    public static class Resources
    {
        public static readonly List<Object> AdditionalObjects = new();
        public static T[] FindObjectsOfTypeAll<T>() where T : class => GameObject.Live.SelectMany(go => go.GetComponents<T>())
            .Concat(GameObject.Live.OfType<T>()).Concat(AdditionalObjects.OfType<T>())
            .Where(value => value is not Object native || !native.Destroyed).ToArray();
    }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); public static float Min(float a, float b) => Math.Min(a, b); public static float Clamp(float x, float a, float b) => Math.Clamp(x, a, b); public static int RoundToInt(float v) => (int)Math.Round(v); public static bool Approximately(float a, float b) => Math.Abs(a - b) < .0001; }
    public enum KeyCode { None, F8, F9, F10, Escape, Tab, LeftShift, RightShift, LeftControl, RightControl, Return, KeypadEnter, Mouse0, Mouse1 }
    public static class Input { public static readonly HashSet<KeyCode> Down = new(); public static bool GetKeyDown(KeyCode key) => Down.Contains(key); public static bool GetKey(KeyCode key) => Down.Contains(key); public static bool anyKeyDown => Down.Count > 0; }
    public enum CursorLockMode { None, Locked, Confined }
    public static class Cursor { public static bool visible; public static CursorLockMode lockState; }
    public static class Time { public static int frameCount; }
    public static class GUIUtility { public static string systemCopyBuffer = ""; }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public class UnityEvent { private readonly List<UnityAction> listeners = new(); public int ListenerCount => listeners.Count; public void AddListener(UnityAction action) => listeners.Add(action); public void RemoveListener(UnityAction action) => listeners.Remove(action); public void RemoveAllListeners() => listeners.Clear(); public void Invoke() { foreach (var action in listeners.ToArray()) action(); } }
}
namespace UnityEngine.EventSystems
{
    public interface IPointerEnterHandler { void OnPointerEnter(PointerEventData data); }
    public interface IPointerExitHandler { void OnPointerExit(PointerEventData data); }
    public sealed class PointerEventData { public UnityEngine.Vector2 position; public UnityEngine.Camera? enterEventCamera; }
    public enum MoveDirection { Left, Right, Up, Down, None }
    public class EventSystem { public UnityEngine.GameObject? currentSelectedGameObject; public void SetSelectedGameObject(UnityEngine.GameObject? value) { currentSelectedGameObject = value; value?.GetComponent<UltimateDuckovStatistics.UI.RunsFocusHandler>()?.Selected?.Invoke(); } }
}
namespace UnityEngine.UI
{
    using UnityEngine;
    public class Graphic : Behaviour { public Color color; public bool raycastTarget, maskable; public RectTransform rectTransform => (RectTransform)transform; }
    public class Image : Graphic { public Sprite? sprite, overrideSprite; public bool preserveAspect; public Type type; public enum Type { Simple, Sliced } }
    public class RectMask2D : Behaviour { public Vector4 padding; public Vector2 softness; }
    public struct Navigation { public Mode mode; public enum Mode { None, Automatic, Explicit } }
    public struct ColorBlock { public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor; public float colorMultiplier, fadeDuration; public static ColorBlock defaultColorBlock => new(); }
    public class Selectable : Behaviour { public enum Transition { None, ColorTint, SpriteSwap, Animation } public Transition transition; public Navigation navigation; public ColorBlock colors; public Graphic targetGraphic = null!; public bool interactable = true; public bool IsActive() => isActiveAndEnabled; public bool IsInteractable() => interactable; }
    public class Button : Selectable { public sealed class ButtonClickedEvent : UnityEngine.Events.UnityEvent { } public ButtonClickedEvent onClick = new(); }
    public class ScrollRect : Behaviour { public sealed class ScrollEvent { private readonly List<Action<Vector2>> listeners = new(); public void AddListener(Action<Vector2> listener) => listeners.Add(listener); public void RemoveAllListeners() => listeners.Clear(); public void Invoke(Vector2 value) { foreach (var listener in listeners.ToArray()) listener(value); } } public ScrollEvent onValueChanged = new(); public RectTransform content = null!, viewport = null!; public bool horizontal, vertical; public float scrollSensitivity; public MovementType movementType; public enum MovementType { Clamped, Elastic, Unrestricted } public void StopMovement() { } }
    public class GraphicRaycaster : Behaviour { }
    public class CanvasScaler : Behaviour { }
    public class LayoutGroup : MonoBehaviour { }
    public class LayoutElement : MonoBehaviour { }
    public class ContentSizeFitter : MonoBehaviour { }
    public class AspectRatioFitter : MonoBehaviour { }
    public class BaseMeshEffect : MonoBehaviour { }
    public class Mask : MonoBehaviour { public bool showMaskGraphic; }
    public static class LayoutRebuilder { public static void MarkLayoutForRebuild(RectTransform rect) { } }
}
namespace UnityEngine.UI.ProceduralImage
{
    public class ProceduralImage : UnityEngine.UI.Image { public float FalloffDistance, BorderWidth; }
    public class UniformModifier : UnityEngine.MonoBehaviour { public float Radius; }
    public class OnlyOneEdgeModifier : UnityEngine.MonoBehaviour { public float Radius; public ProceduralImageEdge Side; public enum ProceduralImageEdge { Top, Bottom, Left, Right } }
}
namespace TMPro
{
    using UnityEngine;
    public enum FontWeight { Regular, Bold }
    public enum FontStyles { Normal, Bold, Italic }
    public enum TextAlignmentOptions { Left, Center, Right, TopLeft, Top, TopRight, MidlineLeft, Midline, BottomLeft }
    public enum TextOverflowModes { Overflow, Ellipsis, Truncate, Masking, ScrollRect }
    public class TMP_FontAsset : Object { public bool HasCharacter(char c, bool searchFallbacks = false, bool tryAddCharacter = false) => true; public bool HasCharacter(uint c, bool searchFallbacks = false, bool tryAddCharacter = false) => true; }
    public class TextMeshProUGUI : UnityEngine.UI.Graphic
    {
        public static long Measurements;
        public string text = ""; public TMP_FontAsset font = null!; public Material fontSharedMaterial = null!;
        public float fontSize = -99, fontSizeMin, fontSizeMax, characterSpacing, lineSpacing, wordSpacing, paragraphSpacing;
        public FontWeight fontWeight; public FontStyles fontStyle; public TextAlignmentOptions alignment; public TextOverflowModes overflowMode;
        public bool enableWordWrapping, enableAutoSizing, enableKerning, richText; public Vector4 margin;
        public Vector2 GetPreferredValues(float width, float height) => GetPreferredValues(text, width, height);
        public float preferredWidth => GetPreferredValues(text).x;
        public float preferredHeight => GetPreferredValues(text, rectTransform.rect.width, float.PositiveInfinity).y;
        private bool awake;
        internal void AwakeForActiveHierarchy()
        {
            if (awake) return;
            awake = true;
            // Installed TMP Awake/LoadDefaultSettings loads these defaults while
            // font size is still -99; Duckov disables wrapping and enables kerning.
            if (fontSize == -99) { fontSize = 30; enableWordWrapping = false; enableKerning = true; }
        }
        public Vector2 GetPreferredValues(string value, float width = float.PositiveInfinity, float height = float.PositiveInfinity)
        {
            Measurements++;
            var explicitLines = value.Split('\n');
            var widths = explicitLines.Select(line => Math.Max(1, line.Length * fontSize * .55f)).ToArray();
            var wraps = enableWordWrapping && float.IsFinite(width) && width > 0;
            var lines = widths.Sum(natural => wraps ? Math.Max(1, Math.Ceiling(natural / width)) : 1);
            return new Vector2(wraps ? Math.Min(width, widths.Max()) : widths.Max(), (float)lines * fontSize * 1.2f);
        }
        public void ForceMeshUpdate(bool ignoreActiveState = false, bool forceTextReparsing = false) { }
        public Bounds textBounds => new() { size = new(preferredWidth, preferredHeight, 0) };
    }
    public static class ShaderUtilities { public static void UpdateShaderRatios(Material material) { } }
}
namespace Duckov.UI { public sealed class TooltipsProvider : UnityEngine.MonoBehaviour { public string text = ""; } }
namespace Duckov.UI.Animations { public class ButtonAnimation : UnityEngine.MonoBehaviour { } }
public static class GameManager { public static UnityEngine.EventSystems.EventSystem? EventSystem = new(); }
public class InputManager : UnityEngine.Object { public static readonly HashSet<UnityEngine.GameObject> Blocks = new(); public static void DisableInput(UnityEngine.GameObject owner) => Blocks.Add(owner); public static void ActiveInput(UnityEngine.GameObject owner) => Blocks.Remove(owner); }
public sealed class LevelManager { public static LevelManager? Instance; public bool IsBaseLevel = true; public InputManager InputManager = new(); }
public sealed class UIInputEventData { public void Use() { } }
public static class UIInputManager { public static event Action<UIInputEventData>? OnCancelEarly; public static int CancelListeners => OnCancelEarly?.GetInvocationList().Length ?? 0; }

namespace UnityEngine { public static class Application { public static string version = "2.3.30"; public static Action<string>? UrlLauncher; public static void OpenURL(string url) => (UrlLauncher ?? throw new InvalidOperationException("Unexpected external launch"))(url); } public static class Debug { public static void LogException(Exception exception) => throw exception; } public struct Vector2Int { public static Vector2 zero => new(0, 0); } }

namespace UnityEngine.SceneManagement
{
    public struct Scene { public bool IsValid() => true; public UnityEngine.GameObject[] GetRootGameObjects() => UnityEngine.GameObject.Live.Where(go => go.transform.parent == null).ToArray(); }
}
public sealed class MainMenu : UnityEngine.MonoBehaviour
{
    public static event Action? OnMainMenuAwake, OnMainMenuDestroy;
    public static int Listeners => (OnMainMenuAwake?.GetInvocationList().Length ?? 0) + (OnMainMenuDestroy?.GetInvocationList().Length ?? 0);
}
public sealed class PauseMenu : UnityEngine.MonoBehaviour
{
    public static PauseMenu? Instance;
    public static event Action? onPauseMenuOn, onPauseMenuOff;
    public static int Listeners => (onPauseMenuOn?.GetInvocationList().Length ?? 0) + (onPauseMenuOff?.GetInvocationList().Length ?? 0);
    public static void Show() { Instance!.gameObject.SetActive(true); onPauseMenuOn?.Invoke(); }
    public static void Hide() { Instance!.gameObject.SetActive(false); onPauseMenuOff?.Invoke(); }
}
public sealed class TextLocalizor : UnityEngine.MonoBehaviour { public string Key = ""; }
namespace SodaCraft.Localizations
{
    public static class LocalizationManager
    {
        public static bool Initialized = true;
        public static object? DataModel = new();
        public static UnityEngine.SystemLanguage CurrentLanguage { get; private set; }
        public static event Action<UnityEngine.SystemLanguage>? OnSetLanguage;
        public static int Listeners => OnSetLanguage?.GetInvocationList().Length ?? 0;
        public static readonly Dictionary<(UnityEngine.SystemLanguage, string), string> Translations = new();
        public static int Reads;
        public static void SetLanguage(UnityEngine.SystemLanguage language)
        { CurrentLanguage = language; OnSetLanguage?.Invoke(language); }
        private static readonly Dictionary<string, string> Overrides = new();
        public static void SetOverrideText(string key, string text) => Overrides[key] = text;
        public static void RemoveOverrideText(string key) => Overrides.Remove(key);
        public static string GetPlainText(string key)
        { Reads++; return Overrides.GetValueOrDefault(key, Translations.GetValueOrDefault((CurrentLanguage, key), "*" + key.Trim() + "*")); }
    }
}
namespace Duckov.UI { public static class NotificationText { public static readonly List<string> Messages = new(); public static void Push(string message) => Messages.Add(message); } }
namespace Duckov.Utilities
{
    public static class GameplayDataSettings
    {
        public static UiStyle UIStyle { get; } = new();
        public static TagsData Tags { get; } = new();
        public static ItemAssets ItemAssets { get; } = new();
        public static CharacterRandomPresetData? CharacterRandomPresetData { get; set; } = new();
    }
    public sealed class ItemAssets { public int DefaultCharacterItemTypeID = 1; }
    public sealed class TagsData { public ItemStatsSystem.ItemTag Bullet { get; } = new() { name = "NativeBulletTag" }; }
    public sealed class CharacterRandomPresetData { public List<CharacterRandomPreset> presets = new(); }
    public sealed class UiStyle
    {
        public UnityEngine.Sprite? FallbackItemIcon;
        public void ApplyDisplayQualityShadow(int quality, LeTai.TrueShadow.TrueShadow shadow) => shadow.AppliedQuality = quality;
    }
}
public sealed class CharacterRandomPreset { public string nameKey = "", name = ""; }
public sealed class SceneInfoEntry { public string ID = "", DisplayNameRaw = ""; }
public static class SceneInfoCollection
{
    public static readonly Dictionary<string, SceneInfoEntry> Scenes = new();
    public static SceneInfoEntry? GetSceneInfo(string id) => Scenes.GetValueOrDefault(id);
}
namespace UnityEngine { public enum SystemLanguage { English, German } }
namespace ItemStatsSystem
{
    public sealed class ItemMetaData
    {
        public int id, displayQuality;
        public string DisplayNameKey = "";
        public UnityEngine.Sprite? icon;
        public List<ItemTag>? tags;
    }
    public sealed class ItemTag { public string name = ""; }
    public sealed class Item { public int TypeID; public List<Items.Slot>? Slots; }
    public static class ItemAssetsCollection
    {
        public static readonly Dictionary<int, ItemMetaData> Metadata = new();
        public static readonly Dictionary<int, Item> Prefabs = new();
        public static ItemMetaData GetMetaData(int typeId) => Metadata.GetValueOrDefault(typeId, new());
        public static Item? GetPrefab(int typeId) => Prefabs.GetValueOrDefault(typeId);
    }
}
namespace ItemStatsSystem.Items { public sealed class Slot { public string Key = "", DisplayName = ""; } }
