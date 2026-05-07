# Plan RM-M2 Optimization: Schedule-Optimizer + Run-Persistenz

**Dokumenttyp:** Aktiver Detailplan / M2
**Status:** In Arbeit (aktiviert nach M1-Abschluss)
**Bezug:** [`roadmap.md`](roadmap.md) (RM-M2-03, RM-M2-04,
RM-M2-05, RM-M2-09), [`plan-RM-M1.md`](../done/plan-RM-M1.md)
(RM-M1-F04, Spezifikations-Folgepunkte),
[`spec/lastenheft.md`](../../../../spec/lastenheft.md)
(LH-OPT-001/007/008/009, LH-PERSIST-007, LH-API-005),
[`spec/architecture.md`](../../../../spec/architecture.md) (§4.2 Driven
Ports, §8.2 Optimierungs-Interface, §11 Persistenz)

---

## Zweck

Dieser Plan konkretisiert das Folgepaket aus RM-M1-F04: die in M1 als
Architekturgrenze ausgewiesene Horizon-Optimierung wird zu einer
implementierten Pipeline aus produktivem `IScheduleOptimizer`,
nachvollziehbarem `OptimizationRun`-Modell und versionierter
Run-Persistenz. M1 bleibt davon unberührt — Import, Tracking und der
Echtzeit-Single-Step-Dispatch im Regelzyklus sind dort vollständig.

---

## Abgrenzung gegen M1

- M1 liefert: Day-Ahead-Fahrplanimport, `Schedule`/`ScheduleWindow`
  (UTC, halboffen), `IScheduleTracker` für aktive
  `MarketCommitment`s, `IDispatchOptimizer`/`NoOpDispatchOptimizer` für
  den 1-s-Echtzeit-Dispatch (LH-OPT-007 als Architekturgrenze).
- M2 liefert hier: produktive Horizon-Optimierung, Run-Datenmodell,
  Run-Persistenz und API-Trigger. Die Schnittstellen aus M1
  (`IScheduleRepository`, `IScheduleTracker`, `IDispatchOptimizer`)
  bleiben stabil; der Schedule-Optimizer schreibt eine neue
  `Schedule`-Version in dieselbe Repository, der Tracker nimmt sie auf
  ohne Code-Änderung.

---

## Komponenten

| Bereich         | Artefakt                                  | Hexagon-Klasse        | LH-Bezug              |
| --------------- | ----------------------------------------- | --------------------- | --------------------- |
| Domain          | `OptimizationRun`, `OptimizationObjectiveBreakdown`, `OptimizationSolverStatus` (Enum) | Domain-Records | LH-OPT-009            |
| Application     | `IScheduleOptimizer` (Driven Port), `ScheduleOptimizationRequest`/`ScheduleOptimizationResult` | Application-Ports | LH-OPT-001/007/008    |
| Application     | `IScheduleOptimizationUseCase` (Driving Port), Auslöser via API | Application-UseCase | LH-API-005            |
| Application     | `IOptimizationRunRepository` (Driven Port) | Application-Port     | LH-PERSIST-007        |
| Adapter (Optim.)| LP-/MILP-Implementierung von `IScheduleOptimizer` (HiGHS/OR-Tools, Solver-Auswahl per Config) | Driven Adapter | LH-OPT-002/006        |
| Adapter (Pers.) | `DapperOptimizationRunRepository`, neue Tabellen `optimization_runs` + `optimization_objective_breakdowns` | Driven Adapter | LH-PERSIST-007        |
| API             | `POST /markets/day-ahead/optimize` löst Run aus, gibt RunId/Status/Horizon/erzeugte Fahrplanversion zurück | Driving Adapter | LH-API-005            |

---

## Arbeitspakete

