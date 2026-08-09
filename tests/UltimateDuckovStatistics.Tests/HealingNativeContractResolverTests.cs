using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class HealingNativeContractResolverTests
{
    [Fact]
    [Trait("Category", "Healing")]
    public void ExactCompatibleContractsResolve()
    {
        var resolved = Resolve(typeof(CompatibleHealth), typeof(CompatibleEffectAction), typeof(CompatibleBuffManager));

        Assert.True(resolved.Success, resolved.Failure);
        Assert.Equal("AddHealth", resolved.HealthMethod?.Name);
        Assert.Equal("NotifyTriggered", resolved.EffectMethod?.Name);
        Assert.Equal("AddBuff", resolved.BuffMethod?.Name);
    }

    [Theory]
    [Trait("Category", "Healing")]
    [InlineData(typeof(HealthWithWrongSignature), typeof(CompatibleEffectAction), typeof(CompatibleBuffManager))]
    [InlineData(typeof(HealthWithoutTargetProperty), typeof(CompatibleEffectAction), typeof(CompatibleBuffManager))]
    [InlineData(typeof(CompatibleHealth), typeof(EffectWithPublicTrigger), typeof(CompatibleBuffManager))]
    [InlineData(typeof(CompatibleHealth), typeof(CompatibleEffectAction), typeof(BuffManagerWithWrongSignature))]
    public void ChangedGameAssemblyContractsDegradeInsteadOfGuessing(
        Type healthType,
        Type effectType,
        Type buffManagerType)
    {
        var resolved = Resolve(healthType, effectType, buffManagerType);

        Assert.False(resolved.Success);
        Assert.Contains("missing or changed", resolved.Failure, StringComparison.Ordinal);
    }

    private static Resolution Resolve(Type healthType, Type effectType, Type buffManagerType)
    {
        var success = HealingNativeContractResolver.TryResolve(
            healthType,
            effectType,
            typeof(CompatibleEffectContext),
            buffManagerType,
            typeof(CompatibleBuff),
            typeof(CompatibleCharacter),
            out var healthMethod,
            out var effectMethod,
            out var buffMethod,
            out var failure);
        return new Resolution(success, healthMethod, effectMethod, buffMethod, failure);
    }

    private sealed record Resolution(
        bool Success,
        System.Reflection.MethodInfo? HealthMethod,
        System.Reflection.MethodInfo? EffectMethod,
        System.Reflection.MethodInfo? BuffMethod,
        string Failure);

    private sealed class CompatibleHealth
    {
        public float CurrentHealth { get; set; }

        public float MaxHealth { get; set; }

        public bool IsMainCharacterHealth { get; set; }

        public void AddHealth(float value) => CurrentHealth += value;
    }

    private sealed class HealthWithWrongSignature
    {
        public float CurrentHealth { get; set; }

        public float MaxHealth { get; set; }

        public bool IsMainCharacterHealth { get; set; }

        public void AddHealth(double value) => CurrentHealth += (float)value;
    }

    private sealed class HealthWithoutTargetProperty
    {
        public float CurrentHealth { get; set; }

        public float MaxHealth { get; set; }

        public void AddHealth(float value) => CurrentHealth += value;
    }

    private sealed class CompatibleEffectContext;

    private sealed class CompatibleEffectAction
    {
        private CompatibleEffectContext? observed;

        internal void NotifyTriggered(CompatibleEffectContext context) => observed = context;
    }

    private sealed class EffectWithPublicTrigger
    {
        private CompatibleEffectContext? observed;

        public void NotifyTriggered(CompatibleEffectContext context) => observed = context;
    }

    private sealed class CompatibleBuff;

    private sealed class CompatibleCharacter;

    private sealed class CompatibleBuffManager
    {
        private object? lastBuff;

        public void AddBuff(CompatibleBuff buff, CompatibleCharacter character, int layers)
        {
            lastBuff = buff;
            _ = character;
            _ = layers;
        }
    }

    private sealed class BuffManagerWithWrongSignature
    {
        private object? lastBuff;

        public void AddBuff(CompatibleBuff buff, CompatibleCharacter character)
        {
            lastBuff = buff;
            _ = character;
        }
    }
}
