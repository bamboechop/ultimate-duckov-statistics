using UnityEngine;

namespace UnityEngine
{
    public sealed class Mesh : Object { }
}

namespace LeTai.TrueShadow
{
    // Installed LeTai.TrueShadow.dll image-caster lifecycle: allocate once on
    // enable, retain while disabled, release renderer/texture but not SpriteMesh
    // on destroy. Tests explicitly dispatch these native callbacks; this does
    // not model Unity rendering or its deferred destruction queue.
    public class TrueShadow : MonoBehaviour
    {
        internal Mesh? SpriteMesh { get; set; }
        public float Size, Spread;
        public bool UseCasterAlpha, IgnoreCasterColor, IgnoreExternalActive, ShadowAsSibling;
        public int AppliedQuality, NativeCleanupCount;
        public bool ThrowDuringNativeCleanup;
        public void NativeEnable() => OnEnable();
        public void NativeDisable() => OnDisable();
        public void NativeDestroy() => OnDestroy();
        protected virtual void OnEnable()
        {
            if (SpriteMesh == null) SpriteMesh = new Mesh();
        }
        protected virtual void OnDisable() { }
        protected virtual void OnDestroy()
        {
            NativeCleanupCount++;
            if (ThrowDuringNativeCleanup) throw new InvalidOperationException("Native renderer teardown failed");
        }
    }
}