| Status | ID       | Paket                                                                | Abhängigkeit       | DoD                                                                                                                                                                                       |
| ------ | -------- | -------------------------------------------------------------------- | ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ✅     | RM-M2-OP-01 | Domain: `OptimizationRun`, `ObjectiveBreakdown`, `SolverStatus`     | RM-M1-12 (Schedule)| `Domain.OptimizationSolverStatus` (7-Wert-Enum: Optimal/Feasible/Infeasible/Unbounded/TimeLimit/IterationLimit/Failed), `OptimizationObjectiveComponent`/`OptimizationObjectiveBreakdown` (eindeutige Namen, finite Werte, expliziter Unit-Denominator pro LH-OPT-008), `ScheduleReference` und `OptimizationRun` mit allen LH-OPT-009-Feldern. Invarianten am Konstruktor: `Optimal`/`Feasible` ⇒ `ProducedSchedule` Pflicht, `HorizonStart < HorizonEnd`, `TimeStep > 0`, `SolverRuntime ≥ 0`, finite ObjectiveValue, non-blank Strings, ScheduleReference-Versionen ≥ 0. 7+12 Domain-Tests. Domain-Coverage 93 % line. (Commit `0b312c8`) |
| ✅     | RM-M2-OP-02 | Application: `IScheduleOptimizer` + Request/Result-DTOs            | RM-M2-OP-01        | `IScheduleOptimizer` als Driven Port in `Application.Optimization` neben `IDispatchOptimizer`. `ScheduleOptimizationRequest` validiert Time-Grid eagerly: Horizon = `n × TimeStep`, Preisreihen mit StepCount-Match + finite Werte + non-blank PriceUnit (LH-OPT-008), Inputs via `ScheduleReference.EnsureValid`. `ScheduleOptimizationResult` hält drei Invarianten: `HasUsableSolution ⇔ ProducedSchedule≠null`, Run-Reference matcht Schedule-Version, Non-Solution-Status verbietet Schedule. Architektur-Tabu-Tests `Schedule_optimizer_types_do_not_depend_on_dispatch_types` + `Dispatch_optimizer_types_do_not_depend_on_schedule_types` erzwingen LH-OPT-007 type-für-type. 8+5+2 Tests. (Commit `a02782d`) |
| ✅     | RM-M2-OP-03 | Application: `IScheduleOptimizationUseCase` (Driving Port)         | RM-M2-OP-02        | `IScheduleOptimizationUseCase` in `Application.Api`; `DefaultScheduleOptimizationUseCase` ruft `IScheduleOptimizer`, appendet `OptimizationRun` (LH-PERSIST-007, **unabhängig vom Status** — auch infeasible/failed Runs landen in der Audit-History), und ruft `IScheduleRepository.Replace` nur bei usable solution. Solver-Crashes propagieren (Run-Eintrag ohne tatsächlichen Run wäre Audit-Lüge). `ScheduleOptimizationOutcome { RunId, Status, ProducedScheduleVersion?, TerminationReason }` als API-Summary; volle LH-OPT-009-Payload bleibt im persistierten Run. Strukturierter `LoggerMessage`-Log. 5 Tests. (Commit `af980e6`) |
| ✅     | RM-M2-OP-04 | Application: `IOptimizationRunRepository`                          | RM-M2-OP-01        | Driven Port + `InMemoryOptimizationRunRepository` (M1-Pattern, Dapper-Variante folgt mit OP-06). Append + FindById + halboffene `[from, until)`-Range-Query gefiltert auf AssetId, ordered by CreatedAt. Append-only: re-appending einer RunId wirft (LH-OPT-009 Audit-Stance). 9 Tests. (Commit `971fa7e`) |
| ⬜     | RM-M2-OP-05 | Adapter: LP-Solver-Adapter (HiGHS oder OR-Tools, Auswahl per Config) | RM-M2-OP-02        | Solver-Auswahl/Bindings sind konfigurierbar; Einheiten- und Zeitschritt-Konsistenz nach LH-OPT-008 ist in Modelltests nachgewiesen (E = P · Δt). **Default per RM-M2-OP-OPEN-01: OR-Tools NuGet.** Bis dahin steht `Application.Optimization.NoOpScheduleOptimizer` (Failed/no-solver-configured) als Default-Wiring (siehe OP-07). |
| ✅     | RM-M2-OP-06 | Adapter: `DapperOptimizationRunRepository` + DDL-Erweiterung       | RM-M2-OP-04, RM-M1-13 | Neue Tabellen `optimization_runs` (UUID-PK + JSON-Text-Spalten für inputs/violations/warnings, nullable Tripel `produced_schedule_*`) + `optimization_objective_breakdowns` (PK `(run_id, name)`, UNIQUE `(run_id, position)`, ON DELETE CASCADE) idempotent im `BessDbSchema.CreateScript`. `DapperOptimizationRunRepository` mirrort das `DapperScheduleRepository`-Pattern: ein `NpgsqlDataSource`-ctor, `DapperConfig.EnsureConfigured()`, Append + Komponenten in einer Transaktion, `PostgresErrorCodes.UniqueViolation` → `InvalidOperationException` (Append-only-Vertrag matcht InMemory-Repo). `QueryAsync` lädt Header über `[from, until)` halboffen + Komponenten via `run_id = ANY(@RunIds)` in einem zweiten Query (kein N+1). `ScheduleTypeWire` und `SolverStatusWire` als geteilte interne Helpers, `DapperScheduleRepository` zieht jetzt aus derselben Quelle. DI: `IOptimizationRunRepository → DapperOptimizationRunRepository` als Singleton. 2 neue Integrationstests: voll-fidelity Roundtrip mit zwei Komponenten + Asset/Range-Filter inkl. halboffener Boundary, Append-only-Verletzung. `make lint` + `make test` (27/27) + `make test-integration` (8/8) + `make arch-check` (13/13) + `make coverage-gate` real grün. |
| ✅     | RM-M2-OP-07 | API: `POST /markets/day-ahead/optimize`                            | RM-M2-OP-03, RM-M1-15 | Endpoint operator-policy-guarded (analog `/operator/stop`). Validation-Reihenfolge: 400 missing-or-invalid-field → 404 asset-not-registered → 400 unknown-schedule-type → 400 invalid-request (Konstruktor-Invarianten). `OptimizationResponse` rendert `Status` als Enum, der zentrale snake_case-Converter liefert `failed`/`optimal`/… auf der Wire. `NoOpScheduleOptimizer` wandert von `Adapters.Optimization` nach `Application.Optimization` (analog zu NoOpBatteryTelemetrySource), damit der API-only-Test-Host ihn ohne Driven-Adapter-Ref auflöst. DI: `AddBessApplicationInMemoryStores` registriert `IOptimizationRunRepository`, `IScheduleOptimizationUseCase` und `IScheduleOptimizer` (NoOp-Default). 6 Endpoint-Tests + 3 NoOp-Tests; `make build` + `make runtime` real grün. (Commit `d3bea8e`) |
| ⬜     | RM-M2-OP-08 | Telemetrie + Metriken                                              | RM-M2-OP-03        | Solverzeit, Run-Anzahl, Objective-Werte, Constraint-Verletzungen sind als Prometheus-Metriken exportiert (LH-MON-002 erweitert). |
| ⬜     | RM-M2-OP-09 | Replay-/Reproduzierbarkeitstest                                    | RM-M2-OP-06        | Zwei Runs mit identischen Inputs liefern entweder identische Ergebnisse oder einen explizit dokumentierten Begründungsvermerk (LH-OPT-009-Akzeptanz). |

