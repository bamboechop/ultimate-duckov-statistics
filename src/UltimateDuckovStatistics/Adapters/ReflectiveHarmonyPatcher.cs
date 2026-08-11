using System.Reflection;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class ReflectiveHarmonyPatcher : IDisposable
{
    internal const string HarmonyId = "at.bamboechop.ultimate-duckov-statistics.healing";
    private static readonly Version MinimumHarmonyVersion = new(2, 4, 1, 0);
    private static readonly object PendingCleanupLock = new();
    private static readonly Dictionary<string, ReflectiveHarmonyPatcher> PendingCleanup = new(StringComparer.Ordinal);
    private readonly string harmonyId;
    private readonly object harmony;
    private readonly ConstructorInfo harmonyMethodConstructor;
    private readonly FieldInfo harmonyPriorityField;
    private readonly MethodInfo patchMethod;
    private readonly MethodInfo unpatchAllMethod;
    private readonly MethodInfo getPatchInfoMethod;
    private bool disposed;

    private ReflectiveHarmonyPatcher(
        string harmonyId,
        object harmony,
        ConstructorInfo harmonyMethodConstructor,
        FieldInfo harmonyPriorityField,
        MethodInfo patchMethod,
        MethodInfo unpatchAllMethod,
        MethodInfo getPatchInfoMethod,
        Version version)
    {
        this.harmonyId = harmonyId;
        this.harmony = harmony;
        this.harmonyMethodConstructor = harmonyMethodConstructor;
        this.harmonyPriorityField = harmonyPriorityField;
        this.patchMethod = patchMethod;
        this.unpatchAllMethod = unpatchAllMethod;
        this.getPatchInfoMethod = getPatchInfoMethod;
        Version = version;
    }

    public Version Version { get; }

    public static bool IsHarmonyLoaded => FindHarmonyAssembly() != null;

    internal static bool HasPendingCleanup
    {
        get
        {
            lock (PendingCleanupLock)
            {
                return PendingCleanup.Count > 0;
            }
        }
    }

    public static bool TryCreate(out ReflectiveHarmonyPatcher? patcher, out string detail)
        => TryCreate(HarmonyId, out patcher, out detail);

    public static bool TryCreate(string harmonyId, out ReflectiveHarmonyPatcher? patcher, out string detail)
    {
        patcher = null;
        if (string.IsNullOrWhiteSpace(harmonyId))
        {
            detail = "Harmony owner ID is required.";
            return false;
        }
        if (!TryCompletePendingCleanup(harmonyId, out var cleanupDetail))
        {
            detail = $"Harmony cleanup from a previous UDS activation is still pending: {cleanupDetail}";
            return false;
        }

        var assembly = FindHarmonyAssembly();
        if (assembly == null)
        {
            detail = "HarmonyLib is not loaded. Install and activate Workshop item 3589088839 before UDS.";
            return false;
        }

        var version = assembly.GetName().Version ?? new Version(0, 0);
        if (version < MinimumHarmonyVersion)
        {
            detail = $"HarmonyLib {version} is older than required {MinimumHarmonyVersion}.";
            return false;
        }

        try
        {
            var harmonyType = assembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
            var harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
            var patchesType = assembly.GetType("HarmonyLib.Patches", throwOnError: true)!;
            var patchType = assembly.GetType("HarmonyLib.Patch", throwOnError: true)!;
            var harmonyConstructor = harmonyType.GetConstructor(new[] { typeof(string) })
                ?? throw new MissingMethodException("Harmony(string) constructor was not found.");
            var harmonyMethodConstructor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) })
                ?? throw new MissingMethodException("HarmonyMethod(MethodInfo) constructor was not found.");
            var harmonyPriorityField = harmonyMethodType.GetField("priority", BindingFlags.Instance | BindingFlags.Public);
            if (harmonyPriorityField?.FieldType != typeof(int))
            {
                throw new MissingFieldException("HarmonyMethod.priority field was not found.");
            }

            foreach (var collectionName in new[] { "Prefixes", "Postfixes", "Transpilers", "Finalizers" })
            {
                if (patchesType.GetProperty(collectionName, BindingFlags.Instance | BindingFlags.Public) == null
                    && patchesType.GetField(collectionName, BindingFlags.Instance | BindingFlags.Public) == null)
                {
                    throw new MissingMemberException($"HarmonyLib.Patches.{collectionName} member was not found.");
                }
            }

            if (patchType.GetProperty("owner", BindingFlags.Instance | BindingFlags.Public) == null
                && patchType.GetField("owner", BindingFlags.Instance | BindingFlags.Public) == null)
            {
                throw new MissingMemberException("HarmonyLib.Patch.owner member was not found.");
            }

            if (patchType.GetProperty("PatchMethod", BindingFlags.Instance | BindingFlags.Public)?.PropertyType
                != typeof(MethodInfo))
            {
                throw new MissingMemberException("HarmonyLib.Patch.PatchMethod property was not found.");
            }

            var harmony = harmonyConstructor.Invoke(new object[] { harmonyId })
                ?? throw new InvalidOperationException("Harmony constructor returned null.");
            var patchMethod = harmonyType.GetMethod(
                "Patch",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                new[]
                {
                    typeof(MethodBase),
                    harmonyMethodType,
                    harmonyMethodType,
                    harmonyMethodType,
                    harmonyMethodType
                },
                modifiers: null)
                ?? throw new MissingMethodException("Harmony.Patch API was not found.");
            var unpatchAllMethod = harmonyType.GetMethod(
                "UnpatchAll",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                new[] { typeof(string) },
                modifiers: null)
                ?? throw new MissingMethodException("Harmony.UnpatchAll API was not found.");
            var getPatchInfoMethod = harmonyType.GetMethod(
                "GetPatchInfo",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                new[] { typeof(MethodBase) },
                modifiers: null)
                ?? throw new MissingMethodException("Harmony.GetPatchInfo API was not found.");
            patcher = new ReflectiveHarmonyPatcher(
                harmonyId,
                harmony,
                harmonyMethodConstructor,
                harmonyPriorityField,
                patchMethod,
                unpatchAllMethod,
                getPatchInfoMethod,
                version);
            detail = $"HarmonyLib {version} loaded.";
            return true;
        }
        catch (Exception exception)
        {
            detail = $"Harmony reflection contract failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}";
            return false;
        }
    }

    public bool IsPatchSetTrusted(
        MethodBase original,
        IReadOnlyList<HarmonyPatchExpectation> expectedOwnedPatches,
        out string detail)
    {
        if (disposed)
        {
            detail = "The UDS Harmony patcher is disposed.";
            return false;
        }

        try
        {
            var patchInfo = getPatchInfoMethod.Invoke(null, new object[] { original });
            return HarmonyPatchSetInspector.TryValidate(
                patchInfo,
                harmonyId,
                expectedOwnedPatches,
                out detail);
        }
        catch (Exception exception)
        {
            var unwrapped = Unwrap(exception);
            detail = $"Harmony patch inspection failed: {unwrapped.GetType().Name}: {unwrapped.Message}";
            return false;
        }
    }

    public void Patch(
        MethodBase original,
        MethodInfo? prefix = null,
        MethodInfo? postfix = null,
        MethodInfo? finalizer = null)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(ReflectiveHarmonyPatcher));
        }

        patchMethod.Invoke(
            harmony,
            new[]
            {
                original,
                CreateHarmonyMethod(prefix, priority: 0),
                CreateHarmonyMethod(postfix, priority: 800),
                null,
                CreateHarmonyMethod(finalizer, priority: 800)
            });
    }

    public void Dispose()
    {
        if (!TryDispose(out var detail))
        {
            throw new InvalidOperationException(detail);
        }
    }

    internal bool TryDispose(out string detail)
    {
        if (disposed)
        {
            ClearPendingCleanup(this);
            detail = "Harmony patches are already removed.";
            return true;
        }

        try
        {
            unpatchAllMethod.Invoke(harmony, new object?[] { harmonyId });
            disposed = true;
            ClearPendingCleanup(this);
            detail = "Harmony patches removed.";
            return true;
        }
        catch (Exception exception)
        {
            RegisterPendingCleanup(this);
            var unwrapped = Unwrap(exception);
            detail = $"Harmony patch cleanup failed: {unwrapped.GetType().Name}: {unwrapped.Message}";
            return false;
        }
    }

    private object? CreateHarmonyMethod(MethodInfo? method, int priority)
    {
        if (method == null)
        {
            return null;
        }

        var result = harmonyMethodConstructor.Invoke(new object[] { method })
            ?? throw new InvalidOperationException("HarmonyMethod constructor returned null.");
        harmonyPriorityField.SetValue(result, priority);
        return result;
    }

    private static Assembly? FindHarmonyAssembly() => AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(candidate => candidate.GetType("HarmonyLib.Harmony", throwOnError: false) != null);

    private static bool TryCompletePendingCleanup(string harmonyId, out string detail)
    {
        ReflectiveHarmonyPatcher? pending;
        lock (PendingCleanupLock)
        {
            PendingCleanup.TryGetValue(harmonyId, out pending);
        }

        if (pending == null)
        {
            detail = "No pending Harmony cleanup.";
            return true;
        }

        return pending.TryDispose(out detail);
    }

    private static void RegisterPendingCleanup(ReflectiveHarmonyPatcher patcher)
    {
        lock (PendingCleanupLock)
        {
            PendingCleanup[patcher.harmonyId] = patcher;
        }
    }

    private static void ClearPendingCleanup(ReflectiveHarmonyPatcher patcher)
    {
        lock (PendingCleanupLock)
        {
            if (PendingCleanup.TryGetValue(patcher.harmonyId, out var pending)
                && ReferenceEquals(pending, patcher))
            {
                PendingCleanup.Remove(patcher.harmonyId);
            }
        }
    }

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException!
            : exception;
}
