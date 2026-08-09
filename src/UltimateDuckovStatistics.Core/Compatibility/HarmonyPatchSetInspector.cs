using System.Collections;
using System.Reflection;

namespace UltimateDuckovStatistics.Core.Compatibility;

public sealed class HarmonyPatchExpectation
{
    public HarmonyPatchExpectation(string collectionName, MethodInfo patchMethod)
    {
        if (string.IsNullOrWhiteSpace(collectionName))
        {
            throw new ArgumentException("A Harmony patch collection name is required.", nameof(collectionName));
        }

        CollectionName = collectionName;
        PatchMethod = patchMethod ?? throw new ArgumentNullException(nameof(patchMethod));
    }

    public string CollectionName { get; }

    public MethodInfo PatchMethod { get; }
}

public static class HarmonyPatchSetInspector
{
    private static readonly string[] PatchCollectionNames =
    {
        "Prefixes",
        "Postfixes",
        "Transpilers",
        "Finalizers"
    };

    public static bool TryValidate(
        object? patchInfo,
        string expectedOwner,
        IReadOnlyList<HarmonyPatchExpectation> expectedOwnedPatches,
        out string detail)
    {
        if (string.IsNullOrWhiteSpace(expectedOwner))
        {
            throw new ArgumentException("A Harmony owner ID is required.", nameof(expectedOwner));
        }

        if (expectedOwnedPatches == null)
        {
            throw new ArgumentNullException(nameof(expectedOwnedPatches));
        }

        if (patchInfo == null)
        {
            if (expectedOwnedPatches.Count == 0)
            {
                detail = "No Harmony patches are installed.";
                return true;
            }

            detail = $"Required UDS patch is missing: {Describe(expectedOwnedPatches[0])}.";
            return false;
        }

        var matched = new bool[expectedOwnedPatches.Count];
        foreach (var collectionName in PatchCollectionNames)
        {
            if (ReflectionContractReader.ReadInstanceMember(patchInfo, collectionName) is not IEnumerable patches)
            {
                detail = $"Harmony patch metadata member {collectionName} is missing or incompatible.";
                return false;
            }

            foreach (var patch in patches)
            {
                if (patch == null)
                {
                    continue;
                }

                var owner = ReflectionContractReader.ReadInstanceMember(patch, "owner") as string;
                if (!string.Equals(owner, expectedOwner, StringComparison.Ordinal))
                {
                    detail = $"Foreign Harmony patch in {collectionName}: {owner ?? "unknown"}.";
                    return false;
                }

                if (ReflectionContractReader.ReadInstanceMember(patch, "PatchMethod") is not MethodInfo patchMethod)
                {
                    detail = $"UDS Harmony patch in {collectionName} has no readable PatchMethod.";
                    return false;
                }

                var expectationIndex = FindExpectation(
                    expectedOwnedPatches,
                    collectionName,
                    patchMethod);
                if (expectationIndex < 0)
                {
                    detail = $"Unexpected UDS Harmony patch in {collectionName}: {Describe(patchMethod)}.";
                    return false;
                }

                if (matched[expectationIndex])
                {
                    detail = $"Duplicate UDS Harmony patch in {collectionName}: {Describe(patchMethod)}.";
                    return false;
                }

                matched[expectationIndex] = true;
            }
        }

        for (var index = 0; index < matched.Length; index++)
        {
            if (!matched[index])
            {
                detail = $"Required UDS patch is missing: {Describe(expectedOwnedPatches[index])}.";
                return false;
            }
        }

        detail = "Harmony patch set contains exactly the required UDS callbacks and no foreign patches.";
        return true;
    }

    private static int FindExpectation(
        IReadOnlyList<HarmonyPatchExpectation> expectations,
        string collectionName,
        MethodInfo patchMethod)
    {
        for (var index = 0; index < expectations.Count; index++)
        {
            var expectation = expectations[index];
            if (string.Equals(expectation.CollectionName, collectionName, StringComparison.Ordinal)
                && MethodsMatch(expectation.PatchMethod, patchMethod))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool MethodsMatch(MethodInfo left, MethodInfo right)
    {
        if (left.Equals(right))
        {
            return true;
        }

        try
        {
            return left.Module.Equals(right.Module) && left.MetadataToken == right.MetadataToken;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string Describe(HarmonyPatchExpectation expectation) =>
        $"{expectation.CollectionName}/{Describe(expectation.PatchMethod)}";

    private static string Describe(MethodInfo method) =>
        $"{method.DeclaringType?.FullName ?? "unknown"}.{method.Name}";
}
