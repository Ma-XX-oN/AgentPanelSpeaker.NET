param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$corePath = Join-Path $repoRoot 'dependencies\AIConversationCore'

Push-Location $repoRoot
try {
  & git submodule update --init --recursive
  if ($LASTEXITCODE -ne 0) {
    throw 'Failed to initialize repository submodules.'
  }

  & (Join-Path $PSScriptRoot 'Verify-CorePinConsistency.ps1')
  if ($LASTEXITCODE -ne 0) {
    throw 'AIConversationCore pin consistency verification failed.'
  }
  $actual = (& git -C $corePath rev-parse HEAD).Trim()

  & npm ci --prefix $corePath --no-audit --no-fund
  if ($LASTEXITCODE -ne 0) {
    throw 'Installing pinned AIConversationCore dependencies failed.'
  }

  Write-Host "PASS repository build preparation: AIConversationCore $actual"
}
finally {
  Pop-Location
}
