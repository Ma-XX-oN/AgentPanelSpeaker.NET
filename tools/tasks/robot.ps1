param(
  [string[]]$RobotArguments = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$requirements = Join-Path $repoRoot 'requirements-test.txt'
$tests = Join-Path $repoRoot 'tests\robot'
$outputRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
  Join-Path ([System.IO.Path]::GetTempPath()) ("AgentPanelSpeaker-robot-{0}" -f [guid]::NewGuid().ToString('N'))
}
else {
  Join-Path $env:RUNNER_TEMP ("AgentPanelSpeaker-robot-{0}" -f [guid]::NewGuid().ToString('N'))
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
    '--report', 'NONE'
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
