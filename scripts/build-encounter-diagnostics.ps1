[CmdletBinding()]
param([string]$DuckovPath = $env:DUCKOV_PATH)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'package-inventory.ps1')
if ([string]::IsNullOrWhiteSpace($DuckovPath)) { throw 'DuckovPath (or DUCKOV_PATH) is required.' }
$resolvedDuckovPath = (Resolve-Path -LiteralPath $DuckovPath).Path
$artifactRoot = Join-Path $repoRoot 'artifacts/encounter-history'
$buildId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffffffZ') + '-' + [Guid]::NewGuid().ToString('N')
$outputRoot = Join-Path $artifactRoot "diagnostics/$buildId"
$buildRoot = Join-Path $outputRoot 'build'
$packageRoot = Join-Path $outputRoot 'package/UltimateDuckovStatistics'

dotnet restore (Join-Path $repoRoot 'UltimateDuckovStatistics.sln')
if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE." }
dotnet build (Join-Path $repoRoot 'src/UltimateDuckovStatistics/UltimateDuckovStatistics.csproj') `
    -c Release --no-restore --no-incremental -p:UseSharedCompilation=false `
    -p:DuckovPath=$resolvedDuckovPath -p:UDSEncounterDiagnostics=true -p:UDSPerformanceDiagnostics=true `
    -p:OutputPath=$buildRoot
if ($LASTEXITCODE -ne 0) { throw "Encounter diagnostic build failed with exit code $LASTEXITCODE." }

# Unique output, never the ordinary package or deployed mod. No old artifact deletion.
New-Item -ItemType Directory -Path $packageRoot | Out-Null
foreach ($input in @(Get-UdsPackageInputs -RepositoryRoot $repoRoot -BuildRoot $buildRoot)) {
    Copy-Item -LiteralPath $input.Source -Destination (Join-Path $packageRoot $input.Destination)
}
$infoPath = Join-Path $packageRoot 'info.ini'
$info = [IO.File]::ReadAllText($infoPath)
$info = [regex]::Replace($info, '(?m)^displayName\s*=.*$', 'displayName = Ultimate Duckov Statistics - ENCOUNTER DIAGNOSTICS')
[IO.File]::WriteAllText($infoPath, $info, [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $packageRoot
& (Join-Path $PSScriptRoot 'audit-artifacts.ps1') -InputPaths @($packageRoot)

$files = @(Get-ChildItem -LiteralPath $packageRoot -File | Sort-Object Name | ForEach-Object {
    [ordered]@{ Name = $_.Name; Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs', '.csproj') }
    Get-Item -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
    Get-Item -LiteralPath $PSCommandPath)
$sourceInventory = @($sources | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = $_.FullName.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/'); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = [ordered]@{
    BuildKind = 'Opt-in encounter diagnostics; never publish as a release'
    EncounterDiagnostics = $true
    PerformanceDiagnostics = $true
    BuiltUtc = (Get-Date).ToUniversalTime().ToString('O')
    RepositoryCommit = (& git -c "safe.directory=$($repoRoot.Replace('\', '/'))" -C $repoRoot rev-parse HEAD).Trim()
    RepositoryWorktreeStatus = @(& git -c "safe.directory=$($repoRoot.Replace('\', '/'))" -C $repoRoot status --short)
    PackagePath = $packageRoot
    SourceInventory = $sourceInventory
    Files = $files
}
$manifestPath = Join-Path $outputRoot 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $artifactRoot 'diagnostics-latest.json'), ($manifest | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Package = $packageRoot; Manifest = $manifestPath }
