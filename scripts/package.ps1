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
$sqliteAssembly = Join-Path $modOutput 'UltimateDuckovStatistics.Sqlite.dll'
. (Join-Path $PSScriptRoot 'package-inventory.ps1')

if (-not (Test-Path -LiteralPath (Join-Path $DuckovPath 'Duckov_Data\Managed\TeamSoda.Duckov.Core.dll'))) {
    throw "DuckovPath does not contain the expected managed assemblies: $DuckovPath"
}
if (-not (Test-Path -LiteralPath $modAssembly) -or -not (Test-Path -LiteralPath $coreAssembly) -or -not (Test-Path -LiteralPath $sqliteAssembly)) {
    throw 'Release assemblies are missing. A successful Release build is required before packaging.'
}

$modSources = Get-ChildItem -Recurse -File -Path (Join-Path $repoRoot 'src\UltimateDuckovStatistics') -Include '*.cs','*.csproj' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
$coreSources = Get-ChildItem -Recurse -File -Path (Join-Path $repoRoot 'src\UltimateDuckovStatistics.Core') -Include '*.cs','*.csproj' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
$sqliteSources = Get-ChildItem -Recurse -File -Path (Join-Path $repoRoot 'src\UltimateDuckovStatistics.Sqlite') -Include '*.cs','*.csproj' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
$newestSqliteSourceWrite = ($sqliteSources | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
$newestModSourceWrite = ($modSources | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
$newestCoreSourceWrite = ($coreSources | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
$buildInputs = @(Get-ChildItem -File -LiteralPath $repoRoot | Where-Object { $_.Extension -in @('.props', '.targets') -or $_.Name -in @('.editorconfig', 'global.json') })
foreach ($buildInput in $buildInputs) {
    if ((Get-Item -LiteralPath $modAssembly).LastWriteTimeUtc -lt $buildInput.LastWriteTimeUtc -or
        (Get-Item -LiteralPath $coreAssembly).LastWriteTimeUtc -lt $buildInput.LastWriteTimeUtc) {
        throw "Release assemblies are older than build configuration: $($buildInput.Name). Rebuild before packaging."
    }
}
if ((Get-Item -LiteralPath $modAssembly).LastWriteTimeUtc -lt $newestModSourceWrite -or
    (Get-Item -LiteralPath $coreAssembly).LastWriteTimeUtc -lt $newestCoreSourceWrite -or
    (Get-Item -LiteralPath $sqliteAssembly).LastWriteTimeUtc -lt $newestSqliteSourceWrite) {
    throw 'Release assemblies are older than source files. Rebuild successfully before packaging.'
}

$versionLine = Get-Content -LiteralPath (Join-Path $repoRoot 'mod\info.ini') | Where-Object { $_ -match '^\s*version\s*=' }
if (@($versionLine).Count -ne 1) { throw 'info.ini must declare exactly one version.' }
$version = ($versionLine -split '=', 2)[1].Trim()
& (Join-Path $PSScriptRoot 'audit-artifacts.ps1') -OrdinaryRelease -ExpectedVersion $version -InputPaths @($modAssembly, $coreAssembly, $sqliteAssembly, (Join-Path $modOutput 'UltimateDuckovStatistics.Sqlite.pdb'), (Join-Path $modOutput 'UltimateDuckovStatistics.pdb'), (Join-Path $modOutput 'UltimateDuckovStatistics.Core.pdb'))

if (Test-Path -LiteralPath $packageRoot) {
    if ([IO.Path]::GetFullPath($packageRoot) -ne [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\package\UltimateDuckovStatistics'))) { throw 'Unsafe package cleanup path.' }
    if ((Get-Item -LiteralPath $packageRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package directory must not be a reparse point.' }
    Remove-Item -Recurse -Force -LiteralPath $packageRoot
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$files = @(Get-UdsPackageInputs -RepositoryRoot $repoRoot -BuildRoot $modOutput)

Test-UdsPinnedDependencies -Directory $modOutput

foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file.Source)) {
        throw "Required package input is missing: $($file.Source)"
    }
    Copy-Item -LiteralPath $file.Source -Destination (Join-Path $packageRoot $file.Destination)
}

& (Join-Path $PSScriptRoot 'audit-artifacts.ps1') -OrdinaryRelease -InputPaths @($packageRoot)
Write-Output $packageRoot
