#!/usr/bin/env bash
# Clear + seed RaceDay Docker DB (1 lệnh).
# Usage: ./deploy/clear-and-seed.sh [--skip-mongo]

set -euo pipefail

SKIP_MONGO=0
for arg in "$@"; do
  case "$arg" in --skip-mongo) SKIP_MONGO=1 ;; esac
done

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$DEPLOY_DIR/docker-compose.yml"
SQL_FILE="$DEPLOY_DIR/sql/clear_and_seed.sql"

step() { printf '\n==> %s\n' "$*"; }

resolve() {
  local service="$1" ancestor="$2"
  if [[ -f "$COMPOSE_FILE" ]]; then
    local id
    id="$(docker compose -f "$COMPOSE_FILE" ps -q "$service" 2>/dev/null | head -n1 || true)"
    if [[ -n "$id" ]]; then
      docker inspect -f '{{.Name}}' "$id" | sed 's#^/##'
      return 0
    fi
  fi
  docker ps --filter "ancestor=$ancestor" --format '{{.Names}}' | head -n1
}

[[ -f "$SQL_FILE" ]] || { echo "Missing $SQL_FILE" >&2; exit 1; }

step "Resolve containers"
PG="$(resolve postgres postgres:18-alpine || true)"
[[ -n "${PG:-}" ]] || PG="$(docker ps --format '{{.Names}}' | grep -E 'postgres' | head -n1 || true)"
[[ -n "${PG:-}" ]] || { echo "Postgres not found. Start deploy/docker-compose.yml postgres." >&2; exit 1; }
echo "    postgres: $PG"

MONGO=""
if [[ "$SKIP_MONGO" -eq 0 ]]; then
  MONGO="$(resolve mongodb mongo:7 || true)"
  [[ -n "$MONGO" ]] && echo "    mongodb:  $MONGO" || echo "    mongodb:  (skip)"
fi

step "Wait pg_isready"
for _ in $(seq 1 30); do
  docker exec "$PG" pg_isready -U postgres -d postgres >/dev/null 2>&1 && break
  sleep 1
done

step "Apply clear_and_seed.sql (UTF-8 via docker cp)"
REMOTE_SQL="/tmp/raceday_clear_and_seed.sql"
docker cp "$SQL_FILE" "$PG:$REMOTE_SQL"
docker exec -e PGCLIENTENCODING=UTF8 "$PG" \
  psql -U postgres -d postgres -v ON_ERROR_STOP=1 -f "$REMOTE_SQL"
docker exec "$PG" rm -f "$REMOTE_SQL" >/dev/null 2>&1 || true

if [[ -n "$MONGO" ]]; then
  step "Clear Mongo audit_entries"
  docker exec "$MONGO" mongosh --quiet --eval "db.getSiblingDB('raceday').audit_entries.deleteMany({})"
fi

step "Done"
echo ""
echo "Password: MatKhau@123 (email verified, no OTP)"
echo "  admin / admin@raceday.vn"
echo "  cuong.le / cuong.le@raceday.vn"
echo "  an.nguyen .. uyen.mai"
echo ""
