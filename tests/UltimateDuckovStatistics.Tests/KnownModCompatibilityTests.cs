using System.Reflection;
using System.Reflection.Emit;
using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class KnownModCompatibilityTests
{
    private static readonly string[] UdsOrder = { "uds" };
    [Fact]
    public void InspectedFpcBarrelCameraPrefixMatchesOnlyItsExactTargetAndPhase()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FirstPersonCamera"), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("fake");
        var type = module.DefineType("FirstPersonCamera.Patches.Breakable_OnDead_ExplosionCameraShake_Patch", TypeAttributes.Public);
        var method = type.DefineMethod("Prefix", MethodAttributes.Static | MethodAttributes.Public, typeof(void), new[] { typeof(Breakable) });
        method.GetILGenerator().Emit(OpCodes.Ret);
        var callback = type.CreateType()!.GetMethod("Prefix")!;
        var target = typeof(Breakable).GetMethod("OnDead", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(KnownModCompatibility.MatchesCallback(target, "Prefixes", "firstpersoncamera.aimpatch", callback));
        Assert.False(KnownModCompatibility.MatchesCallback(target, "Postfixes", "firstpersoncamera.aimpatch", callback));
        Assert.False(KnownModCompatibility.MatchesCallback(target, "Prefixes", "other.owner", callback));
        Assert.False(KnownModCompatibility.MatchesCallback(typeof(Health).GetMethod(nameof(Health.Hurt))!, "Prefixes", "firstpersoncamera.aimpatch", callback));
        Assert.False(KnownModCompatibility.IsVerifiedAssembly(assembly, "FirstPersonCamera"));
    }

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

    [Theory]
    [InlineData("916ebb5c-dc53-4c53-ba0d-a4f672364748", "832be121a23095bc37dba4735b155a3d1e0c8a511eb18d5e992ba91887965cac")]
    [InlineData("d5173e49-e754-49ed-88de-b8af2f607f67", "c7d5b7c7c74bc9ae793ee262bdfd379ffd22b8b8fbe8edfeaaed108620e56835")]
    [InlineData("14b6820f-2f3f-4e87-ad5a-2b5e7cd0342f", "710c8a17a4292315ce429ffb1fe0ca3d5e8616164383dd92924fac1971f81c6c")]
    public void InspectedFirstPersonBuildsRequireMatchingModuleAndBinary(string module, string hash)
    {
        var id = Guid.Parse(module);
        Assert.True(KnownModCompatibility.MatchesInspectedBuild("FirstPersonCamera", id, hash.ToUpperInvariant()));
        Assert.False(KnownModCompatibility.MatchesInspectedBuild("FirstPersonCamera", Guid.Empty, hash));
        Assert.False(KnownModCompatibility.MatchesInspectedBuild("FirstPersonCamera", id, new string('0', 64)));
        Assert.False(KnownModCompatibility.MatchesInspectedBuild("OtherCamera", id, hash));
        var otherModule = module.StartsWith("916", StringComparison.Ordinal)
            ? Guid.Parse("d5173e49-e754-49ed-88de-b8af2f607f67") : Guid.Parse("916ebb5c-dc53-4c53-ba0d-a4f672364748");
        Assert.False(KnownModCompatibility.MatchesInspectedBuild("FirstPersonCamera", otherModule, hash));
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
