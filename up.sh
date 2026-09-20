#!/usr/bin/env bash
# Brings the local stack up - postgres, api, mcp - waits until every healthcheck passes, then runs
# the browser client and opens it.
#
# The browser client is a dev server, not a container, so it holds this terminal: Ctrl+C stops it
# and leaves the containers up.
#
# Run with NO_FE=1 to skip the browser client (for the containers alone), and FE_HOST=1 to expose
# the browser client on the LAN so a phone can reach it.
# FE_HOST implies HTTPS (FE_HTTPS=1, a self-signed certificate): a LAN address is not a secure
# context, and the camera is only offered to one. FE_HTTPS=1 alone serves HTTPS on localhost only.
#
# The browser client calls the API directly at API_URL (http://localhost:5082 unless set). With
# FE_HOST that has to be HTTPS too, or the page's calls are blocked as mixed content: the API is
# also served at https://<LAN_HOST>:5443 (docker-compose.lan.yml) with a self-signed certificate
# made here once and kept in .certs/. LAN_HOST is <hostname>.local unless set - set it to the
# machine's IPv4 address where the phone cannot resolve .local names. Each device opens the API URL
# printed below once and accepts the warning; until it has, the client says the ledger could not
# be reached.
#
# On Windows run it from a Git Bash terminal (plain `bash` in PowerShell is WSL).
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fe="$root/FE"
if [ -n "${FE_HOST:-}" ]; then
  export FE_HTTPS=1
fi
fe_scheme="${FE_HTTPS:+https}"
fe_scheme="${fe_scheme:-http}"
fe_url="$fe_scheme://localhost:5173"

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

# Makes .certs/lan.pem for LAN_HOST unless one that names it and has not expired is already there.
# Reused rather than remade, because a phone's accepted exception belongs to that certificate. The
# parameters are the ones iOS insists on even after a warning is accepted: the name in the SAN,
# serverAuth, a 2048-bit key, and a lifetime well under its 825-day ceiling.
lan_certificate() {
  local certs="$root/.certs" san
  if [[ "$LAN_HOST" =~ ^[0-9]+(\.[0-9]+){3}$ ]]; then san="IP:$LAN_HOST"; else san="DNS:$LAN_HOST"; fi

  if [ -f "$certs/lan.pem" ] && [ -f "$certs/lan-key.pem" ] \
    && openssl x509 -in "$certs/lan.pem" -noout -checkend 86400 >/dev/null 2>&1 \
    && openssl x509 -in "$certs/lan.pem" -noout -ext subjectAltName 2>/dev/null \
      | grep -Eq "(DNS:|IP Address:)$LAN_HOST(,|\$)"; then
    return 0
  fi

  echo "Making a self-signed certificate for $LAN_HOST in .certs/ - devices will need to accept it once."
  mkdir -p "$certs" || return 1
  # MSYS_NO_PATHCONV: Git Bash would otherwise rewrite the /CN=... subject into a Windows path. It
  # also stops it converting file paths for a native openssl, hence running inside .certs/ on
  # relative names.
  (cd "$certs" && MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 397 \
    -subj "/CN=$LAN_HOST" \
    -addext "subjectAltName=$san,DNS:localhost" \
    -addext "extendedKeyUsage=serverAuth" \
    -addext "keyUsage=critical,digitalSignature,keyEncipherment" \
    -addext "basicConstraints=critical,CA:FALSE" \
    -keyout lan-key.pem -out lan.pem >/dev/null 2>&1) || return 1
  # The API runs as a non-root user in its container and reads the key through a bind mount. This
  # is a development certificate for one LAN, not a secret worth a permissions scheme.
  chmod 644 "$certs/lan-key.pem"
}

if ! docker info >/dev/null 2>&1; then
  echo "Docker is not running - start Docker Desktop, then rerun." >&2
  exit 1
fi

compose_files=(-f "$(winpath "$root/docker-compose.yml")")
if [ -n "${FE_HOST:-}" ]; then
  LAN_HOST="${LAN_HOST:-$(hostname).local}"
  export LAN_HOST="${LAN_HOST,,}"

  if ! command -v openssl >/dev/null 2>&1; then
    echo "openssl is not on PATH - it makes the API's LAN certificate. Git Bash ships it; or rerun without FE_HOST." >&2
    exit 1
  fi
  lan_certificate || { echo "Could not make the certificate in .certs/." >&2; exit 1; }

  compose_files+=(-f "$(winpath "$root/docker-compose.lan.yml")")
  api_url="https://$LAN_HOST:5443"
else
  api_url="http://localhost:5082"
fi
# Read by the Vite dev server when it starts, and served to the page as /config.js.
export API_URL="${API_URL:-$api_url}"

# Always --build: an image built only when missing silently keeps serving old code after a pull or
# a change to BE. The layer cache keeps an unchanged rebuild to a few seconds.
docker compose "${compose_files[@]}" up -d --wait --build || exit 1

echo
echo "API      http://localhost:5082/swagger"
if [ -n "${FE_HOST:-}" ]; then
  echo "         https://$LAN_HOST:5443/openapi/v1.json   (open once on each device and accept the warning)"
fi
echo "MCP      http://localhost:5083"
echo "Postgres localhost:5432   user/password/database all 'expenses'"

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
if [ -n "${FE_HOST:-}" ]; then
  # By LAN_HOST: the machine name through mDNS (.local), which phones resolve on the same network, or
  # the address it was set to. The API's origin list allows this origin and no other LAN one, so
  # this is the URL to open. Its certificate is self-signed too, so the phone warns once here as well.
  echo "         $fe_scheme://$LAN_HOST:5173   (from a phone on this network)"
  echo "         'The ledger could not be reached' means the API certificate above has not been accepted on that device."
fi
echo "Ledger   $API_URL"
echo

# Opened once the dev server answers, not before: a browser that arrives first shows a connection
# error and has to be reloaded by hand.
(
  for _ in $(seq 1 60); do
    # -k: the certificate FE_HTTPS serves is self-signed, and all this asks is whether Vite answers.
    if curl -fsSk -o /dev/null --max-time 2 "$fe_url" 2>/dev/null; then
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
