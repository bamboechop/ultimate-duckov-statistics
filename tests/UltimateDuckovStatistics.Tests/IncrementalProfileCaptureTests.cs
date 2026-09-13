using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class IncrementalProfileCaptureTests
{
    private static ProfileRecordCodec Codec() => new(NativeProfileJsonWriter.WriteRecord);
    [Fact]
    public void RecordCodecPreservesPopulatedProfileThroughTheUnchangedJsonReader()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var encoded = Codec().Encode(profile);
        var decoded = ProfileRecordCodec.Decode<ProfileDocument>(encoded);
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(decoded));
        Assert.Equal(Json(profile), Json(decoded));
    }

    [Fact]
    public void CodecRetainsExactNumericBitsPublicDatePrecisionAndTextAcrossIndependentWrites()
    {
        var value = new ExactValues
        {
            Decimal = new decimal(123, 0, 0, false, 19),
            Double = BitConverter.Int64BitsToDouble(long.MinValue),
            Timestamp = DateTime.SpecifyKind(new DateTime(2000, 1, 1).AddTicks(1234567), DateTimeKind.Local),
            Text = "雪\u0000Grüße😀\"\\"
        };
        var codec = Codec();
        var copy = ProfileRecordCodec.Decode<ExactValues>(codec.Encode(value));
        Assert.Equal(decimal.GetBits(value.Decimal), decimal.GetBits(copy.Decimal));
        Assert.Equal(BitConverter.DoubleToInt64Bits(value.Double), BitConverter.DoubleToInt64Bits(copy.Double));
        Assert.Equal(value.Timestamp.Ticks - value.Timestamp.Ticks % TimeSpan.TicksPerMillisecond, copy.Timestamp.Ticks);
        Assert.Equal(value.Timestamp.Kind, copy.Timestamp.Kind);
        Assert.Equal(value.Text, copy.Text);
        Assert.Equal(codec.Encode(value), codec.Encode(value));
    }

    [Fact]
    public async Task CodecSessionsDoNotShareMutableStateAcrossConcurrentRecords()
    {
        var tasks = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            var value = new ExactValues { Decimal = index / 100m, Double = index + 0.125, Text = index + "雪", Timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            return (Original: value, Copy: ProfileRecordCodec.Decode<ExactValues>(Codec().Encode(value)));
        }));
        foreach (var pair in await Task.WhenAll(tasks))
        {
            Assert.Equal(decimal.GetBits(pair.Original.Decimal), decimal.GetBits(pair.Copy.Decimal));
            Assert.Equal(BitConverter.DoubleToInt64Bits(pair.Original.Double), BitConverter.DoubleToInt64Bits(pair.Copy.Double));
            Assert.Equal(pair.Original.Text, pair.Copy.Text);
        }
    }

    [Fact]
    public void AnOldAcknowledgementCannotClearANewerBaseDistance()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var journal = new ProfileChangeJournal(profile.GenerationId);
        profile.Statistics.BaseMovement = new BaseMovementStatistics { RecordedMeters = 10, CollectionStartedUtc = profile.CreatedUtc };
        journal.BaseMovement();
        var older = journal.Capture(profile);
        profile.Statistics.BaseMovement.RecordedMeters = 20;
        profile.Revision++;
        journal.BaseMovement();
        journal.Acknowledge(older);
        var newer = journal.Capture(profile);
        Assert.Equal(10, ReadBase(older).RecordedMeters);
        Assert.Equal(20, ReadBase(newer).RecordedMeters);
        journal.Acknowledge(newer);
        Assert.DoesNotContain(journal.Capture(profile).Records, record => record.Address.Kind == ProfileRecordKind.BaseMovement);
    }

    [Fact]
    public void DroppedCaptureIsIncludedWithLaterChangedFactsUntilAcknowledged()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var journal = new ProfileChangeJournal(profile.GenerationId);
        profile.Statistics.BaseMovement = new BaseMovementStatistics { RecordedMeters = 11, CollectionStartedUtc = profile.CreatedUtc };
        journal.BaseMovement();
        _ = journal.Capture(profile);
        profile.Statistics.WorldTime.CompletedSleepSessions++;
        journal.WorldTime();
        var next = journal.Capture(profile);
        Assert.Equal(11, ReadBase(next).RecordedMeters);
        Assert.Contains(next.Records, record => record.Address.Kind == ProfileRecordKind.WorldTime);
    }

    [Fact]
    public void CapturedSessionValueSurvivesALaterProfileOnlyRetry()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var journal = new ProfileChangeJournal(profile.GenerationId);
        var session = new SessionCheckpoint { GenerationId = profile.GenerationId, SessionId = "owned-session", StartedUtc = profile.CreatedUtc };
        _ = journal.Capture(profile, session, sessionChanged: true);
        session.SessionId = "mutated-after-capture";
        var retry = journal.Capture(profile);
        var captured = retry.Records.Single(record => record.Address.Kind == ProfileRecordKind.Session);
        Assert.Equal("owned-session", ProfileRecordCodec.Decode<SessionCheckpoint>(captured.Bytes!).SessionId);
    }

    [Fact]
    public void InvalidChangedBaseCannotBeCapturedAndAnotherGenerationCannotAcknowledge()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var journal = new ProfileChangeJournal(profile.GenerationId);
        profile.Statistics.BaseMovement = new BaseMovementStatistics { RecordedMeters = double.NaN, CollectionStartedUtc = profile.CreatedUtc };
        journal.BaseMovement();
        Assert.Throws<ArgumentException>(() => journal.Capture(profile));
        profile.Statistics.BaseMovement.RecordedMeters = 1;
        var captured = journal.Capture(profile);
        Assert.Throws<InvalidOperationException>(() => new ProfileChangeJournal(profile.GenerationId).Acknowledge(captured));
    }

    private static BaseMovementStatistics ReadBase(IncrementalProfileWrite write) => ProfileRecordCodec.Decode<BaseMovementStatistics>(
        write.Records.Single(record => record.Address.Kind == ProfileRecordKind.BaseMovement).Bytes!);
    private static byte[] Json(ProfileDocument profile)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(ProfileDocument), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(stream, profile);
        return stream.ToArray();
    }
    [DataContract]
    private sealed class ExactValues
    {
        [DataMember] public decimal Decimal { get; set; }
        [DataMember] public double Double { get; set; }
        [DataMember] public DateTime Timestamp { get; set; }
        [DataMember] public string Text { get; set; } = "";
    }
}
