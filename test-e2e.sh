#!/usr/bin/env bash
# Runs the end-to-end suite against a real stack: PostgreSQL and the HTTP host from
# docker-compose.e2e.yml, the built client served by `vite preview`, and a real browser driving it.
# Nothing is mocked. Exits with the Playwright exit code.
#
# The stack is its own compose project on an empty database, so it neither adopts nor destroys
# whatever ./up.sh has running - except for port 5082, which both publish; stop the development
# API first if it is up.
#
# Run with KEEP=1 to leave the stack running afterwards (the report and any trace are worth more
# with the API still answering), HEADED=1 to watch the browser, and SKIP_BUILD=1 to reuse the
# client already in FE/dist. Anything after `--` is handed to `playwright test`.
#
# On Windows run it from a Git Bash terminal (plain `bash` in PowerShell is WSL).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fe="$root/FE"
compose_file="$root/docker-compose.e2e.yml"

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
# applied and Kestrel is listening (D15).
if ! compose up -d --build --wait; then
  echo "The stack did not come up healthy. Its logs:" >&2
  compose logs --tail 50 >&2
  exit 1
fi

cd "$fe" || exit 1

# The browser binary is a separate download from the npm package, and CI installs it explicitly.
if ! npx playwright install chromium >/dev/null 2>&1; then
  echo "Could not install the Playwright browser. Run: npx playwright install chromium" >&2
  exit 1
fi

if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
  printf '\n=== Building the client ===\n'
  npm run build || exit 1
fi

printf '\n=== Running the suite ===\n'
args=()
[[ "${HEADED:-0}" = "1" ]] && args+=(--headed)

npx playwright test "${args[@]}" "$@"
suite_exit=$?

printf '\nReport: %s\n' "$fe/playwright-report/index.html"
exit "$suite_exit"
