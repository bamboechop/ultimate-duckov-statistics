using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class DiagnosticsPathPrivacyTests
{
    [Theory]
    [InlineData(@"Saved C:\Users\Streamer\AppData\LocalLow\profile.json", @"C:\Users\Streamer")]
    [InlineData("Saved C:/Users/Streamer/AppData/LocalLow/profile.json", @"C:\Users\Streamer")]
    [InlineData(@"Saved d:\Profiles\Streamer\export.json", @"D:\Profiles\Streamer")]
    [InlineData(@"Cannot open D:\Profiles\Streamer", @"D:\Profiles\Streamer")]
    [InlineData(@"Saved C:\Users\Streamer\export.json", "")]
    public void DisplayRedactsUserDirectoriesWithoutChangingTheSource(string source, string home)
    {
        var original = source;
        var display = DiagnosticsPathPrivacy.Redact(source, home);
        Assert.DoesNotContain("Streamer", display, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, source);
    }

    [Fact]
    public void KnownHomePrefixDoesNotMatchAnotherDirectoryName()
    {
        const string source = @"D:\Profiles\Alpine\export.json";
        Assert.Equal(source, DiagnosticsPathPrivacy.Redact(source, @"D:\Profiles\Al"));
    }

    [Theory]
    [InlineData(@"C:\Users\Streamer\AppData\LocalLow\exports\statistics.json")]
    [InlineData("C:/Users/Streamer/AppData/LocalLow/exports/statistics.json")]
    public void ShortExportLocationKeepsOnlyTheFileName(string path)
    {
        Assert.Equal("…/statistics.json", DiagnosticsPathPrivacy.ShortPath(path));
    }
}
