[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$ReproducibilityResult,
    [Parameter(Mandatory = $true)] [string]$PackagePath,
    [Parameter(Mandatory = $true)] [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Refusing to overwrite a frozen campaign.' }
$proof = Get-Content -LiteralPath $ReproducibilityResult -Raw | ConvertFrom-Json
if ($proof.Result -ne 'Pass' -or @($proof.Checkouts).Count -ne 2 -or $proof.SourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'A successful immutable two-checkout proof is required.' }
$versionLine = Get-Content -LiteralPath (Join-Path $PackagePath 'info.ini') | Where-Object { $_ -match '^\s*version\s*=' }
$version = ($versionLine -split '=', 2)[1].Trim()
& (Join-Path $PSScriptRoot 'audit-artifacts.ps1') -OrdinaryRelease -ExpectedVersion $version -InputPaths @($PackagePath)
$hashes = [ordered]@{}
foreach ($name in @('UltimateDuckovStatistics.dll', 'UltimateDuckovStatistics.Core.dll')) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $PackagePath $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $proof.Checkouts[0].Hashes.$name -or $hash -ne $proof.Checkouts[1].Hashes.$name) { throw "Candidate differs from immutable proof: $name" }
    $hashes[$name] = $hash
}
$matrixPath = Join-Path $PSScriptRoot '../docs/M18_CAPTURE_MATRIX.json'
$campaign = [ordered]@{
    SchemaVersion = 1
    CreatedUtc = (Get-Date).ToUniversalTime().ToString('O')
    SourceCommit = $proof.SourceCommit
    Version = $version
    DllHashes = $hashes
    Matrix = (Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json)
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$campaign | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
Write-Output "Frozen campaign: $resolvedOutput"
Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256
