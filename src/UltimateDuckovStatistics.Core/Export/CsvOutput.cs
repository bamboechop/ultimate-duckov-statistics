using System.Text;

namespace UltimateDuckovStatistics.Core.Export;

// Shared row formatting for the legacy string API and bounded streaming exports.
internal sealed class CsvOutput
{
    private readonly TextWriter? writer;
    private readonly StringBuilder? buffer;
    private bool skipFirstLine;
    internal CsvOutput(TextWriter? writer = null, bool skipFirstLine = false) { this.skipFirstLine = skipFirstLine; this.writer = writer; if (writer == null) buffer = new StringBuilder(); }
    internal CsvOutput Append(object? value)
    {
        if (skipFirstLine)
        {
            var text = value?.ToString();
            var newline = text?.IndexOf('\n') ?? -1;
            if (newline < 0) return this;
            skipFirstLine = false;
            value = text!.Substring(newline + 1);
        }
        if (writer == null) buffer!.Append(value); else writer.Write(value?.ToString()); return this;
    }
    internal CsvOutput AppendLine(string? value = null)
    { if (skipFirstLine) { skipFirstLine = false; return this; } if (writer == null) buffer!.AppendLine(value); else writer.WriteLine(value); return this; }
    public override string ToString() => buffer?.ToString() ?? string.Empty;
}
