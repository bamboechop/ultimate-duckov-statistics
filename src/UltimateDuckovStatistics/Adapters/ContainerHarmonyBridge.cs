using System.Reflection;
using System.Runtime.CompilerServices;
using Duckov.Utilities;

namespace UltimateDuckovStatistics.Adapters;

internal static class ContainerHarmonyBridge
{
    private sealed class CorpseMarker
    {
        public CorpseMarker(string provenance) => Provenance = provenance;
        public string Provenance { get; }
    }

    private static readonly ConditionalWeakTable<InteractableLootbox, CorpseMarker> CorpseLootboxes = new();
    [ThreadStatic] private static int deathScopeDepth;
    private static NativeContainerAdapter? adapter;

    public static void Attach(NativeContainerAdapter value)
    {
        adapter = value ?? throw new ArgumentNullException(nameof(value));
        deathScopeDepth = 0;
    }

    public static void Detach(NativeContainerAdapter value)
    {
        if (ReferenceEquals(adapter, value)) adapter = null;
        deathScopeDepth = 0;
    }

    public static bool EnterDeathScope()
    {
        if (adapter == null) return false;
        deathScopeDepth++;
        return true;
    }

    public static void ExitDeathScope(bool entered)
    {
        if (entered && deathScopeDepth > 0) deathScopeDepth--;
    }

    public static void MarkCreatedLootbox(InteractableLootbox? lootbox, InteractableLootbox? prefab)
    {
        if (adapter == null || lootbox == null) return;
        string? provenance = deathScopeDepth > 0 ? "character-death-scope" : null;
        if (provenance == null)
        {
            try
            {
                if (ReferenceEquals(prefab, GameplayDataSettings.Prefabs?.LootBoxPrefab_Tomb))
                    provenance = "native-tomb-prefab";
            }
            catch
            {
                // The death scope remains the authoritative fallback for ordinary enemy corpses.
            }
        }
        if (provenance != null)
            CorpseLootboxes.GetValue(lootbox, _ => new CorpseMarker(provenance));
    }

    public static bool TryGetCorpseProvenance(InteractableLootbox lootbox, out string provenance)
    {
        if (lootbox != null && CorpseLootboxes.TryGetValue(lootbox, out var marker))
        {
            provenance = marker.Provenance;
            return true;
        }
        provenance = string.Empty;
        return false;
    }
}

internal static class ContainerHarmonyCallbacks
{
    private static void CharacterDeathPrefix(out bool __state) =>
        __state = ContainerHarmonyBridge.EnterDeathScope();

    private static Exception? CharacterDeathFinalizer(Exception? __exception, bool __state)
    {
        ContainerHarmonyBridge.ExitDeathScope(__state);
        return __exception;
    }

    private static void CreateFromItemPostfix(
        InteractableLootbox? __result,
        InteractableLootbox? prefab) =>
        ContainerHarmonyBridge.MarkCreatedLootbox(__result, prefab);

    public static MethodInfo CharacterDeathPrefixMethod => Get(nameof(CharacterDeathPrefix));
    public static MethodInfo CharacterDeathFinalizerMethod => Get(nameof(CharacterDeathFinalizer));
    public static MethodInfo CreateFromItemPostfixMethod => Get(nameof(CreateFromItemPostfix));

    private static MethodInfo Get(string name) => typeof(ContainerHarmonyCallbacks).GetMethod(
        name,
        BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(ContainerHarmonyCallbacks).FullName, name);
}
