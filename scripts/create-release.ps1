[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DuckovPath,

    [string]$Version = '0.3.0'
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version must be a semantic version without a leading v: $Version"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -DuckovPath $DuckovPath

$packageRoot = Join-Path $repoRoot 'artifacts\package\UltimateDuckovStatistics'
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $packageRoot

$releaseRoot = Join-Path $repoRoot 'artifacts\release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$archiveName = "UltimateDuckovStatistics-v$Version.zip"
$archivePath = Join-Path $releaseRoot $archiveName
$checksumPath = "$archivePath.sha256"

foreach ($path in @($archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -Force -LiteralPath $path
    }
}

Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $expected = @(
        'UltimateDuckovStatistics/info.ini',
        'UltimateDuckovStatistics/INSTALL.md',
        'UltimateDuckovStatistics/LICENSE',
        'UltimateDuckovStatistics/UltimateDuckovStatistics.Core.dll',
        'UltimateDuckovStatistics/UltimateDuckovStatistics.dll'
    )
    $actual = @($entries | ForEach-Object { $_.FullName.Replace('\', '/') } | Sort-Object)
    $expectedSorted = @($expected | Sort-Object)
    if (Compare-Object -ReferenceObject $expectedSorted -DifferenceObject $actual) {
        throw "Release ZIP entries differ from the required installable package: $($actual -join ', ')"
    }

    $forbidden = $actual | Where-Object {
        $name = [System.IO.Path]::GetFileName($_)
        $name -in @('0Harmony.dll', 'TeamSoda.Duckov.Core.dll', 'ItemStatsSystem.dll', 'Assembly-CSharp.dll') -or
        ($name.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase) -and
            ($name.StartsWith('Unity', [System.StringComparison]::OrdinalIgnoreCase) -or
             $name.StartsWith('System.', [System.StringComparison]::OrdinalIgnoreCase) -or
             $name.Equals('mscorlib.dll', [System.StringComparison]::OrdinalIgnoreCase)))
    }
    if ($forbidden) {
        throw "Forbidden dependency found in release ZIP: $($forbidden -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

$sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = "$sha256  $archiveName`n"
[System.IO.File]::WriteAllText(
    $checksumPath,
    $checksum,
    [System.Text.UTF8Encoding]::new($false))

[PSCustomObject]@{
    Archive = $archivePath
    Checksum = $checksumPath
    Sha256 = $sha256
}
