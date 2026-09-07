[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string[]]$InputPaths,
    [string[]]$ForbiddenIdentities = @($env:USERPROFILE, $env:USERNAME, $env:DUCKOV_PATH)
)
$ErrorActionPreference = 'Stop'
$auditArguments = @('run', '--project', (Join-Path $PSScriptRoot '../tools/ArtifactAudit/ArtifactAudit.csproj'), '-c', 'Release', '--') + $InputPaths
foreach ($identity in $ForbiddenIdentities) {
    if (-not [string]::IsNullOrWhiteSpace($identity)) { $auditArguments += @('--forbid', $identity) }
}
& dotnet @auditArguments
if ($LASTEXITCODE -ne 0) { throw 'Artifact builder-path audit failed.' }
