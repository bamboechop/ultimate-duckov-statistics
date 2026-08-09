using System.Diagnostics;

namespace UltimateDuckovStatistics.Tests;

public sealed class DeploymentTests
{
    private static readonly string[] ExpectedFiles =
    {
        "info.ini",
        "INSTALL.md",
        "LICENSE",
        "UltimateDuckovStatistics.Core.dll",
        "UltimateDuckovStatistics.dll"
    };

    [Fact]
    [Trait("Category", "Package")]
    public void DeploymentReplacesStaleDestinationWithExactPermittedInventory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporaryDirectory = new TemporaryDirectory();
        var gameRoot = Path.Combine(temporaryDirectory.Path, "Duckov");
        var packageRoot = Path.Combine(temporaryDirectory.Path, "package");
        var destination = Path.Combine(gameRoot, "Duckov_Data", "Mods", "UltimateDuckovStatistics");
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(gameRoot, "Duckov.exe"), string.Empty);
        foreach (var name in ExpectedFiles)
        {
            File.WriteAllText(Path.Combine(packageRoot, name), $"package:{name}");
        }

        File.WriteAllText(Path.Combine(destination, "0Harmony.dll"), "stale forbidden dependency");
        File.WriteAllText(Path.Combine(destination, "obsolete.dll"), "stale obsolete dependency");

        var repositoryRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "scripts", "deploy.ps1"));
        startInfo.ArgumentList.Add("-DuckovPath");
        startInfo.ArgumentList.Add(gameRoot);
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(packageRoot);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Deployment failed. Output: {output} Error: {error}");
        Assert.Equal(
            ExpectedFiles.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            Directory.EnumerateFiles(destination).Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateDirectories(destination));
        Assert.False(File.Exists(Path.Combine(destination, "0Harmony.dll")));
        Assert.False(File.Exists(Path.Combine(destination, "obsolete.dll")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(destination)!, ".UltimateDuckovStatistics.*"));
    }

    [Fact]
    [Trait("Category", "Package")]
    public void BackupCleanupFailureKeepsVerifiedDeploymentAndRetainsBackup()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporaryDirectory = new TemporaryDirectory();
        var gameRoot = Path.Combine(temporaryDirectory.Path, "Duckov");
        var packageRoot = Path.Combine(temporaryDirectory.Path, "package");
        var modsRoot = Path.Combine(gameRoot, "Duckov_Data", "Mods");
        var destination = Path.Combine(modsRoot, "UltimateDuckovStatistics");
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(gameRoot, "Duckov.exe"), string.Empty);
        foreach (var name in ExpectedFiles)
        {
            File.WriteAllText(Path.Combine(packageRoot, name), $"package:{name}");
        }

        File.WriteAllText(Path.Combine(destination, "old-a.dll"), "old deployment");
        File.WriteAllText(Path.Combine(destination, "old-b.dll"), "old deployment");

        var repositoryRoot = FindRepositoryRoot();
        var wrapperPath = Path.Combine(temporaryDirectory.Path, "invoke-deploy-cleanup-failure.ps1");
        File.WriteAllText(
            wrapperPath,
            """
            param(
                [string]$DeployScript,
                [string]$DuckovPath,
                [string]$PackagePath
            )

            & $DeployScript -DuckovPath $DuckovPath -PackagePath $PackagePath -BackupCleanupAction {
                param([string]$BackupPath)
                $first = Get-ChildItem -File -LiteralPath $BackupPath | Select-Object -First 1
                if ($null -ne $first) {
                    Remove-Item -Force -LiteralPath $first.FullName
                }
                throw 'simulated partial backup cleanup failure'
            }
            """);

        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(wrapperPath);
        startInfo.ArgumentList.Add("-DeployScript");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "scripts", "deploy.ps1"));
        startInfo.ArgumentList.Add("-DuckovPath");
        startInfo.ArgumentList.Add(gameRoot);
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(packageRoot);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Deployment failed. Output: {output} Error: {error}");
        Assert.Contains("verified UDS deployment succeeded", output + error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ExpectedFiles.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            Directory.EnumerateFiles(destination).Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateDirectories(destination));
        var retainedBackup = Assert.Single(Directory.EnumerateDirectories(
            modsRoot,
            ".UltimateDuckovStatistics.previous-*"));
        Assert.Single(Directory.EnumerateFiles(retainedBackup));
        Assert.Empty(Directory.EnumerateDirectories(modsRoot, ".UltimateDuckovStatistics.deploying-*"));
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
