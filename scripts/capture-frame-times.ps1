[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('A', 'B', 'C', 'D')]
    [string]$Configuration,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$Scenario,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 99)]
    [int]$Run,

    [ValidateRange(10, 120)]
    [int]$CaptureSeconds = 30,

    [ValidateRange(5, 30)]
    [int]$AttachDelaySeconds = 10,

    [switch]$Idle,

    [ValidateRange(0, 119)]
    [int]$ActionStartSeconds = 5,

    [ValidateRange(1, 120)]
    [int]$ActionEndSeconds = 20,

    [string]$ActionLabel = 'fire continuously',
    [string]$Weapon = '',
    [string]$WeaponModifications = '',
    [string]$Ammunition = '',
    [string]$EquipmentAndTotems = '',
    [string]$Location = '',
    [string]$ShotCountExpectation = '',
    [switch]$ConsumableAction,
    [string]$Consumable = '',
    [string]$ConsumableCountExpectation = '',
    [string]$StartingHealth = '',
    [string]$ActiveDamageEffects = '',
    [string]$PassiveHealingEffects = '',
    [string]$SaveGenerationId = '',
    [string]$CharacterState = 'natural survival decay accepted; no quantitative hydration or energy control',
    [string]$BackgroundApplicationState = 'not-recorded',
    [string]$Resolution = 'pilot-not-yet-recorded',
    [string]$DisplayMode = 'pilot-not-yet-recorded',
    [string]$RefreshRateHz = 'pilot-not-yet-recorded',
    [string]$FrameLimiterFps = 'pilot-not-yet-recorded',
    [string]$VSyncState = 'pilot-not-yet-recorded',
    [string]$GraphicsPreset = 'pilot-not-yet-recorded',
    [string]$RouteAssociationState = 'pilot-not-yet-recorded',
    [ValidateSet('production', 'diagnostic')]
    [string]$BuildLabel = 'production',
    [switch]$ValidateOnly,
    [string]$ExpectedUdsVersion = '',
    [ValidatePattern('^$|^[a-fA-F0-9]{64}$')]
    [string]$ExpectedUdsDllSha256 = '',
    [ValidatePattern('^$|^[a-fA-F0-9]{64}$')]
    [string]$ExpectedUdsCoreDllSha256 = '',
    [string]$CapFrameXApiBase = 'http://127.0.0.1:1337/api',
    [string]$CapFrameXInstallPath = 'C:\Program Files (x86)\CapFrameX\CapFrameX.exe',
    [string]$CapFrameXSettingsPath = (Join-Path $env:APPDATA 'CapFrameX\Configuration\AppSettings.json'),
    [string]$DuckovPath = 'E:\SteamLibrary\steamapps\common\Escape from Duckov',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\performance\captures'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot $OutputRoot
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if (-not $Idle -and $ActionEndSeconds -gt $CaptureSeconds) {
    throw 'ActionEndSeconds must not exceed CaptureSeconds.'
}
if (-not $Idle -and $ActionStartSeconds -ge $ActionEndSeconds) {
    throw 'ActionStartSeconds must be earlier than ActionEndSeconds.'
}
if ($Idle -and $ConsumableAction) {
    throw 'ConsumableAction cannot be combined with Idle.'
}
if (-not (Test-Path -LiteralPath $CapFrameXInstallPath)) {
    throw "CapFrameX was not found at: $CapFrameXInstallPath"
}
$capFrameXProcesses = @(Get-Process -Name 'CapFrameX' -ErrorAction SilentlyContinue)
if ($capFrameXProcesses.Count -ne 1) {
    throw "Exactly one running CapFrameX process is required; found $($capFrameXProcesses.Count)."
}
$capFrameXProcess = $capFrameXProcesses[0]
try {
    $capFrameXVersionResponse = Invoke-RestMethod -Uri "$CapFrameXApiBase/version" -Method Get -TimeoutSec 5
    [string[]]$capFrameXDetectedProcesses = Invoke-RestMethod -Uri "$CapFrameXApiBase/processes" -Method Get -TimeoutSec 5
}
catch {
    throw "CapFrameX loopback API is unavailable at $CapFrameXApiBase. $($_.Exception.Message)"
}
$capFrameXVersion = [string]$capFrameXVersionResponse.Version
if ([string]::IsNullOrWhiteSpace($capFrameXVersion)) {
    throw 'CapFrameX returned no version from its loopback API.'
}
if ($capFrameXDetectedProcesses -notcontains 'Duckov') {
    throw "CapFrameX did not detect Duckov. Detected processes: [$($capFrameXDetectedProcesses -join ', ')]."
}
if (-not (Test-Path -LiteralPath $CapFrameXSettingsPath)) {
    throw "CapFrameX settings were not found: $CapFrameXSettingsPath"
}
$capFrameXSettings = Get-Content -LiteralPath $CapFrameXSettingsPath -Raw | ConvertFrom-Json
if ([bool]$capFrameXSettings.UseSensorLogging) {
    throw 'CapFrameX Sensor Logging must be Off for controlled captures.'
}
$rtssProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(RTSS|RivaTuner)' })
$rtssKnownInstallPaths = @(
    'C:\Program Files (x86)\RivaTuner Statistics Server',
    'C:\Program Files\RivaTuner Statistics Server'
)
$rtssInstalled = [bool]($rtssKnownInstallPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
$rtssRunning = $rtssProcesses.Count -gt 0
$capFrameXOverlayConfigured = [bool]$capFrameXSettings.IsOverlayActive
$capFrameXOverlayEffective = $capFrameXOverlayConfigured -and ($rtssInstalled -or $rtssRunning)
if ($capFrameXOverlayEffective) {
    throw 'CapFrameX Overlay must be disabled, or RTSS must be absent, for controlled captures.'
}

$duckovProcesses = @(Get-Process -Name 'Duckov' -ErrorAction SilentlyContinue)
if ($duckovProcesses.Count -ne 1) {
    throw "Exactly one running Duckov process is required; found $($duckovProcesses.Count)."
}
$duckovProcess = $duckovProcesses[0]
$playerLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\TeamSoda\Duckov\Player.log'
if (-not (Test-Path -LiteralPath $playerLog)) {
    throw "Duckov Player.log was not found: $playerLog"
}
$logItem = Get-Item -LiteralPath $playerLog
if ($logItem.LastWriteTimeUtc -lt $duckovProcess.StartTime.ToUniversalTime()) {
    throw 'Player.log does not belong to the current Duckov process.'
}
$logLines = Get-Content -LiteralPath $playerLog
$configuredActiveMods = @($logLines | ForEach-Object {
    if ($_ -match '^ModActive_(?<Id>[^:]+): True$') { $Matches.Id }
} | Sort-Object -Unique)
$loadedMods = @($logLines | ForEach-Object {
    if ($_ -match '^Mod Loaded: (?<Id>.+)$') { $Matches.Id.Trim() }
} | Sort-Object -Unique)
$udsActivationObserved = [bool]($logLines | Where-Object { $_ -match '^\[UDS\] activated\b' } | Select-Object -First 1)
$expectedMods = switch ($Configuration) {
    'A' { @() }
    'B' { @('HarmonyLoadMod') }
    default { @('HarmonyLoadMod', 'UltimateDuckovStatistics') }
}
$modDifference = Compare-Object -ReferenceObject @($expectedMods | Sort-Object) -DifferenceObject $loadedMods
if ($modDifference) {
    throw "Configuration $Configuration load mismatch. Expected loaded mods: [$($expectedMods -join ', ')]; current launch log: [$($loadedMods -join ', ')]."
}
$expectsUds = $Configuration -in @('C', 'D')
if ($udsActivationObserved -ne $expectsUds) {
    throw "Configuration $Configuration UDS activation mismatch. Expected activation=$expectsUds; current launch log activation=$udsActivationObserved."
}

$deployedMod = Join-Path $DuckovPath 'Duckov_Data\Mods\UltimateDuckovStatistics'
$deployedInfoPath = Join-Path $deployedMod 'info.ini'
$deployedDllPath = Join-Path $deployedMod 'UltimateDuckovStatistics.dll'
$deployedCoreDllPath = Join-Path $deployedMod 'UltimateDuckovStatistics.Core.dll'
$deployedInfoVersion = ''
if ($Configuration -in @('C', 'D')) {
    foreach ($requiredPath in @($deployedInfoPath, $deployedDllPath, $deployedCoreDllPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Enabled UDS configuration requires the deployed file: $requiredPath"
        }
    }
    $versionLine = Get-Content -LiteralPath $deployedInfoPath | Where-Object { $_ -match '^\s*version\s*=' } | Select-Object -First 1
    if ($null -eq $versionLine) { throw "Deployed info.ini has no version: $deployedInfoPath" }
    $deployedInfoVersion = ($versionLine -split '=', 2)[1].Trim()
    if ([string]::IsNullOrWhiteSpace($ExpectedUdsVersion) -and $BuildLabel -eq 'production') {
        $ExpectedUdsVersion = if ($Configuration -eq 'C') { '0.8.0' } else { '0.8.1' }
    }
    if ($BuildLabel -eq 'production' -and $Configuration -eq 'C') {
        if ([string]::IsNullOrWhiteSpace($ExpectedUdsDllSha256)) {
            $ExpectedUdsDllSha256 = 'd937f9a5b31e544e8fa9ba337f1ed2082a1c64c7a5a2fac33c6853d55de787a1'
        }
        if ([string]::IsNullOrWhiteSpace($ExpectedUdsCoreDllSha256)) {
            $ExpectedUdsCoreDllSha256 = 'e2b06828ae60e71b2f7b9ef066562cab14241a3168ea0a251d9cb9003075cdeb'
        }
    }
    if (($BuildLabel -eq 'production') -and ($Configuration -eq 'D') -and ([string]::IsNullOrWhiteSpace($ExpectedUdsDllSha256) -or [string]::IsNullOrWhiteSpace($ExpectedUdsCoreDllSha256))) {
        throw 'Production configuration D requires both final candidate DLL hashes.'
    }
    if ((-not [string]::IsNullOrWhiteSpace($ExpectedUdsVersion)) -and ($deployedInfoVersion -ne $ExpectedUdsVersion)) {
        throw "Configuration $Configuration requires UDS version '$ExpectedUdsVersion', found '$deployedInfoVersion'."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedUdsDllSha256)) {
        $deployedDllSha256 = (Get-FileHash -LiteralPath $deployedDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($deployedDllSha256 -ne $ExpectedUdsDllSha256.ToLowerInvariant()) {
            throw "Deployed UDS DLL hash mismatch. Expected $ExpectedUdsDllSha256, found $deployedDllSha256."
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedUdsCoreDllSha256)) {
        $deployedCoreDllSha256 = (Get-FileHash -LiteralPath $deployedCoreDllPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($deployedCoreDllSha256 -ne $ExpectedUdsCoreDllSha256.ToLowerInvariant()) {
            throw "Deployed UDS Core DLL hash mismatch. Expected $ExpectedUdsCoreDllSha256, found $deployedCoreDllSha256."
        }
    }
}

if ($ValidateOnly) {
    [pscustomobject]@{
        Configuration = $Configuration
        ProcessId = $duckovProcess.Id
        ProcessStartedUtc = $duckovProcess.StartTime.ToUniversalTime().ToString('O')
        ConfiguredActiveMods = $configuredActiveMods
        LoadedMods = $loadedMods
        UdsActivationObserved = $udsActivationObserved
        DeployedUdsInfoVersion = $deployedInfoVersion
        BuildLabel = $BuildLabel
        CapFrameXVersion = $capFrameXVersion
        CapFrameXApiBase = $CapFrameXApiBase
        CapFrameXSensorLogging = [bool]$capFrameXSettings.UseSensorLogging
        CapFrameXOverlayConfigured = $capFrameXOverlayConfigured
        CapFrameXOverlayEffective = $capFrameXOverlayEffective
        RtssInstalled = $rtssInstalled
        RtssRunning = $rtssRunning
    }
    return
}

$requiredControls = [ordered]@{
    Weapon = $Weapon
    WeaponModifications = $WeaponModifications
    Ammunition = $Ammunition
    EquipmentAndTotems = $EquipmentAndTotems
    Location = $Location
    ShotCountExpectation = $ShotCountExpectation
    SaveGenerationId = $SaveGenerationId
    BackgroundApplicationState = $BackgroundApplicationState
    Resolution = $Resolution
    DisplayMode = $DisplayMode
    RefreshRateHz = $RefreshRateHz
    FrameLimiterFps = $FrameLimiterFps
    VSyncState = $VSyncState
    GraphicsPreset = $GraphicsPreset
    RouteAssociationState = $RouteAssociationState
}
foreach ($control in $requiredControls.GetEnumerator()) {
    $controlValue = [string]$control.Value
    if ([string]::IsNullOrWhiteSpace($controlValue) -or $controlValue -match '^(pilot-not-yet-recorded|not-recorded)$') {
        throw "Controlled capture requires a non-placeholder $($control.Key) value."
    }
}
if ($ConsumableAction) {
    $requiredConsumableControls = [ordered]@{
        Consumable = $Consumable
        ConsumableCountExpectation = $ConsumableCountExpectation
        StartingHealth = $StartingHealth
        ActiveDamageEffects = $ActiveDamageEffects
        PassiveHealingEffects = $PassiveHealingEffects
    }
    foreach ($control in $requiredConsumableControls.GetEnumerator()) {
        $controlValue = [string]$control.Value
        if ([string]::IsNullOrWhiteSpace($controlValue) -or $controlValue -match '^(pilot-not-yet-recorded|not-recorded)$') {
            throw "Consumable capture requires a non-placeholder $($control.Key) value."
        }
    }
}

$scenarioDirectory = Join-Path (Join-Path $OutputRoot $Configuration) $Scenario
New-Item -ItemType Directory -Path $scenarioDirectory -Force | Out-Null
$baseName = ('{0}-{1}-r{2:D2}' -f $Configuration.ToLowerInvariant(), $Scenario, $Run)
$csvPath = Join-Path $scenarioDirectory "$baseName.csv"
$capFrameXRawJsonPath = Join-Path $scenarioDirectory "$baseName.capframex.json"
$metadataPath = Join-Path $scenarioDirectory "$baseName.capture.json"
foreach ($path in @($csvPath, $capFrameXRawJsonPath, $metadataPath)) {
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite an existing capture artifact: $path"
    }
}

$stagingDirectory = Join-Path $scenarioDirectory ('.{0}-capframex-staging-{1}' -f $baseName, [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
$capFrameXCaptureComment = "UDS M8.1 $Configuration/$BuildLabel $Scenario r$($Run.ToString('D2'))"
$capFrameXCaptureRequest = [ordered]@{
    CaptureTime = [double]$CaptureSeconds
    ProcessName = 'Duckov'
    CaptureFileMode = 'JsonCsv'
    RecordDirectory = $stagingDirectory
    Comment = $capFrameXCaptureComment
}

function Send-CaptureSignal([int]$Frequency) {
    try { [Console]::Beep($Frequency, 250) } catch { }
}

Write-Host "CapFrameX will begin the $CaptureSeconds-second capture after $AttachDelaySeconds seconds. Return focus to Duckov now."
Start-Sleep -Seconds $AttachDelaySeconds
$captureStartedUtc = (Get-Date).ToUniversalTime()
$captureControlClock = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $capFrameXCaptureInvokeParameters = @{
        Uri = "$CapFrameXApiBase/capture"
        Method = 'Post'
        ContentType = 'application/json'
        Body = ($capFrameXCaptureRequest | ConvertTo-Json -Compress)
        TimeoutSec = 10
    }
    $capFrameXCaptureResponse = Invoke-RestMethod @capFrameXCaptureInvokeParameters
}
catch {
    throw "CapFrameX did not start the capture. Staging directory retained at $stagingDirectory. $($_.Exception.Message)"
}
if ([string]$capFrameXCaptureResponse.Message -ne 'Capture started') {
    throw "Unexpected CapFrameX capture response. Staging directory retained at $stagingDirectory. Response: $($capFrameXCaptureResponse | ConvertTo-Json -Compress)"
}
$capFrameXApiResponseElapsedMilliseconds = $captureControlClock.Elapsed.TotalMilliseconds
$captureStartSignalOffsetSeconds = $captureControlClock.Elapsed.TotalSeconds
$captureStartSignalUtc = (Get-Date).ToUniversalTime()
Send-CaptureSignal 1000
$actionStartSignalOffsetSeconds = $null
$actionStartSignalUtc = $null
$actionEndSignalOffsetSeconds = $null
$actionEndSignalUtc = $null
if ($Idle) {
    Write-Host 'CAPTURE START: remain completely idle.'
    Start-Sleep -Seconds $CaptureSeconds
}
else {
    Write-Host "CAPTURE START: hold the matched pre-action state for $ActionStartSeconds seconds."
    Start-Sleep -Seconds $ActionStartSeconds
    $actionStartSignalOffsetSeconds = $captureControlClock.Elapsed.TotalSeconds
    $actionStartSignalUtc = (Get-Date).ToUniversalTime()
    Send-CaptureSignal 1300
    Write-Host "ACTION START: $ActionLabel"
    Start-Sleep -Seconds ($ActionEndSeconds - $ActionStartSeconds)
    $actionEndSignalOffsetSeconds = $captureControlClock.Elapsed.TotalSeconds
    $actionEndSignalUtc = (Get-Date).ToUniversalTime()
    Send-CaptureSignal 700
    Write-Host 'ACTION END: stop the requested action and remain still.'
    Start-Sleep -Seconds ($CaptureSeconds - $ActionEndSeconds)
}
$captureEndSignalOffsetSeconds = $captureControlClock.Elapsed.TotalSeconds
$captureEndSignalUtc = (Get-Date).ToUniversalTime()
Send-CaptureSignal 1000
Write-Host 'CAPTURE END.'
$captureControlClock.Stop()
$captureFilesDeadline = (Get-Date).AddSeconds(20)
do {
    $stagedCsvFiles = @(Get-ChildItem -LiteralPath $stagingDirectory -File -Filter '*.csv' -ErrorAction SilentlyContinue)
    $stagedJsonFiles = @(Get-ChildItem -LiteralPath $stagingDirectory -File -Filter '*.json' -ErrorAction SilentlyContinue)
    if ($stagedCsvFiles.Count -eq 1 -and $stagedJsonFiles.Count -eq 1) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $captureFilesDeadline)
if ($stagedCsvFiles.Count -ne 1 -or $stagedJsonFiles.Count -ne 1) {
    throw "CapFrameX did not produce exactly one raw CSV and JSON record. Staging directory retained at $stagingDirectory. CSV=$($stagedCsvFiles.Count), JSON=$($stagedJsonFiles.Count)."
}
$stagedCsv = $stagedCsvFiles[0]
$stagedJson = $stagedJsonFiles[0]
$capFrameXRecord = Get-Content -LiteralPath $stagedJson.FullName -Raw | ConvertFrom-Json
if ([string]$capFrameXRecord.Info.ProcessName -ne 'Duckov.exe') {
    throw "CapFrameX record process mismatch. Staging directory retained at $stagingDirectory. Found '$($capFrameXRecord.Info.ProcessName)'."
}
if ([string]$capFrameXRecord.Info.Comment -ne $capFrameXCaptureComment) {
    throw "CapFrameX record comment mismatch. Staging directory retained at $stagingDirectory. Found '$($capFrameXRecord.Info.Comment)'."
}
if (@($capFrameXRecord.Runs).Count -ne 1) {
    throw "CapFrameX record must contain exactly one run. Staging directory retained at $stagingDirectory. Found $(@($capFrameXRecord.Runs).Count)."
}
$rawCsvLines = @(Get-Content -LiteralPath $stagedCsv.FullName)
$rawCsvContentLines = @($rawCsvLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.TrimStart().StartsWith('//') })
if ($rawCsvContentLines.Count -lt 2 -or $rawCsvContentLines[0] -notmatch '(^|,)MsBetweenPresents(,|$)') {
    throw "CapFrameX produced no supported raw frame rows. Staging directory retained at $stagingDirectory."
}
Move-Item -LiteralPath $stagedCsv.FullName -Destination $csvPath
Move-Item -LiteralPath $stagedJson.FullName -Destination $capFrameXRawJsonPath
if (@(Get-ChildItem -LiteralPath $stagingDirectory -Force).Count -eq 0) {
    Remove-Item -LiteralPath $stagingDirectory
}

$capFrameXExecutable = Get-Item -LiteralPath $CapFrameXInstallPath
$gameExecutable = Get-Item -LiteralPath (Join-Path $DuckovPath 'Duckov.exe')
$deployedFiles = @()
if (Test-Path -LiteralPath $deployedMod) {
    $deployedFiles = @(Get-ChildItem -LiteralPath $deployedMod -File | Sort-Object Name | ForEach-Object {
        [ordered]@{
            Name = $_.Name
            Length = $_.Length
            FileVersion = $_.VersionInfo.FileVersion
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}
$gameVersion = ''
$gameInfoPath = Join-Path $DuckovPath 'Info.ini'
if (Test-Path -LiteralPath $gameInfoPath) {
    $gameVersionLine = Get-Content -LiteralPath $gameInfoPath | Where-Object { $_ -match '^\s*version\s*=' } | Select-Object -First 1
    if ($null -ne $gameVersionLine) { $gameVersion = ($gameVersionLine -split '=', 2)[1].Trim() }
}
$steamBuildId = ''
$steamAppsRoot = Split-Path -Parent (Split-Path -Parent $DuckovPath)
$steamManifestPath = Join-Path $steamAppsRoot 'appmanifest_3167020.acf'
if (Test-Path -LiteralPath $steamManifestPath) {
    $manifestText = Get-Content -Raw -LiteralPath $steamManifestPath
    if ($manifestText -match '"buildid"\s+"(?<BuildId>[0-9]+)"') { $steamBuildId = $Matches.BuildId }
}
$harmonyPath = Join-Path $steamAppsRoot 'workshop\content\3167020\3589088839\0Harmony.dll'
$harmony = if (Test-Path -LiteralPath $harmonyPath) { Get-Item -LiteralPath $harmonyPath } else { $null }
$capFrameXRun = @($capFrameXRecord.Runs)[0]
$capFrameXSensorPayloadPresent = ($null -ne $capFrameXRun.SensorData) -or ($null -ne $capFrameXRun.SensorData2)
$metadata = [ordered]@{
    SchemaVersion = 5
    Configuration = $Configuration
    Scenario = $Scenario
    Run = $Run
    CapturedUtc = $captureStartedUtc.ToString('O')
    CaptureSeconds = $CaptureSeconds
    AttachDelaySeconds = $AttachDelaySeconds
    CapFrameXApiResponseElapsedMilliseconds = $capFrameXApiResponseElapsedMilliseconds
    CaptureControlElapsedMilliseconds = $captureControlClock.Elapsed.TotalMilliseconds
    CaptureStartSignalOffsetSeconds = $captureStartSignalOffsetSeconds
    CaptureStartSignalUtc = $captureStartSignalUtc.ToString('O')
    RequestedActionStartSeconds = if ($Idle) { $null } else { $ActionStartSeconds }
    RequestedActionEndSeconds = if ($Idle) { $null } else { $ActionEndSeconds }
    ActionStartSeconds = if ($Idle) { $null } else { $actionStartSignalOffsetSeconds }
    ActionStartSignalUtc = if ($Idle) { $null } else { $actionStartSignalUtc.ToString('O') }
    ActionEndSeconds = if ($Idle) { $null } else { $actionEndSignalOffsetSeconds }
    ActionEndSignalUtc = if ($Idle) { $null } else { $actionEndSignalUtc.ToString('O') }
    CaptureEndSignalOffsetSeconds = $captureEndSignalOffsetSeconds
    CaptureEndSignalUtc = $captureEndSignalUtc.ToString('O')
    ActionLabel = if ($Idle) { 'idle' } else { $ActionLabel }
    Weapon = $Weapon
    WeaponModifications = $WeaponModifications
    Ammunition = $Ammunition
    EquipmentAndTotems = $EquipmentAndTotems
    Location = $Location
    ShotCountExpectation = $ShotCountExpectation
    ActionKind = if ($Idle) { 'idle' } elseif ($ConsumableAction) { 'consumable' } else { 'weapon' }
    Consumable = $Consumable
    ConsumableCountExpectation = $ConsumableCountExpectation
    StartingHealth = $StartingHealth
    ActiveDamageEffects = $ActiveDamageEffects
    PassiveHealingEffects = $PassiveHealingEffects
    SaveGenerationId = $SaveGenerationId
    CharacterState = $CharacterState
    BackgroundApplicationState = $BackgroundApplicationState
    Resolution = $Resolution
    DisplayMode = $DisplayMode
    RefreshRateHz = $RefreshRateHz
    FrameLimiterFps = $FrameLimiterFps
    VSyncState = $VSyncState
    GraphicsPreset = $GraphicsPreset
    RouteAssociationState = $RouteAssociationState
    BuildLabel = $BuildLabel
    ExpectedUdsVersion = $ExpectedUdsVersion
    ExpectedUdsDllSha256 = $ExpectedUdsDllSha256.ToLowerInvariant()
    ExpectedUdsCoreDllSha256 = $ExpectedUdsCoreDllSha256.ToLowerInvariant()
    DeployedUdsInfoVersion = $deployedInfoVersion
    DuckovProcessId = $duckovProcess.Id
    DuckovProcessStartedUtc = $duckovProcess.StartTime.ToUniversalTime().ToString('O')
    DuckovGameVersion = $gameVersion
    DuckovSteamBuildId = $steamBuildId
    DuckovExecutableVersion = $gameExecutable.VersionInfo.ProductVersion
    HarmonyVersion = if ($null -eq $harmony) { '' } else { $harmony.VersionInfo.FileVersion }
    HarmonySha256 = if ($null -eq $harmony) { '' } else { (Get-FileHash -LiteralPath $harmony.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    Collector = 'CapFrameX'
    CapFrameXVersion = $capFrameXVersion
    CapFrameXPath = $capFrameXExecutable.FullName
    CapFrameXFileVersion = $capFrameXExecutable.VersionInfo.FileVersion
    CapFrameXSha256 = (Get-FileHash -LiteralPath $capFrameXExecutable.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    CapFrameXProcessId = $capFrameXProcess.Id
    CapFrameXApiBase = $CapFrameXApiBase
    CapFrameXCaptureRequest = $capFrameXCaptureRequest
    CapFrameXCaptureResponse = $capFrameXCaptureResponse
    CapFrameXRawComment = $capFrameXCaptureComment
    CapFrameXSettings = [ordered]@{
        UseSensorLogging = [bool]$capFrameXSettings.UseSensorLogging
        IsOverlayActive = $capFrameXOverlayConfigured
        OverlayEffective = $capFrameXOverlayEffective
        RtssInstalled = $rtssInstalled
        RtssRunning = $rtssRunning
        UsePcLatency = [bool]$capFrameXSettings.UsePcLatency
        UseRunHistory = [bool]$capFrameXSettings.UseRunHistory
        UseAggregation = [bool]$capFrameXSettings.UseAggregation
        SaveAggregationOnly = [bool]$capFrameXSettings.SaveAggregationOnly
        SensorLoggingRefreshPeriod = $capFrameXSettings.SensorLoggingRefreshPeriod
    }
    CapFrameXSensorPayloadPresent = $capFrameXSensorPayloadPresent
    ActiveMods = $loadedMods
    ConfiguredActiveMods = $configuredActiveMods
    LoadedMods = $loadedMods
    UdsActivationObserved = $udsActivationObserved
    RepositoryCommit = (& git -c "safe.directory=$($repoRoot.Replace('\', '/'))" -C $repoRoot rev-parse HEAD).Trim()
    DeployedFiles = $deployedFiles
    RawCsvSha256 = (Get-FileHash -LiteralPath $csvPath -Algorithm SHA256).Hash.ToLowerInvariant()
    RawCapFrameXJsonSha256 = (Get-FileHash -LiteralPath $capFrameXRawJsonPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$json = $metadata | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($metadataPath, $json + "`n", [System.Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Csv = $csvPath
    CapFrameXJson = $capFrameXRawJsonPath
    Metadata = $metadataPath
    Sha256 = $metadata.RawCsvSha256
    Frames = [Math]::Max(0, $rawCsvContentLines.Count - 1)
}
