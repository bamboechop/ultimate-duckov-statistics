namespace UltimateDuckovStatistics.Core.Persistence;

/// <summary>A detached export view with its own lifetime, independent of the active profile.</summary>
public sealed class ProfileExportSnapshot : IDisposable
{
    private Action? release;
    public ProfileExportSnapshot(ProfileDocument document, string profilePath, Action? release = null)
    { Document = document; ProfilePath = profilePath; this.release = release; }
    public ProfileDocument Document { get; }
    public string ProfilePath { get; }
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}

public interface IProfileExportSource
{
    // Reserve the committed generation/revision in queue order; perform the
    // potentially large copy outside the durability queue.
    Task<ProfileExportSnapshot> CaptureExport(string generation, long revision);
}
