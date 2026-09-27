using System.Runtime.Serialization.Json;
using System.Text;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class PlayerKillFeedTests
{
    private readonly PlayerKillFeed feed = new();
    private readonly KillFeedSettings settings = new();

    [Theory]
    [InlineData(CombatOwnership.Player, true)]
    [InlineData(CombatOwnership.OtherNpc, false)]
    [InlineData(CombatOwnership.PetCompanion, false)]
    [InlineData(CombatOwnership.Environmental, false)]
    [InlineData(CombatOwnership.Unknown, false)]
    public void OnlyProvenPlayerFinalBlowsAppear(CombatOwnership ownership, bool expected)
    {
        var value = Kill("1"); value.Ownership = ownership;
        Assert.Equal(expected, feed.Add(value, 35.29, 1, settings));
        if (expected)
        {
            var entry = Assert.Single(feed.Entries);
            Assert.False(entry.PlayerDied); Assert.Equal("target", entry.ActorId); Assert.Equal(35.29, entry.Meters);
        }
    }

    [Fact]
    public void RejectsNonfatalAssistsWorldDeathsPausedBaseAndDegradedEvidence()
    {
        Assert.False(feed.Add(Kill("a") with { IsFinalBlow = false }, null, 1, settings));
        Assert.False(feed.Add(Kill("b") with { KillsByYou = 0, ObservedWorldDeaths = 1 }, null, 1, settings));
        Assert.False(feed.Add(Kill("c") with { GameplayContext = GameplayContext.Base }, null, 1, settings));
        Assert.False(feed.Add(Kill("d") with { GameplayContext = GameplayContext.Paused }, null, 1, settings));
        var degraded = Kill("e"); degraded.Capabilities.KillsByYou.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.False(feed.Add(degraded, null, 1, settings));
        Assert.Empty(feed.Entries);
    }

    [Fact]
    public void DeathReversesActorAndNeverInventsIncomingHeadshot()
    {
        var death = Kill("death") with
        {
            KillsByYou = 0,
            PlayerDeaths = 1,
            HeadshotFinalBlows = 1,
            AttackerId = "enemy",
            AttackerDisplayName = "Scavenger",
            Ownership = CombatOwnership.OtherNpc
        };
        Assert.True(feed.Add(death, 12, 1, settings));
        var entry = Assert.Single(feed.Entries);
        Assert.True(entry.PlayerDied); Assert.False(entry.Headshot); Assert.Equal("enemy", entry.ActorId);
        Assert.Equal("Scavenger", entry.ActorName); Assert.Equal(12, entry.Meters);
        Assert.False(feed.Add(death with { EventId = "later-hurt", PlayerDeaths = 0 }, 12, 1, settings));
    }

    [Fact]
    public void RequiresVerifiedFinalBlowHeadshotRatherThanEarlierHeadshots()
    {
        feed.Add(Kill("a") with { Headshots = 1 }, null, 1, settings);
        Assert.False(feed.Entries[0].Headshot);
        feed.Add(Kill("b") with { HeadshotFinalBlows = 1 }, null, 1, settings);
        Assert.True(feed.Entries[0].Headshot);
        var unavailable = Kill("c") with { HeadshotFinalBlows = 1 };
        unavailable.Capabilities.HeadshotFinalBlows.State = AdapterCapabilityState.DisabledIncompatible;
        feed.Add(unavailable, null, 1, settings);
        Assert.False(feed.Entries[0].Headshot);
    }

    [Fact]
    public void BoundedNewestFirstQueueExpiresAndDoesNotReplayDuplicates()
    {
        for (var i = 0; i < 10; i++) Assert.True(feed.Add(Kill(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), null, i, settings));
        Assert.Equal(6, feed.Entries.Count); Assert.Equal("9", feed.Entries[0].EventId); Assert.Equal("4", feed.Entries[5].EventId);
        Assert.False(feed.Add(Kill("9"), null, 9, settings));
        feed.Trim(18, settings); Assert.Single(feed.Entries);
        Assert.InRange(PlayerKillFeed.Opacity(feed.Entries[0], 18.5, 10), .49f, .51f);
        feed.Trim(19, settings); Assert.Empty(feed.Entries);
        Assert.False(feed.Add(Kill("9"), null, 20, settings));
        feed.Add(Kill("new-run") with { RunId = "another" }, null, 21, settings);
        Assert.Single(feed.Entries);
        feed.Add(Kill("new-profile") with { SaveGenerationId = "other" }, null, 21, settings);
        Assert.Equal("new-profile", Assert.Single(feed.Entries).EventId);
        feed.Trim(21, settings with { Enabled = false }); Assert.Empty(feed.Entries);
        Assert.False(feed.Add(Kill("disabled"), null, 22, settings with { Enabled = false }));
    }

    [Fact]
    public void ChangingLimitAndDurationAppliesToVisibleEntries()
    {
        for (var i = 0; i < 6; i++) feed.Add(Kill(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), null, 1, settings);
        feed.Trim(2, settings with { MaximumEntries = 2 }); Assert.Equal(2, feed.Entries.Count);
        feed.Trim(3, settings with { DurationSeconds = 2 }); Assert.Empty(feed.Entries);
    }

    [Fact]
    public void DistanceUsesHorizontalFatalPositionsAndRejectsInvalidOrCrossMapEvidence()
    {
        var a = new EncounterPosition { X = 0, Y = 50, Z = 0, MapId = "m" };
        var b = new EncounterPosition { X = 3, Y = 0, Z = 4, MapId = "duckov:map:m" };
        Assert.Equal(5, PlayerKillFeed.Distance(a, b, "m"));
        Assert.Null(PlayerKillFeed.Distance(null, b, "m"));
        Assert.Null(PlayerKillFeed.Distance(a, b, "other"));
        b.X = float.NaN; Assert.Null(PlayerKillFeed.Distance(a, b, "m"));
        feed.Add(Kill("invalid"), double.PositiveInfinity, 1, settings);
        Assert.Null(Assert.Single(feed.Entries).Meters);
    }

    [Fact]
    public void DistanceRejectsEitherActorOnAnotherMap()
    {
        var player = new EncounterPosition { X = 0, Z = 0, MapId = "m" };
        var enemy = new EncounterPosition { X = 3, Z = 4, MapId = "other" };
        Assert.Null(PlayerKillFeed.Distance(player, enemy, "m"));
        Assert.Null(PlayerKillFeed.Distance(enemy, player, "m"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    public void DistanceRequiresKnownMapForBothActors(string? missingMap, bool playerMapMissing)
    {
        var player = new EncounterPosition { X = 0, Z = 0, MapId = playerMapMissing ? missingMap : "m" };
        var enemy = new EncounterPosition { X = 3, Z = 4, MapId = playerMapMissing ? "m" : missingMap };
        Assert.Null(PlayerKillFeed.Distance(player, enemy, "m"));
    }

    [Fact]
    public void ExistingSettingsKeepHotkeyAndNewSettingsRoundTripAndNormalize()
    {
        var serializer = new DataContractJsonSerializer(typeof(UserSettings));
        using var oldStream = new MemoryStream(Encoding.UTF8.GetBytes("{\"PanelHotkey\":\"Alt+F8\"}"));
        var old = (UserSettings)serializer.ReadObject(oldStream)!;
        Assert.Equal("Alt+F8", old.PanelHotkey); Assert.Null(old.KillFeed);
        old.KillFeed = new KillFeedSettings
        {
            Enabled = false,
            MaximumEntries = 99,
            DurationSeconds = -1,
            Scale = float.NaN,
            OffsetX = -1000,
            OffsetY = 1000,
            AlignRight = true,
            ShowHeadshots = false
        }.Normalize();
        using var stream = new MemoryStream(); serializer.WriteObject(stream, old); stream.Position = 0;
        var read = (UserSettings)serializer.ReadObject(stream)!;
        Assert.Equal(old.KillFeed, read.KillFeed); Assert.Equal("Alt+F8", read.PanelHotkey);
        Assert.Equal(6, read.KillFeed!.MaximumEntries); Assert.Equal(1, read.KillFeed.DurationSeconds);
        Assert.Equal(1, read.KillFeed.Scale); Assert.Equal(-500, read.KillFeed.OffsetX); Assert.Equal(500, read.KillFeed.OffsetY);
        using var missingFields = new MemoryStream(Encoding.UTF8.GetBytes("{\"KillFeed\":{\"Enabled\":false}}"));
        var minimal = (UserSettings)serializer.ReadObject(missingFields)!;
        Assert.False(minimal.KillFeed!.Enabled); Assert.True(minimal.KillFeed.ShowHeadshots);
        Assert.Equal(10, minimal.KillFeed.DurationSeconds);
    }

    private static CombatRecorded Kill(string id) => new()
    {
        EventId = id,
        RunId = "run",
        SaveGenerationId = "generation",
        MapId = "m",
        GameplayContext = GameplayContext.Raid,
        Ownership = CombatOwnership.Player,
        TargetIsEnemy = true,
        TargetId = "target",
        TargetDisplayName = "Enemy",
        WeaponId = "duckov:weapon:1",
        IsFinalBlow = true,
        KillsByYou = 1,
        Capabilities = new()
        {
            KillsByYou = new() { State = AdapterCapabilityState.Supported },
            PlayerDeaths = new() { State = AdapterCapabilityState.Supported },
            HeadshotFinalBlows = new() { State = AdapterCapabilityState.Supported }
        }
    };
}
