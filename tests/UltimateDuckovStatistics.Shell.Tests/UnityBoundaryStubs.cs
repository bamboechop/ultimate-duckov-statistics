// Test-only Unity hierarchy and TMP measurement boundary. This exercises production
// construction/access/scroll/disposal code; it does not simulate GPU rendering or glyph assets.
namespace UnityEngine
{
    public class Object
    {
        public string name = "";
        public HideFlags hideFlags;
        public bool Destroyed;
        public static void Destroy(Object? value) { if (value == null) return; value.Destroyed = true; if (value is GameObject go) go.DestroyTree(); }
        public static bool operator ==(Object? a, Object? b) => ReferenceEquals(a, b) || (a is null && b?.Destroyed == true) || (b is null && a?.Destroyed == true);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public enum HideFlags { None, DontSave }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
        public T GetComponentInParent<T>() where T : class => GetComponent<T>() ?? transform.parent?.GetComponentInParent<T>()!;
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        public static readonly List<GameObject> Live = new();
        private readonly List<Component> components = new();
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public Transform transform { get; }
        public GameObject(string name, params Type[] types) { this.name = name; transform = new RectTransform { gameObject = this }; components.Add(transform); Live.Add(this); foreach (var type in types) if (type != typeof(RectTransform)) AddComponent(type); }
        public void SetActive(bool value) => activeSelf = value;
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type) { var value = (Component)Activator.CreateInstance(type)!; value.gameObject = this; components.Add(value); return value; }
        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault()!;
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => components.OfType<T>().Concat(transform.Children.Where(t => includeInactive || t.gameObject.activeInHierarchy).SelectMany(t => t.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        internal void DestroyTree() { activeSelf = false; foreach (var child in transform.Children.ToArray()) Destroy(child.gameObject); foreach (var component in components) component.Destroyed = true; transform.SetParent(null); Live.Remove(this); }
    }
    public class Transform : Component
    {
        public readonly List<Transform> Children = new();
        public Transform? parent;
        public Vector3 localScale = Vector3.one;
        public int childCount => Children.Count;
        public void SetParent(Transform? value, bool worldPositionStays = false) { parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); }
        public Transform GetChild(int index) => Children[index];
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
    public class Canvas : Behaviour { public float scaleFactor = 1; public Rect pixelRect => ((RectTransform)transform).rect; public static void ForceUpdateCanvases() { } }
    public class CanvasGroup : Behaviour { public bool interactable = true, blocksRaycasts = true; public float alpha = 1; }
    public class Material : Object { public Material() { } public Material(Material source) { name = source.name; } public bool HasProperty(string key) => true; public void EnableKeyword(string key) { } public void SetColor(string key, Color value) { } }
    public class Sprite : Object { }
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
    public class ScrollRect : Behaviour { public RectTransform content = null!, viewport = null!; public bool horizontal, vertical; public float scrollSensitivity; public MovementType movementType; public enum MovementType { Clamped, Elastic, Unrestricted } public void StopMovement() { } }
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
        public float fontSize = 30, fontSizeMin, fontSizeMax, characterSpacing, lineSpacing, wordSpacing, paragraphSpacing;
        public FontWeight fontWeight; public FontStyles fontStyle; public TextAlignmentOptions alignment; public TextOverflowModes overflowMode;
        public bool enableWordWrapping, enableAutoSizing, richText; public Vector4 margin;
        public Vector2 GetPreferredValues(float width, float height) => GetPreferredValues(text, width, height);
        public float preferredWidth => GetPreferredValues(text).x;
        public float preferredHeight => GetPreferredValues(text, rectTransform.rect.width, float.PositiveInfinity).y;
        public Vector2 GetPreferredValues(string value, float width = float.PositiveInfinity, float height = float.PositiveInfinity) { Measurements++; var natural = Math.Max(1, value.Length * fontSize * .55f); var lines = float.IsFinite(width) && width > 0 ? Math.Max(1, Math.Ceiling(natural / width)) : 1; return new Vector2(float.IsFinite(width) ? Math.Min(width, natural) : natural, (float)lines * fontSize * 1.2f); }
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

namespace UnityEngine { public static class Application { public static string version = "2.3.30"; } public static class Debug { public static void LogException(Exception exception) => throw exception; } public struct Vector2Int { public static Vector2 zero => new(0, 0); } }
