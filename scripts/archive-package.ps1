[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PackagePath,
    [Parameter(Mandatory = $true)] [string]$ArchivePath,
    [Parameter(Mandatory = $true)] [string]$Version
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $PackagePath
$info = Get-Content -LiteralPath (Join-Path $PackagePath 'info.ini') | Where-Object { $_ -match '^\s*version\s*=' }
if (@($info).Count -ne 1 -or ($info -split '=', 2)[1].Trim() -ne $Version) { throw 'Archive version does not match info.ini.' }
& (Join-Path $PSScriptRoot 'audit-artifacts.ps1') -InputPaths @($PackagePath) -ExpectedVersion $Version
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($ArchivePath)) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($ArchivePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
try {
    [string[]]$names = @(Get-ChildItem -File -LiteralPath $PackagePath | ForEach-Object { $_.Name })
    [Array]::Sort($names, [StringComparer]::Ordinal)
    foreach ($name in $names) {
        $entry = $zip.CreateEntry("UltimateDuckovStatistics/$name", [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $fileInput = [IO.File]::OpenRead((Join-Path $PackagePath $name))
        $output = $entry.Open()
        try { $fileInput.CopyTo($output) } finally { $output.Dispose(); $fileInput.Dispose() }
    }
} finally { $zip.Dispose(); $stream.Dispose() }
# Independent extraction verifies content, not just entry names.
$extraction = Join-Path ([IO.Path]::GetDirectoryName($ArchivePath)) ('verify-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $extraction)
$extracted = Join-Path $extraction 'UltimateDuckovStatistics'
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $extracted
& (Join-Path $PSScriptRoot 'audit-artifacts.ps1') -InputPaths @($extracted)
foreach ($name in $names) {
    if ((Get-FileHash -LiteralPath (Join-Path $PackagePath $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $extracted $name)).Hash) {
        throw "Extracted artifact differs: $name"
    }
}
Write-Output "Verified deterministic ZIP: $ArchivePath"
