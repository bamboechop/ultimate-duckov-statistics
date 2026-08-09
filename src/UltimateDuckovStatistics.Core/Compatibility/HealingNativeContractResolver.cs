using System.Reflection;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class HealingNativeContractResolver
{
    private static readonly string[] RequiredHealthProperties =
    {
        "CurrentHealth",
        "MaxHealth",
        "IsMainCharacterHealth"
    };

    public static bool TryResolve(
        Type healthType,
        Type effectActionType,
        Type effectContextType,
        Type buffManagerType,
        Type buffType,
        Type characterType,
        out MethodInfo? healthMethod,
        out MethodInfo? effectMethod,
        out MethodInfo? buffMethod,
        out string failure)
    {
        if (healthType == null)
        {
            throw new ArgumentNullException(nameof(healthType));
        }

        if (effectActionType == null)
        {
            throw new ArgumentNullException(nameof(effectActionType));
        }

        if (effectContextType == null)
        {
            throw new ArgumentNullException(nameof(effectContextType));
        }

        if (buffManagerType == null)
        {
            throw new ArgumentNullException(nameof(buffManagerType));
        }

        if (buffType == null)
        {
            throw new ArgumentNullException(nameof(buffType));
        }

        if (characterType == null)
        {
            throw new ArgumentNullException(nameof(characterType));
        }

        healthMethod = FindExactMethod(
            healthType,
            "AddHealth",
            requirePublic: true,
            requireAssembly: false,
            [typeof(float)]);
        effectMethod = FindExactMethod(
            effectActionType,
            "NotifyTriggered",
            requirePublic: false,
            requireAssembly: true,
            [effectContextType]);
        buffMethod = FindExactMethod(
            buffManagerType,
            "AddBuff",
            requirePublic: true,
            requireAssembly: false,
            [buffType, characterType, typeof(int)]);
        var healthPropertiesPresent = RequiredHealthProperties.All(name =>
        {
            var property = healthType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var expectedType = string.Equals(name, "IsMainCharacterHealth", StringComparison.Ordinal)
                ? typeof(bool)
                : typeof(float);
            return property?.PropertyType == expectedType
                   && property.GetMethod is { IsPublic: true, IsStatic: false };
        });
        if (healthMethod == null || effectMethod == null || buffMethod == null || !healthPropertiesPresent)
        {
            failure = "Exact healing attribution method/property contracts are missing or changed.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static MethodInfo? FindExactMethod(
        Type declaringType,
        string name,
        bool requirePublic,
        bool requireAssembly,
        Type[] parameterTypes)
    {
        var flags = BindingFlags.Instance | (requirePublic ? BindingFlags.Public : BindingFlags.NonPublic);
        return declaringType.GetMethods(flags).SingleOrDefault(method =>
            string.Equals(method.Name, name, StringComparison.Ordinal)
            && method.ReturnType == typeof(void)
            && method.IsPublic == requirePublic
            && (!requireAssembly || method.IsAssembly)
            && !method.IsStatic
            && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes));
    }
}
