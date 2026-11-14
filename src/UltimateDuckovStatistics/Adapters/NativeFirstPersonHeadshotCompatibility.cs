using System.Reflection;

namespace UltimateDuckovStatistics.Adapters;

// Read FPC's explicit target-specific result from its active prefix. Neither an
// assembly fingerprint nor generic critical damage is headshot evidence.
internal sealed class NativeFirstPersonHeadshotCompatibility : IDisposable
{
    private readonly Func<bool>? firstPersonMode;
    private FirstPersonHeadshotResultObserver? observer;
    private readonly Func<Health, DamageInfo, bool>? resultReader;

    internal NativeFirstPersonHeadshotCompatibility(Func<bool>? firstPersonMode = null, string? failure = null,
        FirstPersonHeadshotResultObserver? resultObserver = null, Func<Health, DamageInfo, bool>? resultReader = null)
    {
        this.firstPersonMode = firstPersonMode;
        Failure = failure;
        observer = resultObserver;
        this.resultReader = resultReader;
    }

    public bool IsSupported => Failure == null;
    internal bool ObservesFirstPersonResults => observer != null;
    public string? Failure { get; private set; }

    public static NativeFirstPersonHeadshotCompatibility Resolve(MethodInfo? healthHurt)
    {
        const string assemblyName = "FirstPersonCamera";
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
            return new();

        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(value => value.GetName().Name == assemblyName);
            if (assembly == null || healthHurt == null
                || !KnownModCompatibility.HasActiveFirstPersonHeadshotPrefix(healthHurt, assembly, out var prefix))
                return new(failure: "The First Person Camera headshot prefix is not active or has changed signature.");

            var modeMethod = assembly.GetType("FirstPersonCamera.HeadshotPatch", throwOnError: true)!
                .GetMethod("IsFirstPersonModeActive", BindingFlags.Static | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
            if (modeMethod == null || modeMethod.ReturnType != typeof(bool))
                return new(failure: "First Person Camera's verified headshot mode observer is unavailable.");

            // Cache mode and state field metadata once; do not repeat head physics
            // or hash assemblies during damage callbacks.
            return new((Func<bool>)modeMethod.CreateDelegate(typeof(Func<bool>)))
            { observer = new FirstPersonHeadshotResultObserver(prefix!) };
        }
        catch (Exception exception)
        {
            return new(failure: "First Person Camera headshot contract resolution failed: " + exception.GetType().Name + ".");
        }
    }

    public bool Capture(Health health, DamageInfo damageInfo, bool nativeHeadTargeted)
    {
        if (!IsSupported || damageInfo.isExplosion) return false;
        if (firstPersonMode == null) return nativeHeadTargeted;
        try
        {
            if (!firstPersonMode()) return nativeHeadTargeted;
            var attacker = damageInfo.fromCharacter;
            var target = health.TryGetCharacter();
            if (damageInfo.damageType != DamageTypes.normal || attacker == null
                || !attacker.IsMainCharacter || target == null || target == attacker)
                return false;

            if (observer != null)
            {
                if (observer.TryConsume(health, damageInfo, attacker, target, out var result)) return result;
                Failure = "First Person Camera did not supply a current target-specific headshot result.";
                return false;
            }

            if (resultReader != null) return resultReader(health, damageInfo);
            Failure = "First Person Camera headshot result observer is unavailable.";
            return false;
        }
        catch (Exception exception)
        {
            Failure = "First Person Camera headshot mode observation failed: " + exception.GetType().Name + ".";
            return false;
        }
    }

    public void Dispose() => observer?.Dispose();
}
