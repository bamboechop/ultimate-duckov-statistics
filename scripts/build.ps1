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
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet test (Join-Path $repoRoot 'tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

dotnet run --project (Join-Path $repoRoot 'tools\DuckovContractProbe\DuckovContractProbe.csproj') -c Release --no-restore -- $env:DUCKOV_PATH
if ($LASTEXITCODE -ne 0) {
    throw "Duckov contract probe failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repoRoot 'tools\FrameTimeAnalyzer\FrameTimeAnalyzer.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "frame-time analyzer build failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repoRoot 'src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj') -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "native adapter build failed with exit code $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot 'package.ps1') -DuckovPath $env:DUCKOV_PATH
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath (Join-Path $repoRoot 'artifacts\package\UltimateDuckovStatistics')
