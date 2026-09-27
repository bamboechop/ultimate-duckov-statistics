using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeProfileFloatingPointTests
{
    [Fact]
    public void FiniteFloatsUseTheNativeRoundTripReaderPathAndKeepExactBits()
    {
        var random = new Random(73401);
        var doubles = new List<double> { 29.235182, Math.BitDecrement(29.235182), Math.BitIncrement(29.235182),
            BitConverter.Int64BitsToDouble(-8121461400728632775), 0, -0d, double.Epsilon, double.MaxValue, double.MinValue, 1e-200, 1e200, 9007199254740992 };
        var singles = new List<float> { 0, -0f, float.Epsilon, float.MinValue, float.MaxValue, 0.0160068851f, 474.438263f };
        var bits = new byte[8];
        for (var index = 0; index < 1024; index++)
        {
            random.NextBytes(bits);
            var wide = BitConverter.ToDouble(bits);
            var narrow = BitConverter.ToSingle(bits);
            if (double.IsFinite(wide)) doubles.Add(wide);
            if (float.IsFinite(narrow)) singles.Add(narrow);
        }
        var original = new Numbers { Doubles = doubles.ToArray(), Singles = singles.ToArray(), NullableDouble = 29.235182, NullableSingle = 474.438263f };
        var bytes = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord).Encode(original);
        var json = Encoding.UTF8.GetString(bytes);
        // This wire constraint bypasses the installed Mono XmlConverter short-decimal
        // fast path even when tests execute on a modern CLR without that defect.
        var tokens = Regex.Matches(json, @"(?<=[\[,:])(-?\d+(?:\.\d+)?(?:[Ee][+-]?\d+)?)(?=[,\]}])");
        Assert.Equal(original.Doubles.Length + original.Singles.Length + 2, tokens.Count);
        Assert.All(tokens.Cast<Match>(), token => Assert.Contains("e", token.Value.ToLowerInvariant()));
        // Installed Mono's R formatter loses the final digits here; its own parser
        // accepts that token, but .NET reads an adjacent value. Preserve full precision.
        Assert.Contains("-5.2627200374460404E-235", json);
        Assert.Contains("29.235181999999998e0", json);
        var copy = ProfileRecordCodec.Decode<Numbers>(bytes);
        Assert.Equal(original.Doubles.Select(BitConverter.DoubleToInt64Bits), copy.Doubles.Select(BitConverter.DoubleToInt64Bits));
        Assert.Equal(original.Singles.Select(BitConverter.SingleToInt32Bits), copy.Singles.Select(BitConverter.SingleToInt32Bits));
        Assert.Equal(original.NullableDouble, copy.NullableDouble);
        Assert.Equal(original.NullableSingle, copy.NullableSingle);
    }

    [Fact]
    public void NullAndNonfiniteNumbersRetainTheExistingReaderContract()
    {
        var value = new Numbers { Doubles = new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity },
            Singles = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity } };
        var bytes = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord).Encode(value);
        var copy = ProfileRecordCodec.Decode<Numbers>(bytes);
        Assert.Null(copy.NullableDouble); Assert.Null(copy.NullableSingle);
        Assert.True(double.IsNaN(copy.Doubles[0])); Assert.True(float.IsNaN(copy.Singles[0]));
        Assert.Equal(value.Doubles.Skip(1), copy.Doubles.Skip(1));
        Assert.Equal(value.Singles.Skip(1), copy.Singles.Skip(1));
    }

    [Fact]
    public void DecimalScaleAndIntegralMembersDoNotBecomeFloatingPointTokens()
    {
        var value = new OtherNumbers { Fractional = 1.2300m, Integral = long.MaxValue };
        var bytes = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord).Encode(value);
        var copy = ProfileRecordCodec.Decode<OtherNumbers>(bytes);
        Assert.Equal(decimal.GetBits(value.Fractional), decimal.GetBits(copy.Fractional));
        Assert.Equal(value.Integral, copy.Integral);
        Assert.Contains("1.2300", Encoding.UTF8.GetString(bytes));
        Assert.Contains(long.MaxValue.ToString(CultureInfo.InvariantCulture), Encoding.UTF8.GetString(bytes));
    }

    [DataContract]
    public sealed class Numbers
    {
        [DataMember] public double[] Doubles { get; set; } = Array.Empty<double>();
        [DataMember] public float[] Singles { get; set; } = Array.Empty<float>();
        [DataMember] public double? NullableDouble { get; set; }
        [DataMember] public float? NullableSingle { get; set; }
    }

    [DataContract]
    public sealed class OtherNumbers
    {
        [DataMember] public decimal Fractional { get; set; }
        [DataMember] public long Integral { get; set; }
    }
}
