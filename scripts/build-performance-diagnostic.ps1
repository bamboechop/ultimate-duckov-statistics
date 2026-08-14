[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DuckovPath = $env:DUCKOV_PATH
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DuckovPath)) {
    throw 'DuckovPath (or DUCKOV_PATH) is required.'
}
$resolvedDuckovPath = (Resolve-Path -LiteralPath $DuckovPath).Path
$buildRoot = Join-Path $repoRoot 'artifacts\performance\diagnostic-build'
$packageRoot = Join-Path $repoRoot 'artifacts\performance\diagnostic-package\UltimateDuckovStatistics'
$manifestPath = Join-Path $repoRoot 'artifacts\performance\diagnostic-package.manifest.json'

dotnet restore (Join-Path $repoRoot 'UltimateDuckovStatistics.sln')
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

dotnet build (Join-Path $repoRoot 'src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj') `
    -c Release --no-restore `
    -p:DuckovPath=$resolvedDuckovPath `
    -p:UDSPerformanceDiagnostics=true `
    -p:OutputPath=$buildRoot
if ($LASTEXITCODE -ne 0) { throw "diagnostic build failed with exit code $LASTEXITCODE." }

foreach ($path in @($packageRoot)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -Recurse -Force -LiteralPath $path }
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$inputs = @(
    @{ Source = (Join-Path $repoRoot 'mod\info.ini'); Destination = 'info.ini' },
    @{ Source = (Join-Path $buildRoot 'UltimateDuckovStatistics.dll'); Destination = 'UltimateDuckovStatistics.dll' },
    @{ Source = (Join-Path $buildRoot 'UltimateDuckovStatistics.Core.dll'); Destination = 'UltimateDuckovStatistics.Core.dll' },
    @{ Source = (Join-Path $repoRoot 'INSTALL.md'); Destination = 'INSTALL.md' },
    @{ Source = (Join-Path $repoRoot 'LICENSE'); Destination = 'LICENSE' }
)
foreach ($input in $inputs) {
    Copy-Item -Force -LiteralPath $input.Source -Destination (Join-Path $packageRoot $input.Destination)
}
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $packageRoot

$files = @(Get-ChildItem -LiteralPath $packageRoot -File | Sort-Object Name | ForEach-Object {
    [ordered]@{
        Name = $_.Name
        Length = $_.Length
        FileVersion = $_.VersionInfo.FileVersion
        Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
$sourceFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' -and $_.Extension -in @('.cs', '.csproj') }
    Get-Item -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
) | Sort-Object FullName
$sourceInventory = @($sourceFiles | ForEach-Object {
    $relativePath = $_.FullName.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
    "$relativePath`t$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())"
})
$sourceTreeBytes = [System.Text.Encoding]::UTF8.GetBytes($sourceInventory -join "`n")
$sourceTreeSha256 = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($sourceTreeBytes)).ToLowerInvariant()
$manifest = [ordered]@{
    SchemaVersion = 1
    BuildKind = 'M8.1 opt-in performance diagnostic; never a release candidate'
    PerformanceDiagnostics = $true
    RepositoryCommit = (& git -c "safe.directory=$($repoRoot.Replace('\', '/'))" -C $repoRoot rev-parse HEAD).Trim()
    RepositoryWorktreeStatus = @(& git -c "safe.directory=$($repoRoot.Replace('\', '/'))" -C $repoRoot status --short)
    SourceTreeSha256 = $sourceTreeSha256
    SourceInventory = $sourceInventory
    BuiltUtc = (Get-Date).ToUniversalTime().ToString('O')
    PackagePath = $packageRoot
    Files = $files
}
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 6) + "`n",
    [System.Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Package = $packageRoot
    Manifest = $manifestPath
    NativeDllSha256 = ($files | Where-Object Name -eq 'UltimateDuckovStatistics.dll').Sha256
}
