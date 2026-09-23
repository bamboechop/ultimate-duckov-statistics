using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class BecomeVeteranHealingCompatibilityTests
{
    [Fact]
    public void VanillaHealingNeedsNoModAndKeepsActualDeltaAttribution()
    {
        using var fixture = new Fixture();
        Assert.True(NativeBecomeVeteranHealingCompatibility.TryDiscover(fixture.Tracker, () => true,
            _ => throw new InvalidOperationException(), out var absent, out _));
        Assert.Null(absent);
        fixture.Use("medicine-a", 1, amount: 40, split: false);
        var healing = Assert.Single(fixture.Events);
        Assert.Equal(40, healing.ActualHealthRestored);
        Assert.Equal("use-1", healing.SourceItemUseEventId);
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
    }

    [Fact]
    public void SplitMedicineRecordsImmediateAndTenActualTicksUnderOneCompletedUse()
    {
        using var fixture = new Fixture();
        fixture.Use("medicine-a", 1, 40);
        Assert.Equal(20, Assert.Single(fixture.Events).ActualHealthRestored);
        for (var index = 0; index < 10; index++) fixture.Tick();
        Assert.Equal(11, fixture.Events.Count);
        Assert.Equal(40, fixture.Events.Sum(value => value.ActualHealthRestored));
        Assert.All(fixture.Events, value => Assert.Equal("use-1", value.SourceItemUseEventId));
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
    }

    [Theory]
    [InlineData(true, 0, 0.5, 60)]
    [InlineData(false, 0, 0.5, 60)]
    [InlineData(true, 93, 0.5, 7)]
    [InlineData(false, 93, 0.5, 7)]
    [InlineData(true, 100, 0.5, 0)]
    public void BonusesAndHpCapUseObservedRestoration(bool split, float initialHp, float bonus, double expected)
    {
        using var fixture = new Fixture();
        fixture.Health.CurrentHealth = initialHp;
        fixture.Use("medicine-a", 1, 40, split, bonus);
        for (var index = 0; index < 10; index++) fixture.Tick();
        Assert.Equal(expected, fixture.Events.Sum(value => value.ActualHealthRestored));
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
    }

    [Fact]
    public void SecondMedicineReplacesOnlyPendingQueueAndDoesNotCreditUnusedRemainder()
    {
        using var fixture = new Fixture();
        fixture.Use("medicine-a", 1, 40);
        fixture.Tick(); fixture.Tick();
        fixture.Use("medicine-b", 2, 20);
        for (var index = 0; index < 10; index++) fixture.Tick();
        Assert.Equal(24, fixture.Events.Where(value => value.ItemId == "medicine-a").Sum(value => value.ActualHealthRestored));
        Assert.Equal(20, fixture.Events.Where(value => value.ItemId == "medicine-b").Sum(value => value.ActualHealthRestored));
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
    }

    [Fact]
    public void UnownedReplacementCannotBorrowPreviousMedicineOrAnOuterScope()
    {
        using var fixture = new Fixture();
        fixture.Use("medicine-a", 1, 40);
        var state = fixture.Compatibility.BeginRegistration(fixture.Health);
        MedicFixture.Add(fixture.Health, 30);
        fixture.Compatibility.CompleteRegistration(state);
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
        var unrelated = HealingHarmonyBridge.PushCompatibilityApplication("unrelated");
        try { fixture.Tick(); }
        finally { HealingHarmonyBridge.Pop(unrelated); }
        Assert.Equal(20, Assert.Single(fixture.Events).ActualHealthRestored);
    }

    [Fact]
    public void NaturalRegenerationIsExcludedEvenWhenCalledInsideAMedicineScope()
    {
        using var fixture = new Fixture();
        fixture.BeginUse("medicine-a", 1);
        var patch = fixture.Compatibility.Patches.Single(value => value.Original.Name == "TryApplyHeal");
        var arguments = new object?[] { null };
        patch.Prefix!.Invoke(null, arguments);
        Assert.Null(HealingHarmonyBridge.CurrentCorrelationId);
        fixture.Apply(5);
        patch.Finalizer!.Invoke(null, new[] { null, arguments[0] });
        Assert.NotNull(HealingHarmonyBridge.CurrentCorrelationId);
        fixture.Apply(4);
        fixture.CompleteUse("medicine-a", 1);
        Assert.Equal(9, fixture.Health.CurrentHealth);
        Assert.Equal(4, Assert.Single(fixture.Events).ActualHealthRestored);
    }

    [Fact]
    public void QueueClearAndProfileResetDiscardStaleProvenanceWithoutChangingNativeQueue()
    {
        using var fixture = new Fixture();
        fixture.Use("medicine-a", 1, 40);
        MedicFixture.Clear();
        fixture.Compatibility.ReconcileQueue();
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
        fixture.Use("medicine-b", 2, 20);
        fixture.Adapter.Reset();
        fixture.Compatibility.Reset();
        Assert.NotEmpty(MedicFixture.Entries);
        for (var index = 0; index < 10; index++) fixture.Tick();
        Assert.Equal(30, fixture.Events.Sum(value => value.ActualHealthRestored));
        Assert.Equal(40, fixture.Health.CurrentHealth);
    }

    [Fact]
    public void CancelledUseAndNonPlayerQueuesCannotProduceItemRestoration()
    {
        using var fixture = new Fixture();
        fixture.BeginUse("medicine-a", 1);
        fixture.Register(fixture.Health, 20);
        fixture.Apply(20);
        fixture.Adapter.CompleteUse(1, null);
        fixture.Register(new Health { IsMainCharacterHealth = false }, 30);
        for (var index = 0; index < 10; index++) fixture.Tick();
        Assert.Empty(fixture.Events);
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
    }

    [Fact]
    public void DisablingSplitDoesNotReassignAnExistingQueueToTheNextMedicine()
    {
        using var fixture = new Fixture();
        fixture.Use("medicine-a", 1, 40);
        fixture.Use("medicine-b", 2, 10, split: false);
        for (var index = 0; index < 10; index++) fixture.Tick();
        Assert.Equal(40, fixture.Events.Where(value => value.ItemId == "medicine-a").Sum(value => value.ActualHealthRestored));
        Assert.Equal(10, fixture.Events.Where(value => value.ItemId == "medicine-b").Sum(value => value.ActualHealthRestored));
    }

    [Fact]
    public void UnknownPreexistingQueueAndApplyOutsideTickNeverGainProvenance()
    {
        using var fixture = new Fixture();
        MedicFixture.Add(fixture.Health, 20);
        fixture.Tick();
        Assert.Empty(fixture.Events);
        fixture.Use("medicine-a", 1, 20);
        var scope = fixture.Compatibility.BeginQueuedApplication(fixture.Health);
        try { fixture.Apply(7); }
        finally { HealingHarmonyBridge.Pop(scope); }
        Assert.Equal(10, Assert.Single(fixture.Events).ActualHealthRestored);
    }

    [Fact]
    public void StaleRegistrationCannotBindAcrossResetAndExceptionsPreserveNativeFailure()
    {
        using var fixture = new Fixture();
        fixture.BeginUse("medicine-a", 1);
        var state = fixture.Compatibility.BeginRegistration(fixture.Health);
        MedicFixture.Add(fixture.Health, 20);
        fixture.Compatibility.Reset();
        fixture.Compatibility.CompleteRegistration(state);
        Assert.Equal(0, fixture.Tracker.DeferredSourceCount);
        var scope = fixture.Compatibility.BeginQueuedApplication(fixture.Health);
        var failure = new IOException("native failure");
        var finalizer = fixture.Compatibility.Patches.Single(value => value.Original.Name == "ApplyHeal").Finalizer!;
        Assert.Same(failure, finalizer.Invoke(null, new object?[] { failure, scope }));
        Assert.NotNull(HealingHarmonyBridge.CurrentCorrelationId); // original item scope survives
    }

    private sealed class Fixture : IDisposable
    {
        private readonly NativeBuffApplicationAdapter buffs;
        internal NativeHealingAttributionAdapter Adapter { get; }
        internal NativeBecomeVeteranHealingCompatibility Compatibility { get; }
        internal HealingAttributionTracker Tracker { get; }
        internal List<HealingApplied> Events { get; } = new();
        internal Health Health { get; } = new() { CurrentHealth = 0, MaxHealth = 100, IsMainCharacterHealth = true };

        internal Fixture()
        {
            HarmonyLib.Harmony.ClearAll();
            MedicFixture.Clear();
            var boundary = new NativeBuffApplicationObservationBoundary();
            buffs = new NativeBuffApplicationAdapter(boundary, _ => { });
            Assert.True(buffs.Initialize());
            Adapter = new NativeHealingAttributionAdapter(Events.Add, _ => { }, boundary);
            Assert.Equal(AdapterCapabilityState.Supported, Adapter.Initialize().State);
            Tracker = (HealingAttributionTracker)typeof(NativeHealingAttributionAdapter)
                .GetField("tracker", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Adapter)!;
            Compatibility = NativeBecomeVeteranHealingCompatibility.CreateForContract(typeof(MedicFixture), typeof(NaturalFixture),
                Tracker, () => Adapter.Capability.State == AdapterCapabilityState.Supported,
                message => throw new InvalidOperationException(message));
            Compatibility.Attach();
        }

        internal void BeginUse(string item, int runtimeId)
        {
            Adapter.BeginUse(new ItemUseSnapshot
            {
                ItemId = item, DisplayName = item, RuntimeItemId = runtimeId, SaveGenerationId = "generation-a",
                RunId = "run-a", SegmentId = "segment-a", MapId = "map-a", GameplayContext = GameplayContext.Raid,
                TimestampUtc = DateTime.UtcNow
            });
            Adapter.BeginApplication(runtimeId);
        }

        internal void CompleteUse(string item, int runtimeId) => Adapter.CompleteUse(runtimeId, new ItemUseRecorded
        {
            EventId = "use-" + runtimeId, ItemId = item, DisplayName = item, SaveGenerationId = "generation-a",
            GameplayContext = GameplayContext.Raid, Group = CanonicalItemGroup.Healing
        });

        internal void Use(string item, int runtimeId, float amount, bool split = true, float bonus = 0)
        {
            BeginUse(item, runtimeId);
            amount *= 1 + bonus;
            if (split) { amount *= 0.5f; Register(Health, amount); }
            Apply(amount);
            CompleteUse(item, runtimeId);
        }

        internal void Register(Health health, float amount)
        {
            var state = Compatibility.BeginRegistration(health);
            MedicFixture.Add(health, amount);
            Compatibility.CompleteRegistration(state);
        }

        internal void Apply(float amount)
        {
            var state = HealingHarmonyBridge.BeginHealthApplication(Health, amount);
            Health.AddHealth(amount);
            HealingHarmonyBridge.CompleteHealthApplication(Health, state);
        }

        internal void Tick()
        {
            var began = Compatibility.BeginTick();
            try
            {
                foreach (var (target, amount) in MedicFixture.Entries.ToArray())
                {
                    var scope = Compatibility.BeginQueuedApplication(target);
                    try { if (ReferenceEquals(target, Health)) Apply(amount / 10); }
                    finally { HealingHarmonyBridge.Pop(scope); }
                }
                MedicFixture.Advance();
            }
            finally { if (began) Compatibility.EndTick(); }
        }

        public void Dispose()
        {
            Compatibility.Detach(); Adapter.Dispose(); buffs.Dispose();
            MedicFixture.Clear(); HarmonyLib.Harmony.ClearAll();
        }
    }

    private static class MedicFixture
    {
        private sealed class ActiveHoT
        {
            public object healthTarget = null!;
            internal float Amount;
            internal int Ticks;
        }
        private static readonly List<ActiveHoT> _activeHoTs = new();
        internal static IEnumerable<(object Target, float Amount)> Entries => _activeHoTs.Select(value => (value.healthTarget, value.Amount));
        internal static void Add(object health, float amount) => RegisterHoT(health, amount);
        internal static void Clear() => ClearHoTs();
        internal static void Advance()
        {
            foreach (var entry in _activeHoTs) entry.Ticks++;
            _activeHoTs.RemoveAll(entry => entry.Ticks == 10);
        }
        private static void RegisterHoT(object health, float hotAmount)
        {
            _activeHoTs.RemoveAll(value => ReferenceEquals(value.healthTarget, health));
            _activeHoTs.Add(new ActiveHoT { healthTarget = health, Amount = hotAmount });
        }
        public static void TickAll() { }
        private static void ApplyHeal(object health, float amount) { }
        public static void ClearHoTs() => _activeHoTs.Clear();
    }

    private static class NaturalFixture
    {
        private static bool TryApplyHeal(Health hp, float amount) => false;
    }
}
