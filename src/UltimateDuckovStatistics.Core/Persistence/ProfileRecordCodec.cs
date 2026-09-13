using System.Runtime.Serialization.Json;

namespace UltimateDuckovStatistics.Core.Persistence;

/// <summary>Independent SQLite record payloads with the existing DataContract JSON read contract.</summary>
public sealed class ProfileRecordCodec
{
    public const int Version = 1;
    private readonly Action<Stream, object, Type>? write;

    public ProfileRecordCodec(Action<Stream, object, Type>? write = null) => this.write = write;

    internal byte[] Encode(object value, Type type)
    {
        using var stream = new MemoryStream();
        if (write == null) Serializer(type).WriteObject(stream, value);
        else write(stream, value, type);
        return stream.ToArray();
    }

    public byte[] Encode<T>(T value) where T : notnull => Encode(value, typeof(T));
    public static T Decode<T>(byte[] bytes) => (T)Decode(bytes, typeof(T))!;

    internal static object? Decode(byte[] bytes, Type type)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        using var stream = new MemoryStream(bytes, writable: false);
        return Serializer(type).ReadObject(stream);
    }

    private static DataContractJsonSerializer Serializer(Type type) => new(type,
        new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
}
