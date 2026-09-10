using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.UI;

/// <summary>Owns suppression of native shortcuts that bypass InputManager.DisableInput.</summary>
internal sealed class NativePanelShortcutGuard : IDisposable
{
    private static readonly string[] ShortcutMethods = { "OnUIInventoryInput", "OnUIMapInput", "OnUIQuestViewInput", "OnReloadInput" };
    private static readonly HashSet<NativePanelShortcutGuard> OpenOwners = new();
    private static readonly MethodInfo Prefix = typeof(NativePanelShortcutGuard)
        .GetMethod(nameof(AllowNativeShortcut), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly HarmonyPatchExpectation[] Expected = { new("Prefixes", Prefix) };
    private readonly Action<string> report;
    private readonly List<HarmonyPatchSetStamp> stamps = new();
    private ReflectiveHarmonyPatcher? patcher;
    private bool attempted, disposed;
    private string lastWarning = "";

    public NativePanelShortcutGuard(Action<string> report) => this.report = report;
    public NativeMenuIntegrationState State { get; private set; }

    // Input callbacks and panel lifecycle operations run on the Unity main thread.
    // This deliberately leaves canceled gameplay actions and shared action maps alone.
    private static bool AllowNativeShortcut() => OpenOwners.Count == 0;

    public void SetOpen(bool open)
    {
        if (!open || disposed) { OpenOwners.Remove(this); return; }
        OpenOwners.Add(this);
        if (!attempted) Install();
        Refresh();
    }

    private void Install()
    {
        attempted = true;
        try
        {
            var methods = ShortcutMethods
                .Select(name => typeof(CharacterInputControl).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Single(method => method.Name == name && method.ReturnType == typeof(void)
                        && !method.IsGenericMethod && method.GetParameters().Length == 1
                        && method.GetParameters()[0].ParameterType.FullName == "UnityEngine.InputSystem.InputAction+CallbackContext"))
                .ToArray();
            if (!ReflectiveHarmonyPatcher.TryCreate("at.bamboechop.ultimate-duckov-statistics.panel-shortcuts",
                out patcher, out var detail)) throw new InvalidOperationException(detail);
            foreach (var method in methods)
            {
                if (!patcher!.IsPatchSetTrusted(method, Array.Empty<HarmonyPatchExpectation>(), out detail))
                    throw new InvalidOperationException(detail);
                patcher.Patch(method, Prefix);
                if (!patcher.TryCaptureValidatedPatchSetStamp(method, Expected, out var stamp, out detail) || stamp == null)
                    throw new InvalidOperationException(detail);
                stamps.Add(stamp);
            }
            State = NativeMenuIntegrationState.Available;
        }
        catch (Exception exception)
        {
            Unavailable(exception.GetType().Name + ": " + exception.Message);
            Cleanup();
        }
    }

    public void Refresh()
    {
        if (State != NativeMenuIntegrationState.Available || patcher == null) return;
        foreach (var stamp in stamps)
        {
            if (patcher.IsPatchSetStampCurrent(stamp, out var detail)) continue;
            Unavailable(detail);
            return;
        }
    }

    private void Unavailable(string detail)
    {
        State = NativeMenuIntegrationState.Unavailable;
        if (lastWarning == detail) return;
        lastWarning = detail;
        report("UDS native shortcut isolation unavailable. Inventory, map, quest or reload shortcuts may reach the game while the panel is open. " + detail);
    }

    private void Cleanup()
    {
        if (patcher == null) return;
        if (!patcher.TryDispose(out var detail)) { Unavailable(detail); return; }
        patcher = null;
        stamps.Clear();
    }

    public void Dispose()
    {
        OpenOwners.Remove(this);
        disposed = true;
        Cleanup();
    }
}
