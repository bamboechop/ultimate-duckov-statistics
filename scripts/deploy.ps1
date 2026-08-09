[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DuckovPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'artifacts\package\UltimateDuckovStatistics'
$modsRoot = Join-Path $DuckovPath 'Duckov_Data\Mods'
$destination = Join-Path $modsRoot 'UltimateDuckovStatistics'

if (-not (Test-Path -LiteralPath (Join-Path $DuckovPath 'Duckov.exe'))) {
    throw "Duckov executable not found under: $DuckovPath"
}
if (-not (Test-Path -LiteralPath $source)) {
    throw 'Build and validate the package before deployment.'
}

& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $source
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $source '*') -Destination $destination

Write-Output "Deployed UDS to: $destination"
