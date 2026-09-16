param(
  [string]$Executable,
  [string]$Suite = 'all',
  [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

function Assert-TestCompletion {
  param(
    [int]$ExitCode,
    [string[]]$Output,
    [string]$ExpectedSuite
  )

  if ($ExitCode -ne 0) {
    throw "Regression suite '$ExpectedSuite' exited with code $ExitCode."
  }

  $marker = "TEST-SUITE-COMPLETE $ExpectedSuite exit=0"
  if (-not ($Output | Where-Object { $_.Trim() -ceq $marker })) {
    throw "Regression suite '$ExpectedSuite' did not emit completion marker '$marker'."
  }
}

if ($SelfTest) {
  $rejectedPartial = $false
  try {
    Assert-TestCompletion `
      -ExitCode 0 `
      -Output @('Issue #44 independent-output-oracle suite: 3 tests') `
      -ExpectedSuite 'output-oracle'
  }
  catch {
    $rejectedPartial = $true
  }
  if (-not $rejectedPartial) {
    throw 'Completion validation accepted a header-only partial suite.'
  }

  Assert-TestCompletion `
    -ExitCode 0 `
    -Output @('TEST-SUITE-COMPLETE output-oracle exit=0') `
    -ExpectedSuite 'output-oracle'
  Write-Host 'PASS  test-runner/header-only-output-is-rejected'
  Write-Host 'PASS  test-runner/completion-marker-is-accepted'
  return
}

if ([string]::IsNullOrWhiteSpace($Executable)) {
  throw '-Executable is required unless -SelfTest is used.'
}
if (-not (Test-Path -LiteralPath $Executable)) {
  throw "Test executable does not exist: $Executable"
}

$expectedSuite = if ([string]::IsNullOrWhiteSpace($Suite) -or $Suite -ceq 'all') {
  'all'
}
else {
  $Suite
}
$arguments = @('--test')
if ($expectedSuite -cne 'all') {
  $arguments += $expectedSuite
}

$stdout = Join-Path $env:TEMP ("AgentPanelSpeaker-test-stdout-{0}.log" -f [guid]::NewGuid().ToString('N'))
$stderr = Join-Path $env:TEMP ("AgentPanelSpeaker-test-stderr-{0}.log" -f [guid]::NewGuid().ToString('N'))
try {
  $process = Start-Process `
    -FilePath $Executable `
    -ArgumentList $arguments `
    -Wait `
    -PassThru `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr

  $output = if (Test-Path -LiteralPath $stdout) {
    @(Get-Content -LiteralPath $stdout)
  }
  else {
    @()
  }
  $errors = if (Test-Path -LiteralPath $stderr) {
    @(Get-Content -LiteralPath $stderr)
  }
  else {
    @()
  }

  $output | ForEach-Object { Write-Host $_ }
  $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
  Assert-TestCompletion `
    -ExitCode $process.ExitCode `
    -Output $output `
    -ExpectedSuite $expectedSuite
}
finally {
  Remove-Item -LiteralPath $stdout -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $stderr -Force -ErrorAction SilentlyContinue
}
