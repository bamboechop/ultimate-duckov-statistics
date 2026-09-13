namespace UltimateDuckovStatistics.Core.Persistence;

public enum ProfileMaintenanceReason { LoadingOrSleep, LongSession }

public sealed class ProfileMaintenanceResult
{
    public bool Attempted { get; internal set; }
    public bool Busy { get; internal set; }
    public long WalFrames { get; internal set; }
    public long CheckpointedFrames { get; internal set; }
}

public interface IProfileStorageMaintenance
{
    Task<ProfileMaintenanceResult> RequestMaintenance(ProfileMaintenanceReason reason);
}

public sealed partial class ProfileRepository
{
    public Task<ProfileMaintenanceResult> RequestStorageMaintenance(ProfileMaintenanceReason reason) =>
        incrementalStorage is IProfileStorageMaintenance maintenance
            ? maintenance.RequestMaintenance(reason) : Task.FromResult(new ProfileMaintenanceResult());
}