---

## Abnahmekriterien

- Ein Optimierungslauf für einen definierten Horizont und Asset
  erzeugt eine versionierbare `Schedule` mit Sollwerten, optionalen
  SOC-Zielen und vollem Solverstatus (LH-OPT-001 verschärft).
- Persistierter `OptimizationRun` ist nachträglich mit erzeugter
  Fahrplanversion und verwendeten Inputs verknüpfbar (LH-PERSIST-007).
- Trennung Horizon ↔ Echtzeit-Dispatch bleibt strukturell:
  `NoOpDispatchOptimizer` aus M1 nutzt weiterhin nur den jeweils
  aktiven Schedule-Window über den `IScheduleTracker` (LH-OPT-007).
- Einheiten- und Zeitschritt-Konsistenz ist getestet: ein konstanter
  Leistungsfahrplan über bekanntes Δt ergibt die erwartete
  Energiemenge (LH-OPT-008).
- Solver-Auswahl ist Config-getrieben; ein Wechsel zwischen
  HiGHS/OR-Tools/Heuristik ändert weder das Ergebnisformat noch die
  Run-Persistenz.

---

## Offene Punkte

| Kennung      | Frage                                                                              | Default-Vorschlag |
| ------------ | ---------------------------------------------------------------------------------- | ----------------- |
| RM-M2-OP-OPEN-01 | HiGHS via NuGet vs. OR-Tools via P/Invoke vs. gRPC-Sidecar — welcher Bindungspfad zuerst? | OR-Tools NuGet als M2-Erstimplementierung; gRPC-Sidecar bleibt Option für custom solver pipelines. |
| RM-M2-OP-OPEN-02 | Wie stark wird `IScheduleOptimizer` mit `MarketCommitment`-Strafkosten und Tarifmodell (LH-MKT-008) gekoppelt? | M2 minimal: Day-Ahead-Energiekosten als Objective; Tarif/Reserve folgen mit RM-M2-04 Configurable Objective. |
| RM-M2-OP-OPEN-03 | Replay-Reproduzierbarkeit: deterministische Solver-Konfiguration als M2-Anforderung oder als Soll? | Soll für M2; Solver-Determinismus hängt vom Backend ab und ist nicht überall garantiert. |
| RM-M2-OP-OPEN-04 | API-Trigger: synchrone Antwort mit Solverlauf vs. asynchroner Job mit Status-Endpunkt? | Asynchron — `POST` legt Run an und gibt RunId zurück; `GET /optimization/runs/{id}` liefert Status. Solverlaufzeiten passen nicht in HTTP-Request-Budgets. |

