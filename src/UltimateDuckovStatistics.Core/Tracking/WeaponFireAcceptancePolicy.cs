using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Tracking;

public static class WeaponFireAcceptancePolicy
{
    public static bool ShouldRecord(
        bool activeRun,
        GameplayContext gameplayContext,
        bool exactMainDuck,
        bool loading,
        bool paused) => activeRun
                        && gameplayContext == GameplayContext.Raid
                        && exactMainDuck
                        && !loading
                        && !paused;
}
