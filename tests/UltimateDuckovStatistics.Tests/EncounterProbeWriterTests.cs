using UltimateDuckovStatistics.Encounters.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterDiagnosticWriterTests
{
    [Fact]
    public async Task ByteBudgetStopsCaptureWithAnIncompleteFooter()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "limited.jsonl");
        var writer = new EncounterDiagnosticWriter(path, JsonConvert.SerializeObject, maximumBytes: 2048);
        Assert.True(writer.TryRecord(new { Payload = new string('x', 4096) }));
        await writer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(writer.LimitReached);
        Assert.Null(writer.Failure);
        var footer = JObject.Parse(File.ReadAllText(path));
        Assert.False((bool)footer["Complete"]!);
        Assert.Equal(1, (int)footer["Dropped"]!);
        Assert.True(new FileInfo(path).Length <= 2048);
    }

    [Fact]
    public async Task StopDrainsOrderedDetachedRecordsAndClosesFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "observations.jsonl");
        var writer = new EncounterDiagnosticWriter(path, JsonConvert.SerializeObject);
        for (var i = 0; i < 20; i++) Assert.True(writer.TryRecord(new { Sequence = i }));
        writer.Stop();
        await writer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        writer.Stop();
        Assert.False(writer.TryRecord(new { Sequence = 20 }));
        Assert.Null(writer.Failure);
        var rows = File.ReadAllLines(path).Select(JObject.Parse).ToArray();
        Assert.Equal(21, rows.Length);
        Assert.Equal(Enumerable.Range(0, 20), rows.Take(20).Select(row => (int)row["Sequence"]!));
        Assert.Equal("capture-footer", (string?)rows[^1]["Kind"]);
        Assert.True((bool)rows[^1]["Complete"]!);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task SlowSerializerDoesNotBlockProducerAndOverflowIsExplicit()
    {
        using var directory = new TemporaryDirectory();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = Path.Combine(directory.Path, "observations.jsonl");
        var writer = new EncounterDiagnosticWriter(path, value =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test serializer was not released.");
            return JsonConvert.SerializeObject(value);
        }, capacity: 1);
        try
        {
            Assert.True(writer.TryRecord(new { Sequence = 1 }));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(writer.TryRecord(new { Sequence = 2 }));
            Assert.False(writer.TryRecord(new { Sequence = 3 }));
            Assert.Equal(1, writer.Dropped);
        }
        finally { writer.Stop(); release.Set(); await writer.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
        var footer = JObject.Parse(File.ReadLines(path).Last());
        Assert.False((bool)footer["Complete"]!);
        Assert.Equal(2, writer.Written);
    }

    [Fact]
    public async Task ExistingEvidenceIsNeverOverwritten()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "existing.jsonl");
        File.WriteAllText(path, "original evidence");
        var writer = new EncounterDiagnosticWriter(path, JsonConvert.SerializeObject);
        await writer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(writer.Failure);
        Assert.False(writer.TryRecord(new { Sequence = 1 }));
        Assert.Equal("original evidence", File.ReadAllText(path));
        writer.Stop();
    }

    [Fact]
    public async Task SerializerFailureIsReportedWithoutUnhandledWorkerException()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "failed.jsonl");
        var writer = new EncounterDiagnosticWriter(path, _ => throw new InvalidDataException("bad detached payload"));
        Assert.True(writer.TryRecord(new { Sequence = 1 }));
        await writer.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("InvalidDataException", writer.Failure, StringComparison.Ordinal);
        Assert.False(writer.TryRecord(new { Sequence = 2 }));
        Assert.Empty(File.ReadAllText(path));
        writer.Stop();
    }
}
