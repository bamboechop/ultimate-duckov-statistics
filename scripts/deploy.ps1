[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DuckovPath,

    [Parameter(Mandatory = $false)]
    [string]$PackagePath,

    [Parameter(Mandatory = $false, DontShow = $true)]
    [scriptblock]$BackupCleanupAction = {
        param([string]$BackupPath)
        Remove-Item -Recurse -Force -LiteralPath $BackupPath
    }
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    Join-Path $repoRoot 'artifacts\package\UltimateDuckovStatistics'
} else {
    $PackagePath
}
$duckovRoot = [System.IO.Path]::GetFullPath($DuckovPath)
$modsRoot = [System.IO.Path]::GetFullPath((Join-Path $duckovRoot 'Duckov_Data\Mods'))
$destination = [System.IO.Path]::GetFullPath((Join-Path $modsRoot 'UltimateDuckovStatistics'))
$expectedDestination = Join-Path $modsRoot 'UltimateDuckovStatistics'

if (-not [string]::Equals($destination, $expectedDestination, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to deploy outside the exact UDS mod directory: $destination"
}
if (-not (Test-Path -LiteralPath (Join-Path $duckovRoot 'Duckov.exe'))) {
    throw "Duckov executable not found under: $DuckovPath"
}
if (-not (Test-Path -LiteralPath $source)) {
    throw 'Build and validate the package before deployment.'
}

& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $source
$source = (Resolve-Path -LiteralPath $source).Path
New-Item -ItemType Directory -Path $modsRoot -Force | Out-Null

$deploymentId = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $modsRoot ".UltimateDuckovStatistics.deploying-$deploymentId"
$backup = Join-Path $modsRoot ".UltimateDuckovStatistics.previous-$deploymentId"
$destinationMoved = $false
$stagingPromoted = $false
$deploymentCommitted = $false

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    foreach ($file in Get-ChildItem -File -LiteralPath $source) {
        Copy-Item -Force -LiteralPath $file.FullName -Destination (Join-Path $staging $file.Name)
    }
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $staging

    if (Test-Path -LiteralPath $destination) {
        Move-Item -LiteralPath $destination -Destination $backup
        $destinationMoved = $true
    }

    Move-Item -LiteralPath $staging -Destination $destination
    $stagingPromoted = $true
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $destination
    $deploymentCommitted = $true

    if ($destinationMoved) {
        try {
            & $BackupCleanupAction $backup
            $destinationMoved = $false
        }
        catch {
            Write-Warning "The verified UDS deployment succeeded, but the prior deployment backup could not be removed completely. Retained backup path: $backup. $($_.Exception.Message)"
        }
    }
}
catch {
    if (-not $deploymentCommitted) {
        if ($stagingPromoted -and (Test-Path -LiteralPath $destination)) {
            Remove-Item -Recurse -Force -LiteralPath $destination
            $stagingPromoted = $false
        }
        if ($destinationMoved -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $destination
            $destinationMoved = $false
        }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -Recurse -Force -LiteralPath $staging
    }
}

Write-Output "Deployed UDS to: $destination"
