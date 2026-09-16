// Native observer boundary for source-linked aggregate adapter tests. These tests
// exercise callback composition, not Unity actor discovery or native encounter capture.
// Actor identity lookup is source-linked separately; other native observer behavior
// remains stubbed. Reduction/persistence tests replay recorded native evidence.
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Encounters;

internal sealed partial class NativeEncounterCombatObserver
{
    private readonly IEncounterObservationSink sink;
    internal NativeEncounterCombatObserver(IEncounterObservationSink sink) { this.sink = sink; }
    internal object ReadActorIdentity(CharacterMainControl? actor) => Actor(actor);
    internal static void ObserveHealthBegin(Health health, DamageInfo info) { }
    internal static void ObserveHealthComplete(Health health) { }
    internal static void ObserveHealthFinally(Health health, Exception? exception) { }
    internal static void ObserveHeadshot(Health health) { }
    internal static void ObserveProjectileInit(Projectile projectile, ProjectileContext context) { }
    internal static void ObserveProjectileBegin(Projectile projectile) { }
    internal static void ObserveProjectileRelease(Projectile projectile) { }
    internal static Adapters.CombatNativeScope? LastEffectScope { get; private set; }
    internal static void ObserveEffectBegin(EffectTriggerEventContext context, Adapters.CombatNativeScope? resolvedScope) => LastEffectScope = resolvedScope;
    internal static void ObserveMeleeBegin(ItemAgent_MeleeWeapon weapon, bool dealDamage) { }
    internal static void ObserveAttackFinally(Exception? exception) { }
}
