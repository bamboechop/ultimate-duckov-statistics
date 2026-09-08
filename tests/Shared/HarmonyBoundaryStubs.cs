using System.Reflection;

#pragma warning disable CA1051, CA1822, CA1859 // Faithful public-field Harmony reflection contract.
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public HarmonyMethod(MethodInfo method)
        {
            this.method = method;
        }

        public readonly MethodInfo method;

        public int priority;
    }

    public sealed class Patch
    {
        public Patch(string owner, MethodInfo patchMethod)
        {
            this.owner = owner;
            PatchMethod = patchMethod;
        }

        public readonly string owner;

        public MethodInfo PatchMethod { get; }
    }

    public sealed class Patches
    {
        public readonly List<Patch> Prefixes = new();

        public readonly List<Patch> Postfixes = new();

        public readonly List<Patch> Transpilers = new();

        public readonly List<Patch> Finalizers = new();
    }

    internal static class HarmonySharedState
    {
        private static readonly Dictionary<MethodBase, byte[]> state = new();

        public static void Update(MethodBase original) => state[original] = new byte[1];

        public static void Clear() => state.Clear();
    }

    public sealed class Harmony
    {
        private static readonly Dictionary<MethodBase, Patches> Registry = new();
        private static int unpatchFailuresRemaining;
        private readonly string owner;

        internal static Action? AfterGetPatchInfo { get; set; }

        public Harmony(string owner)
        {
            this.owner = owner;
        }

        public void Patch(
            MethodBase original,
            HarmonyMethod? prefix,
            HarmonyMethod? postfix,
            HarmonyMethod? transpiler,
            HarmonyMethod? finalizer)
        {
            if (!Registry.TryGetValue(original, out var patches))
            {
                patches = new Patches();
                Registry[original] = patches;
            }

            Add(patches.Prefixes, prefix);
            Add(patches.Postfixes, postfix);
            Add(patches.Transpilers, transpiler);
            Add(patches.Finalizers, finalizer);
            HarmonySharedState.Update(original);
        }

        public void UnpatchAll(string ownerId)
        {
            UnpatchAttempts++;
            if (unpatchFailuresRemaining > 0)
            {
                unpatchFailuresRemaining--;
                throw new InvalidOperationException("Injected UnpatchAll failure.");
            }

            foreach (var entry in Registry)
            {
                var patches = entry.Value;
                var previousCount = patches.Prefixes.Count
                                    + patches.Postfixes.Count
                                    + patches.Transpilers.Count
                                    + patches.Finalizers.Count;
                patches.Prefixes.RemoveAll(patch => patch.owner == ownerId);
                patches.Postfixes.RemoveAll(patch => patch.owner == ownerId);
                patches.Transpilers.RemoveAll(patch => patch.owner == ownerId);
                patches.Finalizers.RemoveAll(patch => patch.owner == ownerId);
                var currentCount = patches.Prefixes.Count
                                   + patches.Postfixes.Count
                                   + patches.Transpilers.Count
                                   + patches.Finalizers.Count;
                if (currentCount != previousCount) HarmonySharedState.Update(entry.Key);
            }
        }

        public static Patches? GetPatchInfo(MethodBase original)
        {
            if (!Registry.TryGetValue(original, out var patches)) return null;
            var afterGetPatchInfo = AfterGetPatchInfo;
            if (afterGetPatchInfo == null) return patches;
            var snapshot = Clone(patches);
            AfterGetPatchInfo = null;
            afterGetPatchInfo();
            return snapshot;
        }

        public static int UnpatchAttempts { get; private set; }

        public static void FailNextUnpatches(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            unpatchFailuresRemaining = count;
        }

        public static void ClearAll()
        {
            Registry.Clear();
            HarmonySharedState.Clear();
            AfterGetPatchInfo = null;
            unpatchFailuresRemaining = 0;
            UnpatchAttempts = 0;
        }

        private static Patches Clone(Patches source)
        {
            var result = new Patches();
            result.Prefixes.AddRange(source.Prefixes);
            result.Postfixes.AddRange(source.Postfixes);
            result.Transpilers.AddRange(source.Transpilers);
            result.Finalizers.AddRange(source.Finalizers);
            return result;
        }

        private void Add(ICollection<Patch> patches, HarmonyMethod? method)
        {
            if (method != null)
            {
                patches.Add(new Patch(owner, method.method));
            }
        }
    }
}
#pragma warning restore CA1051, CA1822, CA1859
