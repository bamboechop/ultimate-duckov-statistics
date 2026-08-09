[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DuckovPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repoRoot 'artifacts\package'
$packageRoot = Join-Path $outputRoot 'UltimateDuckovStatistics'
$modOutput = Join-Path $repoRoot 'src\UltimateDuckovStatistics\bin\Release\netstandard2.1'
$modAssembly = Join-Path $modOutput 'UltimateDuckovStatistics.dll'
$coreAssembly = Join-Path $modOutput 'UltimateDuckovStatistics.Core.dll'

if (-not (Test-Path -LiteralPath (Join-Path $DuckovPath 'Duckov_Data\Managed\TeamSoda.Duckov.Core.dll'))) {
    throw "DuckovPath does not contain the expected managed assemblies: $DuckovPath"
}
if (-not (Test-Path -LiteralPath $modAssembly) -or -not (Test-Path -LiteralPath $coreAssembly)) {
    throw 'Release assemblies are missing. A successful Release build is required before packaging.'
}

$modSources = Get-ChildItem -Recurse -File -Path (Join-Path $repoRoot 'src\UltimateDuckovStatistics') -Include '*.cs','*.csproj'
$coreSources = Get-ChildItem -Recurse -File -Path (Join-Path $repoRoot 'src\UltimateDuckovStatistics.Core') -Include '*.cs','*.csproj'
$newestModSourceWrite = ($modSources | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
$newestCoreSourceWrite = ($coreSources | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
if ((Get-Item -LiteralPath $modAssembly).LastWriteTimeUtc -lt $newestModSourceWrite -or
    (Get-Item -LiteralPath $coreAssembly).LastWriteTimeUtc -lt $newestCoreSourceWrite) {
    throw 'Release assemblies are older than source files. Rebuild successfully before packaging.'
}

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -Recurse -Force -LiteralPath $packageRoot
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$files = @(
    @{ Source = (Join-Path $repoRoot 'mod\info.ini'); Destination = 'info.ini' },
    @{ Source = $modAssembly; Destination = 'UltimateDuckovStatistics.dll' },
    @{ Source = $coreAssembly; Destination = 'UltimateDuckovStatistics.Core.dll' },
    @{ Source = (Join-Path $repoRoot 'INSTALL.md'); Destination = 'INSTALL.md' },
    @{ Source = (Join-Path $repoRoot 'LICENSE'); Destination = 'LICENSE' }
)

foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file.Source)) {
        throw "Required package input is missing: $($file.Source)"
    }
    Copy-Item -LiteralPath $file.Source -Destination (Join-Path $packageRoot $file.Destination)
}

Write-Output $packageRoot
