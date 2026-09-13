#!/usr/bin/env bash
# Brings the local stack up - postgres, api, mcp - waits until every healthcheck passes, then starts
# the desktop client and runs the browser client and opens it.
#
# The desktop client is a window of its own: it is started in the background and outlives this
# script, so close it like any other window. The browser client is a dev server, not a container, so
# it holds this terminal: Ctrl+C stops it and leaves the containers and the desktop window up.
#
# Run with NO_DESKTOP=1 to skip the desktop client, NO_FE=1 to skip the browser client (both for the
# containers alone), COMPOSE_BUILD=1 to rebuild the api and mcp images first, and FE_HOST=1 to expose
# the browser client on the LAN so a phone can reach it.
#
# On Windows run it from a Git Bash terminal (plain `bash` in PowerShell is WSL).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fe="$root/FE"
fe_url="http://localhost:5173"
desktop="$root/Avalonia-UI/src/Expenses.Desktop"
desktop_log="${TMPDIR:-/tmp}/expenses-desktop.log"

# Under Git Bash the docker CLI is a native Windows binary: hand it a Windows path.
winpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

browse() {
  if command -v cygpath >/dev/null 2>&1; then
    cmd //c start "" "$1" >/dev/null 2>&1   # Git Bash on Windows
  elif command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$1" >/dev/null 2>&1 &
  elif command -v open >/dev/null 2>&1; then
    open "$1"
  fi
}

if ! docker info >/dev/null 2>&1; then
  echo "Docker is not running - start Docker Desktop, then rerun." >&2
  exit 1
fi

# Images are built only when missing unless COMPOSE_BUILD is set.
docker compose -f "$(winpath "$root/docker-compose.yml")" up -d --wait ${COMPOSE_BUILD:+--build} || exit 1

echo
echo "API      http://localhost:5082/swagger"
echo "MCP      http://localhost:5083"
echo "Postgres localhost:5432   user/password/database all 'expenses'"

if [ -z "${NO_DESKTOP:-}" ]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    echo
    echo "dotnet is not on PATH - install the .NET 10 SDK, or rerun with NO_DESKTOP=1." >&2
    exit 1
  fi

  # Built in the foreground, so a compile error is reported here rather than as a window that never
  # appears.
  echo
  echo "Building the desktop client..."
  dotnet build "$(winpath "$desktop/Expenses.Desktop.csproj")" --nologo -v quiet || exit 1

  # Detached, with its output in a log: the window is independent of this terminal, and the browser
  # client below replaces this process.
  nohup dotnet "$(winpath "$desktop/bin/Debug/net10.0/Expenses.Desktop.dll")" >"$desktop_log" 2>&1 &
  disown
  echo "Desktop  started, log in $desktop_log"
fi

if [ -n "${NO_FE:-}" ]; then
  exit 0
fi

if ! command -v npm >/dev/null 2>&1; then
  echo
  echo "npm is not on PATH - install Node, or rerun with NO_FE=1 for the containers alone." >&2
  exit 1
fi

# First run only. `npm ci` rather than `install` so the lockfile decides, as it does in CI.
if [ ! -d "$fe/node_modules" ]; then
  echo
  echo "Installing client dependencies..."
  (cd "$fe" && npm ci) || exit 1
fi

echo "Client   $fe_url"
echo

# Opened once the dev server answers, not before: a browser that arrives first shows a connection
# error and has to be reloaded by hand.
(
  for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null --max-time 2 "$fe_url" 2>/dev/null; then
      browse "$fe_url"
      exit 0
    fi
    sleep 1
  done
  echo "The client did not come up - open $fe_url by hand once Vite reports ready." >&2
) &

# In the foreground on purpose: this is the one process here that is not a container, so Ctrl+C
# has to reach it. The containers stay up afterwards; `docker compose down` stops those.
cd "$fe" && exec npm run dev -- ${FE_HOST:+--host}
