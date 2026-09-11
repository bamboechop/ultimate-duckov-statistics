using System.Diagnostics;

namespace UltimateDuckovStatistics.Tests;

public sealed class DeploymentTests
{
    private static readonly string[] ExpectedFiles =
    {
        "info.ini",
        "INSTALL.md",
        "LICENSE",
        "preview.png",
        "UltimateDuckovStatistics.Core.dll",
        "UltimateDuckovStatistics.dll"
    };

    [Theory]
    [Trait("Category", "Package")]
    [InlineData("0Harmony.dll", "Forbidden dependency")]
    [InlineData("TeamSoda.Duckov.Core.dll", "Forbidden dependency")]
    [InlineData("UnityEngine.CoreModule.dll", "Framework/game dependency")]
    [InlineData("System.Runtime.dll", "Framework/game dependency")]
    [InlineData("uds-ui-equipment-loadouts.jpg", "exactly the six permitted files")]
    [InlineData("uds-ui-equipment-weapons.jpg", "exactly the six permitted files")]
    [InlineData("uds-ui-equipment-armor-and-gear.jpg", "exactly the six permitted files")]
    [InlineData("uds-ui-equipment-totems.jpg", "exactly the six permitted files")]
    [InlineData(null, "Package is missing required file: preview.png")]
    public void PackageVerificationRejectsInvalidInventory(string? dependencyName, string expectedError)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporaryDirectory = new TemporaryDirectory();
        foreach (var name in ExpectedFiles)
        {
            File.WriteAllText(Path.Combine(temporaryDirectory.Path, name), $"package:{name}");
        }

        if (dependencyName == null)
        {
            File.Delete(Path.Combine(temporaryDirectory.Path, "preview.png"));
        }
        else
        {
            File.WriteAllText(Path.Combine(temporaryDirectory.Path, dependencyName), "must not be bundled");
        }
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
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "scripts", "verify-package.ps1"));
        startInfo.ArgumentList.Add("-PackagePath");
        startInfo.ArgumentList.Add(temporaryDirectory.Path);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains(expectedError, output + error, StringComparison.OrdinalIgnoreCase);
    }

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
        var backupLine = output.Split('\n').Single(line => line.StartsWith("Verified prior UDS deployment retained at: ", StringComparison.Ordinal));
        var backupPath = backupLine["Verified prior UDS deployment retained at: ".Length..].Trim();
        Assert.Equal("stale forbidden dependency", File.ReadAllText(Path.Combine(backupPath, "0Harmony.dll")));
        Assert.Equal("stale obsolete dependency", File.ReadAllText(Path.Combine(backupPath, "obsolete.dll")));
        foreach (var name in ExpectedFiles)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(packageRoot, name)), File.ReadAllBytes(Path.Combine(destination, name)));
            Assert.Contains(name + " SHA256=", output, StringComparison.Ordinal);
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(destination)!, ".UltimateDuckovStatistics.*"));
    }

    [Fact]
    [Trait("Category", "Package")]
    public void BackupCleanupFailureCannotExposeAPriorManifestToNativeModDiscovery()
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

        File.WriteAllText(Path.Combine(destination, "info.ini"), "name = UltimateDuckovStatistics");
        File.WriteAllText(Path.Combine(destination, "UltimateDuckovStatistics.dll"), "old deployment");

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
                throw 'simulated denied backup cleanup'
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
        // ModManager.Rescan enumerates every immediate Mods subdirectory, even dot names.
        Assert.Equal(destination, Assert.Single(Directory.EnumerateDirectories(modsRoot)));
        var retainedBackup = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(gameRoot, "Duckov_Data", ".UltimateDuckovStatistics-deployment"),
            "previous", SearchOption.AllDirectories));
        Assert.Equal("name = UltimateDuckovStatistics", File.ReadAllText(Path.Combine(retainedBackup, "info.ini")));
        Assert.Equal("old deployment", File.ReadAllText(Path.Combine(retainedBackup, "UltimateDuckovStatistics.dll")));
        Assert.Equal("package:UltimateDuckovStatistics.dll", File.ReadAllText(Path.Combine(destination, "UltimateDuckovStatistics.dll")));

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
