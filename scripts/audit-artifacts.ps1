[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string[]]$InputPaths,
    [string[]]$ForbiddenIdentities = @($env:USERPROFILE, $env:USERNAME, $env:DUCKOV_PATH),
    [string]$ExpectedVersion
)
$ErrorActionPreference = 'Stop'
$auditArguments = @('run', '--project', (Join-Path $PSScriptRoot '../tools/ArtifactAudit/ArtifactAudit.csproj'), '-c', 'Release', '--') + $InputPaths
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) { $auditArguments += @('--version', $ExpectedVersion) }
foreach ($identity in $ForbiddenIdentities) {
    if (-not [string]::IsNullOrWhiteSpace($identity)) { $auditArguments += @('--forbid', $identity) }
}
& dotnet @auditArguments
if ($LASTEXITCODE -ne 0) { throw 'Artifact builder-path audit failed.' }
