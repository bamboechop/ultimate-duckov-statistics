using System.Reflection;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

// LevelManager.Instance searches the scene whenever its backing reference is absent.
// Native initialization assigns this field before publishing OnLevelBeginInitializing.
// Read it afresh (including Unity's destroyed-object check), never cache a missing level.
internal static class NativeLevelAvailability
{
    private static FieldInfo? instanceField = FindInstanceField(typeof(LevelManager));

    internal static bool MayExist
    {
        get
        {
            var field = instanceField;
            if (field == null) return true; // Contract drift: retain the public lookup path.
            try { return field.GetValue(null) is UnityEngine.Object value && value != null; }
            catch (Exception)
            {
                instanceField = null;
                Debug.LogWarning("UDS level-presence optimization unavailable; using native public lookups.");
                return true;
            }
        }
    }

    internal static FieldInfo? FindInstanceField(Type owner)
    {
        var field = owner.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        return field != null && field.FieldType == owner && typeof(UnityEngine.Object).IsAssignableFrom(owner)
            ? field : null;
    }
}
