using UnityEngine;

namespace UltimateDuckovStatistics.Encounters;

internal static class EncounterPaths
{
    // This cache location predates production integration. Keep it stable so
    // recorded artwork remains available after restart without revisiting a map.
    internal static string MapDirectory => Path.Combine(Application.persistentDataPath,
        "UltimateDuckovStatistics", "encounter-prototype", "maps");
}
