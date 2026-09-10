// Installed native signatures for the source-linked combat adapter and callbacks.
// No projectile physics or game loop is simulated; tests invoke the production boundaries.
#pragma warning disable CA1050, CA1051, CA1707, CA1711, CA1716, CA1720, CA1822, CA2211, CS0067
using ItemStatsSystem;
using UnityEngine;

public sealed partial class CharacterMainControl
{
    public AttackAction attackAction { get; } = new();
    public CharacterPreset? characterPreset { get; set; }
    public string name { get; set; } = "Native character";
    public int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    public T? GetComponent<T>() where T : class => null;
    public ItemAgent_MeleeWeapon? MeleeWeapon { get; set; }
    public ItemAgent_MeleeWeapon? GetMeleeWeapon() => MeleeWeapon;
}
public sealed class AttackAction
{
    public event Action? OnAttack;
    public void RaiseAttack() => OnAttack?.Invoke();
}
public sealed class CharacterPreset
{
    public string nameKey { get; set; } = "";
    public string name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
public sealed class PetAI { public CharacterMainControl? master; }
public sealed class AICharacterController { public CharacterMainControl? leader; }
public sealed partial class Health
{
    public Teams team { get; set; } = Teams.enemy;
    public bool isZombie { get; set; }
    public CharacterMainControl? Character { get; set; }
    public CharacterMainControl? TryGetCharacter() => Character;
    public bool Hurt(DamageInfo damageInfo) => true;
}
public enum Teams { player, enemy }
public enum DamageTypes { normal, realDamage }
public sealed class Projectile
{
    public ProjectileContext context;
    public int GetInstanceID() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    public void Init(ProjectileContext _context) => context = _context;
    private void Update() { }
    private void Release() { }
}
public struct ProjectileContext
{
    public CharacterMainControl? realFromCharacter, fromCharacter;
    public ItemSetting_Gun? fromGunItemSetting;
    public int fromWeaponItemID;
}
public sealed class ItemAgent_MeleeWeapon
{
    public CharacterMainControl? Holder { get; set; }
    public Item? Item { get; set; }
    private int CheckCollidersInRange(bool dealDamage) => 0;
}
public sealed class ZoneDamage { private void Damage() { } }
public sealed partial class Grenade
{
    public void Launch(Vector3 startPoint, Vector3 velocity, CharacterMainControl fromCharacter, bool firstFrameCheck) { }
    private void Explode() { }
}
public sealed partial class LevelManagerInstance
{
    public CharacterMainControl? ControllingCharacter { get; set; }
    public CharacterMainControl? PetCharacter { get; set; }
    public CombatInputBoundary InputManager { get; } = new();
}
public sealed class CombatInputBoundary { public bool AimingEnemyHead { get; set; } }
namespace ItemStatsSystem
{
    public sealed partial class Item
    {
        public CharacterMainControl? Character { get; set; }
        public CharacterMainControl? GetCharacterMainControl() => Character;
    }
    public class EffectTrigger
    {
        public EffectMaster? Master { get; set; }
        public object? Parent { get; set; }
        public T? GetComponentInParent<T>() where T : class => Parent as T;
    }
    public sealed class TickTrigger : EffectTrigger { }
    public sealed class UpdateTrigger : EffectTrigger { }
    public sealed class Effect
    {
        public Item? Item { get; set; }
        public List<EffectTrigger> Triggers { get; } = new();
        public T? GetComponentInParent<T>() where T : class => null;
        public void SetItem(Item item) => Item = item;
        private void Trigger(EffectTriggerEventContext context) { }
    }
}
namespace Duckov.Buffs
{
    public sealed partial class Buff
    {
        public CharacterMainControl? fromWho;
        public int fromWeaponID;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
    }
}
namespace UnityEngine
{
    public readonly struct Quaternion { }
    public static class Time { public static int frameCount { get; set; } public static float timeScale { get; set; } = 1; }
    public partial class Object
    {
        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation) => original;
    }
    public sealed partial class GameObject : Object
    {
        public List<object> Components { get; } = new();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Components.OfType<T>().ToArray();
    }
}