---

## Anschluss an M1

Dieser Plan rückt erst nach RM-M1-Abschluss in `in-progress/`. Das
M1-Domänen- und Persistenzfundament wird als gegeben vorausgesetzt:
`Schedule` + `IScheduleRepository` aus RM-M1-12, Dapper-Persistenz aus
RM-M1-13, Application-IO-Ports aus RM-M1-13a. Der Optimization-Adapter
`BatteryEms.Adapters.Optimization` enthält in M1 nur den
`NoOpDispatchOptimizer` (RM-M1-08); M2 ergänzt ihn um die
Schedule-Optimizer-Implementierung, ohne den Single-Step-Pfad zu
ersetzen.

---

## Folgewelle: HIL-Simulator (nach RM-M2-OP-05)

Sobald RM-M2-OP-05 (LP-Solver-Adapter) ein Resultat liefert, zieht die
HIL-Welle aus [`../open/HIL-simulator.md`](../open/HIL-simulator.md)
nach `in-progress/`. HIL prüft das LP-Resultat gegen ein dynamisches
PCS-/PQ-Capability-Modell des externen Images
`bess-hil-simulator:local`. Die in HIL-01..05 nötigen
Modbus-Adapter-Erweiterungen (Input Registers, Word-Order, optionaler
Q-Setpoint) sind gleichzeitig Vorarbeit für RM-M4 (OPC-UA / Vendor-
Profile mit MW-Skalierung). HIL bleibt **kein M2-Pflichtgate** —
`make gates`/`make ci`/`make test-integration` bleiben auf den Go-
`bess-field-sim` ausgerichtet.
