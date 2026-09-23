param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$corePath = Join-Path $repoRoot 'dependencies\AIConversationCore'
$workerPath = Join-Path $repoRoot 'tools\AIConversationCore-worker.mjs'
$markerPath = Join-Path $repoRoot 'tools\AIConversationCore-runtime\CORE_COMMIT'
$clientPath = Join-Path $repoRoot 'AgentPanelSpeaker\AIConversationCoreClient.cs'
$workflowPath = Join-Path $repoRoot '.github\workflows\core-integration-validation.yml'

function Read-SingleCommitMatch {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$Pattern,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "$Label file missing: $Path"
  }
  $text = Get-Content -LiteralPath $Path -Raw
  $matches = [regex]::Matches($text, $Pattern)
  if ($matches.Count -ne 1) {
    throw "$Label must contain exactly one Core commit pin; found $($matches.Count)."
  }
  return $matches[0].Groups[1].Value
}

function Assert-CorePin {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Label,
    [Parameter(Mandatory = $true)]
    [string]$Expected,
    [Parameter(Mandatory = $true)]
    [string]$Actual
  )

  if ($Actual -ne $Expected) {
    throw "AIConversationCore pin mismatch: authority=gitlink expected=$Expected source=$Label actual=$Actual"
  }
}

Push-Location $repoRoot
try {
  $treeLine = (& git ls-tree HEAD dependencies/AIConversationCore)
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($treeLine)) {
    throw 'Unable to read the pinned AIConversationCore gitlink from HEAD.'
  }
  $parts = $treeLine.Trim() -split '\s+'
  if ($parts.Count -lt 3) {
    throw "Unexpected AIConversationCore gitlink record: $treeLine"
  }
  $expected = $parts[2]

  $actual = (& git -C $corePath rev-parse HEAD).Trim()
  if ($LASTEXITCODE -ne 0) {
    throw 'Unable to read the checked-out AIConversationCore revision.'
  }
  Assert-CorePin 'checked-out-submodule' $expected $actual

  $workerCommit = Read-SingleCommitMatch $workerPath "const CORE_COMMIT = '([0-9a-f]{40})';" 'worker'
  Assert-CorePin 'worker' $expected $workerCommit

  if (-not (Test-Path -LiteralPath $markerPath)) {
    throw "Bundled Core marker missing: $markerPath"
  }
  $markerCommit = (Get-Content -LiteralPath $markerPath -Raw).Trim()
  if ($markerCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Bundled Core marker is not one commit SHA: $markerCommit"
  }
  Assert-CorePin 'bundled-runtime-marker' $expected $markerCommit

  $clientCommit = Read-SingleCommitMatch $clientPath 'ExpectedCoreCommit\s*=\s*"([0-9a-f]{40})";' 'C# client'
  Assert-CorePin 'csharp-client' $expected $clientCommit

  $workflowCommit = Read-SingleCommitMatch $workflowPath '\$expected\s*=\s*''([0-9a-f]{40})''' 'integration workflow'
  Assert-CorePin 'integration-workflow' $expected $workflowCommit

  Write-Host "PASS AIConversationCore pin consistency: $expected"
}
finally {
  Pop-Location
}
