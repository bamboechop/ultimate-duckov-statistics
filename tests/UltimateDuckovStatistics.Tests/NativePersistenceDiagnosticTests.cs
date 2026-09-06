using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativePersistenceDiagnosticTests
{
    [Fact]
    public void RepeatedPersistenceBarrierExceptionsEmitAtMostOneFullStackPerMinute()
    {
        var now = 0d;
        var exceptions = new List<Exception>();
        UnityEngine.Debug.ExceptionLogged = exceptions.Add;
        try
        {
            using var coordinator = new NativeProfileCoordinator(() => now);
            coordinator.SetActiveRunCheckpointBarrier(() => false);
            var summary = new RunSummary { RunId = "pending" };
            for (var frame = 0; frame < 10000; frame++)
                Assert.False(coordinator.HandleRunCompleted(summary));
            Assert.Single(exceptions);
            Assert.IsType<IOException>(exceptions[0]);
            now = 59.999;
            Assert.False(coordinator.HandleRunCompleted(summary));
            Assert.Single(exceptions);
            now = 60;
            Assert.False(coordinator.HandleRunCompleted(summary));
            Assert.Equal(2, exceptions.Count);
            coordinator.SetActiveRunCheckpointBarrier(() => true);
        }
        finally
        {
            UnityEngine.Debug.ExceptionLogged = null;
        }
    }
}
