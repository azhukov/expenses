#Requires -Version 5.1
# Runs Expenses.Integration.Tests with coverage, opens the HTML report, and prints the
# pass/fail tally plus any failing test names. Deterministic entry point for
# /coverage-integration. Needs Docker running (Testcontainers, D17).
$ErrorActionPreference = 'Stop'

# Global dotnet tools may not be on PATH yet in a fresh shell, or right after we install one below.
$env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"

$be = Split-Path -Parent $PSScriptRoot
Set-Location $be

# Stale result folders would be picked up by the report glob below.
$results = Join-Path $be 'TestResults'
if (Test-Path $results) { Remove-Item -Recurse -Force $results }

dotnet test tests/Expenses.Integration.Tests --collect:"XPlat Code Coverage" `
  --results-directory $results | Tee-Object -Variable testOutput
$testExit = $LASTEXITCODE

$tally  = $testOutput | Where-Object { $_ -match '^(Passed|Failed)!\s' }
$failed = $testOutput | Where-Object { $_ -match '^\s*Failed\s+\S' }

if (Test-Path (Join-Path $results '*\coverage.cobertura.xml')) {
  if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    Write-Host "`nInstalling dotnet-reportgenerator-globaltool..."
    dotnet tool install -g dotnet-reportgenerator-globaltool
  }

  $report = Join-Path $results 'report'
  reportgenerator -reports:"$results\*\coverage.cobertura.xml" -targetdir:$report `
    -reporttypes:"Html;TextSummary" -filefilters:"-*.generated.cs" | Out-Null

  # Summary.txt lists every class; print the totals block and the per-assembly lines only.
  Write-Host "`n=== Coverage ==="
  $lines = Get-Content (Join-Path $report 'Summary.txt')
  $inHeader = $true
  foreach ($line in $lines) {
    if ($inHeader) {
      if ($line.Trim() -eq '') { $inHeader = $false } else { Write-Host $line }
    } elseif ($line -match '^\S') {
      Write-Host $line
    }
  }

  $index = Join-Path $report 'index.html'
  Write-Host "`nReport: $index"
  Start-Process $index
} else {
  Write-Host "`nNo coverage data was produced - the run failed before or during collection."
}

Write-Host "`n=== Tests ==="
if ($tally) { $tally | ForEach-Object { Write-Host $_.Trim() } }
if ($failed) {
  Write-Host "`nFailures:"
  $failed | ForEach-Object { Write-Host ("  " + $_.Trim()) }
}
exit $testExit
