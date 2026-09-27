using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Persistence;

[DataContract]
public sealed record class KillFeedSettings
{
    [DataMember(Order = 1)] public bool Enabled { get; set; } = true;
    [DataMember(Order = 2)] public int DurationSeconds { get; set; } = 10;
    [DataMember(Order = 3)] public int MaximumEntries { get; set; } = 6;
    [DataMember(Order = 4)] public float Scale { get; set; } = 1;
    [DataMember(Order = 5)] public bool AlignRight { get; set; }
    [DataMember(Order = 6)] public int OffsetX { get; set; }
    [DataMember(Order = 7)] public int OffsetY { get; set; }
    [DataMember(Order = 8)] public bool ShowDistance { get; set; } = true;
    [DataMember(Order = 9)] public bool ShowHeadshots { get; set; } = true;

    public KillFeedSettings Normalize() => this with
    {
        DurationSeconds = Math.Clamp(DurationSeconds, 1, 30),
        MaximumEntries = Math.Clamp(MaximumEntries, 1, 6),
        Scale = float.IsNaN(Scale) || float.IsInfinity(Scale) ? 1 : Math.Clamp(Scale, .5f, 2),
        OffsetX = Math.Clamp(OffsetX, -500, 500),
        OffsetY = Math.Clamp(OffsetY, -500, 500)
    };

    [OnDeserializing]
    private void BeforeRead(StreamingContext _)
    {
        Enabled = ShowDistance = ShowHeadshots = true;
        DurationSeconds = 10; MaximumEntries = 6; Scale = 1;
    }
}
