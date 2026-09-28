#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
for command_name in node pnpm dotnet python3; do
  command -v "$command_name" >/dev/null 2>&1 || { echo "Falta el comando requerido: $command_name" >&2; exit 2; }
done
pg_bindir="$(pg_config --bindir 2>/dev/null || true)"
if [[ -z "$pg_bindir" || ! -x "$pg_bindir/initdb" ]]; then
  for candidate in /usr/lib/postgresql/*/bin; do [[ -x "$candidate/initdb" ]] && { pg_bindir="$candidate"; break; }; done
fi
[[ -n "$pg_bindir" && -x "$pg_bindir/initdb" ]] || { echo "No se encontró initdb de PostgreSQL." >&2; exit 2; }

qa_tmp="$(mktemp -d "${TMPDIR:-/tmp}/lavomva-e2e.XXXXXX")"
mkdir -p "$qa_tmp/socket"
api_pid=""
pwa_pid=""
cleanup() {
  [[ -n "$pwa_pid" ]] && kill "$pwa_pid" 2>/dev/null || true
  [[ -n "$api_pid" ]] && kill "$api_pid" 2>/dev/null || true
  [[ -n "$pwa_pid" ]] && wait "$pwa_pid" 2>/dev/null || true
  [[ -n "$api_pid" ]] && wait "$api_pid" 2>/dev/null || true
  if [[ -f "$qa_tmp/data/postmaster.pid" ]]; then "$pg_bindir/pg_ctl" -D "$qa_tmp/data" -m fast -w stop >/dev/null || true; fi
  rm -rf "$qa_tmp"
}
trap cleanup EXIT INT TERM

port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')"
api_port="$port"
while [[ "$api_port" == "$port" ]]; do port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')"; done
pwa_port="$port"

export E2E_TMP="$qa_tmp"
export E2E_API_URL="http://127.0.0.1:$api_port"
export E2E_PWA_URL="http://127.0.0.1:$pwa_port"
export E2E_DATABASE_PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')"
export ConnectionStrings__TicketsCombustible="Host=127.0.0.1;Port=$E2E_DATABASE_PORT;Database=lavomva_e2e;Username=qa_runner;Pooling=false"
export Jwt__Key="$(python3 -c 'import secrets; print("E2E TEST ONLY JWT " + secrets.token_urlsafe(40))')"
export Qr__SigningSecret="$(python3 -c 'import secrets; print("E2E TEST ONLY QR " + secrets.token_urlsafe(40))')"
export Bootstrap__Secret="$(python3 -c 'import secrets; print("E2E TEST ONLY bootstrap " + secrets.token_urlsafe(40))')"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="$E2E_API_URL"
export VITE_API_PROXY_TARGET="$E2E_API_URL"

"$pg_bindir/initdb" -D "$qa_tmp/data" -U qa_runner --auth-local=trust --auth-host=trust --no-instructions >/dev/null
"$pg_bindir/pg_ctl" -D "$qa_tmp/data" -l "$qa_tmp/postgres.log" -o "-h 127.0.0.1 -p $E2E_DATABASE_PORT -k $qa_tmp/socket" -w start >/dev/null
"$pg_bindir/createdb" -h 127.0.0.1 -p "$E2E_DATABASE_PORT" -U qa_runner lavomva_e2e
"$pg_bindir/psql" -h 127.0.0.1 -p "$E2E_DATABASE_PORT" -U qa_runner -d lavomva_e2e -v ON_ERROR_STOP=1 -f DATABASE_FINALLL >/dev/null
for migration in backend/TicketsCombustible.Api/Migrations/*.sql; do
  "$pg_bindir/psql" -h 127.0.0.1 -p "$E2E_DATABASE_PORT" -U qa_runner -d lavomva_e2e -v ON_ERROR_STOP=1 -f "$migration" >/dev/null
done

echo "== Building local API and E2E fixture helper =="
dotnet build backend/TicketsCombustible.Api/TicketsCombustible.Api.csproj --configuration Release >/dev/null
dotnet build scripts/e2e-support/E2eSupport.csproj --configuration Release >/dev/null
echo "== Starting API and PWA against temporary PostgreSQL =="
dotnet backend/TicketsCombustible.Api/bin/Release/net8.0/TicketsCombustible.Api.dll --urls "$E2E_API_URL" >"$qa_tmp/api.log" 2>&1 & api_pid=$!
(cd app-movil && node node_modules/vite/bin/vite.js --host 127.0.0.1 --port "$pwa_port" --strictPort) >"$qa_tmp/pwa.log" 2>&1 & pwa_pid=$!
python3 - "$E2E_API_URL/swagger/index.html" "$E2E_PWA_URL/" "$api_pid" "$pwa_pid" <<'PY'
import sys, time, urllib.request
urls = sys.argv[1:3]
pids = [int(sys.argv[3]), int(sys.argv[4])]
deadline = time.time() + 45
while time.time() < deadline:
    try:
        if all(urllib.request.urlopen(url, timeout=1).status < 500 for url in urls): break
    except Exception: pass
    for pid in pids:
        try:
            import os
            os.kill(pid, 0)
        except OSError: raise SystemExit(f"E2E server process {pid} exited before becoming ready")
    time.sleep(.25)
else: raise SystemExit("Local API/PWA did not become ready")
PY

echo "== Playwright Chromium E2E (synthetic camera, temporary database) =="
pnpm exec playwright test --config playwright.config.js
