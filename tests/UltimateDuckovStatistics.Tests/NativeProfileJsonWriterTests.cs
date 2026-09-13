using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class NativeProfileJsonWriterTestGroup
{
    public const string CollectionName = "Profile JSON writer";
}

[Collection(NativeProfileJsonWriterTestGroup.CollectionName)]
public sealed class NativeProfileJsonWriterTests
{
    internal static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] QualifiedLibraryHashes =
    [
        "9e7ee62a4aecdb5cacbc648894c36d9d073748a7cd11f6a2d1bce83d368d652f", // Pinned NuGet netstandard2.0.
        "a56146202232958f46bd6a28b5a7da166aea123ee0d646735a46e5c341dfbf1f"  // Installed Duckov 2.3.30.
    ];

    [Fact]
    public void TestsUseQualifiedNewtonsoftVersionAndTarget()
    {
        var assembly = typeof(JsonSerializer).Assembly;
        Assert.Equal(new Version(13, 0, 0, 0), assembly.GetName().Version);
        Assert.StartsWith("13.0.2", FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(), QualifiedLibraryHashes);
    }

    [Fact]
    public void PopulatedProfileContractGraphMatchesDcsWithoutGlobalDefaults()
    {
        var profile = (ProfileDocument)Populated(typeof(ProfileDocument), new HashSet<Type>())!;
        var previous = JsonConvert.DefaultSettings;
        JsonConvert.DefaultSettings = () => throw new InvalidOperationException("Global defaults must not be consulted.");
        try { AssertParity(profile); }
        finally { JsonConvert.DefaultSettings = previous; }
    }

    [Fact]
    public void DefaultsRequiredFlagsOptionalNullsAndNullableZeroKeepTheirWireMeaning()
    {
        var profile = CreateProfile();
        profile.Statistics.HealingCaptureComplete = false;
        profile.Identity.ObservedLength = 0;
        profile.Statistics.RunTotals.EquipmentStatistics.Composition.Loadouts["loadout"] = new LoadoutDefinition();
        var bytes = AssertParity(profile);
        var json = JObject.Parse(Encoding.UTF8.GetString(bytes));
        Assert.Null(json.Property("PendingSave"));
        Assert.Equal(0, (long)json["Identity"]!["ObservedLength"]!);
        Assert.False((bool)json["Statistics"]!["HealingCaptureComplete"]!);
        var definition = json["Statistics"]!["RunTotals"]!["EquipmentStatistics"]!["Composition"]!["Loadouts"]!["loadout"]!;
        Assert.False((bool)definition["RootsComplete"]!);
        Assert.False((bool)definition["NestedComplete"]!);
        ((JObject)definition).Remove("RootsComplete");
        Assert.Throws<SerializationException>(() => Read(Encoding.UTF8.GetBytes(json.ToString(Formatting.None))));
    }

    [Fact]
    public void NumericExtremesAndUnicodePreserveExactPersistedValues()
    {
        var profile = CreateProfile();
        profile.Revision = long.MaxValue;
        profile.Identity.SaveTimeBinary = long.MinValue;
        profile.GenerationReason = "雪 🦆 \" / \\ \b \n \r \t \u0000 \u2028 \u2029";
        var values = new[] { double.MinValue, double.MaxValue, double.Epsilon, -0d, 1e-200, 945.7753462999999 };
        for (var i = 0; i < values.Length; i++) profile.Statistics.Overall.AmountsByUnit["雪/\"/" + i] = values[i];
        var decimals = new[] { decimal.MinValue, decimal.MaxValue, 0m, 0.0m, 1.0000000000000000000000000000m, 0.0000000000000000000000000001m, 945.7753462999999m };
        for (var i = 0; i < decimals.Length; i++)
            profile.Statistics.RunTotals.EquipmentStatistics.Items[i.ToString(CultureInfo.InvariantCulture)] = new() { ActiveDurationSeconds = decimals[i] };
        var recovered = Read(AssertParity(profile));
        for (var i = 0; i < values.Length; i++)
            Assert.Equal(BitConverter.DoubleToInt64Bits(values[i]), BitConverter.DoubleToInt64Bits(recovered.Statistics.Overall.AmountsByUnit["雪/\"/" + i]));
        for (var i = 0; i < decimals.Length; i++)
            Assert.Equal(decimal.GetBits(decimals[i]), decimal.GetBits(recovered.Statistics.RunTotals.EquipmentStatistics.Items[i.ToString(CultureInfo.InvariantCulture)].ActiveDurationSeconds));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DateKindsPrecisionAndBoundaryRejectionsMatchDcs(DateTimeKind kind)
    {
        var ticks = new[] { 0L, DateTime.UnixEpoch.Ticks - 1, DateTime.UnixEpoch.Ticks + 9999, Now.Ticks + 1234567, DateTime.MaxValue.Ticks };
        foreach (var value in ticks)
        {
            var profile = CreateProfile();
            profile.CreatedUtc = new DateTime(value, kind);
            var expected = Record.Exception(() => DcsBytes(profile));
            if (expected == null) AssertParity(profile);
            else Assert.IsType(expected.GetType(), Record.Exception(() => CandidateBytes(profile)));
        }
    }

    [Fact]
    public void RequiredNullsAreWrittenAndRejectedByExistingSemanticValidation()
    {
        var profile = CreateProfile();
        profile.Statistics.RunTotals.EquipmentStatistics.Composition.Loadouts = null!;
        var loaded = Read(AssertParity(profile));
        Assert.Null(loaded.Statistics.RunTotals.EquipmentStatistics.Composition.Loadouts);
        Assert.NotNull(ProfileFormat.ValidateRecoveryCandidate(loaded));
        profile.Identity = null!;
        Assert.Null(Read(AssertParity(profile)).Identity);
    }

    [Fact]
    public void ConcurrentWritesShareOnlyContractsAndLeaveOwnedStreamsOpen()
    {
        var profiles = Enumerable.Range(0, 16).Select(i => { var profile = CreateProfile(); profile.Revision = i; profile.PendingSave = new() { CollectedUtc = Now.AddTicks(i), ContentSha256BeforeSave = "hash-" + i }; return profile; }).ToArray();
        Parallel.ForEach(profiles, profile =>
        {
            using var stream = new MemoryStream();
            NativeProfileJsonWriter.Write(stream, profile);
            Assert.True(stream.CanWrite);
            Assert.Equal(DcsBytes(Read(DcsBytes(profile))), DcsBytes(Read(stream.ToArray())));
        });
    }

    internal static ProfileDocument CreateProfile() => new()
    {
        GenerationId = "writer-generation",
        Slot = 1,
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Statistics = new() { SaveGenerationId = "writer-generation", CreatedUtc = Now, UpdatedUtc = Now, Holdings = new() { SaveGenerationId = "writer-generation" } }
    };

    internal static byte[] CandidateBytes(ProfileDocument profile)
    {
        using var stream = new MemoryStream();
        NativeProfileJsonWriter.Write(stream, profile);
        return stream.ToArray();
    }
    internal static byte[] DcsBytes(ProfileDocument profile)
    {
        using var stream = new MemoryStream();
        Serializer().WriteObject(stream, profile);
        return stream.ToArray();
    }
    internal static ProfileDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return (ProfileDocument)Serializer().ReadObject(stream)!;
    }
    private static DataContractJsonSerializer Serializer() => new(typeof(ProfileDocument), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true, SerializeReadOnlyTypes = false });
    private static byte[] AssertParity(ProfileDocument profile)
    {
        var baseline = DcsBytes(profile);
        var candidate = CandidateBytes(profile);
        Assert.Equal(DcsBytes(Read(baseline)), DcsBytes(Read(candidate)));
        // Also reject extra/missing members that the DCS reader could ignore.
        Assert.Equal(Shape(baseline), Shape(candidate));
        return candidate;
    }
    private static string Shape(byte[] bytes)
    {
        using var text = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        var token = JToken.ReadFrom(reader);
        return string.Join("\n", Descendants(token).Select(node => node.Path + ":" + (node.Type is JTokenType.Integer or JTokenType.Float ? "Number" : node.Type.ToString())).OrderBy(x => x, StringComparer.Ordinal));
    }
    private static IEnumerable<JToken> Descendants(JToken token)
    {
        yield return token;
        foreach (var child in token.Children())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    // A synthetic fixture reaches each nested persisted contract without storing
    // a user's profile in tests. DCS is the independent serialization oracle.
    private static object? Populated(Type type, HashSet<Type> ancestors)
    {
        if (Nullable.GetUnderlyingType(type) is { } nullable) return Populated(nullable, ancestors);
        if (type == typeof(string)) return "fixture 雪 / \" \\";
        if (type == typeof(DateTime)) return Now.AddTicks(1234);
        if (type == typeof(decimal)) return 1.000m;
        if (type == typeof(double)) return -0d;
        if (type == typeof(bool)) return true;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (type.IsPrimitive) return Convert.ChangeType(1, type, CultureInfo.InvariantCulture);
        if (!ancestors.Add(type)) return null;
        try
        {
            var concrete = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>)
                ? typeof(List<>).MakeGenericType(type.GetGenericArguments()) : type;
            var value = Activator.CreateInstance(concrete)!;
            if (value is IDictionary dictionary)
            {
                var arguments = type.GetGenericArguments();
                dictionary.Add(Populated(arguments[0], ancestors)!, Populated(arguments[1], ancestors));
            }
            else if (value is IList list) list.Add(Populated(type.GetGenericArguments()[0], ancestors));
            else
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<DataMemberAttribute>() != null))
                    property.SetValue(value, Populated(property.PropertyType, ancestors));
            return value;
        }
        finally { ancestors.Remove(type); }
    }
}
