using System.Text.RegularExpressions;

namespace UltimateDuckovStatistics.UI;

// Display-only redaction. Clipboard operations retain the original path.
internal static class DiagnosticsPathPrivacy
{
    private static readonly char[] Separators = { '\\', '/' };
    public static string Redact(string value, string userProfile)
    {
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var components = userProfile.TrimEnd(Separators).Split(Separators);
            var pattern = string.Join(@"[\\/]", components.Select(Regex.Escape));
            value = Regex.Replace(value, pattern + @"(?=[\\/]|$|[""\r\n])", "<user-profile>", RegexOptions.IgnoreCase);
        }
        return Regex.Replace(value, @"(?i)([a-z]:[\\/]Users[\\/])[^\\/\r\n""<>]+(?=[\\/])", "$1<username>");
    }

    public static string ShortPath(string path)
    {
        var parts = path.TrimEnd(Separators).Split(Separators);
        return parts.Length == 0 || parts[parts.Length - 1].Length == 0 ? "…" : "…/" + parts[parts.Length - 1];
    }
}
