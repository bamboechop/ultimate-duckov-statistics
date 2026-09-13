using System.Text;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProfileJsonWriterPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeWrittenPrimaryAndTemporaryRemainReadableWithDcs(bool temporary)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>(NativeProfileJsonWriter.Write);
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        store.Save(path, profile);
        if (temporary) File.Move(path, path + ".tmp");
        var loaded = new AtomicJsonStore<ProfileDocument>().Load(path, ProfileFormat.ValidateRecoveryCandidate);
        Assert.Equal(temporary ? AtomicJsonLoadSource.Temporary : AtomicJsonLoadSource.Primary, loaded.Source);
        Assert.Equal(profile.GenerationId, loaded.Value!.GenerationId);
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(loaded.Value));
    }

    [Theory]
    [InlineData("syntax")]
    [InlineData("required")]
    public void InvalidNativeWrittenPrimaryStillLosesToAnIntactBackup(string corruption)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profile.json");
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var store = new AtomicJsonStore<ProfileDocument>(NativeProfileJsonWriter.Write);
        store.Save(path, profile);
        var initial = File.ReadAllBytes(path);
        profile.Revision++;
        profile.Statistics = null!;
        store.Save(path, profile);
        if (corruption == "syntax") File.WriteAllText(path, "{broken");
        var loaded = store.Load(path, ProfileFormat.ValidateRecoveryCandidate);
        Assert.Equal(AtomicJsonLoadSource.Backup, loaded.Source);
        Assert.Equal(initial, File.ReadAllBytes(path));
        Assert.Equal(initial, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public void WriterFailureCannotReplacePrimaryOrBackupAndRemainsRetryable()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profile.json");
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var normal = new AtomicJsonStore<ProfileDocument>(NativeProfileJsonWriter.Write);
        normal.Save(path, profile);
        profile.Revision++;
        normal.Save(path, profile);
        var primary = File.ReadAllBytes(path);
        var backup = File.ReadAllBytes(path + ".bak");
        var throwing = new AtomicJsonStore<ProfileDocument>((stream, _) =>
        {
            stream.Write(Encoding.UTF8.GetBytes("{partial"));
            throw new IOException("expected writer failure");
        });
        Assert.Throws<IOException>(() => throwing.Save(path, profile));
        Assert.Equal(primary, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        profile.Revision++;
        normal.Save(path, profile);
        Assert.Equal(profile.Revision, normal.Load(path).Value!.Revision);
        Assert.Equal(primary, File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public void RepositoryUsesWriterForProfilesAndPreservesValidationAndRetryOwnership()
    {
        using var directory = new TemporaryDirectory();
        var fail = false;
        var calls = 0;
        var repository = new ProfileRepository(directory.Path, () => NativeProfileJsonWriterTests.Now, () => Guid.NewGuid().ToString("N"),
            writeProfile: (stream, profile) =>
            {
                calls++;
                if (fail) throw new IOException("expected profile writer failure");
                NativeProfileJsonWriter.Write(stream, profile);
            });
        var identity = new SaveIdentitySnapshot { Slot = 1, GameVersion = "2.3.30" };
        repository.Open(identity);
        Assert.True(calls > 0);
        var gameVersion = repository.Current.Identity.GameVersion;
        repository.Current.Identity.GameVersion = null!;
        var snapshot = repository.CapturePersistenceSnapshot();
        var before = calls;
        Assert.Throws<InvalidOperationException>(() => repository.SaveSnapshot(snapshot));
        Assert.Equal(before, calls);
        repository.Current.Identity.GameVersion = gameVersion;
        var clock = 0d;
        var writer = new DeferredSnapshotWriter<ProfilePersistenceSnapshot>(repository.CapturePersistenceSnapshot, repository.SaveSnapshot, () => clock);
        var receipt = repository.LastSaveReceipt;
        fail = true;
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        Assert.True(writer.IsDirty);
        Assert.Same(receipt, repository.LastSaveReceipt);
        var failedCalls = calls;
        Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        Assert.Equal(failedCalls, calls);
        fail = false;
        clock = 1;
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.False(writer.IsDirty);
        Assert.NotSame(receipt, repository.LastSaveReceipt);
        var generation = repository.CurrentGenerationId;
        repository.CloseClean();
        var reopened = new ProfileRepository(directory.Path, () => NativeProfileJsonWriterTests.Now, () => Guid.NewGuid().ToString("N"));
        Assert.False(reopened.Open(identity).CreatedNew);
        Assert.Equal(generation, reopened.CurrentGenerationId);
        reopened.CloseClean();
    }
}
