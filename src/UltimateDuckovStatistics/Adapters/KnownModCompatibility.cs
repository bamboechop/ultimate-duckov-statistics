using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Adapters;

// Optional integrations are qualified against inspected builds, not owner strings
// or assembly version numbers (FPC reuses 1.0.0.0 for different implementations).
// Identity work happens during activation/patch inspection, never for each hit.
internal static class KnownModCompatibility
{
    private static readonly ConditionalWeakTable<Assembly, VerifiedIdentity> Identities = new();

    public static Assembly? FindVerifiedAssembly(string simpleName)
    {
        var matches = AppDomain.CurrentDomain.GetAssemblies()
            .Where(value => string.Equals(value.GetName().Name, simpleName, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 && IsVerifiedAssembly(matches[0], simpleName) ? matches[0] : null;
    }

    public static bool IsVerifiedAssembly(Assembly assembly, string simpleName)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        return string.Equals(assembly.GetName().Name, simpleName, StringComparison.Ordinal)
            && Identities.GetValue(assembly, VerifyIdentity).Verified;
    }

    internal static bool AllowsPatch(MethodBase original, string collection, object patch)
    {
        if (ReflectionContractReader.ReadInstanceMember(patch, "owner") is not string owner
            || ReflectionContractReader.ReadInstanceMember(patch, "PatchMethod") is not MethodInfo callback
            || !MatchesCallback(original, collection, owner, callback)
            || !HasInspectedOrder(patch)) return false;
        var assembly = callback.Module.Assembly;
        var name = assembly.GetName().Name;
        return name != null && IsVerifiedAssembly(assembly, name);
    }

    // Kept separate from binary identity so an installed but unpatched FPC DLL
    // cannot cause crit damage to be reinterpreted as headshot evidence.
    internal static bool HasVerifiedFirstPersonHeadshotPatch(MethodInfo healthHurt)
    {
        try
        {
            var harmonies = AppDomain.CurrentDomain.GetAssemblies()
                .Select(value => value.GetType("HarmonyLib.Harmony", false))
                .Where(value => value != null).ToArray();
            if (harmonies.Length != 1) return false;
            var get = harmonies[0]!.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(MethodBase) }, null);
            var info = get?.Invoke(null, new object[] { healthHurt });
            if (info == null) return false;
            return HasExactCallback(info, healthHurt, "Prefixes", "FirstPersonCamera.HeadshotPatch", "Prefix")
                && HasExactCallback(info, healthHurt, "Postfixes", "FirstPersonCamera.HeadshotPatch", "Postfix")
                && HasExactCallback(info, healthHurt, "Finalizers", "FirstPersonCamera.HeadshotPatch", "Finalizer")
                && HasExactCallback(info, healthHurt, "Postfixes", "FirstPersonCamera.HitEffectsPatch", "Postfix");
        }
        catch { return false; }
    }

    private static bool HasExactCallback(object info, MethodBase original, string collection, string type, string method)
    {
        if (ReflectionContractReader.ReadInstanceMember(info, collection) is not IEnumerable patches) return false;
        var count = 0;
        foreach (var patch in patches)
        {
            if (patch == null || ReflectionContractReader.ReadInstanceMember(patch, "PatchMethod") is not MethodInfo callback
                || callback.DeclaringType?.FullName != type || callback.Name != method) continue;
            if (!AllowsPatch(original, collection, patch)) return false;
            count++;
        }
        return count == 1;
    }

    internal static bool MatchesCallback(MethodBase original, string collection, string owner, MethodInfo callback)
    {
        if (!callback.IsStatic || original.DeclaringType == null
            || original.DeclaringType.Assembly.GetType("Health", false) == null) return false;
        var target = original.DeclaringType.FullName + "." + original.Name;
        var patch = callback.DeclaringType?.FullName + "." + callback.Name;
        var assembly = callback.Module.Assembly.GetName().Name;
        return (target, collection, owner, assembly, patch) switch
        {
            ("Health.Hurt", "Prefixes", "firstpersoncamera.aimpatch", "FirstPersonCamera",
                "FirstPersonCamera.HeadshotPatch.Prefix") => true,
            ("Health.Hurt", "Postfixes", "firstpersoncamera.aimpatch", "FirstPersonCamera",
                "FirstPersonCamera.HeadshotPatch.Postfix" or "FirstPersonCamera.HitEffectsPatch.Postfix") => true,
            ("Health.Hurt", "Finalizers", "firstpersoncamera.aimpatch", "FirstPersonCamera",
                "FirstPersonCamera.HeadshotPatch.Finalizer") => true,
            ("Grenade.Launch", "Postfixes", "firstpersoncamera.aimpatch", "FirstPersonCamera",
                "FirstPersonCamera.Patches.Grenade_Launch_Patch.Postfix") => true,
            _ => false
        };
    }

    internal static bool HasInspectedOrder(object patch) =>
        ReflectionContractReader.ReadInstanceMember(patch, "priority") is int priority && priority == 400
        && ReflectionContractReader.ReadInstanceMember(patch, "before") is string[] before && before.Length == 0
        && ReflectionContractReader.ReadInstanceMember(patch, "after") is string[] after && after.Length == 0;

    private static VerifiedIdentity VerifyIdentity(Assembly assembly)
    {
        try
        {
            var expected = assembly.GetName().Name switch
            {
                "BecomeVeteran" => ("f86b3a0a-3767-44bc-a545-72252bbf26f4", "a79a3391fe66b2ea0000eb2310e24d512ed77276af0850eb292515c48a8e8b6b"),
                "FirstPersonCamera" => ("916ebb5c-dc53-4c53-ba0d-a4f672364748", "832be121a23095bc37dba4735b155a3d1e0c8a511eb18d5e992ba91887965cac"),
                "StorageSearchBar" => ("6d2fdec7-3d42-480f-8fad-c02752e66fc5", "2887f088a6bd99186b855135b0e72a548fbd6f72a17076f69e28dee3b3e205b0"),
                _ => (string.Empty, string.Empty)
            };
            if (expected.Item1.Length == 0 || assembly.IsDynamic
                || assembly.ManifestModule.ModuleVersionId != Guid.Parse(expected.Item1)
                || string.IsNullOrWhiteSpace(assembly.Location)) return new(false);
            using var stream = File.OpenRead(assembly.Location);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            return new(string.Equals(hash, expected.Item2, StringComparison.OrdinalIgnoreCase));
        }
        catch { return new(false); }
    }

    private sealed class VerifiedIdentity
    {
        public VerifiedIdentity(bool verified) => Verified = verified;
        public bool Verified { get; }
    }
}
