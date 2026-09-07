[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$DuckovPath,
    [Parameter(Mandatory = $true)] [ValidatePattern('^[a-f0-9]{40}$')] [string]$SourceCommit
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedCommit = & git -c "safe.directory=$repoRoot" -C $repoRoot rev-parse "$SourceCommit^{commit}"
if ($LASTEXITCODE -ne 0 -or $resolvedCommit -ne $SourceCommit) { throw 'Reproducibility requires an exact available commit.' }
$reproRoot = Join-Path $repoRoot ('artifacts/reproducibility/' + $SourceCommit + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $reproRoot | Out-Null
$sourceArchive = Join-Path $reproRoot 'source.zip'
& git -c "safe.directory=$repoRoot" -C $repoRoot archive --format=zip --output=$sourceArchive $SourceCommit
if ($LASTEXITCODE -ne 0) { throw 'Immutable source archive failed.' }
$env:DUCKOV_PATH = (Resolve-Path -LiteralPath $DuckovPath).Path
$records = @()
foreach ($checkoutName in @('checkout-a', 'different-long-checkout-root-b')) {
    $checkout = Join-Path $reproRoot $checkoutName
    Expand-Archive -LiteralPath $sourceArchive -DestinationPath $checkout
    & dotnet build (Join-Path $checkout 'src/UltimateDuckovStatistics/UltimateDuckovStatistics.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Reproducibility build failed.' }
    & (Join-Path $checkout 'scripts/package.ps1') -DuckovPath $DuckovPath
    $package = Join-Path $checkout 'artifacts/package/UltimateDuckovStatistics'
    $zip = Join-Path $checkout 'artifacts/candidate.zip'
    $version = ((Get-Content -LiteralPath (Join-Path $package 'info.ini') | Where-Object { $_ -match '^\s*version\s*=' }) -split '=', 2)[1].Trim()
    & (Join-Path $checkout 'scripts/archive-package.ps1') -PackagePath $package -ArchivePath $zip -Version $version
    $files = @($zip) + @(Get-ChildItem -File -LiteralPath (Join-Path $checkout 'src/UltimateDuckovStatistics/bin/Release/netstandard2.1') | Where-Object { $_.Name -match '^UltimateDuckovStatistics(?:\.Core)?\.(?:dll|pdb)$' } | ForEach-Object { $_.FullName })
    $hashes = [ordered]@{}
    foreach ($file in $files) { $hashes[[IO.Path]::GetFileName($file)] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
    $records += [pscustomobject]@{ Checkout = $checkoutName; Hashes = $hashes }
}
foreach ($name in $records[0].Hashes.Keys) {
    if ($records[0].Hashes[$name] -ne $records[1].Hashes[$name]) { throw "Reproducibility mismatch: $name" }
}
$report = [ordered]@{ SourceCommit = $SourceCommit; DotnetSdk = (& dotnet --version); Result = 'Pass'; Checkouts = $records }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reproRoot 'result.json') -Encoding utf8
Write-Output "Reproducibility passed: $reproRoot/result.json"
