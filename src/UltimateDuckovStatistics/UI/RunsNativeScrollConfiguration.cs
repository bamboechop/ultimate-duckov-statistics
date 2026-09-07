using Duckov.Utilities;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

internal static class RunsNativeScrollConfiguration
{
    public static void Apply(ScrollRect target)
    {
        // Keep native input feel, but retained statistics pages must stop at their content bounds.
        // Read the shared prefab without instantiating its hierarchy or listeners.
        var source = GameplayDataSettings.UIPrefabs.ScrollRect;
        target.movementType = ScrollRect.MovementType.Clamped;
        target.elasticity = source.elasticity;
        target.inertia = source.inertia;
        target.decelerationRate = source.decelerationRate;
        target.scrollSensitivity = source.scrollSensitivity;
    }
}
