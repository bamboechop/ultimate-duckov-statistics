using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class EncounterActorIdentityTests
{
    [Fact]
    public void DestroyedShooterRemainsAssociatedWithItsProjectileDamageAndPlayerDeath()
    {
        var player = new CharacterMainControl { IsMainCharacter = true };
        var shooter = new CharacterMainControl { characterPreset = new() { nameKey = "Scavenger" } };
        CharacterMainControl.Main = player;
        try
        {
            var sink = new ActorSink();
            var observer = new NativeEncounterCombatObserver(sink);
            var playerLabel = observer.ReadActorIdentity(player);
            var launchedShooter = observer.ReadActorIdentity(shooter);
            var reducer = new EncounterEvidenceProjection("s");
            reducer.Apply("run", "map", "segment", 1, "combat_fatal", JObject.FromObject(new
            {
                FatalSequence = 1, Target = launchedShooter,
                Source = new { Kind = "projectile", Physical = playerLabel, Credited = playerLabel }
            }));

            UnityEngine.Object.Destroy(shooter);
            Assert.True(shooter == null); // Native null semantics; the managed reference survives.
            Assert.False(ReferenceEquals(shooter, null));
            shooter!.characterPreset = null;
            var impactShooter = observer.ReadActorIdentity(shooter);
            Assert.Same(launchedShooter, impactShooter);
            Assert.Equal(2, sink.Reads);
            var impact = JObject.FromObject(new
            {
                Transaction = 2, FatalSequence = 2, Target = playerLabel,
                Source = new { Kind = "projectile", Physical = impactShooter, Credited = impactShooter, WeaponId = 42 },
                NativeCallCompleted = true, ProposedHpLossOwnedAssignments = 10,
                Candidate = new
                {
                    PlayerPosition = new { Available = true, X = 1f, Y = 0f, Z = 2f, LogicalScene = "map" },
                    SourcePosition = new { Available = false }
                }
            });
            reducer.Apply("run", "map", "segment", 2, "combat_hurt_complete", impact);
            reducer.Apply("run", "map", "segment", 2, "combat_fatal", impact);
            var records = reducer.Flush();
            var kill = Assert.Single(records, r => r.Encounter?.Outcome == EncounterOutcome.PlayerKill).Encounter!;
            var death = Assert.Single(records, r => r.Encounter?.Outcome == EncounterOutcome.PlayerDeath).Encounter!;
            Assert.Equal(kill.ActorId, death.ActorId);
            Assert.Equal("Scavenger", death.EnemyPresetKey);
            Assert.Equal(kill.ActorId, death.FinalSource!.PhysicalActorId);
            Assert.Null(death.EnemyPosition); // Known identity does not imply a live position.
            Assert.NotNull(death.PlayerPosition);
            var damage = Assert.Single(records, r => r.Damage != null).Damage!;
            Assert.True(damage.Incoming); Assert.Equal(10, damage.Amount);
            Assert.Equal(kill.ActorId, damage.Source.CreditedActorId);
        }
        finally { CharacterMainControl.ResetNativeState(); }
    }

    [Fact]
    public void UnobservedDestroyedAndNullActorsStayUnknownAndDistinctObjectsStayDistinct()
    {
        var sink = new ActorSink(); var observer = new NativeEncounterCombatObserver(sink);
        var dead = new CharacterMainControl(); UnityEngine.Object.Destroy(dead);
        Assert.Equal(0, (int)JObject.FromObject(observer.ReadActorIdentity(dead))["Id"]!);
        Assert.Equal(0, (int)JObject.FromObject(observer.ReadActorIdentity(null))["Id"]!);
        Assert.Equal(0, sink.Reads);
        var first = observer.ReadActorIdentity(new CharacterMainControl { characterPreset = new() { nameKey = "Scavenger" } });
        var second = observer.ReadActorIdentity(new CharacterMainControl { characterPreset = new() { nameKey = "Scavenger" } });
        Assert.NotEqual((int)JObject.FromObject(first)["Id"]!, (int)JObject.FromObject(second)["Id"]!);
    }

    private sealed class ActorSink : IEncounterObservationSink
    {
        public int Reads { get; private set; }
        public EncounterObservationContext Context { get; } = new();
        public void Record(string eventKind, object payload) { }
        public int ActorId(UnityEngine.Object actor)
        {
            Assert.False(actor is CharacterMainControl character && character == null);
            return ++Reads;
        }
    }
}
