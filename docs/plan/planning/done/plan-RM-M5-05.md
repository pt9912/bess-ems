# Plan RM-M5-05 Erweiterte Metriken

Status: abgeschlossen am 2026-05-12. Dieser Slice schliesst die
M5-Observability-Luecke fuer Solverstatus, Sidecar-Health,
Fallback-Taxonomie, Terminalzustand und Command-Latenz.

## Ziel

Prometheus-Scrapes muessen erfolgreiche und fehlerhafte Optimierungslaeufe
so zeigen, dass Operatoren von einem M5-Sidecar-Fehler direkt auf
Solverstatus, Fallback-Quelle, Fallback-Grund, Terminalzustand und Laufzeit
schliessen koennen. Bestehende Control-Cycle-Metriken bleiben die Quelle fuer
Command-Latenz.

## Ergebnis

- `IOptimizationCoreMetrics` ist ein framework-freier Application-Port fuer
  optimization-core-spezifische Metriken; `NoOpOptimizationCoreMetrics`
  haelt API-/Testhosts ohne Prometheus-Abhaengigkeit lauffaehig.
- `PrometheusOptimizationCoreMetrics` exportiert:
  - `bess_optimization_core_runs_total`
  - `bess_optimization_core_run_duration_seconds`
  - `bess_optimization_core_terminal_states_total`
  - `bess_optimization_core_sidecar_health_status`
- Labels folgen der M5-Taxonomie: `asset_id`, `status`, `fallback_source`,
  `fallback_reason` und `terminal_state`. Persistente
  Idempotency-`terminal_reason`-Werte bleiben kebab-case; Prometheus-
  `fallback_reason` nutzt die underscore-Werte aus plan-RM-M5.
- `OptimizationCoreScheduleOptimizer` zeichnet Health-Probe-Zustaende,
  erfolgreiche Sidecar-Commits, Failed-No-Activation, lokale Fallback-Commits,
  Cancelled, Duplicate und Late-Response-Ignored auf.
- Bestehende `PrometheusControlCycleMetrics` decken Command-Latenz weiter ueber
  `bess_command_latency_seconds` ab; bestehende `PrometheusOptimizationRunMetrics`
  decken generischen Solverstatus und Solverlaufzeit ab.

## Nachweise

- `PrometheusOptimizationCoreMetricsTests` scrapen erfolgreiche und fehlerhafte
  Sidecar-Metrikpfade inklusive Fallback-/Terminal-Labels.
- `OptimizationCoreRoundtripTests.Optimize_success_produces_optimal_run_with_schedule`
  pinnt, dass der echte In-Process-Test-Sidecar-Pfad Metrics fuer
  `sidecar_result`, `none` und `sidecar_committed` schreibt.
- Gates: `make test`, `make test-hil-optimization-core`.
