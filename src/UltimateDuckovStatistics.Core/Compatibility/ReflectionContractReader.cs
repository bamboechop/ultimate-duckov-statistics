using System.Reflection;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class ReflectionContractReader
{
    public static object? ReadInstanceMember(object instance, string memberName)
    {
        if (instance == null)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        if (string.IsNullOrWhiteSpace(memberName))
        {
            throw new ArgumentException("A member name is required.", nameof(memberName));
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        return type.GetProperty(memberName, flags)?.GetValue(instance)
               ?? type.GetField(memberName, flags)?.GetValue(instance);
    }
}
