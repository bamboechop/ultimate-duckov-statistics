// Native observer boundary for source-linked aggregate adapter tests. These tests
// exercise callback composition, not Unity actor discovery or native encounter capture.
// Encounter reduction/persistence tests separately replay recorded native evidence.
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Encounters;

internal static class NativeEncounterCombatObserver
{
    internal static void ObserveHealthBegin(Health health, DamageInfo info) { }
    internal static void ObserveHealthComplete(Health health) { }
    internal static void ObserveHealthFinally(Health health, Exception? exception) { }
    internal static void ObserveHeadshot(Health health) { }
    internal static void ObserveProjectileInit(Projectile projectile, ProjectileContext context) { }
    internal static void ObserveProjectileBegin(Projectile projectile) { }
    internal static void ObserveProjectileRelease(Projectile projectile) { }
    internal static void ObserveEffectBegin(EffectTriggerEventContext context) { }
    internal static void ObserveMeleeBegin(ItemAgent_MeleeWeapon weapon, bool dealDamage) { }
    internal static void ObserveAttackFinally(Exception? exception) { }
}
