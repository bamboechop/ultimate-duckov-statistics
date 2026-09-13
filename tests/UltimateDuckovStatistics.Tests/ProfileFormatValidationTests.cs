using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Serialization.Json;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProfileFormatValidationTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("identity", "Profile.Identity.GameVersion")]
    [InlineData("sequence-row", "Profile.Capabilities[1]")]
    [InlineData("sequence-member", "Profile.Capabilities[0].Version")]
    [InlineData("scalar-sequence", "Profile.Statistics.RecentEventIds[1]")]
    [InlineData("dictionary-row", "Profile.Statistics.Groups[group]")]
    [InlineData("dictionary-member", "Profile.Statistics.Items[item].Totals.AmountsByUnit")]
    [InlineData("optional-present", "Profile.PendingSave.ContentSha256BeforeSave")]
    public void ColdAndWarmValidationKeepExactMissingMemberPaths(string corruption, string missingPath)
    {
        var profile = CreateProfile();
        Corrupt(profile, corruption);
        var expected = "Current-schema profile roots are incomplete. Missing required data member: " + missingPath + ".";

        // A fresh assembly context exercises the first validation without relying
        // on test order or reaching into the validator's metadata cache.
        using var isolated = new IsolatedValidator();
        Assert.Equal(expected, isolated.Validate(profile));
        Assert.Equal(expected, isolated.Validate(profile));
        Assert.Equal(expected, ProfileFormat.ValidateRecoveryCandidate(profile));

        Assert.Null(isolated.Validate(CreateProfile()));
        Assert.Equal(expected, isolated.Validate(profile));
    }

    [Fact]
    public void RepeatedValidationReadsNewValuesAndRetainsOptionalMemberRules()
    {
        var profile = CreateProfile();
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));
        profile.PendingSave = new PendingSaveObservation();
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));
        profile.PendingSave.ContentSha256BeforeSave = null!;
        Assert.Contains("Profile.PendingSave.ContentSha256BeforeSave", ProfileFormat.ValidateRecoveryCandidate(profile));
        profile.PendingSave = null;
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));

        profile.Statistics.Economy.CashAcquired = -1;
        Assert.Contains("invalid economy state", ProfileFormat.ValidateRecoveryCandidate(profile));
        profile.Statistics.Economy.CashAcquired = 0;
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));

        profile.Statistics.RecentEventIds.Add("present scalar");
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));
        profile.Statistics.RecentEventIds.Add(null!);
        Assert.Contains("Profile.Statistics.RecentEventIds[1]", ProfileFormat.ValidateRecoveryCandidate(profile));
        profile.Statistics.RecentEventIds.RemoveAt(1);
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));
    }

    [Fact]
    public void RepairableRowsStillReachDomainRepairAndRequiredRowsRemainRejected()
    {
        var profile = CreateProfile();
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));
        var weapons = profile.Statistics.RunTotals.WeaponStatistics;
        weapons.Weapons["weapon:null"] = null!;
        var failure = ProfileFormat.ValidateRecoveryCandidate(profile);
        Assert.NotNull(failure);
        Assert.DoesNotContain("Missing required data member", failure);
        Assert.True(WeaponStatisticsReducer.NormalizePersisted(weapons).Changed);
        Assert.True(weapons.WasRepairedFromInvalidState);
        Assert.Empty(weapons.Weapons);
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));

        profile.Statistics.Groups["group"] = null!;
        Assert.Contains("Profile.Statistics.Groups[group]", ProfileFormat.ValidateRecoveryCandidate(profile));
        profile.Statistics.Groups.Clear();
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));
    }

    [Fact]
    public void ConcurrentFirstAndRepeatedValidationsKeepIndependentResults()
    {
        using var isolated = new IsolatedValidator();
        Parallel.For(0, 32, index =>
        {
            var profile = CreateProfile();
            var invalid = index % 2 == 1;
            if (invalid) profile.Identity.GameVersion = null!;
            for (var repeat = 0; repeat < 4; repeat++)
            {
                var result = isolated.Validate(profile);
                if (invalid) Assert.Contains("Profile.Identity.GameVersion", result);
                else Assert.Null(result);
            }
        });
    }

    private static ProfileDocument CreateProfile() => new()
    {
        GenerationId = "generation-validation",
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Statistics = new()
        {
            SaveGenerationId = "generation-validation",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Holdings = new EconomyHoldingsSnapshot { SaveGenerationId = "generation-validation" }
        }
    };

    private static void Corrupt(ProfileDocument profile, string corruption)
    {
        switch (corruption)
        {
            case "identity": profile.Identity.GameVersion = null!; break;
            case "sequence-row": profile.Capabilities.Add(new()); profile.Capabilities.Add(null!); break;
            case "sequence-member": profile.Capabilities.Add(new() { Version = null! }); break;
            case "scalar-sequence": profile.Statistics.RecentEventIds.AddRange(new[] { "event", null! }); break;
            case "dictionary-row": profile.Statistics.Groups["group"] = null!; break;
            case "dictionary-member":
                profile.Statistics.Items["item"] = new ItemAggregate { Totals = new AggregateTotals { AmountsByUnit = null! } };
                break;
            case "optional-present": profile.PendingSave = new PendingSaveObservation { ContentSha256BeforeSave = null!, CollectedUtc = Now }; break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }

    private sealed class IsolatedValidator : IDisposable
    {
        private readonly AssemblyLoadContext context = new(null, isCollectible: true);
        private readonly MethodInfo validate;
        private readonly Type profileType;

        public IsolatedValidator()
        {
            var assembly = context.LoadFromAssemblyPath(typeof(ProfileFormat).Assembly.Location);
            profileType = assembly.GetType(typeof(ProfileDocument).FullName!, throwOnError: true)!;
            validate = assembly.GetType(typeof(ProfileFormat).FullName!, throwOnError: true)!
                .GetMethod(nameof(ProfileFormat.ValidateRecoveryCandidate))!;
        }

        public string? Validate(ProfileDocument profile)
        {
            using var stream = new MemoryStream();
            var settings = new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true };
            new DataContractJsonSerializer(typeof(ProfileDocument), settings).WriteObject(stream, profile);
            stream.Position = 0;
            var isolatedProfile = new DataContractJsonSerializer(profileType, settings).ReadObject(stream);
            return (string?)validate.Invoke(null, new[] { isolatedProfile });
        }

        public void Dispose() => context.Unload();
    }
}
