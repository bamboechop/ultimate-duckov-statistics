using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Diagnostics;

[DataContract]
public sealed class DiagnosticEntry
{
    [DataMember(Order = 1)]
    public DateTime TimestampUtc { get; set; }

    [DataMember(Order = 2)]
    public string Severity { get; set; } = "Info";

    [DataMember(Order = 3)]
    public string Message { get; set; } = string.Empty;
}

[DataContract]
public sealed class DiagnosticsDocument
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public bool RawEventTraceEnabled { get; set; }

    [DataMember(Order = 3)]
    public List<DiagnosticEntry> Entries { get; set; } = new();
}

public sealed class DiagnosticStore
{
    private readonly int capacity;
    private readonly string path;
    private readonly Func<DateTime> utcNow;
    private readonly AtomicJsonStore<DiagnosticsDocument> store = new();
    private readonly DiagnosticsDocument document;

    public DiagnosticStore(string path, int capacity, Func<DateTime> utcNow)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
        this.capacity = capacity;
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        document = store.Load(this.path).Value ?? new DiagnosticsDocument();
        document.RawEventTraceEnabled = false;
        document.Entries ??= new List<DiagnosticEntry>();
        Trim();
    }

    public IReadOnlyList<DiagnosticEntry> Entries => document.Entries;

    public bool RawEventTraceEnabled => document.RawEventTraceEnabled;

    public void Add(string message, string severity = "Info")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        document.Entries.Add(new DiagnosticEntry
        {
            TimestampUtc = EnsureUtc(utcNow()),
            Severity = string.IsNullOrWhiteSpace(severity) ? "Info" : severity,
            Message = message
        });
        Trim();
        store.Save(path, document);
    }

    private void Trim()
    {
        while (document.Entries.Count > capacity)
        {
            document.Entries.RemoveAt(0);
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : value.ToUniversalTime();
}
