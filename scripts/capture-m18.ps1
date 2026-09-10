[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$CampaignPath,
    [Parameter(Mandatory = $true)] [string]$ControlsPath,
    [Parameter(Mandatory = $true)] [ValidateSet('B', 'D')] [string]$Configuration,
    [Parameter(Mandatory = $true)] [string]$Scenario,
    [Parameter(Mandatory = $true)] [ValidateRange(1, 99)] [int]$Run,
    [Parameter(Mandatory = $true)] [string]$DuckovPath,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$campaign = Get-Content -LiteralPath $CampaignPath -Raw | ConvertFrom-Json
$cell = @($campaign.Matrix.Scenarios | Where-Object Id -eq $Scenario)
if ($campaign.SchemaVersion -ne 1 -or $cell.Count -ne 1) { throw 'Unknown campaign or scenario.' }
$cell = $cell[0]
$controls = Get-Content -LiteralPath $ControlsPath -Raw | ConvertFrom-Json -AsHashtable
$allowedControls = @('Weapon','WeaponModifications','Ammunition','EquipmentAndTotems','Location','ShotCountExpectation',
    'Consumable','ConsumableCountExpectation','StartingHealth','ActiveDamageEffects','PassiveHealingEffects',
    'SaveGenerationId','CharacterState','BackgroundApplicationState','Resolution','DisplayMode','RefreshRateHz',
    'FrameLimiterFps','VSyncState','GraphicsPreset','RouteAssociationState','ActionLabel')
foreach ($key in $controls.Keys) { if ($key -notin $allowedControls) { throw "Unknown control: $key" } }
$capture = @{} + $controls
$capture.Configuration = $Configuration; $capture.Scenario = $Scenario; $capture.Run = $Run
$capture.DuckovPath = $DuckovPath; $capture.ValidateOnly = $ValidateOnly
$capture.BuildLabel = 'production'; $capture.ExpectedUdsVersion = $campaign.Version
$capture.ExpectedUdsDllSha256 = $campaign.DllHashes.'UltimateDuckovStatistics.dll'
$capture.ExpectedUdsCoreDllSha256 = $campaign.DllHashes.'UltimateDuckovStatistics.Core.dll'
$capture.CandidateSourceCommit = $campaign.SourceCommit
$capture.CampaignSha256 = (Get-FileHash -LiteralPath $CampaignPath -Algorithm SHA256).Hash.ToLowerInvariant()
$capture.CaptureSeconds = $campaign.Matrix.CaptureSeconds; $capture.AttachDelaySeconds = $campaign.Matrix.AttachDelaySeconds
$capture.OutputRoot = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($CampaignPath))) 'captures'
$capture.Idle = $cell.Kind -eq 'idle'; $capture.ConsumableAction = $cell.Kind -eq 'consumable'
$capture.ActivityAction = $cell.Kind -eq 'activity'
if (-not $capture.Idle) { $capture.ActionStartSeconds = $cell.Start; $capture.ActionEndSeconds = $cell.End }
& (Join-Path $PSScriptRoot 'capture-frame-times.ps1') @capture
