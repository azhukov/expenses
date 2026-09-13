#!/usr/bin/env bash
# Runs the desktop client's suites in Avalonia-UI/. By default, the two that need nothing running:
# the view-model suite (no UI platform at all) and the headless UI suite (the real views on
# Avalonia's headless platform, the ledger faked). Neither needs a display, Docker or a database.
#
# Run with E2E=1 to run the end-to-end suite instead: the stack from docker-compose.e2e.yml on an
# empty database, the headless client against the real API, and the stack torn down afterwards -
# the same arrangement ./test-e2e.sh gives the browser client, and like it, it publishes port 5082,
# so stop the development API first if it is up.
#
# Run with KEEP=1 (E2E only) to leave the stack running afterwards. Anything after `--` is handed
# to `dotnet test`, so `./test-ui.sh -- --filter HomeViewModel` narrows the run.
#
# On Windows run it from a Git Bash terminal (plain `bash` in PowerShell is WSL).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ui="$root/Avalonia-UI"
compose_file="$root/docker-compose.e2e.yml"

# `dotnet test` reads a bare `--` as the start of inline run settings, where a --filter is silently
# ignored, so the separator this script documents is dropped before the rest is handed on.
[[ "${1:-}" == "--" ]] && shift

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is not on PATH - install the .NET 10 SDK, then rerun." >&2
  exit 1
fi

if [[ "${E2E:-0}" != "1" ]]; then
  suite_exit=0

  for project in Expenses.Desktop.Core.Tests Expenses.Desktop.UI.Tests; do
    printf '\n=== %s ===\n' "$project"
    dotnet test "$ui/tests/$project" "$@" || suite_exit=1
  done

  exit "$suite_exit"
fi

if ! docker info >/dev/null 2>&1; then
  echo "Docker is not running - start it, then rerun (the suite needs the real API and database)." >&2
  exit 1
fi

compose() { docker compose -f "$compose_file" "$@"; }

teardown() {
  if [[ "${KEEP:-0}" = "1" ]]; then
    printf '\nStack left running (KEEP=1). Tear it down with:\n  docker compose -f %s down -v\n' \
      "$compose_file"
    return
  fi

  printf '\n=== Tearing the stack down ===\n'
  # -v as well: the whole point of the e2e database is that it starts empty every time.
  compose down -v >/dev/null 2>&1
}
trap teardown EXIT

printf '=== Bringing the stack up ===\n'
# --wait blocks until every healthcheck passes, which for the API means migrations have been
# applied and Kestrel is listening.
if ! compose up -d --build --wait; then
  echo "The stack did not come up healthy. Its logs:" >&2
  compose logs --tail 50 >&2
  exit 1
fi

printf '\n=== Running the suite ===\n'
EXPENSES_API="${EXPENSES_API:-http://localhost:5082}" dotnet test "$ui/tests/Expenses.Desktop.E2E.Tests" "$@"
exit $?
