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
  if ($actual -ne $expected) {
    throw "Unexpected AIConversationCore submodule revision: expected=$expected actual=$actual"
  }

  & npm ci --prefix $corePath --no-audit --no-fund
  if ($LASTEXITCODE -ne 0) {
    throw 'Installing pinned AIConversationCore dependencies failed.'
  }

  Write-Host "PASS repository build preparation: AIConversationCore $actual"
}
finally {
  Pop-Location
}
