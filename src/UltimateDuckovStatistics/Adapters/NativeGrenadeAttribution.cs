using System.Reflection;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

// Grenade.Explode calls ExplosionManager -> DamageReceiver -> Health.Hurt synchronously.
// Capture the native originating item, never the player's currently held weapon.
internal static class NativeGrenadeAttribution
{
    [ThreadStatic] private static Scope? current;
    internal sealed class Scope
    {
        internal Scope? Parent;
        internal CharacterMainControl? Source;
        internal int ItemId;
        internal bool CreatesExplosion;
    }
    internal static Scope Begin(Grenade grenade)
    {
        var scope = new Scope { Parent = current, Source = grenade.damageInfo.fromCharacter,
            ItemId = grenade.damageInfo.fromWeaponItemID, CreatesExplosion = grenade.createExplosion };
        current = scope;
        return scope;
    }
    internal static void End(Scope? scope)
    {
        if (scope != null && ReferenceEquals(current, scope)) current = scope.Parent;
    }
    internal static bool Matches(DamageInfo damage) => current is { CreatesExplosion: true, ItemId: > 0 } scope
        && scope.Source != null && scope.Source.IsMainCharacter
        && ReferenceEquals(scope.Source, damage.fromCharacter)
        && damage.fromWeaponItemID == scope.ItemId && damage.isExplosion;
    internal static CombatAttackKind Classify(bool supported, DamageInfo damage, CombatAttackKind fallback) =>
        supported && Matches(damage) ? CombatAttackKind.Throwable : fallback;
    private static void Prefix(Grenade __instance, out Scope __state) => __state = Begin(__instance);
    private static Exception? Finalizer(Exception? __exception, Scope? __state)
    { End(__state); return __exception; }
    internal static MethodInfo PrefixMethod => typeof(NativeGrenadeAttribution).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)!;
    internal static MethodInfo FinalizerMethod => typeof(NativeGrenadeAttribution).GetMethod(nameof(Finalizer), BindingFlags.NonPublic | BindingFlags.Static)!;
}
