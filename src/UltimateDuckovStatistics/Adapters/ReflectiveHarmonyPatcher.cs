using System.Collections;
using System.Reflection;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class ReflectiveHarmonyPatcher : IDisposable
{
    internal const string HarmonyId = "at.bamboechop.ultimate-duckov-statistics.healing";
    private static readonly Version MinimumHarmonyVersion = new(2, 4, 1, 0);
    private readonly object harmony;
    private readonly ConstructorInfo harmonyMethodConstructor;
    private readonly FieldInfo harmonyPriorityField;
    private readonly MethodInfo patchMethod;
    private readonly MethodInfo unpatchAllMethod;
    private readonly MethodInfo getPatchInfoMethod;
    private bool disposed;

    private ReflectiveHarmonyPatcher(
        object harmony,
        ConstructorInfo harmonyMethodConstructor,
        FieldInfo harmonyPriorityField,
        MethodInfo patchMethod,
        MethodInfo unpatchAllMethod,
        MethodInfo getPatchInfoMethod,
        Version version)
    {
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

    public static bool TryCreate(out ReflectiveHarmonyPatcher? patcher, out string detail)
    {
        patcher = null;
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

            if (patchesType.GetProperty("Transpilers", BindingFlags.Instance | BindingFlags.Public) == null
                && patchesType.GetField("Transpilers", BindingFlags.Instance | BindingFlags.Public) == null)
            {
                throw new MissingMemberException("HarmonyLib.Patches.Transpilers member was not found.");
            }

            if (patchType.GetProperty("owner", BindingFlags.Instance | BindingFlags.Public) == null
                && patchType.GetField("owner", BindingFlags.Instance | BindingFlags.Public) == null)
            {
                throw new MissingMemberException("HarmonyLib.Patch.owner member was not found.");
            }

            var harmony = harmonyConstructor.Invoke(new object[] { HarmonyId })
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

    public bool HasForeignTranspiler(MethodBase original, out string owners)
    {
        owners = string.Empty;
        var patchInfo = getPatchInfoMethod.Invoke(null, new object[] { original });
        if (patchInfo == null)
        {
            return false;
        }

        var transpilers = ReflectionContractReader.ReadInstanceMember(patchInfo, "Transpilers") as IEnumerable;
        if (transpilers == null)
        {
            return false;
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var patch in transpilers)
        {
            if (patch == null)
            {
                continue;
            }

            var owner = ReflectionContractReader.ReadInstanceMember(patch, "owner") as string
                        ?? "unknown";
            if (!string.Equals(owner, HarmonyId, StringComparison.Ordinal))
            {
                found.Add(owner);
            }
        }

        owners = string.Join(", ", found.OrderBy(value => value, StringComparer.Ordinal));
        return found.Count > 0;
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
        if (disposed)
        {
            return;
        }

        disposed = true;
        unpatchAllMethod.Invoke(harmony, new object?[] { HarmonyId });
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

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException
            : exception;
}
