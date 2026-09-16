using System.IO.Compression;
using Newtonsoft.Json;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterRunPresentationTests
{
    private static readonly string[] VisitMaps = ["duckov:map:Level_GroundZero_1", "duckov:map:Level_HiddenWarehouse",
        "duckov:map:Level_HiddenWarehouse_CellarUnderGround", "duckov:map:Level_HiddenWarehouse"];
    private static readonly int[] VisitNumbers = [1, 1, 1, 2], VisitCounts = [1, 2, 1, 2], EventCounts = [4, 4, 0, 2];
    private static readonly int[] EventVisits = [0, 0, 0, 0, 1, 1, 1, 1, 3, 3], FirstWarehouseNumbers = [5, 6, 7, 8], SecondWarehouseNumbers = [9, 10];
    private static EncounterRecord[] NativeVisits()
    {
        var assembly = typeof(EncounterRunPresentationTests).Assembly;
        using var resource = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith("warehouse-visits.records.json.gz", StringComparison.Ordinal)))!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var records = JsonConvert.DeserializeObject<EncounterRecord[]>(reader.ReadToEnd())!;
        EncounterRecordValidation.ValidateHistory(records);
        return records;
    }

    [Fact]
    public void NativeWarehouseRevisitKeepsFourRouteSelectionsAndOneChronologicalFeed()
    {
        // Scramble storage order: numbering must follow event time, not record insertion.
        var records = NativeVisits().Reverse().ToArray();
        var run = StoredEncounterRun.Build(records);
        Assert.Equal(VisitMaps, run.Visits.Select(visit => visit.MapId));
        Assert.Equal(VisitNumbers, run.Visits.Select(visit => visit.MapVisitNumber));
        Assert.Equal(VisitCounts, run.Visits.Select(visit => visit.MapVisitCount));
        Assert.Equal(EventCounts, run.Visits.Select(visit => visit.Events.Length));
        Assert.Equal(EventVisits, Enumerable.Range(0, 10).Select(run.VisitIndex));
        Assert.Equal(FirstWarehouseNumbers, run.Visits[1].Events.Select(record => run.EventIndex(record.Id) + 1));
        Assert.Equal(SecondWarehouseNumbers, run.Visits[3].Events.Select(record => run.EventIndex(record.Id) + 1));
        Assert.Null(run.Visits[2].Calibration); Assert.NotEmpty(run.Visits[1].Strokes); Assert.NotEmpty(run.Visits[3].Strokes);
        foreach (var visit in run.Visits)
        {
            Assert.All(visit.Records.Where(record => record.Route != null), record => Assert.Equal(visit.VisitId, record.VisitId));
            Assert.All(visit.Events, record => Assert.Equal(visit.VisitId, record.Encounter!.OutcomeVisitId));
        }
    }

    [Fact]
    public void SelectingFromWholeFeedSwitchesExactVisitAndClosingKeepsThatVisit()
    {
        var state = new EncounterRunSelection(StoredEncounterRun.Build(NativeVisits()));
        var originalFeed = state.Run.Events;
        state.SelectVisit(2); // The no-art Cellar still has the whole run's list.
        Assert.Equal(10, state.Run.Events.Length); Assert.Null(state.CurrentVisit!.Calibration);
        state.ToggleEvent(8);
        Assert.Equal(3, state.VisitIndex); Assert.Equal(8, state.EventIndex);
        Assert.Same(originalFeed[8], state.CurrentEvent);
        state.ToggleEvent(8);
        Assert.Equal(3, state.VisitIndex); Assert.Null(state.CurrentEvent);
        // A numbered marker and a list row resolve through the same global index.
        var markerEvent = state.Run.Visits[1].Events[0];
        state.ToggleEvent(state.Run.EventIndex(markerEvent.Id));
        Assert.Equal(1, state.VisitIndex); Assert.Equal(4, state.EventIndex); Assert.Same(markerEvent, state.CurrentEvent);
        state.SelectVisit(0);
        Assert.Null(state.CurrentEvent); Assert.Same(originalFeed, state.Run.Events);
    }

    [Fact]
    public void FatalVisitWinsOverFirstContactAndMissingArtworkDoesNotHideTheEvent()
    {
        var records = NativeVisits();
        var enemy = records.First(record => record.Encounter?.Outcome != null);
        var cellar = records.Single(record => record.Visit?.MapId.EndsWith("CellarUnderGround", StringComparison.Ordinal) == true);
        enemy.Encounter!.OutcomeVisitId = cellar.Id;
        enemy.Encounter.EndedSeconds = cellar.Visit!.StartedSeconds + 5;
        var state = new EncounterRunSelection(StoredEncounterRun.Build(records));
        state.ToggleEvent(state.Run.EventIndex(enemy.Id));
        Assert.Equal(2, state.VisitIndex); Assert.Null(state.CurrentVisit!.Calibration); Assert.Same(enemy, state.CurrentEvent);
        Assert.Contains(state.Run.Records, record => record.EncounterId == enemy.Id && record.Damage != null);
    }

    [Fact]
    public void EmptyRunAndInvalidSelectionsHaveNoDanglingEvent()
    {
        var state = new EncounterRunSelection(StoredEncounterRun.Empty);
        state.ToggleEvent(0); state.SelectVisit(0); state.ToggleEvent(-1);
        Assert.Null(state.CurrentVisit); Assert.Null(state.CurrentEvent); Assert.Empty(state.Run.Events);
    }

    [Theory]
    [InlineData("effect")]
    [InlineData("unscoped-effect")]
    public void EffectDamageDoesNotPretendToBeAnAmmunitionHit(string mechanism)
    {
        var damage = new EncounterDamage { Amount = 1, Hits = 1,
            Source = new EncounterSource { Mechanism = mechanism, WeaponTypeId = 256 } };
        var parts = EncounterDamageText.Parts(damage, id => { Assert.Equal(256, id); return "MMG"; }, Text);
        Assert.Equal("MMG · Effect damage", string.Join(" · ", parts.Select(part => part.Text)));
        Assert.Equal(256, parts[0].Icon); Assert.Null(parts[1].Icon);
    }

    [Fact]
    public void MissingProjectileAmmunitionRemainsExplicitAndMeleeHasItsOwnLabel()
    {
        var damage = new EncounterDamage { Amount = 12, Hits = 2, Source = new EncounterSource { Mechanism = "projectile", WeaponTypeId = 736 } };
        string Item(int? id) => id.HasValue ? "Weapon" : "Unavailable";
        var parts = EncounterDamageText.Parts(damage, Item, Text);
        Assert.Equal("Weapon · Unavailable · in 2 hits (0 headshots)", string.Join(" · ", parts.Select(part => part.Text)));
        Assert.Null(parts[1].Icon);
        damage.Source.Mechanism = "melee";
        Assert.Equal("Weapon · Melee damage · in 2 hits", string.Join(" · ", EncounterDamageText.Parts(damage, Item, Text).Select(part => part.Text)));
    }

    [Theory]
    [InlineData(false, "projectile", 1, 1, "in 1 hit (1 headshot)")]
    [InlineData(false, "projectile", 2, 1, "in 2 hits (1 headshot)")]
    [InlineData(false, "projectile", 2, 2, "in 2 hits (2 headshots)")]
    [InlineData(true, "projectile", 1, 0, "in 1 hit")]
    [InlineData(true, "projectile", 2, 1, "in 2 hits")]
    [InlineData(false, "melee", 1, 0, "in 1 hit")]
    [InlineData(false, "unscoped", 1, 0, "in 1 hit")]
    public void InlineHitSummaryUsesSingularAndOnlySupportedHeadshotEvidence(bool incoming, string mechanism, long hits, long headshots, string expected)
    {
        var damage = new EncounterDamage { Incoming = incoming, Hits = hits, Headshots = headshots,
            Source = new EncounterSource { Mechanism = mechanism } };
        var parts = EncounterDamageText.Parts(damage, _ => "Item", Text);
        Assert.Equal(expected, parts[parts.Count - 1].Text);
    }

    private static string Text(string key) => key switch
    {
        "ui.encounters_effect_damage" => "Effect damage", "ui.encounters_melee_damage" => "Melee damage",
        "ui.encounters_hit_one" => "in {0} hit", "ui.encounters_hit_count" => "in {0} hits",
        "ui.encounters_headshot_one" => "{0} headshot", "ui.encounters_headshot_count" => "{0} headshots",
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
}
