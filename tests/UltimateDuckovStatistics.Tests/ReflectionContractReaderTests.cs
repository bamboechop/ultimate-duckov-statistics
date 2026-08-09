using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class ReflectionContractReaderTests
{
    [Fact]
    [Trait("Category", "Healing")]
    public void ReadsHarmonyStylePublicFieldContract()
    {
        var value = ReflectionContractReader.ReadInstanceMember(new FieldContract(), "Transpilers");

        Assert.Same(FieldContract.Value, value);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void AlsoAcceptsPropertyContractForCompatibleHarmonyVariants()
    {
        var contract = new PropertyContract();

        var value = ReflectionContractReader.ReadInstanceMember(contract, "Transpilers");

        Assert.Same(contract.Transpilers, value);
    }

    private sealed class FieldContract
    {
        internal static readonly object Value = new();

        public readonly object Transpilers = Value;
    }

    private sealed class PropertyContract
    {
        public object Transpilers { get; } = new();
    }
}
