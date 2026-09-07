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
function Assert-DuckovClosed {
    if (@(Get-Process -Name Duckov -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Duckov must be closed before UDS deployment.' }
}
function Get-TreeHashes([string]$Root) {
    $result = [ordered]@{}
    $entries = @(Get-ChildItem -Recurse -Force -LiteralPath $Root)
    if ((Get-Item -LiteralPath $Root).Attributes -band [IO.FileAttributes]::ReparsePoint -or
        @($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
        throw 'Deployment inputs and destinations must not contain reparse points.'
    }
    foreach ($file in $entries | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName)
        $result[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $result
}
function Assert-TreeHashes([string]$Root, $Expected) {
    $actual = Get-TreeHashes $Root
    if ($actual.Count -ne $Expected.Count) { throw 'Deployment inventory changed during replacement.' }
    foreach ($name in $Expected.Keys) {
        if ($actual[$name] -ne $Expected[$name]) { throw "Deployment hash mismatch: $name" }
    }
}
Assert-DuckovClosed
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
foreach ($path in @($duckovRoot, (Join-Path $duckovRoot 'Duckov_Data'), $modsRoot)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Deployment ancestors must not be reparse points.'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $duckovRoot 'Duckov.exe'))) {
    throw "Duckov executable not found under: $DuckovPath"
}
if (-not (Test-Path -LiteralPath $source)) {
    throw 'Build and validate the package before deployment.'
}

& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $source
$source = (Resolve-Path -LiteralPath $source).Path
$sourceHashes = Get-TreeHashes $source
New-Item -ItemType Directory -Path $modsRoot -Force | Out-Null

$deploymentId = [Guid]::NewGuid().ToString('N')
$staging = Join-Path $modsRoot ".UltimateDuckovStatistics.deploying-$deploymentId"
$backup = Join-Path $modsRoot ".UltimateDuckovStatistics.previous-$deploymentId"
$retainedBackupRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts/deployment-backups/$deploymentId"))
$retainedBackup = Join-Path $retainedBackupRoot 'UltimateDuckovStatistics'
foreach ($managedPath in @($staging, $backup, $destination)) {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($managedPath)) -ne $modsRoot) { throw 'Unsafe UDS replacement path.' }
}
if (-not $retainedBackupRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/deployment-backups')) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe reversible backup path.' }
$destinationMoved = $false
$stagingPromoted = $false
$deploymentCommitted = $false

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    foreach ($file in Get-ChildItem -File -LiteralPath $source) {
        Copy-Item -Force -LiteralPath $file.FullName -Destination (Join-Path $staging $file.Name)
    }
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $staging
    Assert-TreeHashes $staging $sourceHashes

    if (Test-Path -LiteralPath $destination) {
        $previousHashes = Get-TreeHashes $destination
        New-Item -ItemType Directory -Path $retainedBackupRoot -Force | Out-Null
        Copy-Item -Recurse -LiteralPath $destination -Destination $retainedBackup
        Assert-TreeHashes $retainedBackup $previousHashes
        Assert-TreeHashes $destination $previousHashes
        $previousHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $retainedBackupRoot 'previous-hashes.json') -Encoding utf8
        Assert-DuckovClosed
        Move-Item -LiteralPath $destination -Destination $backup
        $destinationMoved = $true
    }

    Assert-DuckovClosed
    Move-Item -LiteralPath $staging -Destination $destination
    $stagingPromoted = $true
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $destination
    Assert-TreeHashes $destination $sourceHashes
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

foreach ($name in $sourceHashes.Keys) { Write-Output "$name SHA256=$($sourceHashes[$name])" }
if (Test-Path -LiteralPath $retainedBackup) { Write-Output "Verified prior UDS deployment retained at: $retainedBackup" }
Write-Output "Deployed UDS to: $destination"
