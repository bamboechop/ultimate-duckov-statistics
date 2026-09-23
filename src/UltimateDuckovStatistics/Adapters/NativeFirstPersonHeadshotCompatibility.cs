using System.Reflection;

namespace UltimateDuckovStatistics.Adapters;

// The inspected FPC build replaces launch-time head aim with a target-specific
// result in its Health.Hurt prefix. This adapter reads that result only after
// the verified prefix, and only while the same mode/eligibility contract applies.
internal sealed class NativeFirstPersonHeadshotCompatibility
{
    private readonly Func<bool>? firstPersonMode;

    internal NativeFirstPersonHeadshotCompatibility(Func<bool>? firstPersonMode = null, string? failure = null)
    {
        this.firstPersonMode = firstPersonMode;
        Failure = failure;
    }

    public bool IsSupported => Failure == null;
    public string? Failure { get; private set; }

    public static NativeFirstPersonHeadshotCompatibility Resolve(MethodInfo? healthHurt)
    {
        const string assemblyName = "FirstPersonCamera";
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
            return new();

        try
        {
            var assembly = KnownModCompatibility.FindVerifiedAssembly(assemblyName);
            if (assembly == null || healthHurt == null
                || !KnownModCompatibility.HasVerifiedFirstPersonHeadshotPatch(healthHurt))
                return new(failure: "The installed First Person Camera headshot patch is not the verified active contract.");

            var modeMethod = assembly.GetType("FirstPersonCamera.HeadshotPatch", throwOnError: true)!
                .GetMethod("IsFirstPersonModeActive", BindingFlags.Static | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
            if (modeMethod == null || modeMethod.ReturnType != typeof(bool))
                return new(failure: "First Person Camera's verified headshot mode observer is unavailable.");

            // Cache once. Damage callbacks invoke a strongly typed delegate, not
            // reflection, patch enumeration, hashing, or duplicate head physics.
            return new((Func<bool>)modeMethod.CreateDelegate(typeof(Func<bool>)));
        }
        catch (Exception exception)
        {
            return new(failure: "First Person Camera headshot contract resolution failed: " + exception.GetType().Name + ".");
        }
    }

    public bool Capture(Health health, DamageInfo damageInfo, bool nativeHeadTargeted)
    {
        if (!IsSupported) return false;
        if (firstPersonMode == null) return nativeHeadTargeted;
        try
        {
            if (!firstPersonMode()) return nativeHeadTargeted;
            var attacker = damageInfo.fromCharacter;
            var target = health.TryGetCharacter();
            if (damageInfo.damageType != DamageTypes.normal || attacker == null
                || !attacker.IsMainCharacter || target == null || target == attacker)
                return false;

            // For this exact active prefix and eligibility, crit is assigned
            // directly from actualHeadHit (0/1). It is not generic crit evidence.
            return damageInfo.crit == 1;
        }
        catch (Exception exception)
        {
            Failure = "First Person Camera headshot mode observation failed: " + exception.GetType().Name + ".";
            return false;
        }
    }
}
