using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UltimateDuckovStatistics.Adapters;

internal static class FirstPersonHeadshotProbe
{
    internal static void RunUpdatedBuild()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("FirstPersonCamera"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("updated-mod").DefineType("FirstPersonCamera.HeadshotPatch", TypeAttributes.Public);
        var prefix = type.DefineMethod("Prefix", MethodAttributes.Public | MethodAttributes.Static, typeof(void),
            new[] { typeof(Health), typeof(DamageInfo).MakeByRefType(), typeof(FpcResultFixture.State).MakeByRefType() });
        prefix.DefineParameter(1, ParameterAttributes.None, "__instance");
        prefix.DefineParameter(2, ParameterAttributes.None, "damageInfo");
        prefix.DefineParameter(3, ParameterAttributes.Out, "__state");
        var il = prefix.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(FpcResultFixture).GetMethod(nameof(FpcResultFixture.Prefix))!); il.Emit(OpCodes.Ret);
        var mode = type.DefineMethod("IsFirstPersonModeActive", MethodAttributes.Private | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
        mode.GetILGenerator().Emit(OpCodes.Ldc_I4_1); mode.GetILGenerator().Emit(OpCodes.Ret);
        var callback = type.CreateType()!.GetMethod("Prefix")!;
        Check(!KnownModCompatibility.IsVerifiedAssembly(assembly, "FirstPersonCamera"), "Fixture unexpectedly has an approved binary identity.");
        var healthHurt = typeof(Health).GetMethod(nameof(Health.Hurt))!;
        var harmony = new Harmony("probe.updated.fpc");
        harmony.Patch(healthHurt, prefix: new HarmonyMethod(callback));
        using var compatibility = NativeFirstPersonHeadshotCompatibility.Resolve(healthHurt);
        Check(compatibility.IsSupported, "An unrecognized binary with the result contract was rejected: " + compatibility.Failure);
        FpcResultFixture.Compatibility = compatibility;
        harmony.Patch(healthHurt, prefix: new HarmonyMethod(typeof(FpcResultFixture).GetMethod(nameof(FpcResultFixture.Record))!) { priority = 0 });
        var health = new Health();
        var damage = new DamageInfo { fromCharacter = new() { IsMainCharacter = true }, Head = true };
        health.Hurt(damage);
        Check(FpcResultFixture.Recorded, "Production resolution lost an updated FPC build's headshot.");
        damage.isExplosion = true; health.Hurt(damage);
        Check(!FpcResultFixture.Recorded, "Production resolution counted an explosion as a headshot.");
        harmony.Unpatch(healthHurt, callback);
        damage.isExplosion = false; health.Hurt(damage);
        Check(!FpcResultFixture.Recorded && !compatibility.IsSupported,
            "A removed FPC prefix must disable headshot evidence rather than fall back to crit or stale state.");
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("PASS: production FPC resolution accepts a new module without a hash allowlist and still excludes explosions.");
    }
    internal static void Run()
    {
        var prefix = typeof(FpcResultFixture).GetMethod(nameof(FpcResultFixture.Prefix))!;
        var hurt = typeof(Health).GetMethod(nameof(Health.Hurt))!;
        var foreign = new Harmony("probe.fpc.updated.build");
        foreign.Patch(hurt, prefix: new HarmonyMethod(prefix));
        using var observer = new FirstPersonHeadshotResultObserver(prefix);
        using var compatibility = new NativeFirstPersonHeadshotCompatibility(() => true, resultObserver: observer);
        FpcResultFixture.Compatibility = compatibility;
        foreign.Patch(hurt, prefix: new HarmonyMethod(typeof(FpcResultFixture).GetMethod(nameof(FpcResultFixture.Record))!) { priority = 0 });
        var player = new CharacterMainControl { IsMainCharacter = true };
        var health = new Health();
        var info = new DamageInfo { fromCharacter = player, Head = true };
        health.Hurt(info);
        Check(FpcResultFixture.Recorded, "Explicit FPC head-hit result was lost through real Harmony injection.");
        info.Head = false; info.crit = 1;
        new Health().Hurt(info);
        Check(!FpcResultFixture.Recorded, "Generic critical body damage became a headshot.");
        info.Head = true; info.isExplosion = true;
        health.Hurt(info);
        Check(!FpcResultFixture.Recorded, "Explosion became a headshot.");
        info.isExplosion = false;
        health.Hurt(info);
        Check(FpcResultFixture.Recorded, "Explosion suppression leaked to later projectile.");
        Check(!observer.TryConsume(health, info, player, health.Character, out _), "Consumed result was reused.");
        foreign.Unpatch(hurt, HarmonyPatchType.Prefix, foreign.Id);
        Check(!observer.TryConsume(health, info, player, health.Character, out _), "Missing FPC callback reused stale evidence.");
        FpcResultFixture.Prefix(health, ref info, out _);
        FirstPersonHeadshotResultObserver.CompleteHealth(health);
        Check(!observer.TryConsume(health, info, player, health.Character, out _), "Finalized health scope retained unconsumed evidence.");
        Console.WriteLine("PASS: FPC explicit head-hit results across real Harmony prefix injection; unrelated assembly identity; critical body hits; explosion exclusion; consecutive targets; consume-once evidence.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

public sealed class CharacterMainControl { public bool IsMainCharacter { get; set; } }
public enum DamageTypes { normal }
public struct DamageInfo
{
    public bool Head, isExplosion;
    public int crit;
    public DamageTypes damageType;
    public CharacterMainControl fromCharacter;
}
public sealed class Health
{
    public CharacterMainControl Character { get; } = new();
    public CharacterMainControl TryGetCharacter() => Character;
    [MethodImpl(MethodImplOptions.NoInlining)] public bool Hurt(DamageInfo damageInfo) => true;
}
public static class FpcResultFixture
{
    internal static NativeFirstPersonHeadshotCompatibility? Compatibility;
    public static bool Recorded;
    public struct State
    {
        public bool tracked, actualHeadHit;
        public CharacterMainControl attacker, target;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Prefix(Health __instance, ref DamageInfo damageInfo, out State __state)
    {
        __state = new State { tracked = true, actualHeadHit = damageInfo.Head,
            attacker = damageInfo.fromCharacter, target = __instance.Character };
        // Deliberately keep generic crit true even on body hits: UDS must read the explicit result.
        damageInfo.crit = 1;
    }
    public static void Record(Health __instance, DamageInfo damageInfo)
        => Recorded = Compatibility!.Capture(__instance, damageInfo, nativeHeadTargeted: true);
}
