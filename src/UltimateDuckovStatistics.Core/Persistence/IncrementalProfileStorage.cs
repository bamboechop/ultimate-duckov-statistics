using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Persistence;

/// <summary>Generation storage. Only repository-produced, owned commands can be submitted.</summary>
public interface IIncrementalProfileStorage : IDisposable
{
    string Path { get; }
    IncrementalProfileState? Load();
    void Import(ProfileDocument profile, SessionCheckpoint? session, ActiveRunCheckpoint? checkpoint, bool sessionEvidencePresent = false);
    Task Commit(IncrementalProfileWrite write);
    Task Drain();
}

public sealed class IncrementalProfileState
{
    public IncrementalProfileState(ProfileDocument profile, SessionCheckpoint? session, ActiveRunCheckpoint? checkpoint, bool sessionEvidencePresent = false)
    { Profile = profile; Session = session; Checkpoint = checkpoint; SessionEvidencePresent = sessionEvidencePresent || session != null; }
    public ProfileDocument Profile { get; }
    public SessionCheckpoint? Session { get; }
    public ActiveRunCheckpoint? Checkpoint { get; }
    public bool SessionEvidencePresent { get; }
}
