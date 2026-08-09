using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Persistence;

[DataContract]
public sealed class UserSettings
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string PanelHotkey { get; set; } = "F8";
}
