using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class V1ProfileFormatTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NativeCompositionUsesSeparateV1Directory()
    {
        var coordinator = new NativeProfileCoordinator();
        Assert.Equal(Path.Combine(UnityEngine.Application.persistentDataPath, ProductInfo.ModId, "v1"), coordinator.DataRoot);
    }

    [Fact]
    public void CleanInstallAndCurrentFormatReinstallationKeepExactGeneration()
    {
        using var directory = new TemporaryDirectory();
        var first = Repository(directory.Path);
        Assert.True(first.Open(Identity()).CreatedNew);
        var generation = first.CurrentGenerationId;
        Assert.Equal(ProductInfo.ProfileFormatId, first.Current.FormatId);
        first.CloseClean();

        var second = Repository(directory.Path);
        var result = second.Open(Identity());
        Assert.False(result.CreatedNew);
        Assert.False(result.RotatedGeneration);
        Assert.Equal(generation, second.CurrentGenerationId);
        Assert.Empty(result.LoadFailures);
        second.CloseClean();
    }

    [Theory]
    [InlineData(null, 18)]
    [InlineData("uds-profile-v0", 18)]
    [InlineData("uds-profile-v2", 18)]
    [InlineData("uds-profile-v1", 999)]
    [InlineData("uds-profile-v1", 1)]
    public void IncompatiblePrimaryIsPreservedWithoutConversionOrBackupRollback(string? format, int schema)
    {
        using var directory = new TemporaryDirectory();
        var first = Repository(directory.Path);
        first.Open(Identity());
        var originalGeneration = first.CurrentGenerationId;
        var path = first.CurrentProfilePath!;
        first.CloseClean();
        var store = new AtomicJsonStore<ProfileDocument>();
        var incompatible = store.Load(path).Value!;
        incompatible.FormatId = format!;
        incompatible.SchemaVersion = schema;
        store.Save(path, incompatible); // Retains a valid current-format backup.
        var primaryBytes = File.ReadAllBytes(path);

        var second = Repository(directory.Path);
        var result = second.Open(Identity());
        Assert.True(result.UnsupportedSchemaArchived);
        Assert.True(result.CreatedNew);
        Assert.NotEqual(originalGeneration, second.CurrentGenerationId);
        var archivedPrimary = Assert.Single(Directory.GetFiles(
            Path.Combine(directory.Path, "profiles", "slot-01", "archives"), "profile.json", SearchOption.AllDirectories));
        Assert.Equal(primaryBytes, File.ReadAllBytes(archivedPrimary));
        Assert.True(File.GetAttributes(archivedPrimary).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal(ProductInfo.ProfileFormatId, second.Current.FormatId);
        second.CloseClean();
    }

    [Fact]
    public void CurrentEnvelopeWithMissingStatisticsIsRejectedBeforeNormalization()
    {
        var profile = new ProfileDocument { Statistics = null! };
        Assert.NotNull(ProfileFormat.ValidateRecoveryCandidate(profile));
        Assert.Throws<NotSupportedException>(() => ProfileFormat.Normalize(profile));
        Assert.Null(profile.Statistics);
    }

    private static ProfileRepository Repository(string path) => new(path, () => Now, () => Guid.NewGuid().ToString("N"));
    private static SaveIdentitySnapshot Identity() => new() { Slot = 1, GameVersion = "2.3.30" };
}
