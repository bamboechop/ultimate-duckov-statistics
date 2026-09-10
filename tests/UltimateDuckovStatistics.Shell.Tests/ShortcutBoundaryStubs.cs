using System.Reflection;

namespace UnityEngine.InputSystem
{
    public class InputAction
    {
        public struct CallbackContext { public bool performed; }
    }
}

// Native callback bodies and Harmony detouring are external boundaries. Dispatch
// invokes the actual prefixes registered by the production guard, then the body.
public class CharacterInputControl
{
    public int Calls { get; private set; }
    public void OnUIInventoryInput(UnityEngine.InputSystem.InputAction.CallbackContext context) { if (context.performed) Calls++; }
    public void OnUIMapInput(UnityEngine.InputSystem.InputAction.CallbackContext context) { if (context.performed) Calls++; }
    public void OnUIQuestViewInput(UnityEngine.InputSystem.InputAction.CallbackContext context) { if (context.performed) Calls++; }
    public void OnReloadInput(UnityEngine.InputSystem.InputAction.CallbackContext context) { if (context.performed) Calls++; }

    public void Dispatch(string methodName, bool performed = true)
    {
        var method = typeof(CharacterInputControl).GetMethod(methodName)!;
        var prefixes = HarmonyLib.Harmony.GetPatchInfo(method)?.Prefixes.ToArray() ?? Array.Empty<HarmonyLib.Patch>();
        var runOriginal = true;
        foreach (var prefix in prefixes)
            if (prefix.PatchMethod.Invoke(null, null) is false) runOriginal = false;
        if (runOriginal) method.Invoke(this, new object[] { new UnityEngine.InputSystem.InputAction.CallbackContext { performed = performed } });
    }
}
