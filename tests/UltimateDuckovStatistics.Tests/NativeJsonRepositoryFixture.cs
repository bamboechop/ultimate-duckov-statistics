using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

// Existing JSON failure fixtures explicitly retain that backend. SQLite native
// composition tests use the real default NativeProfileStorage factory.
internal static class NativeJsonRepositoryFixture
{
    internal static ProfileRepository Create(string root, Action<string> diagnostic) =>
        new(root, () => DateTime.UtcNow, () => Guid.NewGuid().ToString("N"), diagnostic, NativeProfileJsonWriter.Write);
}
