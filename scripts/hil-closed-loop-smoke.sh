#!/usr/bin/env bash
# RM-M2 Carve-out Demo-01: Closed-Loop-Smoke gegen den HIL-Stack.
#
# Bringt deploy/compose.hil.yml hoch, postet einen Day-Ahead-
# Optimize-Request mit einer Preisreihe, die in Schritt 0 zum
# Discharge zwingt, und prüft drei Stationen der Pipeline:
#
#   1. POST /markets/day-ahead/optimize → HTTP 200 + Status=Optimal
#   2. bess-ems Control-Cycle emittiert mode=Discharge mit positivem
#      power_kw (Log-Grep)
#   3. bess-hil-simulator empfängt das Setpoint via Modbus
#      ([MODBUS] Command received mit non-zero P)
#
# Tear-down passiert immer (trap), damit ein Failure den Stack
# nicht stehen lässt. Skript verlangt einen Operator-Token via
# BESS_HIL_OPERATOR_TOKEN — der Compose-Header zwingt ohnehin dazu.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/deploy/compose.hil.yml"
COMPOSE_PROJECT="bess-hil-smoke"

: "${BESS_HIL_OPERATOR_TOKEN:?BESS_HIL_OPERATOR_TOKEN must be set (e.g. export BESS_HIL_OPERATOR_TOKEN=$(openssl rand -hex 16))}"

DOCKER="${DOCKER:-docker}"
COMPOSE=("$DOCKER" "compose" "-f" "$COMPOSE_FILE" "-p" "$COMPOSE_PROJECT")

cleanup() {
    echo "[smoke] tearing down stack…"
    "${COMPOSE[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "[smoke] bringing up HIL stack…"
"${COMPOSE[@]}" up -d --quiet-pull >/dev/null

echo "[smoke] waiting for /health…"
deadline=$(( $(date +%s) + 60 ))
while (( $(date +%s) < deadline )); do
    health="$(curl -sS -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/health || true)"
    if [[ "$health" == "200" ]]; then
        break
    fi
    sleep 1
done
if [[ "$health" != "200" ]]; then
    echo "[smoke] FAIL — bess-ems /health did not return 200 within 60 s (last: $health)" >&2
    exit 1
fi

echo "[smoke] POST /markets/day-ahead/optimize…"
# Start one minute in the past so the first 15-min window is
# already active when bess-ems' next control cycle ticks; without
# this the schedule sits in the future and the cycle keeps
# emitting Idle / no-active-commitment.
horizon_start="$(date -u -d '-1 min' +%Y-%m-%dT%H:%M:%SZ)"
horizon_end="$(date -u -d '+59 min' +%Y-%m-%dT%H:%M:%SZ)"
# Prices steeply drop after step 0 → LP discharges at max in step 0
# to capture the high spot, then stays put. Four 15-min steps.
payload=$(cat <<JSON
{
  "asset_id": "single-bess-1",
  "schedule_type": "day_ahead",
  "horizon_start": "$horizon_start",
  "horizon_end": "$horizon_end",
  "time_step_seconds": 900,
  "prices_per_step": [500.0, 50.0, 50.0, 50.0],
  "price_unit": "EUR/MWh"
}
JSON
)
http_code=$(curl -sS -o /tmp/hil-smoke-resp.json -w '%{http_code}' \
    -X POST http://127.0.0.1:8080/markets/day-ahead/optimize \
    -H "Authorization: Bearer $BESS_HIL_OPERATOR_TOKEN" \
    -H "Content-Type: application/json" \
    -d "$payload" || echo "000")
response=$(cat /tmp/hil-smoke-resp.json 2>/dev/null || echo "")
echo "[smoke]   response (HTTP $http_code): $response"
if [[ "$http_code" != "200" ]]; then
    echo "[smoke] FAIL — optimize returned HTTP $http_code" >&2
    exit 1
fi
# `|| true` absorbs grep's no-match exit so set -e / pipefail
# don't kill the script before we can produce a FAIL message.
status=$(echo "$response" | grep -oE '"[Ss]tatus":[[:space:]]*"[^"]+"' | head -1 \
    | grep -oE '"[^"]+"$' | tr -d '"' || true)
status_lc="$(echo "$status" | tr '[:upper:]' '[:lower:]')"
if [[ "$status_lc" != "optimal" && "$status_lc" != "feasible" ]]; then
    echo "[smoke] FAIL — optimize returned status='$status' (expected optimal/feasible)" >&2
    exit 1
fi
echo "[smoke]   OK — optimize status=$status"

echo "[smoke] waiting for Discharge command in bess-ems log…"
deadline=$(( $(date +%s) + 60 ))
discharge_seen=0
while (( $(date +%s) < deadline )); do
    if "${COMPOSE[@]}" logs bess-ems --tail 200 2>/dev/null \
        | grep -qE 'mode=Discharge.*power_kw=[1-9][0-9]*'; then
        discharge_seen=1
        break
    fi
    sleep 2
done
if (( discharge_seen == 0 )); then
    echo "[smoke] FAIL — bess-ems did not emit a non-zero Discharge command within 60 s" >&2
    "${COMPOSE[@]}" logs bess-ems --tail 30 >&2 || true
    exit 1
fi
echo "[smoke]   OK — bess-ems emitted Discharge command"

echo "[smoke] verifying HIL received the setpoint via Modbus…"
# The HIL simulator logs P in MW (not kW), so a 50 kW setpoint
# arrives as `P=0.05`. Match any non-zero magnitude: integer 1+,
# 0.something-non-zero, or .something-non-zero.
if ! "${COMPOSE[@]}" logs bess-hil-simulator --tail 200 2>/dev/null \
    | grep -qE '\[MODBUS\] Command received: P=([1-9][0-9]*|0?\.[0-9]*[1-9])'; then
    echo "[smoke] FAIL — bess-hil-simulator never logged a non-zero Modbus setpoint" >&2
    "${COMPOSE[@]}" logs bess-hil-simulator --tail 30 >&2 || true
    exit 1
fi
echo "[smoke]   OK — HIL received non-zero setpoint via Modbus"

echo "[smoke] PASS — full closed-loop optimize → dispatch → HIL roundtrip green"
