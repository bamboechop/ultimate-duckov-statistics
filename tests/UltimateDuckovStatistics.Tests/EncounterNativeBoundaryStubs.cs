// Test accessors for the source-linked production observer. Native game objects
// and Harmony invocation are simulated; observer behavior is not stubbed.

namespace UltimateDuckovStatistics.Encounters;

internal sealed partial class NativeEncounterCombatObserver
{
    // Identity-only tests do not attach native hooks or become the active observer.
    internal NativeEncounterCombatObserver(IEncounterObservationSink sink) { this.sink = sink; log = _ => { }; }
    internal object ReadActorIdentity(CharacterMainControl? actor) => Actor(actor);
    internal static object? ReadActiveSource() => attack?.Source;
    internal static void AssignHealth(Health health, float value)
    {
        HealthValuePrefix(health, value, out var state);
        health.CurrentHealth = value;
        HealthValuePostfix(health, state);
    }
}
