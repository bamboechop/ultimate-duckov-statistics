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
        using var writer = new DecimalScaleWriter(text) { CloseOutput = false };
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

    private sealed class DecimalScaleWriter(TextWriter writer) : JsonTextWriter(writer)
    {
        // JsonTextWriter adds ".0" to integral decimals. DCS preserves their
        // existing scale, which must survive a read through the unchanged reader.
        public override void WriteValue(decimal value) => WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
