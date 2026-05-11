# Plan RM-M5-02 — MPC-Kernel (State-Space, Kalman, Vorhersagehorizont)

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M5-02)
**Status:** Offen — wird in Sub-Slices RM-M5-02-A..D umgesetzt
**Bezug:**
[`plan-RM-M5.md`](plan-RM-M5.md) (Master-Plan, RM-M5-02-Zeile mit
DoD; §Sidecar-Status-Taxonomie + §Fallback-Matrix gelten unverändert
für MPC-Pfade; §Fallback-Plan-Gueltigkeit erweitert sich um den
MPC-State-Stempel),
[`../../adr/0005-optimization-core-sidecar-transport.md`](../../adr/0005-optimization-core-sidecar-transport.md)
(gRPC-Transport-Adoption; §7 Phase-4-Pivot-Trigger trägt den 1-ms-
Latenz-Bound, der dieses Slice direkt berührt),
[`../done/plan-RM-M5-01.md`](../done/plan-RM-M5-01.md) (Contract-
Slice; `OptimizeMpc`-RPC ist heute Vertrag-only — D-08 — und liefert
mit diesem Slice das Backend),
[`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
(M2-OptimizationRun-Modell, `IScheduleOptimizer`-Driven-Port; das
MPC-Backend lebt **neben**, nicht statt der LP-Linie — siehe D-01),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(LH-CTRL-005/006 — kurzzyklischer MPC-Regler).

---

## 1. Zweck

RM-M5-02 ist der **MPC-Backend-Slice** für den optimization-core-
Sidecar. RM-M5-01 hat den Wire-Vertrag plus die LP-Surface
(`IScheduleOptimizer`-Linie) geliefert; dieser Slice füllt den
`OptimizeMpc`-RPC mit einem produktionsnahen MPC-Kernel:

- **State-Space-Modell** für das Batterie-Asset (SOC-Dynamik mit
  Effizienz, Ramp-/Leistungs-/SOC-Bounds als Constraints).
- **Endlicher Vorhersagehorizont** mit konfigurierbarer Sample-Zeit
  und Solver-Optionen (Time-Limit, Gap, max-Iterationen).
- **Kalman-Filter-State-Estimator** zwischen Telemetrie und MPC-
  Eingang: Rauschen, fehlende Messwerte, unplausible Werte werden
  fail-closed gehandelt (Safe-Stop statt MPC-Step mit fauler State).
- **Constraints-Einhaltung** (SOC-, Leistungs-, Ramp-Bounds) ist
  Pflicht-Pin per Property-Test gegen das State-Space-Modell.
- **Reproduzierbarkeitsvertrag**: jeder MPC-Run trägt Seed, Solver-
  Optionen, Runtime-/Numerik-Versionen plus den State-Estimator-
  Stempel. Reproduzierbarkeit ist abhängig vom konfigurierten
  `DeterministicMode` (Strict/BestEffort/None — D-04 spezifiziert die
  Toleranz-Profile pro Modus). Im `Strict`-Modus produziert ein Replay
  bit-identische Trajektorien für int/string-Stempel und ist innerhalb
  einer 1e-9 relativen Toleranz pro Schedule-Point reproduzierbar; im
  `BestEffort`-Modus gilt 1e-6 relativ; `None` ist explizit
  Replay-untauglich (z. B. wenn der Operator OR-Tools-Multi-Thread-
  Default akzeptiert).
- **Driving-Port-Erweiterung**: neue Application-Schicht-Linie
  `IMpcDispatchOptimizer` (oder ein eingeschränkter `IScheduleOptimizer`-
  Erweiterungspfad — siehe D-01) ruft das MPC-Backend pro Control-
  Cycle auf statt pro Optimize-Request.
- **Sidecar-Backend-Implementierung** im optimization-core-Adapter:
  der `OptimizeMpc`-RPC (M5-01 Vertrag-only) bekommt eine echte
  Antwort-Pipeline; lokaler Fallback (M5-01 Korrektur-Pass) erbt
  sich auf den MPC-Pfad weiter.

**Bewusster Scope-Cut:** RM-M5-02 macht **nur** das State-Space-LTI-
Modell plus Kalman-Filter plus Constraints-Einhaltung — kein
Multi-Asset, kein piecewise-Modell, kein Stochastic-MPC, keine
adaptive Modell-Identifikation. Diese Erweiterungen sind eigene
Folge-Slices (RM-M5-02-FUP-*); der Trigger für jede einzelne ist im
§9 Folgearbeiten-Block dokumentiert.

---

## 2. Aktivierungsbedingungen

- **RM-M5-01 ✅** (`done/plan-RM-M5-01.md` am 2026-05-11
  abgeschlossen inkl. Sub-Slice-C-Korrektur-Pass; `OptimizeMpc`-RPC-
  Vertrag steht, Adapter-Wire-Surface fährt, Fallback-Matrix-
  Integration ist gemerged).
- **M2-OptimizationRun-Modell stabil** (`done/plan-RM-M2-optimization.md`;
  Reproduzibilität-Stempel-Felder im `OptimizationRun` sind die
  Basis für die MPC-Stempel-Erweiterung — D-04).
- **ADR 0006 ggf.** (`docs/plan/adr/0006-mpc-kernel-modeling-and-
  solver.md` — Modell-Form-Wahl LTI vs. piecewise vs. nichtlinear +
  Solver-Wahl LP/QP/SOCP). Dieser Plan **kann** ohne ADR 0006
  starten, wenn Sub-Slice A bewusst LTI fixiert und die Modell-
  Erweiterung als ADR-Trigger im Folgearbeiten-Block dokumentiert
  wird. Reviewer entscheidet beim Plan-Review-Pass.

**Optional, nicht-zündend:**

- RM-M5-03 (Native-Filterung), RM-M5-04 (Replay-Plattform), RM-M5-05
  (Erweiterte Metriken), RM-M5-06 (Container-Orchestrierungs-Gate)
  sind eigenständige Slices und blocken RM-M5-02-A..D nicht. RM-M5-04
  reused den Reproduzierbarkeits-Vertrag aus D-04 dieses Slices —
  ein bewusster Sequenz-Hinweis, kein Block.

---

## 3. Scope

**In Scope (RM-M5-02-A..D zusammen):**

- **`BatteryEms.Application.Mpc`-Namespace** (neu):
  - `MpcModel`-Domain-Typ (State-Space-Matrizen `A`, `B`, `C`,
    `D` plus Constraints-Hülle — SOC-Bounds, Leistungs-Bounds,
    Ramp-Bound). Initial LTI; nichtlinear/piecewise per
    Carve-out.
  - `MpcState`-Domain-Typ (aktueller geschätzter State plus
    Covariance-Matrix für das Kalman-Update).
  - `MpcTrajectory`-Output-Domain-Typ (endlicher Vektor von
    Sollwerten über den Vorhersagehorizont).
  - `IMpcDispatchOptimizer`-Driving-Port (`Task<MpcDispatchResult>
    NextStepAsync(...)`) — Pre-Step-State-Estimation + MPC-Solve +
    Post-Step-Trajectory-Liefern.
  - `IMpcStateEstimator`-Driven-Port (Kalman-Filter-Variante
    unter D-03; Default LinearKf — Robustheit-Pfad ist Sub-Slice C).
  - `IMpcModelSolver`-Driven-Port (QP-Solver-Abstraktion;
    konkrete Implementierung wahlweise lokal oder via Sidecar —
    siehe D-02).
- **`MpcOptions`-Konfigurations-Record** mit Sample-Zeit, Horizon-
  Länge, Solver-Optionen (Time-Limit, Gap, max-Iters), Kalman-
  Parameter (Process-/Measurement-Noise-Covariance), Constraint-
  Toleranzen plus `DeterministicMode`-Slot (`Strict`/`BestEffort`/
  `None`; Default `Strict`). `Strict` aktiviert pro Solver-Adapter die
  vollständige Threading-/Numerik-Determinism-Disziplin (siehe D-04
  Reproduzierbarkeits-Vertrag); `BestEffort` lässt
  Multi-Thread-Solver-Default zu mit dokumentierter Drift-Toleranz;
  `None` ist Replay-untauglich und wird im `MpcRun`-Stempel als
  solcher markiert, sodass RM-M5-04-Replay-Lese fail-closed mit
  `mpc-non-deterministic-run` ablehnt.
- **State-Estimator-Implementierung** (`DefaultLinearKalmanFilter`):
  Standard-Kalman-Filter-Pipeline (Predict + Update). Fail-closed-
  Pfade für `missing-measurement`, `non-finite-state`, `covariance-
  divergence`. Validator-Hook in `MpcDispatchResult` damit der
  Control-Cycle den State-Status sieht (nicht nur den Trajektorie-
  Output).
- **MPC-Solver-Backend** (D-02 entscheidet die konkrete Linie):
  - **Variante (a) Local-First**: lokaler QP-Solver (OR-Tools /
    OSQP / HiGHS) als Default; Sidecar nur wenn explizit
    konfiguriert (`BessHostOptions.MpcBackend = "optimization_core"`).
    Spiegelt die M5-01-Linie wo Sidecar opt-in war.
  - **Variante (b) Sidecar-First**: `optimization-core`-Sidecar
    via `OptimizeMpc`-RPC ist Default; lokaler Fallback ist die
    bekannte Linie aus M5-01-Korrektur-Pass.
  - **Variante (c) Bi-Modal**: beide Backends gleichberechtigt
    registriert, Operator wählt per `BessHostOptions.MpcBackend`-
    Slot.
  Pre-Code-Entscheidung — Reviewer + ADR 0006 entscheiden im
  Plan-Review-Pass.
- **`OptimizeMpc`-RPC-Backend** in `BatteryEms.Adapters.
  OptimizationCore.OptimizationCoreMpcOptimizer` (neu, sitzt
  neben dem bestehenden `OptimizationCoreScheduleOptimizer`).
  Wire-Übersetzung: `MpcRequest` → `OptimizeMpcRequest`-Proto,
  Stream-Reading, Result-Decoding zu `MpcTrajectory`.
- **MPC-spezifische Request-ID + Retention** (siehe D-09): die
  Master-LP-Linie aus M5-01 verwendet eine deterministische
  `request_id` aus `(asset_id, schedule_type, horizon_start,
  horizon_end, time_step, base_schedule_version, market_bid_area)` —
  passt aber nicht für den MPC-Sub-Sekunden-Tick. Pro Tick
  produziert MPC eine eigene `mpc_request_id` aus
  `(asset_id, control_cycle_tick_utc_ms, mpc_model_version_hash,
  state_estimator_variant)`; `control_cycle_tick_utc_ms` ist die
  zum Tick-Start gerundete UTC-Millisekunde (Truncate, nicht Round).
  Plan-Validator-Linie aus M5-01 bleibt orthogonal — Idempotency
  detektiert Retry/Duplicate (selber Tick zweimal abgefeuert),
  Validator detektiert State-Drift. Persistenz lebt in einer
  **eigenen Tabelle** `mpc_runs` (nicht im LP-`optimization_idempotency`-
  Store), weil MPC-Schreib-Rate (4 Hz pro Asset) eine andere
  Retention-/Compaction-Linie verlangt — Migration
  `0004_mpc_runs.sql` deklariert eine TTL-/Top-N-Compaction-Policy
  (D-09).
- **Terminal-Reason `mpc-committed`** analog zu
  `sidecar-committed` aus M5-01; Late-Response-Ignored-Pfad erbt
  sich vom Idempotency-Pattern aus M5-01-C.
- **TestSidecar-Erweiterung** für MPC: neue Stub-Linie
  `OptimalMpcStub` + `ScriptableMpcOutcomeStub` in
  `tests/integration/BatteryEms.OptimizationCore.IntegrationTests/`
  analog zur LP-Linie aus M5-01-B. Wire-Roundtrip-Pins + Negativ-
  Pfade (Solver-Time-Limit, Infeasible, Stream-Crash).
- **Constraint-Property-Pins** im Unit-Test-Layer
  (`BatteryEms.Application.Tests/Mpc/`):
  - SOC bleibt in `[MinSocPercent, MaxSocPercent]` über den
    gesamten Horizon (Property-Test mit randomisierten gültigen
    Inputs).
  - Power-Output bleibt in `[-MaxDischargePowerKw, MaxChargePowerKw]`.
  - Ramp-Constraint bleibt unter `MaxRampKwPerSecond * TimeStep`.
  - Constraint-Violation → Failed-Run mit kebab-case-Reason
    (`mpc-constraint-violated-soc-lower`, `mpc-constraint-
    violated-power-upper`, `mpc-constraint-violated-ramp`).
- **Kalman-Filter-Robustheits-Pins** (`DefaultLinearKalmanFilterTests`):
  - Missing-Measurement (NaN/Infinity im Eingang) → State-Update
    skip'd, Covariance grows; nach N skip'd-Steps → Failed mit
    `mpc-state-stale-too-long`.
  - Unplausibler State (SOC=200%, T=-273°C) → Validator-Reject
    pre-Solve; `mpc-state-non-physical`.
  - Covariance-Divergence (Determinant überschreitet Threshold)
    → Failed mit `mpc-covariance-diverged`.
- **Reproduzierbarkeitsvertrag** (D-04):
  - `MpcRun`-Domain-Typ (analog zu `OptimizationRun`) trägt
    Seed, Solver-Optionen-Hash, Numerik-Versionen (`Math.NET`,
    `OR-Tools`, `BLAS`-Backend), Kalman-Initial-State-Stamp,
    State-Estimator-Variante-Name.
  - Persistenz-Tabelle `mpc_runs` (Migration `0004_mpc_runs.sql`)
    plus Dapper-Repository.
  - Replay-Hook: `MpcDispatchResult` exposed
    `IReadOnlyDictionary<string, string>` mit allen Stempel-Werten
    damit RM-M5-04-Replay-Plattform sie ohne Code-Änderung lesen
    kann.
- **Plan-Gültigkeits-Check-Erweiterung**: `FallbackPlanValidator`
  bekommt eine MPC-Stempel-Achse (State-Estimator-Variante +
  Kalman-Covariance-Footprint müssen matchen) damit ein gespeicherter
  MPC-Plan nicht mit einem Plan aus einem anderen Estimator-Stand
  verwechselt wird.
- **Quality-Doku** §2.2.3 + neue §2.5 (`make test-mpc-property`)
  für die randomisierten Property-Pins. CI-Gate
  `make test-hil-optimization-core` deckt die Wire-Linie weiter.
- **Master-Plan-Cleanup**: bei Closure flippt RM-M5-02-Zeile in
  `plan-RM-M5.md` auf ✅ mit D-05-Replacement-Text (vorab gepinnt
  in §5).

**Out of Scope (separate Slices / Folgearbeiten):**

- **Multi-Asset-MPC** (gemeinsame Optimierung über mehrere Batterien
  / Netz-Knotenpunkte) → RM-M5-02-FUP-multi-asset. Trigger: erster
  Operator-Workflow mit 2+ Batterien im selben Netz-Knoten.
- **Piecewise-/Nichtlineare-Modell-Erweiterung** (z. B. SOC-
  abhängige Effizienz-Kurve, Temperatur-abhängige Power-Bounds) →
  RM-M5-02-FUP-nonlinear. Trigger: erste Replay-Differenz, die
  bei LTI-Annahme nicht erklärbar ist.
- **Stochastic-MPC / Robust-MPC** (Unsicherheits-Sets statt Mean-
  State) → RM-M5-02-FUP-stochastic. Trigger: erste Operator-
  Anforderung nach Forecast-Unsicherheits-Handling jenseits der
  Kalman-Covariance.
- **Adaptive Modell-Identifikation** (Online-Re-Fitting der
  State-Space-Matrizen aus laufender Telemetrie) → eigene
  ADR-Linie wegen Compliance/Audit-Trail-Implikationen.
- **Native-Embedding** (MPC-Kernel im C-Native-Core via P/Invoke)
  → RM-M5-03 plus ADR 0005 §7 Phase-4-Pivot-Linie. Trigger:
  Latenz-Bound < 1 ms pro MPC-Step verlässlich messbar.
- **Multi-Sample-Time-MPC** (verschiedene Sample-Zeiten pro Asset)
  → eigene Folge-Slice; initial nur eine globale `MpcOptions.
  SampleTime`.
- **Time-Varying-Reference-Tracking** (Trajektorien-Following
  jenseits der Marktpreis-Linie) → RM-M5-FUP-trajectory-tracking.
  Trigger: erste TSO-/Operator-Anforderung nach Reference-Profil-
  Following.

---

## 4. Sub-Slices

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ⬜ | RM-M5-02-A | State-Space-Modell + Application-Schicht-Ports + Constraint-Property-Pins — **~700-1000 LOC** | Neuer `BatteryEms.Application.Mpc`-Namespace mit `MpcModel`, `MpcState`, `MpcTrajectory`, `MpcOptions`-Records. `IMpcDispatchOptimizer`-Driving-Port + `IMpcStateEstimator`-Driven-Port + `IMpcModelSolver`-Driven-Port. `DefaultMpcDispatchOrchestrator` als wireing-Klasse, die `IMpcStateEstimator.PredictUpdateAsync` + `IMpcModelSolver.SolveAsync` orchestriert. **In diesem Sub-Slice kein konkreter Solver-Adapter** — `IMpcModelSolver` wird als `NotImplementedException`-Stub registriert; das hält die Schicht-Wartbarkeit beim Cut sauber. Constraint-Property-Pins (5+): SOC-in-Bounds, Power-in-Bounds, Ramp-in-Bounds, Constraint-Violation-Reason-Pinning, Empty-Trajectory-Reject. State-Estimator-Stub `IdentityStateEstimator` (`State_new = State_old`, kein Filtering) für die Property-Pins. Tests in `BatteryEms.Application.Tests/Mpc/`. |
| ⬜ | RM-M5-02-B | QP-Solver-Backend + erster Roundtrip-Pin — **~800-1200 LOC** | Konkreter `IMpcModelSolver`-Adapter gemäß D-02-Entscheidung. Bei Local-First (a): neuer `BatteryEms.Adapters.Optimization.Mpc.OrToolsMpcSolver` oder ein dedizierter `OsqpMpcSolver` (Solver-Wahl steckt in der ADR-Entscheidung). Bei Sidecar-First (b): `OptimizeMpc`-RPC im optimization-core-Adapter wird mit Body gefüllt; `OptimizationCoreMpcOptimizer` lebt **neben** dem bestehenden `OptimizationCoreScheduleOptimizer` und teilt sich Channel + Idempotency-Store. TestSidecar-Erweiterung mit `OptimalMpcStub` (Echo: liefert konstante SOC-Trajektorie) + `ScriptableMpcOutcomeStub` (Per-Test-Outcome-Queue, deckt Solver-Time-Limit/Infeasible/Stream-Crash). 8+ Roundtrip-Pins in neuer `OptimizationCoreMpcRoundtripTests` + `OptimizationCoreMpcNegativeTests`. Constraint-Property-Pins aus A werden gegen den echten Solver erneut gefahren — die Property-Tests laufen jetzt nicht nur gegen den Stub sondern auch gegen die Production-Linie. |
| ⬜ | RM-M5-02-C | Kalman-Filter + Robustheits-Pfade + Fallback-Erweiterung — **~700-900 LOC** | `DefaultLinearKalmanFilter`-Implementierung in `BatteryEms.Application.Mpc.Estimators`: Predict-Step (`x_pred = A * x_old + B * u`, `P_pred = A * P_old * A^T + Q`), Update-Step (`K = P_pred * C^T * (C * P_pred * C^T + R)^{-1}`, `x_new = x_pred + K * (y - C * x_pred)`, `P_new = (I - K * C) * P_pred`). Robustheits-Pfade: Missing-Measurement-Skip (counter persistent, nach `MaxConsecutiveMissingMeasurements` → `mpc-state-stale-too-long`), Unplausible-Werte-Validator (SOC im physischen Bereich, Temperatur in Asset-Bounds; sonst `mpc-state-non-physical`), Covariance-Divergence-Check (Determinant > threshold → `mpc-covariance-diverged`). Fallback-Erweiterung: `OptimizationCoreMpcOptimizer` erbt die `TryRunFallbackAsync`-Linie aus M5-01-Korrektur-Pass für den Sidecar-Failure-Pfad; lokaler Fallback bei MPC ist ein `IFallbackMpcOptimizer`-Driven-Port (neuer Marker, analog zu `IFallbackScheduleOptimizer`). 10+ Pins (Kalman-Convergence, Missing-Measurement-Recovery, Unplausible-State-Reject, Covariance-Divergence-Reject, Fallback-Wire-Integration). |
| ⬜ | RM-M5-02-D | Reproduzierbarkeitsvertrag + Replay-Hooks + Worker-Wiring + Retention + Quality-Doku + Master-Plan-Closure — **~700-900 LOC** | `MpcRun`-Domain-Typ + Migration `0004_mpc_runs.sql` mit D-09-Schema (incl. `mpc_request_id` PK, `control_cycle_tick_utc_ms`, `estimator_p0_hash`, `random_seed`, `deterministic_mode`, alle drei D-04-Stempel-Achsen). Dapper-Repository `DapperMpcRunRepository` mit Restart-Replay-Pin + Retention-Compaction-Pin (D-09: Top-N + MaxAge). `MpcDispatchResult` exposed `IReadOnlyDictionary<string, string>`-Stempel für RM-M5-04-Replay. `FallbackPlanValidator` erweitert um MPC-Stempel-Achse (State-Estimator-Variante + Canonical-`P_0`-Hash vs. Kontext; Frobenius-Norm bleibt operator-sichtbar aber nicht identitätsbildend). **Cross-Run-Determinism-Pin** (D-04 Pflicht): zweimal selber MPC-Step → identische Stempel-Hashes + Schedule-Points innerhalb 1e-9 relativ. **Worker-Control-Cycle-Wiring-Pin** in `BatteryEms.Worker.Tests`: zählender Stub-`IMpcDispatchOptimizer` registriert, `ControlCycleHostedService` läuft ≥3 Ticks, Stub-CallCount ≥3 (D-01-Wiring-Beweis). 12+ Persistence-Pins (Run-Roundtrip, alle drei Stempel-Achsen vollständig, Restart-Replay, CAS-Race, Late-Response-Ignored, Retention-Compaction-100k). Quality-Doku §2.2.3-Erweiterung + neue §2.5 (`make test-mpc-property` Property-Gate). F-Folgearbeiten (§9) in `note-RM-M5-followups.md` ergänzen. Master-Plan-Zeile RM-M5-02 flippt auf ✅ mit D-05-Replacement-Text. Slice-Plan wird nach `done/plan-RM-M5-02.md` verschoben. |

---

## 5. Design-Entscheidungen

**D-01 MPC-Backend sitzt neben — nicht statt — der LP-Linie aus M2.**
`IScheduleOptimizer` (LP-Optimierer aus M2) und `IMpcDispatchOptimizer`
(neuer MPC-Port) sind zwei orthogonale Driving-Ports. Der Worker-
Control-Cycle ruft den MPC pro Tick (Sub-Sekunden-Linie); der LP-
Optimierer läuft pro Schedule-Reopt (Minuten-/Stunden-Linie).

Begründung gegen Alternative (a) „MPC ersetzt den LP-Schedule":
Schedule-Persistenz + Markt-Bid-Linie ist M2-Hard-Constraint;
MPC arbeitet auf dem persistierten Schedule als Sollwert-Trajektorie
und tracked ihn pro Control-Cycle. Begründung gegen (b) „MPC als
Sub-Modus von `IScheduleOptimizer`": die Schnittstellen-Verbreiterung
zwingt LP-Callers, MPC-spezifische State-/Estimator-Felder zu
ignorieren — bricht das M2-Optimization-Modell.

**D-02 Solver-Backend-Wahl (offen, ADR-Trigger).**
Drei Varianten — Reviewer + ggf. ADR 0006 entscheiden vor RM-M5-02-B:

- **(a) Local-First**: QP-Solver lebt in-process (OR-Tools-QP /
  OSQP / HiGHS). Sidecar opt-in. Spiegelt M5-01-Linie und hält
  die Default-Topologie ohne externe Abhängigkeit.
- **(b) Sidecar-First**: `OptimizeMpc`-RPC ist Default. Cross-
  Language-Optimierungs-Backend (Python/Rust/C++) wird damit der
  Default-Pfad; In-Process-Fallback nur bei `optimization_core`-
  Backend nicht konfiguriert. Konsistenter mit ADR 0005 Vision.
- **(c) Bi-Modal**: beide Backends gleichberechtigt; Operator
  wählt per `BessHostOptions.MpcBackend`-Slot. Maximale Flexibilität,
  höchster Test-/Doku-Aufwand.

Default-Vorschlag wenn kein ADR 0006: **(c) Bi-Modal mit
Local-First-Default**. Begründung: ADR 0005 §7 Phase-4-Pivot-Linie
deutet auf einen In-Process-Pivot bei harten Latenz-Bounds — eine
Bi-Modal-Struktur macht den späteren Pivot reibungslos. Sub-Slice-B
implementiert Local-First konkret; Sidecar-Pfad erbt sich von M5-01.

**D-03 Kalman-Variante: Linear-KF als Default.**
LTI-Modell-Hülle aus Sub-Slice A passt zu einem Standard-Linear-KF.
Extended-/Unscented-KF (EKF/UKF) sind Folge-Linien sobald das Modell
nichtlinear wird (siehe Out-of-Scope-Block). Begründung: Standard-
KF-Code ist ~150-300 LOC + analytische Test-Linien; EKF/UKF
verdoppeln das mit Jacobian-Code + numerischer Stabilitäts-
Härtungs-Aufwand. Bei späterem nichtlinearem Modell wird die
Estimator-Variante als Stempel-Feld im `MpcRun` persistiert (D-04)
damit Replay-Vergleiche valide bleiben.

**D-04 Reproduzierbarkeits-Vertrag ist persistiert + im Result-
Stempel.**

Reproduzierbarkeit ist **bedingt** durch den `DeterministicMode`-
Slot (`MpcOptions.DeterministicMode`); die DoD-Klausel
„bit-identisch" gilt nicht naïv über alle Solver-/CPU-/BLAS-
Kombinationen, sondern unter einem expliziten Solver-/Threading-
Disziplin-Vertrag:

- **`Strict`-Modus** (Default): Solver-Adapter setzt Single-Thread
  (`solver.SetNumThreads(1)` oder Solver-spezifisches Äquivalent —
  OR-Tools-QP `SolverOptions.SetSolverSpecificParametersAsString`,
  OSQP `solver.warm_start=false` + `solver.scaling=0`), aktiviert
  Solver-Determinism-Flags wo verfügbar, persistiert den verwendeten
  Random-Seed (Solver-eigener Seed plus optionaler MPC-Seed für
  Tie-Breaking). Toleranz: bit-identisch für int/string-Stempel,
  ±1e-9 relativ pro Float-Schedule-Point (akkomodiert die
  unvermeidliche FP-Reihenfolge-Drift zwischen BLAS-Backends; ist
  empirisch im Plan-Review-Pass zu kalibrieren — Reviewer wählt
  zwischen 1e-9 und 1e-12 nach Solver-Wahl).
- **`BestEffort`-Modus**: Solver fährt mit Multi-Thread-Default;
  Toleranz: 1e-6 relativ pro Float-Schedule-Point. Geeignet für
  Production-Topologien wo Latenz wichtiger als Replay-Determinism
  ist. `MpcRun.deterministic_mode` markiert den Modus für RM-M5-04-
  Replay-Tools.
- **`None`-Modus**: explizit Replay-untauglich. `MpcRun` wird mit
  `deterministic_mode=none` gestempelt; RM-M5-04-Replay-Lese-Pfad
  rejected solche Runs fail-closed mit `mpc-non-deterministic-run`.

Drei orthogonale Stempel-Achsen pro `MpcRun`:

1. **Numerik-Stempel**: `Math.NET`-Version + `OR-Tools`/`OSQP`-
   Version + BLAS-Backend-Name (`OpenBLAS` / `MKL` / `netlib`) +
   CPU-Architektur-Tag (`x86_64-avx2` / `arm64-neon` — relevant
   weil FP-Ordering pro SIMD-Variante differiert) + .NET-Runtime-
   Version. Persistiert als JSON-Map. Replay-Match-Toleranz:
   exakter String-Match — andere Numerik-Versionen ⇒
   Plan-Gültigkeits-Check fail-closed mit
   `mpc-numerik-version-mismatch`.
2. **Solver-Konfigurations-Hash**: SHA-256 über die canonical-form-
   Serialisierung von (`MpcOptions` plus solver-spezifische
   Threading-/Determinism-Flags plus persistierter Random-Seed).
   Replay-Match-Toleranz: exakter Hash.
3. **State-Estimator-Stempel**: Variante-Name (`linear-kf` /
   `extended-kf` / `unscented-kf`) + **canonical-form-Hash der
   `P_0`-Matrix**: SHA-256 über das row-major-Bytes-Layout der
   `P_0`-Matrix nach Truncation auf 1e-12 Float-Präzision (Drift
   unter Truncate ist numerisches Rauschen, oberhalb echte Identitäts-
   Differenz). Frobenius-Norm wird zusätzlich als operator-sichtbare
   Plausibilitäts-Anzeige persistiert (gerundet auf 6 Nachkommastellen),
   ist aber **nicht** Identitätskriterium — die Norm ist nicht
   injektiv und würde sonst zwei unterschiedliche `P_0`-Matrizen mit
   gleicher Frobenius-Norm als identisch zulassen. Replay-Match-
   Toleranz: exakter Variante-Name + exakter Canonical-Hash.

`MpcDispatchResult` exposed alle drei Achsen als
`IReadOnlyDictionary<string, string>` damit RM-M5-04-Replay-Plattform
sie ohne Code-Änderung lesen kann. Begründung: Sub-Slice-D
materialisiert den Vertrag, RM-M5-04 sammelt ihn nur ein — Replay
darf keine MPC-Adapter-Kenntnis brauchen.

**Pflicht-Pin in Sub-Slice D**: Ein Cross-Run-Determinism-Pin fährt
zweimal denselben MPC-Step (selbe Inputs + selber Random-Seed + selber
DeterministicMode=Strict) und vergleicht beide `MpcRun`-Stempel-
Hashes auf Identität plus Schedule-Points auf 1e-9 relativ. Bei
Drift ⇒ Pin failed; der Operator muss den Solver-Adapter prüfen
bevor RM-M5-02 grün gehen darf. Sub-Slice-D-DoD verlangt
**zusätzlich** einen Cross-Platform-Smoke-Test (gleiche Inputs auf
zwei verschiedenen CPU-Architekturen-Containern) als Carve-out wenn
ein Multi-Arch-Deployment getriggert wird.

**D-05 Master-Plan-Wortlaut bei Closure (vorab gepinnt).**
Bei Closure wird RM-M5-02-Zeile in `plan-RM-M5.md` umformuliert.
Verbindlicher Replacement-Text:

> Slice-Plan: [`done/plan-RM-M5-02.md`](../done/plan-RM-M5-02.md).
> MPC-Kernel über `IMpcDispatchOptimizer`-Driving-Port (D-01 — neben
> der M2-`IScheduleOptimizer`-LP-Linie; `ControlCycleHostedService`
> ruft `NextStepAsync` pro Tick auf, verifiziert per Worker-Wiring-
> Integration-Pin). `IMpcStateEstimator`-Driven-Port mit
> `DefaultLinearKalmanFilter` als Default-Estimator (D-03);
> `IMpcModelSolver`-Driven-Port verdrahtet gemäß im Plan-Review-Pass
> gewählter D-02-Variante (Local-First / Sidecar-First / Bi-Modal).
> Constraints-Einhaltung (SOC-, Power-, Ramp-Bounds) per Property-
> Pins gegen das State-Space-Modell. Robustheits-Pfade: Missing-
> Measurement-Skip, Unplausible-State-Reject, Covariance-Divergence-
> Reject. **Reproduzierbarkeits-Vertrag mit `DeterministicMode`-
> Slot** (D-04 — Strict/BestEffort/None mit klar dokumentierten
> Toleranz-Profilen; canonical-form-SHA-256-Hash der `P_0`-Matrix
> als Identitätskriterium, Frobenius-Norm nur als operator-sichtbares
> Display). `MpcRun`-Domain-Typ + Migration `0004_mpc_runs.sql` +
> Dapper-Repository persistieren Seed/Solver-Konfig-Hash/Numerik-
> Versionen/Estimator-Canonical-Hash für RM-M5-04-Replay (D-04 +
> D-09); MPC-Idempotency-Schlüssel als deterministischer SHA-256
> über `(asset_id, control_cycle_tick_utc_ms, mpc_model_version,
> state_estimator_variant)` plus Top-N + MaxAge-Retention-Policy
> für den 4-Hz-Schreib-Pfad (D-09). `FallbackPlanValidator`
> erweitert um MPC-Stempel-Achse. NN Pins gesamt (Pin-Count bei
> Closure mit real-gelieferter Zahl ersetzt — Lehre aus M5-01-D
> Pin-Count-Drift) plus neue `make test-mpc-property` Mandatory in
> `make gates` und `make ci` und Cross-Run-Determinism-Pin als
> Pflicht-Gate. RM-M5-03 (Native-Embed-Pivot), RM-M5-FUP-multi-
> asset, RM-M5-FUP-nonlinear, RM-M5-FUP-stochastic siehe
> `note-RM-M5-followups.md`.

Pin-Count `NN` wird bei Closure mit der real-gelieferten Zahl ersetzt;
das hatte M5-01 als Failure-Mode (vorab-gepinnte 17 vs. real 25).

**D-06 Test-Layout erbt von M5-01 D-07.**
Property-Pins in `BatteryEms.Application.Tests/Mpc/`. Roundtrip-/
Negativ-Pins in `tests/integration/BatteryEms.OptimizationCore.
IntegrationTests/` (selbe Embedded-TestSidecar-Fixture mit
erweiterten Stubs). Kalman-/Robustheits-Pins in
`BatteryEms.Application.Tests/Mpc/Estimators/`. Persistence-Pins
in `BatteryEms.Persistence.IntegrationTests/`. Property-Test-
Framework: **FsCheck.Xunit** oder **CsCheck** — Entscheidung beim
ersten Pin-Commit, bewusst nicht vorab fixiert weil beide
äquivalent gute Coverage liefern.

**D-07 Constraint-Encoding: hard im QP, kein Soft-Penalty.**
SOC-, Power-, Ramp-Bounds sind hard QP-Constraints. Verletzung →
QP-Infeasible → MPC-Failed-Run mit kebab-case-Reason aus der
Constraint-Taxonomie. Soft-Constraints (z. B. SOC-Target-Penalty
in der Objective) bleiben separater Slot — nur die Asset-physischen
Limits sind hard. Begründung: ein MPC-Step der einen physischen
Limit verletzen *könnte* aber im Cost-Tradeoff durchläuft ist
Safety-relevant; hard ist die einzige verteidigbare Default.

**D-08 State-Estimator vs. Telemetrie-Drift: zwei orthogonale Linien.**
Plan-`FallbackPlanValidator` aus M5-01 prüft *gespeicherte Pläne*
gegen *aktuelle Telemetrie*; MPC-State-Estimator-Robustheits-Checks
(C-Sub-Slice) prüfen *aktuelle Telemetrie* gegen *physische Bounds*
bevor sie ins Kalman-Update fließt. Beide kennen unplausible Werte,
adressieren aber verschiedene Risiken. Sub-Slice C erweitert den
`FallbackPlanValidator` nicht — er bekommt nur die MPC-Stempel-
Achse aus D-04. Begründung: Layer-Trennung. Wenn Estimator-Check
und Validator-Check beide ausschlagen, ist der Operator-Reason
unterschiedlich (`mpc-state-non-physical` vs.
`fallback_telemetry_drift`) — beide Reasons bleiben separat
audit-fähig.

**D-09 MPC-Idempotency-Schlüssel und Retention für den Sub-Sekunden-
Tick.**

MPC läuft mit typischer Control-Cycle-Periode 250 ms (4 Hz) pro
Asset — über einen Tag sind das 345 600 Runs pro Asset. Die M5-01-
`optimization_idempotency`-Tabelle ist auf LP-Tag-Skala
dimensioniert (Schedule-Reopt pro Stunde / pro Asset) und passt
deshalb **nicht** für MPC. Drei orthogonale Entscheidungen:

1. **Eigene Tabelle `mpc_runs`** (nicht die LP-`optimization_idempotency`-
   Tabelle ko-nutzen). Spalten:
   `mpc_request_id TEXT PK, asset_id TEXT NOT NULL,
   control_cycle_tick_utc_ms BIGINT NOT NULL,
   mpc_model_version TEXT NOT NULL,
   state_estimator_variant TEXT NOT NULL,
   solver_config_hash TEXT NOT NULL,
   numerik_stamp_json TEXT NOT NULL,
   estimator_p0_hash TEXT NOT NULL,
   estimator_p0_frobenius_display DOUBLE PRECISION NOT NULL,
   random_seed BIGINT NOT NULL,
   deterministic_mode TEXT NOT NULL,
   terminal_state TEXT NOT NULL,
   terminal_reason TEXT NOT NULL,
   produced_trajectory_json TEXT,
   solver_runtime_ms DOUBLE PRECISION NOT NULL,
   created_at TIMESTAMP WITH TIME ZONE NOT NULL,
   committed_at TIMESTAMP WITH TIME ZONE`.
   Indizes auf `(asset_id, control_cycle_tick_utc_ms)`
   (Replay-Lookup) und `(asset_id, created_at DESC)` (Operator-
   Retention-Query).

2. **`mpc_request_id`-Schema** als deterministischer SHA-256-Hash
   über die canonical-form-Serialisierung des Tupels
   `(asset_id, control_cycle_tick_utc_ms,
   mpc_model_version, state_estimator_variant)`. Im 4-Hz-Cycle
   liefert dasselbe Tupel pro Tick exakt eine `mpc_request_id`;
   ein Worker-Restart der innerhalb desselben Ticks wieder
   reinkommt detektiert den Replay über `ON CONFLICT
   (mpc_request_id) DO NOTHING` analog zur M5-01-Linie.
   `control_cycle_tick_utc_ms` ist die zum konfigurierten
   `MpcOptions.SampleTime`-Boundary getruncte UTC-Millisekunde
   (z. B. bei 250 ms-Sample-Time: `tick_utc_ms / 250 * 250`);
   das verhindert dass ein Worker mit Sub-Millisekunden-Clock-
   Skew dieselbe logische Tick als zwei verschiedene Identitäten
   sieht.

3. **Retention-/Compaction-Policy** (eigene Migration-Job-Linie
   oder in-store-Compaction analog zum
   `DapperActivationDedupeStore`-Pattern aus M4-03): pro Asset
   maximal `MpcRetentionOptions.MaxRunsPerAsset` Runs (Default
   86 400 ⇒ 6 Stunden Retention bei 4 Hz), zusätzlich harte TTL
   `MpcRetentionOptions.MaxAge` (Default 24 h). Compaction läuft
   im Worker-Tick-Pfad asynchron (out-of-line, nicht im kritischen
   Solver-Call-Pfad). Operator-Override-Slots in
   `BessHostOptions.MpcRetention`. Pflicht-Pin in Sub-Slice D:
   Compaction-Test legt 100 k Synthetic-Runs an und beweist dass
   die Tabelle auf Max-Cap zurückfällt.

Retry-/Late-Response-Race-Verhalten (RM-M5-01-Pattern): erste
`INSERT ... ON CONFLICT DO NOTHING` gewinnt, spätere Aufrufe lesen
den existierenden Terminalzustand und geben `mpc-late-response-
ignored` zurück ohne Re-Aktivierung. Worker-Restart-mid-Tick
detektiert den Replay zuverlässig weil `mpc_request_id` rein aus
fachlichen Identitäts-Feldern abgeleitet ist (kein Wall-Clock-
Now).

Begründung gegen die naive Alternative „LP-`optimization_idempotency`-
Tabelle ko-nutzen": die LP-Tabelle ist auf wenige Hundert Rows pro
Asset/Tag dimensioniert; MPC-Schreib-Rate würde sie in unter einer
Stunde unbenutzbar groß werfen. Eigene Tabelle + Retention-Policy
ist die saubere Trennung.

---

## 6. Akzeptanzkriterien

- **State-Space-Modell** als `MpcModel`-Domain-Typ; LTI-Constraints
  (SOC, Power, Ramp) sind im Modell verankert.
- **`IMpcDispatchOptimizer`** ist composition-root-mäßig austauschbar
  via konfigurierten Adapter-Slot. Die konkrete D-02-Variante (Local-
  First / Sidecar-First / Bi-Modal) entscheidet der Plan-Review-Pass
  vor Sub-Slice-B-Start; das Acceptance-Criterium gilt unabhängig
  davon, weil alle drei Varianten die `IMpcDispatchOptimizer`-DI-
  Schicht erfüllen.
- **Worker-Control-Cycle-Wiring**: `ControlCycleHostedService` (oder
  der RM-M1-19-Worker-Pfad-Äquivalent in der aktuellen Codebase)
  ruft `IMpcDispatchOptimizer.NextStepAsync` pro Control-Cycle-Tick
  auf. Pflicht-Integration-Pin in `BatteryEms.Worker.Tests` mit einem
  zählenden Stub-`IMpcDispatchOptimizer` beweist die produktive
  Verdrahtung: ≥3 Ticks → Stub.CallCount == ≥3. **Ohne diesen Pin
  darf RM-M5-02 nicht ✅ flippen** — er ist die einzige Garantie,
  dass D-01 (MPC neben LP-Linie) tatsächlich produktiv aktiv ist
  und nicht nur als DI-Service registriert.
- **`DefaultLinearKalmanFilter`** mit Predict+Update-Pipeline und
  drei Robustheits-Pfaden (Missing-Measurement, Unplausible-State,
  Covariance-Divergence).
- **Constraint-Property-Pins** beweisen SOC-/Power-/Ramp-Einhaltung
  über randomisierte Inputs (mindestens 1000 Property-Iterationen
  pro Pin im CI-Lauf).
- **`OptimizeMpc`-RPC-Backend** liefert echte Trajektorien (Vertrag-
  only-Phase aus M5-01-D-08 ist abgeschlossen).
- **Reproduzierbarkeitsvertrag** unter `DeterministicMode=Strict`:
  zwei Replay-Läufe mit identischen Inputs + identischen Stempel-
  Werten + identischem Random-Seed + identischen Solver-Threading-
  Flags produzieren Trajektorien innerhalb der in D-04 dokumentierten
  Toleranz (exakt für int/string-Stempel, ±1e-9 relativ pro Float-
  Schedule-Point — Pin in Sub-Slice-D bei Plan-Review kalibriert,
  abhängig von der Solver-Wahl). `DeterministicMode=BestEffort` und
  `=None` haben dokumentierte, lockere Toleranz-Profile bzw. sind
  explizit Replay-untauglich.
- **`mpc_runs`-Migration** mit allen D-04-/D-09-Spalten; Dapper-
  Repository inkl. Restart-Replay-Pin, Late-Response-Ignored-Pin
  (CAS-Race-Pattern aus M5-01), Retention-Compaction-100k-Pin
  (Top-N + MaxAge gemäß D-09).
- **MPC-Idempotency-Schlüssel** (D-09): deterministischer SHA-256
  über `(asset_id, control_cycle_tick_utc_ms, mpc_model_version,
  state_estimator_variant)`; Worker-Restart-mid-Tick detektiert
  Replay zuverlässig (kein Wall-Clock-Now-Input).
- **Cross-Run-Determinism-Pin** (D-04): zweimal selber MPC-Step
  mit `DeterministicMode=Strict` ⇒ identische Stempel-Hashes
  (Solver-Konfig + `P_0`-Canonical) + Schedule-Points innerhalb der
  in D-04 kalibrierten Toleranz.
- **`FallbackPlanValidator`** trägt eine MPC-Stempel-Achse.
- **Quality-Doku** §2.2.3 + neue §2.5 listet alle neuen Pins und das
  neue Property-Gate `make test-mpc-property`.
- **`make test-hil-optimization-core`** erweitert um die MPC-Pins
  (LP-Linie bleibt unverändert; Pin-Count steigt monoton).
- **`make test-mpc-property` grün** in `make gates` und `make ci`.
- **Slice-Plan** in `docs/plan/planning/done/plan-RM-M5-02.md`.
- **Master-Plan-Zeile RM-M5-02** flippt auf ✅ mit dem D-05-
  Replacement-Text.

---

## 7. Risiken und Tradeoffs

- **Solver-Wahl-Drift.** OR-Tools-QP, OSQP, HiGHS haben
  unterschiedliche Numerik-Profile. Wenn ein Operator zwischen
  Solvern wechselt, brechen die Replay-Stempel — das ist
  beabsichtigt (D-04), aber Operator-UX verlangt klare Doku.
  Mitigation: Solver-Name + Version sind Pflicht-Stempel-Felder;
  `mpc-numerik-version-mismatch`-Reason ist Plan-Validator-Linie.
- **Kalman-Tuning-Subjektivität.** Process-/Measurement-Noise-
  Covariance (`Q`, `R`) sind operator-tunable; ungünstige Werte
  produzieren divergente Estimator-States. Mitigation:
  Default-Werte aus M5-02-A-Pins werden als „Reference-Tuning"
  in der Quality-Doku gepinnt; Production-Override muss explizit
  via `MpcOptions.Q`/`MpcOptions.R` mit Doku-Verweis kommen.
- **LTI-Annahme.** Reale Batterien haben SOC-/Temperatur-abhängige
  Effizienz-Kurven und temperaturabhängige Power-Limits. M5-02
  modelliert sie als LTI-Default mit Worst-Case-Bounds; reale
  Replay-Vergleiche werden Drift zeigen. Mitigation: nichtlineares
  Modell ist Folgearbeit (Out-of-Scope-Block); LTI-Stand wird im
  `MpcModel.Version`-Slot persistiert damit ein späterer
  nichtlinearer Lauf nicht stillschweigend als „selbe Modell-
  Identität" gilt.
- **Sidecar-Latenz für Sub-Sekunden-Cycle.** Wenn MPC pro Control-
  Cycle (z. B. alle 250 ms) läuft und über den Sidecar geht, ist
  der gRPC-Roundtrip möglicherweise zu langsam. Mitigation: D-02
  Bi-Modal-Default mit Local-First erlaubt Production-Topologie
  ohne Sidecar; ADR 0005 §7 Phase-4-Pivot-Trigger ist der
  formalisierte Pfad wenn Local-First nicht ausreicht.
- **Constraint-Hardness vs. QP-Infeasible-Rate.** Hard-Constraints
  (D-07) erhöhen die Infeasible-Rate bei eng angesetzten Bounds.
  Mitigation: Fallback-Matrix aus M5-01 fängt Infeasible auf — der
  letzte gültige Plan bleibt aktiv (bzw. der bekannte
  `last-known-plan-fallback`-Pfad aus F-M5-05 wenn realisiert).
- **State-Estimator-Stempel-Komplexität.** Initial-Covariance ist
  eine Matrix; eine reine Frobenius-Norm wäre kein injektives
  Identitätskriterium (zwei unterschiedliche `P_0`-Matrizen können
  identische Norm haben). Mitigation: D-04 verwendet einen
  SHA-256-Hash über die canonical-form row-major-Bytes der `P_0`-
  Matrix (truncated auf 1e-12 Float-Präzision) als Identitäts-
  Kriterium; die Frobenius-Norm bleibt als operator-sichtbares
  Display-Feld, ist aber **nicht** Replay-Match-Kriterium.
- **Solver-Determinism-Drift zwischen Plattformen.** OR-Tools/OSQP/
  HiGHS sind Multi-Thread-Default + reagieren sensibel auf BLAS-
  Backend, CPU-SIMD-Variante (AVX2 vs. NEON), Linux-/Windows-
  libc-FP-Rundungs-Modi. Ein „bit-identisch"-DoD ohne diese
  Disziplin ist praktisch nicht erfüllbar. Mitigation: D-04
  führt den `DeterministicMode`-Slot ein (Strict/BestEffort/None);
  `Strict` schreibt Single-Thread + dokumentierten Solver-Flag-
  Stempel + persistierten Random-Seed vor, Toleranz ±1e-9 relativ
  pro Float (kalibriert beim Plan-Review nach Solver-Wahl).
  `BestEffort` lockert auf 1e-6 relativ. `None` markiert den Run
  als Replay-untauglich und RM-M5-04-Replay-Lese rejected ihn
  fail-closed mit `mpc-non-deterministic-run`. Cross-Run-
  Determinism-Pin in Sub-Slice D ist Pflicht-Pin bevor RM-M5-02
  grün gehen darf.
- **MPC-Schreib-Rate-Tabellen-Wachstum.** 4 Hz pro Asset × N Assets
  produziert sehr viele `mpc_runs`-Rows; ohne Retention läuft die
  Tabelle in unter einer Stunde unhandhabbar groß. Mitigation:
  D-09 spezifiziert Top-N + MaxAge-Compaction mit Operator-
  Override-Slots (`BessHostOptions.MpcRetention`); Compaction-
  100k-Pin in Sub-Slice D verifiziert das Retention-Verhalten.

---

## 8. Sequenz

**Schritt 1: Plan reviewen.** Externer Review-Pass analog zur
M5-01-Linie. Kritische Punkte:

- Hält D-01 (MPC neben LP)? Reviewer prüft ob die Worker-Control-
  Cycle-Linie aus M1 ohne Erweiterung den MPC-Port aufrufen kann.
- Hält D-02 (Solver-Wahl-Variante)? **Pflicht-Entscheidung vor
  Sub-Slice-B-Start**. Reviewer entscheidet zwischen (a)/(b)/(c)
  und triggert ggf. ADR 0006.
- Hält D-03 (Linear-KF-Default)? Reviewer prüft ob nichtlineares
  Modell-Element im aktuellen Asset-Set bereits erforderlich ist.
- Hält D-04 (Stempel-Achsen)? Reviewer prüft ob die drei Achsen
  RM-M5-04-Replay vollständig bedienen.
- Hält D-07 (hard Constraints)? Reviewer prüft ob Soft-Variante
  als opt-in carve-out gebraucht wird.

**Schritt 2: ADR 0006 (optional, Pflicht bei Solver-Wahl-Ambiguität).**
Wenn D-02 nicht im Plan-Review-Pass fixiert werden kann, entsteht
`docs/plan/adr/0006-mpc-kernel-modeling-and-solver.md` mit den drei
Solver-Varianten + Modell-Form-Diskussion. Schreibstil und Tabellen-
Form analog zu ADR 0005.

**Schritt 3: Sub-Slices in Reihenfolge A → B → C → D umsetzen.**

1. **Sub-Slice A**: Application-Schicht + Constraint-Property-Pins.
   Reines Domain-/Port-/Property-Test-Material; kein Solver-Adapter.
2. **Sub-Slice B**: QP-Solver-Backend (D-02-Variante). Erste echte
   MPC-Wire-Roundtrip-Surface plus TestSidecar-MPC-Stubs.
3. **Sub-Slice C**: Kalman-Filter + Robustheits-Pfade + Fallback-
   Erweiterung. Estimator-Schicht; größter Sub-Slice.
4. **Sub-Slice D**: Reproduzierbarkeits-Vertrag + Persistenz +
   Doku-Sync + Closure.

**Schritt 4: Closure-Commit.** Pattern wie M5-01-Linie — ein Commit
pro Sub-Slice plus optional Review-Fix-Commit nach externem Review.
Master-Plan-Move nach allen Sub-Slices grün; Pin-Count im D-05-
Replacement-Text wird auf real-gelieferten Stand gesetzt (Lehre aus
M5-01-D wo der vorab-gepinnte Pin-Count zur Re-Closure-Inkonsistenz
führte).

---

## 9. Folgearbeiten (gehen in `note-RM-M5-followups.md`)

**Neu von M5-02-D explizit angelegt:**

- **F-M5-06 Multi-Asset-MPC.** Trigger: erster Operator-Workflow
  mit 2+ Batterien im selben Netz-Knoten. Multi-Asset-Coupling-
  Constraints (gemeinsame Power-Limits, Netz-Kapazität) + State-
  Estimator-Erweiterung.
- **F-M5-07 Nichtlineares-/Piecewise-MPC-Modell.** Trigger: erste
  Replay-Differenz die unter LTI-Annahme nicht erklärbar ist; oder
  konkrete Operator-Anforderung nach SOC-/Temperatur-abhängiger
  Effizienz-/Power-Modellierung. Setzt Modell-Form-Erweiterung +
  EKF/UKF-Estimator-Adoption voraus.
- **F-M5-08 Stochastic-/Robust-MPC.** Trigger: erste Operator-
  Anforderung nach Forecast-Unsicherheits-Handling jenseits der
  Kalman-Covariance (z. B. Markt-Preis-Szenarien).
- **F-M5-09 Adaptive Modell-Identifikation.** Trigger: erste
  Aging-/Degradations-bedingte Modell-Drift in der Operator-Praxis.
  Setzt eigene ADR voraus (Compliance/Audit-Trail-Implikationen
  für Online-Lernens).
- **F-M5-10 Time-Varying-Reference-Tracking.** Trigger: erste
  TSO-/Operator-Anforderung nach Trajektorien-Following jenseits
  der reinen Schedule-Linie.

**Bestehend, unverändert (aus RM-M5-01-Linie):**

- F-M5-01..F-M5-05 (Cert-Rotation, Multi-Tenant-Bearer-Token-Auth,
  Drittsprach-Sidecar, Schedule-Stempel-Erweiterung, Letzter-
  bekannter-Plan-Fallback) bleiben in `note-RM-M5-followups.md`
  unverändert.

**Bestehend, getrennt (aus ADR 0005 §7 Phase-4-Pivot):**

- Latenz-Pflicht-Bound unter 1 ms pro MPC-Schritt → RM-M5-03
  Native-Embed-Pivot. M5-02 fährt im Default mit Bi-Modal-Local-
  First; wenn die Latenz-Telemetrie aus M5-02-D-Doku konsistent
  über dem Bound liegt, zündet RM-M5-03.
