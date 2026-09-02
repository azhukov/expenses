#!/usr/bin/env bash
# Runs Expenses.Integration.Tests with coverage, writes a browsable HTML report to
# BE/TestResults/report, prints the pass/fail tally and the coverage summary, and opens the
# report in the browser. Deterministic: fixed paths, no prompts. Exits with the dotnet test
# exit code. Needs Docker running (Testcontainers, D17).
#
# On Windows run it from a Git Bash terminal (plain `bash` in PowerShell is WSL).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
be="$root/BE"

# Under Git Bash, dotnet and reportgenerator are native Windows binaries: hand them Windows paths.
winpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# Global dotnet tools may not be on PATH in a fresh shell, or right after we install one below.
PATH="${USERPROFILE:-$HOME}/.dotnet/tools:$HOME/.dotnet/tools:$PATH"

results="$be/TestResults"
report="$results/report"
log="$results/test-output.txt"
trx="$results/tests.trx"

# The suite spins up its own PostgreSQL through Testcontainers (D17), so Docker has to be up.
# The rest of the local stack is not needed here - ./up.sh starts that.
if ! docker info >/dev/null 2>&1; then
  echo "Docker is not running - start Docker Desktop, then rerun (Testcontainers needs it, D17)." >&2
  exit 1
fi

# Stale result folders would be picked up by the coverage glob below.
rm -rf "$results"
mkdir -p "$results"

cd "$be"
dotnet test tests/Expenses.Integration.Tests --collect:"XPlat Code Coverage" \
  --logger "trx;LogFileName=tests.trx" --results-directory "$(winpath "$results")" 2>&1 | tee "$log"
test_exit=${PIPESTATUS[0]}

# The trx counters are the authoritative tally - the console summary line is absent when the run
# is cut short before any test executes.
count() { sed -n 's/.*<Counters [^>]*'"$1"'="\([0-9]*\)".*/\1/p' "$trx" | head -1; }
if [[ -f "$trx" ]]; then
  total=$(count total); passed=$(count passed); failed=$(count failed)
  skipped=$(( ${total:-0} - ${passed:-0} - ${failed:-0} ))
  tally="${passed:-0} passed, ${failed:-0} failed, $skipped skipped, ${total:-0} total"
else
  tally="no test results were produced"
fi

printf '\n=== Tests ===\n%s\n' "$tally"
if [[ -f "$trx" ]] && grep -q 'outcome="Failed"' "$trx"; then
  printf '\nFailures:\n'
  sed -n 's/.*<UnitTestResult[^>]*testName="\([^"]*\)"[^>]*outcome="Failed".*/  \1/p' "$trx"
  sed -n 's|.*<Message>\(.*\)</Message>.*|    \1|p' "$trx" | head -20
fi

coverage=("$results"/*/coverage.cobertura.xml)
if [[ ! -f "${coverage[0]}" ]]; then
  printf '\nNo coverage data was produced - the run failed before or during collection.\n'
  exit "$test_exit"
fi

if ! command -v reportgenerator >/dev/null 2>&1; then
  printf '\nInstalling dotnet-reportgenerator-globaltool...\n'
  dotnet tool install -g dotnet-reportgenerator-globaltool
fi

reports=""
for file in "${coverage[@]}"; do reports+="${reports:+;}$(winpath "$file")"; done
# The title carries the tally into the HTML report, so the page shows tests and coverage both.
reportgenerator "-reports:$reports" "-targetdir:$(winpath "$report")" \
  -reporttypes:"Html;TextSummary" -filefilters:"-*.generated.cs" \
  -title:"Integration tests - $tally" >/dev/null

# Summary.txt lists every class after the totals block; print the totals and the per-assembly
# lines only - indented class rows are dropped.
printf '\n=== Coverage ===\n'
awk 'BEGIN { in_header = 1 }
     in_header && $0 == "" { in_header = 0; next }
     in_header || /^[^[:space:]]/ { print }' "$report/Summary.txt"

printf '\nReport: %s\n' "$report/index.html"

# Opening it is the point of the script - the tally alone does not need a browser.
if command -v cygpath >/dev/null 2>&1; then
  cmd //c start "" "$(winpath "$report/index.html")" >/dev/null 2>&1   # Git Bash on Windows
elif command -v xdg-open >/dev/null 2>&1; then
  xdg-open "$report/index.html" >/dev/null 2>&1 &
elif command -v open >/dev/null 2>&1; then
  open "$report/index.html"
fi

exit "$test_exit"
