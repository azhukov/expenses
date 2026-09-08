#!/usr/bin/env bash
# Runs the client's unit suite - vitest under jsdom, with `fetch` mocked. No Docker, no database,
# no stack: this is the half of the FE tests that needs nothing running. The browser suite is
# ./test-e2e.sh. Exits with the vitest exit code.
#
# Run with WATCH=1 to stay in vitest's watch mode instead of a single run. Anything after `--` is
# handed to vitest, so `./test-fe.sh -- src/features/purchases` narrows the run.
#
# On Windows run it from a Git Bash terminal (plain `bash` in PowerShell is WSL).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fe="$root/FE"

if ! command -v npm >/dev/null 2>&1; then
  echo "npm is not on PATH - install Node, then rerun." >&2
  exit 1
fi

cd "$fe" || exit 1

# First run only. `npm ci` rather than `install` so the lockfile decides, as it does in CI.
if [[ ! -d node_modules ]]; then
  printf '=== Installing client dependencies ===\n'
  npm ci || exit 1
fi

printf '\n=== Running the suite ===\n'
if [[ "${WATCH:-0}" = "1" ]]; then
  exec npm run test:watch -- "$@"
fi

npm test -- "$@"
exit $?
