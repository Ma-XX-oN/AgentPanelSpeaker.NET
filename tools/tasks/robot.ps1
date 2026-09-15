param(
  [string[]]$RobotArguments = @(),
  [switch]$SkipBuildPreparation
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$requirements = Join-Path $repoRoot 'requirements-test.txt'
$tests = Join-Path $repoRoot 'tests\robot'
$probeProject = Join-Path $repoRoot 'tests\AgentPanelSpeaker.TestProbe\AgentPanelSpeaker.TestProbe.csproj'
$probeExe = Join-Path $repoRoot 'tests\AgentPanelSpeaker.TestProbe\bin\Release\net10.0-windows10.0.22621.0\AgentPanelSpeaker.TestProbe.exe'
$outputRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
  Join-Path ([System.IO.Path]::GetTempPath()) ("AgentPanelSpeaker-robot-{0}" -f [guid]::NewGuid().ToString('N'))
}
else {
  Join-Path $env:RUNNER_TEMP ("AgentPanelSpeaker-robot-{0}" -f [guid]::NewGuid().ToString('N'))
}

if (-not $SkipBuildPreparation) {
  & (Join-Path $repoRoot 'tools\Prepare-Build.ps1')
  & dotnet build $probeProject --configuration Release
  if ($LASTEXITCODE -ne 0) {
    throw 'Building the generic Robot test probe failed.'
  }
}
else {
  & dotnet restore $probeProject
  if ($LASTEXITCODE -ne 0) {
    throw 'Restoring the generic Robot test probe failed.'
  }
  & dotnet build $probeProject --configuration Release --no-restore -p:BuildProjectReferences=false
  if ($LASTEXITCODE -ne 0) {
    throw 'Building the generic Robot test probe failed.'
  }
}

if (-not (Test-Path -LiteralPath $probeExe)) {
  throw "Generic Robot test probe was not produced: $probeExe"
}

& python -m pip install --disable-pip-version-check --requirement $requirements
if ($LASTEXITCODE -ne 0) {
  throw 'Installing pinned Robot Framework test dependencies failed.'
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
try {
  $arguments = @(
    '-m', 'robot',
    '--outputdir', $outputRoot,
    '--log', 'NONE',
    '--report', 'NONE',
    '--variable', "TEST_PROBE:$probeExe"
  )
  $arguments += $RobotArguments
  $arguments += $tests
  & python @arguments
  if ($LASTEXITCODE -ne 0) {
    throw "Robot Framework regression suite failed with code $LASTEXITCODE."
  }
}
finally {
  Remove-Item -LiteralPath $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
}
