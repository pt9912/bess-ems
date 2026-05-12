# Plan RM-M5-06 Container-Orchestrierungstests

Status: abgeschlossen am 2026-05-12. Dieser Slice liefert das
Worker-plus-Sidecar-Compose-Gate fuer RM-M5.

## Ziel

CI muss nicht nur den process-internen TestSidecar sehen, sondern eine echte
Container-Topologie: `bess-ems` als Worker/API-Host spricht per gRPC-h2c mit
einem separaten `optimization-core`-Sidecar, faellt bei Sidecar-Ausfall auf
den lokalen Fallback zurueck und erholt sich nach Sidecar-Restart.

## Ergebnis

- `tests/support/BatteryEms.OptimizationCore.TestSidecar` ist ein
  standalone ASP.NET-Core/gRPC-TestSidecar-Executable. Es implementiert
  `Health`, `Version` und `Optimize`, emittiert korrelierbare
  `request_id`-Logs und trennt gRPC-h2c (`8081`) von HTTP-Health (`8082`).
- Das Dockerfile baut das Sidecar ueber das Target
  `optimization-core-test-sidecar`.
- `tests/optimization-core-compose/compose.yml` startet `bess-ems` mit
  `ScheduleSolver.Backend=optimization_core`,
  `OptimizationCoreRuntimeProfile=Development` und lokalem
  `or_tools`-Fallback gegen den separaten Sidecar-Container.
- `scripts/test-optimization-core-compose.sh` prueft:
  - `/health` des Worker/API-Hosts
  - erfolgreichen Sidecar-Optimierungslauf
  - Prometheus-Metrik `terminal_state="sidecar_committed"`
  - Sidecar-Stopp und lokalen Fallback mit
    `terminal_state="fallback_committed"` /
    `fallback_source="local_optimizer"`
  - Sidecar-Restart und erneuten erfolgreichen Optimierungslauf
  - Container-Logs mit `request_id=` und `run_id=`
- `make test-optimization-core-compose` baut Runtime- und Sidecar-Image,
  startet den Compose-Stack und ist in `make ci` verdrahtet.

## Nachweise

- `make test-optimization-core-compose`
