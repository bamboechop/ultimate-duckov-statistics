using System.Buffers.Binary;
using System.Text;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteCompressionTests : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    private string DatabasePath => Path.Combine(directory.Path, "profile.sqlite");

    public SqliteCompressionTests() => SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));

    [Fact]
    public void PackingPreservesEveryByteAndLeavesSmallOrIncompressibleValuesAlone()
    {
        var json = Encoding.UTF8.GetBytes("{\"n\":-0.0,\"decimal\":1.0000000000000000000000000000,\"count\":9223372036854775807,\"text\":\""
            + string.Concat(Enumerable.Repeat("雪 🦆 \\\" / \\\\ \\u0000", 1000)) + "\"}");
        var packed = SqliteRecordPayload.Encode(json);
        Assert.True(packed.Length < json.Length / 4);
        Assert.Equal(json, SqliteRecordPayload.Decode(packed));
        var small = Encoding.UTF8.GetBytes("{\"n\":0}");
        Assert.Same(small, SqliteRecordPayload.Encode(small));
        Assert.Same(small, SqliteRecordPayload.Decode(small));
        var random = new byte[4096]; new Random(1234).NextBytes(random);
        Assert.Same(random, SqliteRecordPayload.Encode(random));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("negative-length")]
    [InlineData("long-length")]
    [InlineData("short-length")]
    [InlineData("truncated")]
    public void DamagedCompressedRecordsFailWithoutTrustingTheirAllocationSize(string damage)
    {
        var packed = SqliteRecordPayload.Encode(Encoding.UTF8.GetBytes(new string('x', 4096)));
        switch (damage)
        {
            case "version": packed[3] = 2; break;
            case "negative-length": BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(4, 4), -1); break;
            case "long-length": BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(4, 4), int.MaxValue); break;
            case "short-length": BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(4, 4), 1); break;
            case "truncated": packed = packed[..^4]; break;
        }
        Assert.Throws<InvalidDataException>(() => SqliteRecordPayload.Decode(packed));
    }

    [Fact]
    public async Task PreviousFormatConvertsExactlyIncludingCheckpointExportsAndReceipts()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var tracker = IncrementalCheckpointProtocolTests.Started(profile.GenerationId);
        var now = NativeProfileJsonWriterTests.Now;
        var checkpoint = tracker.CreateCheckpoint(now, 12)!;
        using (var initial = new SqliteProfileStorage(DatabasePath, codec))
        {
            initial.Import(profile, null, checkpoint);
            var journal = new ProfileChangeJournal(profile.GenerationId, codec);
            await initial.Commit(journal.Capture(profile));
        }
        ConvertFixtureToPreviousFormat();
        byte[] receipt;
        List<object[]> original;
        using (var old = new SqliteStore(DatabasePath, true))
        {
            receipt = old.Blob("SELECT digest FROM receipt")!;
            original = old.Rows("SELECT ordinal,kind,k1,k2,k3,payload,payload_sha FROM records ORDER BY ordinal");
        }
        var expectedExport = ProfileExportWriter.Write(profile, Path.Combine(directory.Path, "baseline", "profile.json"), now);
        using (var upgraded = new SqliteProfileStorage(DatabasePath, codec))
        {
            var state = upgraded.Load()!;
            Assert.Equal(codec.Encode(profile), codec.Encode(state.Profile));
            Assert.Equal(codec.Encode(checkpoint), codec.Encode(state.Checkpoint!));
            using var snapshot = await upgraded.CaptureExport(profile.GenerationId, profile.Revision);
            var exported = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory.Path, "export"), now);
            Assert.Equal(37, exported.Files.Count);
            foreach (var file in expectedExport.Files)
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(exported.Directory, Path.GetFileName(file))));
        }
        using var db = new SqliteStore(DatabasePath, true);
        Assert.Equal(7, db.ScalarLong("PRAGMA user_version"));
        Assert.Equal("ok", db.ScalarText("PRAGMA integrity_check"));
        Assert.Equal(receipt, db.Blob("SELECT digest FROM receipt"));
        var rows = db.Rows("SELECT ordinal,kind,k1,k2,k3,payload,payload_sha FROM records ORDER BY ordinal");
        Assert.Equal(original.Count, rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            Assert.Equal(original[index].Take(5), rows[index].Take(5));
            Assert.Equal((byte[])original[index][5], SqliteRecordPayload.Decode((byte[])rows[index][5]));
            Assert.Equal((byte[])original[index][6], (byte[])rows[index][6]);
        }
        Assert.True(rows.Sum(row => ((byte[])row[5]).Length) < original.Sum(row => ((byte[])row[5]).Length));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedConversionRollsBackPayloadsAndVersionAndCanRetry(bool badChecksum)
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using (var initial = new SqliteProfileStorage(DatabasePath, codec)) initial.Import(profile, null, null);
        ConvertFixtureToPreviousFormat();
        using var fault = new SqliteStore(DatabasePath);
        var original = fault.Rows("SELECT ordinal,payload FROM records ORDER BY ordinal");
        var checksum = fault.Blob("SELECT payload_sha FROM records WHERE kind=15")!;
        if (badChecksum) fault.Exec("UPDATE records SET payload_sha=? WHERE kind=15", new byte[32]);
        else fault.Exec("CREATE TRIGGER fail_conversion BEFORE UPDATE ON records WHEN old.kind=15 BEGIN SELECT RAISE(ABORT,'conversion failure'); END");
        var storage = new SqliteProfileStorage(DatabasePath, codec);
        if (badChecksum) Assert.Throws<InvalidDataException>(() => storage.Load());
        else Assert.Throws<SqliteFailure>(() => storage.Load());
        Assert.Equal(6, fault.ScalarLong("PRAGMA user_version"));
        var after = fault.Rows("SELECT ordinal,payload FROM records ORDER BY ordinal");
        Assert.Equal(original.Count, after.Count);
        for (var index = 0; index < original.Count; index++) Assert.Equal((byte[])original[index][1], (byte[])after[index][1]);
        if (badChecksum) fault.Exec("UPDATE records SET payload_sha=? WHERE kind=15", checksum);
        else fault.Exec("DROP TRIGGER fail_conversion");
        if (badChecksum) Assert.Throws<InvalidDataException>(() => storage.Dispose());
        else Assert.Throws<SqliteFailure>(() => storage.Dispose());
        using var retry = new SqliteProfileStorage(DatabasePath, codec);
        Assert.Equal(codec.Encode(profile), codec.Encode(retry.Load()!.Profile));
    }

    [Fact]
    public async Task RoutineSaveCannotRewriteCompletedRunsOrRepeatConversion()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var tracker = IncrementalCheckpointProtocolTests.Started(profile.GenerationId, route: true);
        var run = tracker.Apply(new()
        {
            Kind = Core.Tracking.RunLifecycleEventKind.Extracted,
            TimestampUtc = new DateTime(2026, 8, 10, 10, 0, 10, DateTimeKind.Utc),
            MonotonicSeconds = 10
        }).Completed!;
        Assert.True(RunReducer.Apply(profile.Statistics, run));
        using (var initial = new SqliteProfileStorage(DatabasePath, codec)) initial.Import(profile, null, null);
        ConvertFixtureToPreviousFormat();
        using var storage = new SqliteProfileStorage(DatabasePath, codec);
        var loaded = storage.Load()!.Profile;
        Assert.Equal(codec.Encode(run), codec.Encode(loaded.Statistics.Runs.Single()));
        using var observer = new SqliteStore(DatabasePath);
        var payload = observer.Blob("SELECT payload FROM records WHERE kind=17")!;
        Assert.Equal((byte)'U', payload[0]);
        observer.Exec("CREATE TRIGGER forbid_history_rewrite BEFORE UPDATE ON records WHEN old.kind=17 BEGIN SELECT RAISE(ABORT,'history rewritten'); END");
        profile.Revision++;
        profile.Statistics.BaseMovement = new() { RecordedMeters = 1.125, CollectionStartedUtc = profile.CreatedUtc };
        var journal = new ProfileChangeJournal(profile.GenerationId, codec); journal.BaseMovement();
        var command = journal.Capture(profile);
        await storage.Commit(command); journal.Acknowledge(command);
        await storage.Commit(command);
        Assert.Equal(payload, observer.Blob("SELECT payload FROM records WHERE kind=17"));
        storage.Dispose();
        using var reopened = new SqliteProfileStorage(DatabasePath, codec);
        Assert.Equal(codec.Encode(profile), codec.Encode(reopened.Load()!.Profile));
    }

    [Fact]
    public void ChecksumsStillRejectValidCompressedButChangedContent()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using (var initial = new SqliteProfileStorage(DatabasePath, codec)) initial.Import(profile, null, null);
        using (var corruptor = new SqliteStore(DatabasePath))
        {
            var bytes = SqliteRecordPayload.Decode(corruptor.Blob("SELECT payload FROM records WHERE kind=1")!);
            var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("writer-generation", "other-generation", StringComparison.Ordinal));
            corruptor.Exec("UPDATE records SET payload=? WHERE kind=1", SqliteRecordPayload.Encode(changed));
        }
        using var storage = new SqliteProfileStorage(DatabasePath, codec);
        Assert.Throws<InvalidDataException>(() => storage.Load());
        Assert.Throws<InvalidDataException>(() => storage.Dispose());
    }

    // Reproduce the immediate predecessor's exact raw-payload representation;
    // the real captured pre-change database is qualified separately on copies.
    private void ConvertFixtureToPreviousFormat()
    {
        using var db = new SqliteStore(DatabasePath);
        db.Configure("DELETE");
        foreach (var row in db.Rows("SELECT ordinal,payload FROM records"))
            db.Exec("UPDATE records SET payload=? WHERE ordinal=?", SqliteRecordPayload.Decode((byte[])row[1]), row[0]);
        db.Exec("PRAGMA user_version=6");
    }

    public void Dispose() => directory.Dispose();
}
