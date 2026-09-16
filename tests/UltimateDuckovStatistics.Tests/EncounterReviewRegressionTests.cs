using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterAttributionRegressionTests
{
    [Theory]
    [InlineData(true, true, "effect", EncounterOutcome.PlayerKill, EncounterCredit.Player, 1)]
    [InlineData(false, true, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Other, 0)]
    [InlineData(true, false, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(false, false, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(true, null, "effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(true, null, "unscoped-effect", EncounterOutcome.OtherDeath, EncounterCredit.Unknown, 0)]
    [InlineData(true, null, "projectile", EncounterOutcome.PlayerKill, EncounterCredit.Player, 1)]
    public void EffectOwnershipControlsBothDamageAndFatalCredit(bool retainedPlayer, bool? resolved, string mechanism,
        EncounterOutcome outcome, EncounterCredit credit, int damageRecords)
    {
        var reducer = new EncounterEvidenceProjection("s");
        var data = JObject.FromObject(new
        {
            Transaction = 1, FatalSequence = 1, Target = new { Id = 2, IsMain = false },
            Source = new { Kind = mechanism, ActorCreditResolved = resolved, Credited = new { Id = 1, IsMain = retainedPlayer }, WeaponId = 42 },
            NativeCallCompleted = true, ProposedHpLossOwnedAssignments = 10
        });
        reducer.Apply("r", "m", "segment", 1, "combat_hurt_complete", data);
        reducer.Apply("r", "m", "segment", 1, "combat_fatal", data);
        var records = reducer.Flush();
        var encounter = Assert.Single(records, r => r.Encounter != null).Encounter!;
        Assert.Equal(outcome, encounter.Outcome);
        Assert.Equal(credit, encounter.FinalSource!.Credit);
        Assert.Equal(damageRecords, records.Count(r => r.Damage != null));
        if (credit == EncounterCredit.Unknown)
        {
            Assert.Null(encounter.FinalSource.CreditedActorId);
            Assert.Null(encounter.FinalSource.WeaponTypeId);
        }
    }

    [Fact]
    public void AmbiguousIncomingEffectRetainsDamageWithoutJoiningTheRetainedActor()
    {
        var reducer = new EncounterEvidenceProjection("s");
        var data = JObject.FromObject(new
        {
            Transaction = 1, Target = new { Id = 1, IsMain = true },
            Source = new { Kind = "effect", ActorCreditResolved = false,
                Physical = new { Id = 2 }, Credited = new { Id = 2 }, WeaponId = 42 },
            NativeCallCompleted = true, ProposedHpLossOwnedAssignments = 10
        });
        reducer.Apply("r", "m", "segment", 1, "combat_hurt_complete", data);
        var records = reducer.Flush();
        var damage = Assert.Single(records, r => r.Damage != null).Damage!;
        Assert.True(damage.Incoming); Assert.Equal(10, damage.Amount);
        Assert.Equal(EncounterCredit.Unknown, damage.Source.Credit);
        Assert.Null(damage.Source.PhysicalActorId); Assert.Null(damage.Source.WeaponTypeId);
        Assert.NotEqual("s/a2", Assert.Single(records, r => r.Encounter != null).Encounter!.ActorId);
    }
}
