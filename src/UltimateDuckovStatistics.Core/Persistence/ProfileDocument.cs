using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

[DataContract]
public sealed class SaveIdentitySnapshot
{
    [DataMember(Order = 1)]
    public int Slot { get; set; }

    [DataMember(Order = 2)]
    public bool SaveFilePresent { get; set; }

    [DataMember(Order = 3, EmitDefaultValue = false)]
    public long? SaveFileCreationUtcTicks { get; set; }

    [DataMember(Order = 4, EmitDefaultValue = false)]
    public long? ObservedWriteUtcTicks { get; set; }

    [DataMember(Order = 5, EmitDefaultValue = false)]
    public long? ObservedLength { get; set; }

    [DataMember(Order = 6)]
    public string GameVersion { get; set; } = string.Empty;

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public string? ContentSha256 { get; set; }
}

[DataContract]
public sealed class CapabilityRecord
{
    [DataMember(Order = 1)]
    public string AdapterId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public AdapterCapabilityState State { get; set; }

    [DataMember(Order = 3)]
    public string Version { get; set; } = string.Empty;

    [DataMember(Order = 4, EmitDefaultValue = false)]
    public string? Detail { get; set; }
}

[DataContract]
public sealed class ProfileDocument
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string GenerationId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int Slot { get; set; }

    [DataMember(Order = 4)]
    public string GenerationReason { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public DateTime CreatedUtc { get; set; }

    [DataMember(Order = 6)]
    public DateTime UpdatedUtc { get; set; }

    [DataMember(Order = 7)]
    public long Revision { get; set; }

    [DataMember(Order = 8)]
    public long InterruptedSessionCount { get; set; }

    [DataMember(Order = 9)]
    public SaveIdentitySnapshot Identity { get; set; } = new();

    [DataMember(Order = 10)]
    public ProfileStatistics Statistics { get; set; } = new();

    [DataMember(Order = 11)]
    public List<CapabilityRecord> Capabilities { get; set; } = new();
}

[DataContract]
public sealed class SessionCheckpoint
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string GenerationId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public DateTime StartedUtc { get; set; }

    [DataMember(Order = 5)]
    public long ProfileRevisionAtStart { get; set; }
}
