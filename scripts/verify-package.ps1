[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $PackagePath).Path
$required = @('info.ini', 'UltimateDuckovStatistics.dll', 'UltimateDuckovStatistics.Core.dll', 'INSTALL.md', 'LICENSE')
$forbiddenExact = @('0Harmony.dll', 'TeamSoda.Duckov.Core.dll', 'ItemStatsSystem.dll', 'Assembly-CSharp.dll')
$forbiddenPrefixes = @('UnityEngine', 'Unity.', 'System.', 'mscorlib')

foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolved $name))) {
        throw "Package is missing required file: $name"
    }
}

$allFiles = Get-ChildItem -Recurse -File -LiteralPath $resolved
foreach ($file in $allFiles) {
    if ($forbiddenExact -contains $file.Name) {
        throw "Forbidden dependency found in package: $($file.FullName)"
    }
    foreach ($prefix in $forbiddenPrefixes) {
        if ($file.Name.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -and $file.Extension -eq '.dll') {
            throw "Framework/game dependency found in package: $($file.FullName)"
        }
    }
}

$unexpectedDlls = $allFiles | Where-Object {
    $_.Extension -eq '.dll' -and $_.Name -notin @('UltimateDuckovStatistics.dll', 'UltimateDuckovStatistics.Core.dll')
}
if ($unexpectedDlls) {
    throw "Unexpected DLL(s) found in package: $($unexpectedDlls.Name -join ', ')"
}

Write-Output "Package validation passed: $resolved"
