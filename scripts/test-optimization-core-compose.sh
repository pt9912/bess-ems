#!/usr/bin/env sh
set -eu

DOCKER="${DOCKER:-docker}"
IMAGE_PREFIX="${IMAGE_PREFIX:-bess-ems}"
PROJECT_NAME="${COMPOSE_PROJECT_NAME:-bess-ems-rm-m5-06}"
COMPOSE_FILE="${COMPOSE_FILE:-tests/optimization-core-compose/compose.yml}"
COMPOSE="$DOCKER compose -p $PROJECT_NAME -f $COMPOSE_FILE"

cleanup() {
    status=$?
    if [ "$status" -ne 0 ]; then
        $COMPOSE logs --no-color bess-ems optimization-core >&2 || true
    fi
    $COMPOSE down -v --remove-orphans >/dev/null 2>&1 || true
    exit "$status"
}
trap cleanup EXIT INT TERM

export IMAGE_PREFIX

echo "[rm-m5-06] starting Worker + optimization-core compose stack"
$COMPOSE up -d --wait --wait-timeout 90

echo "[rm-m5-06] probing bess-ems /health"
$COMPOSE exec -T bess-ems curl --fail --silent --show-error http://localhost:8080/health >/dev/null

base_epoch="$($COMPOSE exec -T bess-ems date -u +%s | tr -d '\r')"

iso_at_offset() {
    epoch=$(( base_epoch + $1 ))
    $COMPOSE exec -T bess-ems date -u -d "@$epoch" +"%Y-%m-%dT%H:%M:%SZ" | tr -d '\r'
}

optimize() {
    horizon_start="$1"
    horizon_end="$2"
    $COMPOSE exec -T bess-ems sh -c \
        'curl --fail --silent --show-error \
          -H "Authorization: Bearer rm-m5-06-operator-token" \
          -H "Content-Type: application/json" \
          --data-binary @- \
          http://localhost:8080/markets/day-ahead/optimize' <<JSON
{
  "asset_id": "single-bess-1",
  "schedule_type": "day_ahead",
  "horizon_start": "$horizon_start",
  "horizon_end": "$horizon_end",
  "time_step_seconds": 3600,
  "prices_per_step": [20.0, 100.0],
  "price_unit": "EUR/MWh"
}
JSON
}

echo "[rm-m5-06] running sidecar-backed optimization"
sidecar_response="$(optimize "$(iso_at_offset -1800)" "$(iso_at_offset 5400)")"
printf '%s\n' "$sidecar_response" | grep '"status":"optimal"' >/dev/null
printf '%s\n' "$sidecar_response" | grep '"produced_schedule_version":1' >/dev/null

echo "[rm-m5-06] checking sidecar-committed metrics"
$COMPOSE exec -T bess-ems curl --fail --silent --show-error http://localhost:8080/metrics \
    | grep 'terminal_state="sidecar_committed"' >/dev/null

echo "[rm-m5-06] stopping sidecar and verifying local fallback"
$COMPOSE stop -t 2 optimization-core >/dev/null
fallback_response="$(optimize "$(iso_at_offset -1900)" "$(iso_at_offset 5300)")"
printf '%s\n' "$fallback_response" | grep '"produced_schedule_version":2' >/dev/null

$COMPOSE exec -T bess-ems curl --fail --silent --show-error http://localhost:8080/metrics \
    | grep 'terminal_state="fallback_committed"' >/dev/null
$COMPOSE exec -T bess-ems curl --fail --silent --show-error http://localhost:8080/metrics \
    | grep 'fallback_source="local_optimizer"' >/dev/null

echo "[rm-m5-06] restarting sidecar and verifying recovery"
$COMPOSE start optimization-core >/dev/null
deadline=$(( $(date +%s) + 60 ))
until $COMPOSE exec -T optimization-core curl --fail --silent --show-error http://localhost:8082/healthz >/dev/null 2>&1
do
    if [ "$(date +%s)" -ge "$deadline" ]; then
        echo "[rm-m5-06] optimization-core did not become healthy after restart" >&2
        exit 1
    fi
    sleep 1
done

recovered_response="$(optimize "$(iso_at_offset -2000)" "$(iso_at_offset 5200)")"
printf '%s\n' "$recovered_response" | grep '"status":"optimal"' >/dev/null
printf '%s\n' "$recovered_response" | grep '"produced_schedule_version":3' >/dev/null

echo "[rm-m5-06] checking correlatable request/run ids in logs"
logs="$($COMPOSE logs --no-color bess-ems optimization-core)"
printf '%s\n' "$logs" | grep 'request_id=' >/dev/null
printf '%s\n' "$logs" | grep 'run_id=' >/dev/null

echo "[rm-m5-06] compose sidecar gate green"
