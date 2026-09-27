using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Persistence;

[DataContract]
public sealed class UserSettings
{
    public const string DefaultPanelHotkey = "Ctrl+Alt+S";
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string PanelHotkey { get; set; } = DefaultPanelHotkey;
}
