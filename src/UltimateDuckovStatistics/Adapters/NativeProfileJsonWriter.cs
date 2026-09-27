using System.Globalization;
using System.Runtime.Serialization.Json;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeProfileJsonWriter
{
    // Only contract metadata is shared; every write owns its serializer,
    // converter and stream wrappers. Game/mod JsonConvert defaults are ignored.
    private static readonly DefaultContractResolver contractResolver = new();

    public static void Write(Stream stream, ProfileDocument profile)
        => WriteRecord(stream, profile, typeof(ProfileDocument));

    internal static void WriteRecord(Stream stream, object value, Type type)
    {
        using var text = new StreamWriter(stream, new UTF8Encoding(false, true), 4096, leaveOpen: true);
        using var writer = new DataContractNumberWriter(text) { CloseOutput = false };
        var serializer = new JsonSerializer
        {
            ContractResolver = contractResolver,
            Culture = CultureInfo.InvariantCulture,
            DateFormatHandling = DateFormatHandling.MicrosoftDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            FloatFormatHandling = FloatFormatHandling.Symbol,
            TypeNameHandling = TypeNameHandling.None
        };
        serializer.Converters.Add(new DataContractDateConverter());
        serializer.Serialize(writer, value, type);
        writer.Flush();
        text.Flush();
    }

    private sealed class DataContractDateConverter : JsonConverter
    {
        private readonly DataContractJsonSerializer serializer = new(typeof(DateTime));

        public override bool CanConvert(Type type) => type == typeof(DateTime) || type == typeof(DateTime?);
        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer jsonSerializer) =>
            throw new NotSupportedException();

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer jsonSerializer)
        {
            if (value == null) { writer.WriteNull(); return; }
            // Preserve DCS millisecond/kind encoding and extreme-date rejection
            // on the running platform instead of duplicating its date rules.
            using var stream = new MemoryStream();
            serializer.WriteObject(stream, value);
            writer.WriteRawValue(Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length)));
        }
    }

    private sealed class DataContractNumberWriter(TextWriter writer) : JsonTextWriter(writer)
    {
        // JsonTextWriter adds ".0" to integral decimals. DCS preserves their
        // existing scale, which must survive a read through the unchanged reader.
        public override void WriteValue(decimal value) => WriteRawValue(value.ToString(CultureInfo.InvariantCulture));

        // Unity Mono's DCS reader takes a short-decimal fast path that can move a
        // double by one ULP (29.235182 is a captured example). An exponent selects
        // its ordinary round-trip parser without changing the numeric JSON value.
        // G17/G9 also avoid Mono R formatting that can round-trip only on Mono,
        // but decode to adjacent bits on modern .NET. Keep the full source precision
        // across readers and keep exact equality in encounter-prefix validation.
        public override void WriteValue(double value)
        {
            if (!double.IsFinite(value)) { base.WriteValue(value); return; }
            WriteFloating(value == 0 && BitConverter.DoubleToInt64Bits(value) < 0
                ? "-0" : value.ToString("G17", CultureInfo.InvariantCulture));
        }

        public override void WriteValue(float value)
        {
            if (!float.IsFinite(value)) { base.WriteValue(value); return; }
            WriteFloating(value == 0 && BitConverter.SingleToInt32Bits(value) < 0
                ? "-0" : value.ToString("G9", CultureInfo.InvariantCulture));
        }

        public override void WriteValue(double? value)
        { if (value.HasValue) WriteValue(value.Value); else WriteNull(); }

        public override void WriteValue(float? value)
        { if (value.HasValue) WriteValue(value.Value); else WriteNull(); }

        private void WriteFloating(string value) => WriteRawValue(
            value.Contains('E') || value.Contains('e') ? value : value + "e0");
    }
}
