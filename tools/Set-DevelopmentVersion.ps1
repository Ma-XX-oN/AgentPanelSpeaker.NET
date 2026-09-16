param(
  [Parameter(Mandatory = $true)]
  [ValidateRange(1, 2147483647)]
  [int]$Issue,
  [ValidateRange(0, 2147483647)]
  [int]$Iteration = 0,
  [string]$DevelopmentVersionPath,
  [string]$ProjectPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($DevelopmentVersionPath)) {
  $DevelopmentVersionPath = Join-Path $repoRoot 'DEVELOPMENT-VERSION'
}
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
  $ProjectPath = Join-Path $repoRoot 'AgentPanelSpeaker\AgentPanelSpeaker.csproj'
}

$current = (Get-Content -LiteralPath $DevelopmentVersionPath -Raw).Trim()
$match = [regex]::Match(
  $current,
  '^(?<base>\d+\.\d+\.\d+)-issue\.(?<issue>\d+)\.(?<iteration>\d+)$')
if (-not $match.Success) {
  throw "Invalid current development version: $current"
}

if ($Iteration -eq 0) {
  $Iteration = if ([int]$match.Groups['issue'].Value -eq $Issue) {
    [int]$match.Groups['iteration'].Value + 1
  }
  else {
    1
  }
}

$next = "{0}-issue.{1}.{2}" -f $match.Groups['base'].Value, $Issue, $Iteration
$project = Get-Content -LiteralPath $ProjectPath -Raw
$versionMatches = [regex]::Matches($project, '<Version>[^<]+</Version>')
if ($versionMatches.Count -ne 1) {
  throw "Expected exactly one <Version> element in $ProjectPath; found $($versionMatches.Count)."
}
$project = [regex]::Replace(
  $project,
  '<Version>[^<]+</Version>',
  "<Version>$next</Version>",
  1)

Set-Content -LiteralPath $DevelopmentVersionPath -Value $next -NoNewline
Set-Content -LiteralPath $ProjectPath -Value $project -NoNewline
Write-Output $next
