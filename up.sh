#!/usr/bin/env bash
# Brings the local stack up - postgres, api, mcp - and waits until every healthcheck passes.
# Run with COMPOSE_BUILD=1 to rebuild the api and mcp images first.
#
# On Windows run it from a Git Bash terminal (plain `bash` in PowerShell is WSL).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Under Git Bash the docker CLI is a native Windows binary: hand it a Windows path.
winpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

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
