[CmdletBinding()]
param(
    [string]$Marker = 'LOADER-PERSISTENCE-20260809-C1',
    [string]$DuckovDataRoot = (Join-Path $env:USERPROFILE 'AppData\LocalLow\TeamSoda\Duckov')
)

$ErrorActionPreference = 'Stop'
$saveRoot = Join-Path $DuckovDataRoot 'Saves'
$settingFiles = @(
    (Join-Path $saveRoot 'Global.json'),
    (Join-Path $saveRoot 'Global.json.bac')
)

foreach ($file in $settingFiles) {
    $document = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
    $allow = $document.PSObject.Properties['AllowLoadingMod'].Value.value
    $enabled = $document.PSObject.Properties['ModActive_UltimateDuckovStatistics'].Value.value
    $item = Get-Item -LiteralPath $file
    [pscustomobject]@{
        Kind = 'PersistedSetting'
        File = $item.Name
        LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('O')
        AllowLoadingMod = [bool]$allow
        ModEnabled = [bool]$enabled
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
    }
}

foreach ($name in @('Player.log', 'Player-prev.log')) {
    $path = Join-Path $DuckovDataRoot $name
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    $item = Get-Item -LiteralPath $path
    $matches = Select-String -LiteralPath $path -SimpleMatch -Pattern $Marker
    [pscustomobject]@{
        Kind = 'Log'
        File = $item.Name
        LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('O')
        MarkerCount = @($matches).Count
        MarkerLines = (@($matches | ForEach-Object { "{0}:{1}" -f $_.LineNumber, $_.Line }) -join ' | ')
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
    }
}
