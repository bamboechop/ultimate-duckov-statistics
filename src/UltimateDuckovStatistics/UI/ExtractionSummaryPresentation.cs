using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

internal static class ExtractionSummaryPresentation
{
    public static string[] Values(ExtractionSummarySnapshot? snapshot, Func<string, string> text)
    {
        var unavailable = text("ui.unavailable");
        var duration = unavailable;
        if (snapshot?.ActiveSeconds is { } seconds && seconds < TimeSpan.MaxValue.TotalSeconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            duration = $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }
        var kills = snapshot == null ? unavailable : snapshot.KillsComplete
            ? snapshot.Kills.ToString("N0", CultureInfo.CurrentCulture)
            : snapshot.Kills > 0 ? snapshot.Kills.ToString("N0", CultureInfo.CurrentCulture) + " (" + text("ui.results_partial") + ")"
            : unavailable;
        var value = snapshot?.EstimatedNetValue is { } amount
            ? amount.ToString("+#,0.##;-#,0.##;0", CultureInfo.CurrentCulture) : unavailable;
        return [duration, kills, value];
    }

    public static string Note(ExtractionSummarySnapshot? snapshot, Func<string, string> text) =>
        text(snapshot?.Outcome == RunOutcome.Died ? "ui.results_death_value"
            : snapshot?.EstimatedNetValue == null ? "ui.results_missing_value" : "ui.results_estimate");
}
