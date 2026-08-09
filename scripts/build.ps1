[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DuckovPath = $env:DUCKOV_PATH
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($DuckovPath)) {
    throw 'DuckovPath (or DUCKOV_PATH) is required for the complete local build.'
}

$env:DUCKOV_PATH = (Resolve-Path -LiteralPath $DuckovPath).Path

dotnet restore (Join-Path $repoRoot 'UltimateDuckovStatistics.sln')
dotnet test (Join-Path $repoRoot 'tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj') -c Release --no-restore
dotnet run --project (Join-Path $repoRoot 'tools\DuckovContractProbe\DuckovContractProbe.csproj') -c Release --no-restore -- $env:DUCKOV_PATH
dotnet build (Join-Path $repoRoot 'src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj') -c Release --no-restore

& (Join-Path $PSScriptRoot 'package.ps1') -DuckovPath $env:DUCKOV_PATH
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath (Join-Path $repoRoot 'artifacts\package\UltimateDuckovStatistics')
