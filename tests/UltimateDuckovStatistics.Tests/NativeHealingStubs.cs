// Native API surface for executing the production healing adapter's activation path.
#pragma warning disable CA1822, CA1050
public sealed class CharacterBuffManager
{
    public CharacterMainControl? Master { get; set; }
    public List<Duckov.Buffs.Buff> Buffs { get; } = new();
    public event Action<CharacterBuffManager, Duckov.Buffs.Buff>? onRemoveBuff;
    public void AddBuff(Duckov.Buffs.Buff buff, CharacterMainControl? fromWho, int overrideWeaponID) => Buffs.Add(buff);
    public void Remove(Duckov.Buffs.Buff buff) => onRemoveBuff?.Invoke(this, buff);
}
namespace Duckov.Buffs
{
    public sealed class Buff { public int ID { get; set; } public int GetInstanceID() => ID; }
}
namespace ItemStatsSystem
{
    public sealed class EffectTriggerEventContext { }
    public sealed class EffectMaster { public Item? Item { get; set; } }
    public sealed class EffectAction
    {
        public EffectMaster? Master { get; set; }
        public T? GetComponentInParent<T>() where T : class => null;
        internal void NotifyTriggered(EffectTriggerEventContext context) { }
    }
}
namespace UltimateDuckovStatistics.Adapters
{
    internal static class CombatHarmonyBridge
    {
        public static void CaptureBuffApplication(CharacterBuffManager manager, Duckov.Buffs.Buff buff, CharacterMainControl? fromWho, int overrideWeaponID, bool newlyCreated) { }
    }
}
