using System.Reflection;
using System.Reflection.Emit;
using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class KnownModCompatibilityTests
{
    private static readonly string[] UdsOrder = { "uds" };
    [Fact]
    public void CopiedModNameOwnerAndCallbackCannotEnableUnverifiedCode()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("StorageSearchBar"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("fake");
        var outer = module.DefineType("StorageSearchBar.Patching.Patches.LootViewPatch", TypeAttributes.Public);
        var nested = outer.DefineNestedType("LootViewClosePatch", TypeAttributes.NestedPublic);
        var prefix = nested.DefineMethod("Prefix", MethodAttributes.Static | MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        prefix.GetILGenerator().Emit(OpCodes.Ret);
        var callback = nested.CreateType()!.GetMethod("Prefix")!;
        outer.CreateType();
        var target = typeof(Duckov.UI.LootView).GetMethod("OnClose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var patch = new PatchMetadata(callback);
        Assert.True(KnownModCompatibility.MatchesCallback(target, "Prefixes", "pisiunas.SSB", callback));
        Assert.True(KnownModCompatibility.HasInspectedOrder(patch));
        Assert.False(KnownModCompatibility.IsVerifiedAssembly(assembly, "StorageSearchBar"));
        Assert.False(KnownModCompatibility.AllowsPatch(target, "Prefixes", patch));
        Assert.False(KnownModCompatibility.MatchesCallback(target, "Finalizers", "pisiunas.SSB", callback));
        Assert.False(KnownModCompatibility.MatchesCallback(typeof(Health).GetMethod(nameof(Health.AddHealth))!, "Prefixes", "pisiunas.SSB", callback));
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(800, false, false)]
    [InlineData(400, true, false)]
    [InlineData(400, false, true)]
    public void ReorderedKnownPatchCannotClaimInspectedOrdering(int priority, bool before, bool after)
    {
        var patch = new PatchMetadata(typeof(KnownModCompatibilityTests).GetMethod(nameof(ReorderedKnownPatchCannotClaimInspectedOrdering))!,
            priority, before ? UdsOrder : Array.Empty<string>(), after ? UdsOrder : Array.Empty<string>());
        Assert.False(KnownModCompatibility.HasInspectedOrder(patch));
    }

    private sealed class PatchMetadata
    {
        public PatchMetadata(MethodInfo method, int priority = 400, string[]? before = null, string[]? after = null)
        { PatchMethod = method; this.priority = priority; this.before = before ?? Array.Empty<string>(); this.after = after ?? Array.Empty<string>(); }
        public MethodInfo PatchMethod { get; }
        public string owner { get; } = "pisiunas.SSB";
        public int priority { get; }
        public string[] before { get; }
        public string[] after { get; }
    }
}
