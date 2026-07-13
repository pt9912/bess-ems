#!/usr/bin/env bash
#
# ADR 0013 §5.3 — SUT smoke: prove the config-only path by running bess-ems
# (deploy/compose.sut.yml) against a SEPARATELY started field stack
# (deploy/compose.field.yml) coupled ONLY through the shared external Docker
# network `bess-sut`. This is the mechanical evidence behind
# docs/user/sut-field-endpoint.md (§4/§5, the authoritative recipe): the same
# image, pointed at an "external" broker purely via environment, leaves the
# safety fallback and runs control cycles.
#
# Green criterion: the good-case line — matched on its machine-readable JSON
# anchor "EventId":1701 ("Control cycle emitted command"), immune to message
# rewording — appears within SUT_SMOKE_TIMEOUT seconds, and NO additional
# safe-stop line ("EventId":1702) appears during the SUT_SMOKE_WARMUP watch
# window that starts at the good-case signal. Start-up safe-stops BEFORE the
# first good-case signal are expected (the loop runs before the first
# telemetry arrives) and tolerated by design.
#
# Scenario: the committed fixture
# simulators/bess-field-sim/testdata/scenarios/sut-smoke-cadence.json
# (constant 1s cadence, 300s) — diff-reviewable and loader-covered by
# TestAllFixturesLoad in `make simulator-test`.
#
# Environment:
#   SUT_SMOKE_TIMEOUT — seconds to wait for the good-case signal (default 90)
#   SUT_SMOKE_WARMUP  — seconds to watch for new safe-stops (default 20)
#   DOCKER            — docker binary (default docker)
set -euo pipefail

DOCKER="${DOCKER:-docker}"
TIMEOUT="${SUT_SMOKE_TIMEOUT:-90}"
WARMUP="${SUT_SMOKE_WARMUP:-20}"
NETWORK="bess-sut"
GOOD_ANCHOR='"EventId":1701'
SAFESTOP_ANCHOR='"EventId":1702'

# Pin the coupling inputs: a leftover BESS_FIELD_SCENARIO or
# BESS_SUT_BROKER_HOST/PORT export from a manual doc walkthrough would make
# this smoke silently test something other than the stand-in coupling.
export BESS_FIELD_SCENARIO="../simulators/bess-field-sim/testdata/scenarios/sut-smoke-cadence.json"
unset BESS_SUT_BROKER_HOST BESS_SUT_BROKER_PORT

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

field() { "${DOCKER}" compose -f deploy/compose.field.yml -p bess-sut-field "$@"; }
sut()   { "${DOCKER}" compose -f deploy/compose.sut.yml   -p bess-sut-ems   "$@"; }

created_network=0
cleanup() {
  sut down -v --remove-orphans >/dev/null 2>&1 || true
  field down -v --remove-orphans >/dev/null 2>&1 || true
  # Only remove the network if THIS run created it — never delete a
  # pre-existing network of the same name that something else owns.
  if (( created_network )); then
    "${DOCKER}" network rm "${NETWORK}" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

if "${DOCKER}" network inspect "${NETWORK}" >/dev/null 2>&1; then
  echo "[sut-smoke] WARNING: network '${NETWORK}' already exists — reusing it and leaving it in place afterwards"
else
  echo "[sut-smoke] creating shared external network '${NETWORK}'"
  "${DOCKER}" network create "${NETWORK}" >/dev/null
  created_network=1
fi

echo "[sut-smoke] starting the stand-in field stack (separate compose project)"
field up -d --wait --wait-timeout 60

echo "[sut-smoke] starting the SUT variant against it (config-only coupling)"
sut up -d --wait --wait-timeout 60

# Logs are always captured into a variable first: (a) grep -q on a live pipe
# can SIGPIPE `docker compose logs` under pipefail, (b) a failing log fetch
# must FAIL the smoke instead of counting as zero matches (no false green).
fetch_logs() {
  sut logs bess-ems
}

echo "[sut-smoke] waiting up to ${TIMEOUT}s for the good-case signal (${GOOD_ANCHOR})"
deadline=$(( SECONDS + TIMEOUT ))
until logs="$(fetch_logs)" && grep -qF "${GOOD_ANCHOR}" <<<"${logs}"; do
  if (( SECONDS >= deadline )); then
    echo "[sut-smoke] FAILED — no good-case signal within ${TIMEOUT}s; last log lines:" >&2
    tail -40 <<<"${logs:-<no logs readable>}" >&2
    exit 1
  fi
  sleep 2
done
echo "[sut-smoke] good-case signal seen; watching ${WARMUP}s for NEW safe-stops (${SAFESTOP_ANCHOR})"

safe_stop_count() {
  local logs
  logs="$(fetch_logs)" || return 1
  grep -cF "${SAFESTOP_ANCHOR}" <<<"${logs}" || true
}
before="$(safe_stop_count)"
sleep "${WARMUP}"
after="$(safe_stop_count)"
if (( after > before )); then
  echo "[sut-smoke] FAILED — ${after} safe-stop lines after the good-case signal (baseline ${before}); the loop fell back:" >&2
  fetch_logs | grep -F "${SAFESTOP_ANCHOR}" | tail -5 >&2 || true
  exit 1
fi

echo "[sut-smoke] OK — control loop runs against the external endpoint (config-only); no new safe-stop in the ${WARMUP}s watch window"
