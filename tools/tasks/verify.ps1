param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repoRoot 'AgentPanelSpeaker\AgentPanelSpeaker.csproj'
$exe = Join-Path $repoRoot 'AgentPanelSpeaker\bin\Release\net10.0-windows10.0.22621.0\AgentPanelSpeaker.exe'

& (Join-Path $repoRoot 'tools\Prepare-Build.ps1')

& npm test --prefix (Join-Path $repoRoot 'dependencies\AIConversationCore')
if ($LASTEXITCODE -ne 0) {
  throw 'Pinned AIConversationCore regression tests failed.'
}

& python (Join-Path $repoRoot 'dependencies\AIConversationCore\tests\validate-phase2-baseline.py')
if ($LASTEXITCODE -ne 0) {
  throw 'AIConversationCore Phase 2 fixture/baseline validation failed.'
}

& dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) {
  throw 'AgentPanelSpeaker Release build failed.'
}

& (Join-Path $repoRoot 'tools\Verify-VersionConsistency.ps1') -Executable $exe
& (Join-Path $repoRoot 'tools\Invoke-TestSuite.ps1') -SelfTest
& (Join-Path $repoRoot 'tools\Invoke-TestSuite.ps1') -Executable $exe -Suite all
& (Join-Path $repoRoot 'tools\tasks\robot.ps1') -SkipBuildPreparation
