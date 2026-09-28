#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

for command_name in node pnpm dotnet python3; do
  command -v "$command_name" >/dev/null 2>&1 || { echo "Falta el comando requerido: $command_name" >&2; exit 2; }
done

pg_bindir=""
if command -v pg_config >/dev/null 2>&1; then pg_bindir="$(pg_config --bindir)"; fi
if [[ -z "$pg_bindir" || ! -x "$pg_bindir/initdb" ]]; then
  for candidate in /usr/lib/postgresql/*/bin; do
    if [[ -x "$candidate/initdb" ]]; then pg_bindir="$candidate"; break; fi
  done
fi
[[ -n "$pg_bindir" && -x "$pg_bindir/initdb" ]] || { echo "No se encontró initdb de PostgreSQL." >&2; exit 2; }

qa_tmp="$(mktemp -d "${TMPDIR:-/tmp}/lavomva-qa.XXXXXX")"
mkdir -p "$qa_tmp/socket"
cleanup() {
  if [[ -f "$qa_tmp/data/postmaster.pid" ]]; then "$pg_bindir/pg_ctl" -D "$qa_tmp/data" -m fast -w stop >/dev/null || true; fi
  rm -rf "$qa_tmp"
}
trap cleanup EXIT
qa_port="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')"

"$pg_bindir/initdb" -D "$qa_tmp/data" -U qa_runner --auth-local=trust --auth-host=trust --no-instructions >/dev/null
"$pg_bindir/pg_ctl" -D "$qa_tmp/data" -l "$qa_tmp/postgres.log" -o "-h 127.0.0.1 -p $qa_port -k $qa_tmp/socket" -w start >/dev/null
"$pg_bindir/createdb" -h 127.0.0.1 -p "$qa_port" -U qa_runner lavomva_test
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/schema/DATABASE_FINALLL >/dev/null
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/migrations/001_auditoria_inmutable_y_resultado.sql >/dev/null
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/migrations/002_ticket_lifecycle.sql >/dev/null
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/migrations/003_cierre_diario.sql >/dev/null
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/migrations/004_ticket_delivery.sql >/dev/null
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/migrations/005_solicitud_scheduling.sql >/dev/null
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/migrations/006_persistent_notifications.sql >/dev/null
"$pg_bindir/psql" -h 127.0.0.1 -p "$qa_port" -U qa_runner -d lavomva_test -v ON_ERROR_STOP=1 -f BASEDATOS/migrations/007_auth_sessions.sql >/dev/null
export QA_TEST_CONNECTION="Host=127.0.0.1;Port=$qa_port;Database=lavomva_test;Username=qa_runner;Pooling=false"

echo "== Web unit tests =="
pnpm test
echo "== Web build =="
pnpm build:web
echo "== Mobile unit tests =="
npm --prefix APPMOBILE test
echo "== Secret and configuration regression tests =="
pnpm test:security
echo "== Mobile build =="
npm --prefix APPMOBILE run build
echo "== API build =="
dotnet build BACKEND/API/TicketsCombustible.Api.csproj --configuration Release
echo "== API integration tests + coverage (PostgreSQL aislado) =="
dotnet test BACKEND/Tests/API/TicketsCombustible.Api.Tests.csproj --configuration Release --collect:"XPlat Code Coverage" --results-directory "$qa_tmp/TestResults" --logger "console;verbosity=minimal"
python3 - "$qa_tmp/TestResults" <<'PY'
import pathlib, sys, xml.etree.ElementTree as ET
files = list(pathlib.Path(sys.argv[1]).rglob("coverage.cobertura.xml"))
if not files:
    print("No se generó coverage.cobertura.xml", file=sys.stderr)
    sys.exit(1)
root = ET.parse(files[-1]).getroot()
print(f"Coverage backend: líneas={float(root.get('line-rate', '0'))*100:.2f}% ramas={float(root.get('branch-rate', '0'))*100:.2f}%")
methods = list(root.iter("method"))
if methods:
    covered = sum(1 for method in methods if any(int(line.get("hits", "0")) > 0 for line in method.iter("line")))
    print(f"Coverage backend: métodos={covered}/{len(methods)} ({covered/len(methods)*100:.2f}%)")
PY

echo "Todas las suites finalizaron correctamente. La base de datos aislada se elimina al salir."
