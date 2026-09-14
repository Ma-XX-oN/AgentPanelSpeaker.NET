param(
  [Parameter(Mandatory = $true)]
  [string]$Executable
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'AgentPanelSpeaker\AgentPanelSpeaker.csproj'
$identityPath = Join-Path $repoRoot 'AgentPanelSpeaker\ApplicationIdentity.cs'
$mainFormPath = Join-Path $repoRoot 'AgentPanelSpeaker\MainForm.cs'
$diagnosticPath = Join-Path $repoRoot 'AgentPanelSpeaker\DiagnosticLog.cs'
$developmentVersionPath = Join-Path $repoRoot 'DEVELOPMENT-VERSION'

if (-not (Test-Path $Executable)) {
  throw "Built application missing: $Executable"
}
if (-not (Test-Path $identityPath)) {
  throw "Application version authority missing: $identityPath"
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
  throw 'AgentPanelSpeaker.csproj does not define the authoritative Version.'
}
if ($version -notmatch '^\d+\.\d+\.\d+(?:-issue\.\d+\.\d+)?$') {
  throw "Invalid AgentPanelSpeaker version shape: $version"
}

if (Test-Path $developmentVersionPath) {
  $expectedDevelopmentVersion = (
    Get-Content -LiteralPath $developmentVersionPath -Raw
  ).Trim()
  if ($expectedDevelopmentVersion -notmatch '^\d+\.\d+\.\d+-issue\.\d+\.\d+$') {
    throw (
      'DEVELOPMENT-VERSION must contain exactly one issue-development ' +
      "version; got: $expectedDevelopmentVersion")
  }
  if ($version -ne $expectedDevelopmentVersion) {
    throw (
      'Active development version mismatch: ' +
      "guard=$expectedDevelopmentVersion project=$version")
  }
}

$productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
  (Resolve-Path -LiteralPath $Executable)
).ProductVersion
if ($productVersion -ne $version) {
  throw "Built ProductVersion mismatch: project=$version executable=$productVersion"
}

$identity = Get-Content -LiteralPath $identityPath -Raw
if ($identity -notmatch 'AssemblyInformationalVersionAttribute') {
  throw 'ApplicationIdentity does not derive Version from assembly informational metadata.'
}

$mainForm = Get-Content -LiteralPath $mainFormPath -Raw
if ($mainForm -notmatch 'Text\s*=\s*ApplicationIdentity\.WindowTitle\s*;') {
  throw 'MainForm does not consume ApplicationIdentity.WindowTitle.'
}
if ($mainForm -match 'Agent Panel Speaker v\d') {
  throw 'MainForm still contains a hand-maintained application-version title.'
}

$diagnostic = Get-Content -LiteralPath $diagnosticPath -Raw
if ($diagnostic -notmatch 'version\s*=\s*ApplicationIdentity\.Version\s*,') {
  throw 'DiagnosticLog app.start does not consume ApplicationIdentity.Version.'
}
if ($diagnostic -match 'version\s*=\s*"\d') {
  throw 'DiagnosticLog still contains a hand-maintained application version.'
}

Write-Host "PASS application version consistency: $version"
