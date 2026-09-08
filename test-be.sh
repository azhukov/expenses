#!/usr/bin/env bash
# Runs every BE suite - Domain, Application and Integration - with coverage, merges the three into
# one summary and prints the pass/fail tally and the coverage table to the console. No HTML, no
# browser: everything this script has to say it says in the terminal. Deterministic: fixed paths,
# no prompts. Exits non-zero if any suite failed. Needs Docker running (Testcontainers, D17).
#
# Run with UNIT=1 for the two container-free suites alone, which needs no Docker and finishes in
# seconds, and DETAIL=1 for the per-class coverage rows rather than the per-assembly totals.
# Anything after `--` is handed to every `dotnet test` invocation.
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
summary_dir="$results/coverage"
log="$results/test-output.txt"

# One project per invocation: `dotnet test` takes one, and a second is MSB1008. Domain and
# Application first - they need no containers, so a domain regression is reported long before
# PostgreSQL has started, which is the same order CI runs them in.
suites=(Domain Application)
[[ "${UNIT:-0}" = "1" ]] || suites+=(Integration)

# Only the integration suite spins up its own PostgreSQL through Testcontainers (D17), so Docker
# has to be up for it. The rest of the local stack is not needed here - ./up.sh starts that.
if [[ "${UNIT:-0}" != "1" ]] && ! docker info >/dev/null 2>&1; then
  echo "Docker is not running - start Docker Desktop, then rerun (Testcontainers needs it, D17)." >&2
  echo "For the Domain and Application suites alone, which need no containers: UNIT=1 ./test-be.sh" >&2
  exit 1
fi

# Stale result folders would be picked up by the trx and coverage globs below.
rm -rf "$results"
mkdir -p "$results"

cd "$be"
: > "$log"
test_exit=0
for suite in "${suites[@]}"; do
  printf '\n=== %s tests ===\n' "$suite" | tee -a "$log"
  # Each run gets its own trx and its own guid folder of coverage, both collected below.
  dotnet test "tests/Expenses.$suite.Tests" --collect:"XPlat Code Coverage" \
    --logger "trx;LogFileName=$suite.trx" --results-directory "$(winpath "$results")" \
    "$@" 2>&1 | tee -a "$log"
  # The first failure does not stop the rest: one run should report every broken suite.
  [[ ${PIPESTATUS[0]} -eq 0 ]] || test_exit=1
done

# The trx counters are the authoritative tally - the console summary line is absent when a run
# is cut short before any test executes. Summed across the suites that produced one.
trx=("$results"/*.trx)
count() { sed -n 's/.*<Counters [^>]*'"$2"'="\([0-9]*\)".*/\1/p' "$1" | head -1; }
if [[ -f "${trx[0]}" ]]; then
  total=0; passed=0; failed=0
  for file in "${trx[@]}"; do
    total=$(( total + $(count "$file" total) ))
    passed=$(( passed + $(count "$file" passed) ))
    failed=$(( failed + $(count "$file" failed) ))
  done
  tally="$passed passed, $failed failed, $(( total - passed - failed )) skipped, $total total"
else
  tally="no test results were produced"
fi

printf '\n=== Tests ===\n%s\n' "$tally"
for file in "${trx[@]}"; do
  [[ -f "$file" ]] && grep -q 'outcome="Failed"' "$file" || continue
  printf '\nFailures in %s:\n' "$(basename "$file" .trx)"
  sed -n 's/.*<UnitTestResult[^>]*testName="\([^"]*\)"[^>]*outcome="Failed".*/  \1/p' "$file"
  sed -n 's|.*<Message>\(.*\)</Message>.*|    \1|p' "$file" | head -20
done

coverage=("$results"/*/coverage.cobertura.xml)
if [[ ! -f "${coverage[0]}" ]]; then
  printf '\nNo coverage data was produced - the run failed before or during collection.\n'
  exit "$test_exit"
fi

if ! command -v reportgenerator >/dev/null 2>&1; then
  printf '\nInstalling dotnet-reportgenerator-globaltool...\n'
  dotnet tool install -g dotnet-reportgenerator-globaltool
fi

# One summary over all three files: a line the unit suites cover and the integration suite does not
# is covered, and only the merge can say so. TextSummary alone - no HTML is written, so the only
# artifact is the text file this then prints.
reports=""
for file in "${coverage[@]}"; do reports+="${reports:+;}$(winpath "$file")"; done
reportgenerator "-reports:$reports" "-targetdir:$(winpath "$summary_dir")" \
  -reporttypes:"TextSummary" -filefilters:"-*.generated.cs" >/dev/null

# Summary.txt lists every class after the totals block. The per-assembly lines are the useful
# altitude, so the indented class rows are dropped unless DETAIL=1 asks for them.
printf '\n=== Coverage ===\n'
if [[ "${DETAIL:-0}" = "1" ]]; then
  cat "$summary_dir/Summary.txt"
else
  awk 'BEGIN { in_header = 1 }
       in_header && $0 == "" { in_header = 0; next }
       in_header || /^[^[:space:]]/ { print }' "$summary_dir/Summary.txt"
fi

exit "$test_exit"
