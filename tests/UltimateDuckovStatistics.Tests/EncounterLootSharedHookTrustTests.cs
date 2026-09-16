using System.Reflection;
using Duckov.UI;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class EncounterLootSharedHookTrustTests : IDisposable
{
    private readonly LootSink sink = new();
    private readonly NativeContainerAdapter adapter;
    private readonly List<string> diagnostics = new();
    private readonly CharacterMainControl player = new() { IsMainCharacter = true, CharacterItem = new() { Inventory = new() } };

    public EncounterLootSharedHookTrustTests()
    {
        HarmonyLib.Harmony.ClearAll(); Application.version = "2.3.30";
        CharacterMainControl.Main = player;
        player.CharacterItem!.Inventory!.AttachedToItem = player.CharacterItem;
        adapter = new(() => "g", () => sink.Context.RunId, () => "map", () => true,
            _ => true, _ => { }, _ => { }, diagnostics.Add);
    }

    [Theory]
    [InlineData(typeof(CharacterMainControl), "OnDead")]
    [InlineData(typeof(InteractableLootbox), "CreateFromItem")]
    public void PreexistingSharedPatchReportsMissingLootCoverageInEachRun(Type type, string method)
    {
        AddForeignPatch(type, method);
        adapter.Initialize();
        Assert.False(ContainerHarmonyBridge.EncounterCorpseHooksTrusted);
        using var observer = new NativeEncounterLootObserver(sink, diagnostics.Add);
        Assert.Equal(EncounterCaptureIssue.LootIncomplete, observer.FailureIssue);
        var corpse = CreateCorpse();
        Open(corpse.Box);
        Assert.DoesNotContain(sink.Rows, row => row.Kind is "loot.corpse-join" or "loot.inventory-open");
        observer.Tick(sink.Context);
        sink.Context.RunId = "next";
        observer.Tick(sink.Context); observer.Tick(sink.Context);
        var records = sink.Flush();
        Assert.Equal(2, records.Count(row => row.Coverage?.Issue == EncounterCaptureIssue.LootIncomplete));
        Assert.Contains(records, row => row.RunId == "r" && row.Coverage != null);
        Assert.Contains(records, row => row.RunId == "next" && row.Coverage != null);
        Assert.DoesNotContain(records, row => row.Inventory != null || row.Loot != null);
    }

    [Theory]
    [InlineData(typeof(CharacterMainControl), "OnDead")]
    [InlineData(typeof(InteractableLootbox), "CreateFromItem")]
    public void RuntimeSharedPatchStopsNewJoinsButKeepsVerifiedCorpseInspectionAndTransfers(Type type, string method)
    {
        InitializeTrusted();
        using var observer = new NativeEncounterLootObserver(sink, diagnostics.Add);
        var verified = CreateCorpse();
        Assert.Single(sink.Rows, row => row.Kind == "loot.corpse-join");
        AddForeignPatchAndInspect(type, method);
        // The owner's callbacks remain installed after runtime detachment. Invoke
        // them before the observer's next Tick to test the real retained tap path.
        var rejected = CreateCorpse();
        Open(rejected.Box);
        Assert.Single(sink.Rows, row => row.Kind == "loot.corpse-join");
        Assert.DoesNotContain(sink.Rows, row => row.Kind == "loot.inventory-open");
        Assert.Equal(EncounterCaptureIssue.LootIncomplete, observer.FailureIssue);

        Open(verified.Box);
        Take(verified.Item);
        Assert.Single(sink.Rows, row => row.Kind == "loot.inventory-open");
        Assert.Single(sink.Rows, row => row.Kind == "loot.transfer");
        observer.Tick(sink.Context);
        var records = sink.Flush();
        Assert.Equal(9, Assert.Single(records, row => row.Loot != null).Loot!.TakenToPlayer);
        Assert.Equal(9, Assert.Single(Assert.Single(records, row => row.Inventory != null).Inventory!.Slots).Quantity);
        Assert.Equal(2, records.Count(row => row.Encounter != null)); // Combat records survive independently.
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(records).CoverageNoticeKey);
        var codec = new ProfileRecordCodec();
        var reopened = records.Select(row => ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(row))).ToArray();
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(reopened).CoverageNoticeKey);
        Assert.Equal(9, Assert.Single(reopened, row => row.Loot != null).Loot!.TakenToPlayer);
    }

    [Theory]
    [InlineData(typeof(CharacterMainControl), "OnDead")]
    [InlineData(typeof(InteractableLootbox), "CreateFromItem")]
    public void SharedTrustLossDuringDeathRejectsItsPendingCorpseJoin(Type type, string method)
    {
        InitializeTrusted();
        using var observer = new NativeEncounterLootObserver(sink, diagnostics.Add);
        var actor = new CharacterMainControl { CharacterItem = new() };
        object?[] death = [actor, null];
        ContainerHarmonyCallbacks.CharacterDeathPrefixMethod.Invoke(null, death);
        AddForeignPatchAndInspect(type, method);
        try
        {
            var box = new InteractableLootbox(); box.SetInventory(new());
            ContainerHarmonyCallbacks.CreateFromItemPostfixMethod.Invoke(null, [actor.CharacterItem, box, null]);
        }
        finally { ContainerHarmonyCallbacks.CharacterDeathFinalizerMethod.Invoke(null, [null, death[1]]); }
        Assert.DoesNotContain(sink.Rows, row => row.Kind == "loot.corpse-join");
        Assert.Single(sink.Rows, row => row.Kind == "loot.coverage");
        Assert.Single(sink.Flush(), row => row.Coverage?.Issue == EncounterCaptureIssue.LootIncomplete);
    }

    [Fact]
    public void HealthySharedHooksKeepNormalLootCaptureWithoutCoverageWarning()
    {
        InitializeTrusted();
        using var observer = new NativeEncounterLootObserver(sink, diagnostics.Add);
        var corpse = CreateCorpse(); Open(corpse.Box); Take(corpse.Item);
        observer.Tick(sink.Context);
        Assert.Null(observer.FailureIssue);
        var records = sink.Flush();
        Assert.Equal(9, Assert.Single(records, row => row.Loot != null).Loot!.TakenToPlayer);
        Assert.DoesNotContain(records, row => row.Coverage != null);
    }

    [Fact]
    public void DetachedOwnerRejectsStaleCallbacksAndRetainsPreviouslyProvenLoot()
    {
        InitializeTrusted();
        using var observer = new NativeEncounterLootObserver(sink, diagnostics.Add);
        var verified = CreateCorpse();
        Assert.True(adapter.TryCleanup());
        CreateCorpse();
        Open(verified.Box); Take(verified.Item);
        Assert.Single(sink.Rows, row => row.Kind == "loot.corpse-join");
        Assert.Single(sink.Flush(), row => row.Coverage?.Issue == EncounterCaptureIssue.LootIncomplete);
    }

    private void InitializeTrusted()
    {
        adapter.Initialize();
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.UniqueContainersLooted.State);
        Assert.True(ContainerHarmonyBridge.EncounterCorpseHooksTrusted);
    }

    private (InteractableLootbox Box, Item Item) CreateCorpse()
    {
        var actor = new CharacterMainControl { CharacterItem = new(), characterPreset = new() { nameKey = "Scavenger" } };
        sink.Record("combat_fatal", new { FatalSequence = sink.ActorId(actor), Target = new { Id = sink.ActorId(actor), PresetKey = "Scavenger" },
            Source = new { Kind = "projectile", Credited = new { Id = sink.ActorId(player), IsMain = true } } });
        var inventory = new Inventory(); var item = new Item { TypeID = 42, StackCount = 9 };
        inventory.AddAt(item, 0);
        var box = new InteractableLootbox(); box.SetInventory(inventory); box.SetInteractingCharacter(player);
        object?[] death = [actor, null];
        ContainerHarmonyCallbacks.CharacterDeathPrefixMethod.Invoke(null, death);
        try { ContainerHarmonyCallbacks.CreateFromItemPostfixMethod.Invoke(null, [actor.CharacterItem, box, null]); }
        finally { ContainerHarmonyCallbacks.CharacterDeathFinalizerMethod.Invoke(null, [null, death[1]]); }
        return (box, item);
    }

    private static void Open(InteractableLootbox box)
    {
        box.StartLoot();
        var view = new LootView(); view.SetTarget(box);
        LootCallback("ViewOpenPostfix").Invoke(null, [view]);
    }

    private void Take(Item item)
    {
        var inventory = player.CharacterItem!.Inventory!;
        var index = inventory.Content.Count;
        object?[] operation = [inventory, item, index, null];
        LootCallback("AddAtPrefix").Invoke(null, operation);
        var result = inventory.AddAt(item, index);
        LootCallback("AddAtFinalizer").Invoke(null, [result, null, operation[3]]);
    }

    private static MethodInfo LootCallback(string name) => typeof(NativeEncounterLootObserver).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void AddForeignPatch(Type type, string name)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;
        new HarmonyLib.Harmony("foreign.loot-test").Patch(method,
            new HarmonyLib.HarmonyMethod(typeof(EncounterLootSharedHookTrustTests).GetMethod(nameof(ForeignPrefix), BindingFlags.NonPublic | BindingFlags.Static)!), null, null, null);
    }
    private void AddForeignPatchAndInspect(Type type, string name)
    {
        AddForeignPatch(type, name);
        var inspect = typeof(NativeContainerAdapter).GetMethod("InspectNextPatchStamp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < 3; i++) inspect.Invoke(adapter, [DateTime.UtcNow.AddSeconds(5 + i)]);
        Assert.False(ContainerHarmonyBridge.EncounterCorpseHooksTrusted);
        Assert.Contains(diagnostics, detail => detail.Contains("corpse-provenance patch drift", StringComparison.Ordinal));
    }
    private static void ForeignPrefix() { }
    public void Dispose()
    {
        Assert.True(adapter.TryCleanup());
        CharacterMainControl.ResetNativeState(); HarmonyLib.Harmony.ClearAll();
    }

    private sealed class LootSink : IEncounterObservationSink
    {
        private readonly Dictionary<UnityEngine.Object, int> actors = new(ReferenceEqualityComparer.Instance);
        private readonly EncounterCapturePipeline pipeline = new();
        internal readonly List<(string Kind, JObject Data)> Rows = new();
        public EncounterObservationContext Context { get; } = new()
        { Active = true, GenerationId = "g", RunId = "r", MapId = "map", SegmentId = "segment" };
        public int ActorId(UnityEngine.Object actor)
        {
            if (!actors.TryGetValue(actor, out var id)) actors.Add(actor, id = actors.Count + 1);
            return id;
        }
        public void Record(string eventKind, object payload)
        {
            Rows.Add((eventKind, JObject.FromObject(payload)));
            // Same coverage routing as EncounterCaptureHost.Record.
            if (eventKind == "loot.coverage") pipeline.ReportCoverage(Context.GenerationId, Context.RunId,
                Context.MonotonicSeconds, EncounterCaptureIssue.LootIncomplete);
            pipeline.Record(Context.GenerationId, Context.RunId, Context.MapId, Context.SegmentId,
                Context.MonotonicSeconds, eventKind, payload);
        }
        internal EncounterRecord[] Flush()
        {
            var result = new Dictionary<(string, string), EncounterRecord>();
            Assert.True(pipeline.Pump((_, row) => { result[(row.RunId, row.Id)] = row; return true; }, flush: true));
            Assert.True(pipeline.Failure == null, pipeline.Failure);
            return result.Values.ToArray();
        }
    }
}
