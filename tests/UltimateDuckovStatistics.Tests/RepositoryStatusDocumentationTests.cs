using System.Text.RegularExpressions;

namespace UltimateDuckovStatistics.Tests;

public sealed class RepositoryStatusDocumentationTests
{
    [Fact]
    [Trait("Category", "SourceSafety")]
    public void ReadmeDelegatesMutablePullRequestStateInsteadOfEmbeddingAStaleSnapshot()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md"));
        var statusParagraph = Regex
            .Split(readme, "\\r?\\n\\r?\\n")
            .Single(paragraph => paragraph.StartsWith("M10 delivery status is tracked", StringComparison.Ordinal));

        Assert.Contains(
            "GitHub pull requests](https://github.com/bamboechop/ultimate-duckov-statistics/pulls)",
            statusParagraph,
            StringComparison.Ordinal);
        Assert.Contains(
            "is authoritative for the live remote head, review state, and CI results",
            statusParagraph,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("\\b[0-9a-f]{7,40}\\b", RegexOptions.CultureInvariant), statusParagraph);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts", "deploy.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
