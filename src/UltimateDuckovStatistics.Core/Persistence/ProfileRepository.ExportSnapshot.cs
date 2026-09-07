using System.Runtime.Serialization.Json;

namespace UltimateDuckovStatistics.Core.Persistence;

public sealed partial class ProfileRepository
{
    // Export workers may outlive any gameplay/lifecycle boundary. Unlike the
    // cadence writer's snapshot, no aggregate or completed-run object can be
    // shared with the active repository once this main-thread capture returns.
    public ProfilePersistenceSnapshot CaptureExportSnapshot()
    {
        if (current == null || currentDirectory == null)
            throw new InvalidOperationException("No current profile can be captured.");
        EnsureSchemaCanBeSaved(current);
        var serializer = new DataContractJsonSerializer(typeof(ProfileDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, current);
        stream.Position = 0;
        var document = (ProfileDocument)serializer.ReadObject(stream)!;
        document.SchemaVersion = ProductInfo.SchemaVersion;
        document.Statistics.SchemaVersion = ProductInfo.SchemaVersion;
        document.Statistics.SaveGenerationId = document.GenerationId;
        return new ProfilePersistenceSnapshot(GetProfilePath(currentDirectory), document);
    }
}
