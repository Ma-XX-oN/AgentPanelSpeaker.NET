param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
  [string]$Task,
  [string[]]$ArgumentList = @()
)

$ErrorActionPreference = 'Stop'
$taskPath = Join-Path $PSScriptRoot ("tasks\{0}.ps1" -f $Task)
if (-not (Test-Path -LiteralPath $taskPath)) {
  throw "Unknown repository task '$Task'. Expected checked-in task: $taskPath"
}

& $taskPath @ArgumentList
