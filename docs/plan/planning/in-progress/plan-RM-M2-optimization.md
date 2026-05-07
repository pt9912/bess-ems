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
| ✅     | RM-M2-OP-05 | Adapter: LP-Solver-Adapter für `IScheduleOptimizer`                 | RM-M2-OP-02        | `OrToolsScheduleOptimizer` (GLOP-LP via `Google.OrTools` 9.11.4210, zentral gepinnt) im neuen `BatteryEms.Adapters.Optimization.OrTools`-Namespace; native Bindings restorieren im Debian-SDK-Image. Modell §Arbeitsmodell: `p_charge[t]`/`p_discharge[t]`/`soc[t]` mit SOC-Bounds aus `MinSocPercent`/`MaxSocPercent`/`Capacity`, SOC-Dynamik mit η_charge·Δt und Δt/η_discharge, Initial-SOC = `(min+max)/2` (oder `InitialSocPercent`-Override), freier Terminal-SOC, Objective `Σ price[t]·(p_charge−p_discharge)·Δt/1000` für `PriceUnit = "EUR/MWh"`. `OrToolsResultMapper` bildet alle 7 GLOP-Stati ab; Time-Limit-Reklassifizierung nur strict `>` und nur für `NOT_SOLVED` (Review #2 — eine `FEASIBLE`-Lösung am Budget-Rand bleibt feasible, ihr Schedule wird nicht verworfen). `ScheduleSolverOptions` als immutable Record (init-only props + `ScheduleSolverOptionsBuilder` für DI-Configure-Callback, Review #11). Schedule-Identity wandert in den Use-Case (Review #1/#3): `DefaultScheduleOptimizationUseCase` ergreift einen per-(asset,type)-`SemaphoreSlim`, liest existierenden Schedule, erbt MarketBidArea + erhöht Version, baut die volle `ScheduleOptimizationRequest` und übergibt sie dem Optimizer; ohne Vorgänger gilt M2-Konstante "DE-LU"/Version 1. Neue `ScheduleOptimizationInputs`-DTO trennt Caller-Eingabe (API) von Optimizer-Vertrag. UTC-Defense (#4) normalisiert `HorizonStart` vor Window-Konstruktion; Snap-to-Zero (#18) auf Objective < 1e-9 vermeidet Floating-Point-Rauschen; Post-Solve-Cancellation-Check (#9). Pre-Flight-Failure-Pfade (`missing-prices`, `unsupported-price-unit:{unit}`, `initial-soc-out-of-bounds`) liefern `Failed`-Run ohne Solver-Aufruf. Test-Seam `Func<Solver.ResultStatus, Solver.ResultStatus>` erlaubt Infeasible/Unbounded-Pfad-Coverage ohne contrived LP (#7); `[ExcludeFromCodeCoverage]` auf `BuildNonSolutionResult` entfernt. 47 Adapter-Tests + 13 Use-Case-Tests inklusive Konkurrenz-Serialisierung-Test (zwei parallele Calls für gleiches `(asset,type)` produzieren v1+v2, nie zwei v1), Bid-Area-Inheritance, Closed-Form-Objective-Check (LH-OPT-008), Sum-Discharge-Energie-Invariante bei flat prices, Window-Count-Property, Non-UTC-Offset-Normalisierung. `make lint` + `make test` + `make arch-check` (13/13) + `make coverage-gate` (Optimization ≥ 90 % line / branch) real grün. |
| ✅     | RM-M2-OP-06 | Adapter: `DapperOptimizationRunRepository` + DDL-Erweiterung       | RM-M2-OP-04, RM-M1-13 | Neue Tabellen `optimization_runs` (UUID-PK + JSON-Text-Spalten für inputs/violations/warnings, nullable Tripel `produced_schedule_*`) + `optimization_objective_breakdowns` (PK `(run_id, name)`, UNIQUE `(run_id, position)`, ON DELETE CASCADE) idempotent im `BessDbSchema.CreateScript`. `DapperOptimizationRunRepository` mirrort das `DapperScheduleRepository`-Pattern: ein `NpgsqlDataSource`-ctor, `DapperConfig.EnsureConfigured()`, Append + Komponenten in einer Transaktion, `PostgresErrorCodes.UniqueViolation` → `InvalidOperationException` (Append-only-Vertrag matcht InMemory-Repo). `QueryAsync` lädt Header über `[from, until)` halboffen + Komponenten via `run_id = ANY(@RunIds)` in einem zweiten Query (kein N+1). `ScheduleTypeWire` und `SolverStatusWire` als geteilte interne Helpers, `DapperScheduleRepository` zieht jetzt aus derselben Quelle. DI: `IOptimizationRunRepository → DapperOptimizationRunRepository` als Singleton. 2 neue Integrationstests: voll-fidelity Roundtrip mit zwei Komponenten + Asset/Range-Filter inkl. halboffener Boundary, Append-only-Verletzung. `make lint` + `make test` (27/27) + `make test-integration` (8/8) + `make arch-check` (13/13) + `make coverage-gate` real grün. |
| ✅     | RM-M2-OP-07 | API: `POST /markets/day-ahead/optimize`                            | RM-M2-OP-03, RM-M1-15 | Endpoint operator-policy-guarded (analog `/operator/stop`). Validation-Reihenfolge: 400 missing-or-invalid-field → 404 asset-not-registered → 400 unknown-schedule-type → 400 invalid-request (Konstruktor-Invarianten). `OptimizationResponse` rendert `Status` als Enum, der zentrale snake_case-Converter liefert `failed`/`optimal`/… auf der Wire. `NoOpScheduleOptimizer` wandert von `Adapters.Optimization` nach `Application.Optimization` (analog zu NoOpBatteryTelemetrySource), damit der API-only-Test-Host ihn ohne Driven-Adapter-Ref auflöst. DI: `AddBessApplicationInMemoryStores` registriert `IOptimizationRunRepository`, `IScheduleOptimizationUseCase` und `IScheduleOptimizer` (NoOp-Default). 6 Endpoint-Tests + 3 NoOp-Tests; `make build` + `make runtime` real grün. (Commit `d3bea8e`) |
| ✅     | RM-M2-OP-08 | Telemetrie + Metriken                                              | RM-M2-OP-03        | `IOptimizationRunMetrics`-Port (Application.Observability) + `NoOpOptimizationRunMetrics`-Default + `PrometheusOptimizationRunMetrics`-Adapter (Telemetry.Prometheus). Vier Instrumente unter `bess_optimization_*`: Counter `runs_total{asset_id,status}`, Histogram `run_duration_seconds{asset_id,status}` (log-spaced 5 ms .. 600 s), Gauge `objective_value{asset_id}` (Last-Value), Counter `constraint_violations_total{asset_id}` inkrementiert um Verletzungs-Anzahl. Status-Label snake_case-aligned mit `SolverStatusWire` (RM-M2-OP-06) und API-JSON-Converter (RM-M2-OP-07). `DefaultScheduleOptimizationUseCase` ruft `Record(run)` **nach** `AppendAsync` (LH-OPT-009: Metric-Counts ↔ persistierte Run-Historie können nicht divergieren); Solver-Crashes werfen vor dem Append → keine Metric-Emission, getestet. DI: `AddBessApplicationInMemoryStores` registriert NoOp-Default, `AddBessTelemetry` überschreibt mit Prometheus-Adapter (last-registration-wins, gleiches Pattern wie `IControlCycleMetrics`/`IHealthQuery`). 3 neue UseCase-Tests (zwei Status-Pfade, Solver-Crash) + 5 Adapter-Scrape-Tests (alle 7 Status-Labels, Counter ohne Inc bleibt unsichtbar, Null-Run wirft). `make lint` + `make test` + `make arch-check` + `make coverage-gate` (≥ 90% line) real grün. |
| ⬜     | RM-M2-OP-09 | Replay-/Reproduzierbarkeitstest                                    | RM-M2-OP-06        | Zwei Runs mit identischen Inputs liefern entweder identische Ergebnisse oder einen explizit dokumentierten Begründungsvermerk (LH-OPT-009-Akzeptanz). |

---

## Detailierung RM-M2-OP-05

**Status:** Die folgenden Punkte sind eine Arbeitsgrundlage für OP-05,
keine finalen Designentscheidungen. OP-05 ersetzt den bisherigen
Schedule-Optimizer-Platzhalter nicht global, sondern ergänzt den Driven
Adapter um ein produktives, konfigurierbares Backend. API-only-Hosts und
Tests ohne Solver-Bindings behalten `NoOpScheduleOptimizer`.

**Lieferobjekte**

- Solver-Binding nach Entscheidung als zentral versioniertes NuGet-Paket
  in `Directory.Packages.props`; P/Invoke- oder gRPC-Sidecar-Arbeit bleibt
  außerhalb des M2-Minimalschnitts, solange kein expliziter Beschluss
  dafür fällt.
- Options-Typ im Optimization-Adapter, z. B.
  `OrToolsScheduleOptimizerOptions { TimeLimit, GapTolerance }` oder ein
  backend-neutraler `ScheduleSolverOptions`, abhängig von der
  Solver-Entscheidung.
- Produktiver Adapter `...ScheduleOptimizer : IScheduleOptimizer` in
  `BatteryEms.Adapters.Optimization`; keine Abhängigkeit auf API,
  Persistence oder Worker.
- DI-Wiring in `OptimizationRegistration`: Default bleibt NoOp, ein
  konfiguriertes Backend überschreibt gezielt `IScheduleOptimizer`.
- Interne Mapper für Status und Solver-Terminierung; unbekannte oder
  native Fehler werden zu `OptimizationSolverStatus.Failed` mit
  aussagekräftiger `TerminationReason`.

**Arbeitsmodell**

- Zeitraster ist exakt `request.StepCount`; jedes Solver-Intervall
  entspricht `[HorizonStart + i * TimeStep, HorizonStart + (i+1) *
  TimeStep)`.
- Entscheidungsvariablen pro Schritt:
  `p_charge[t] ∈ [0, MaxChargePowerKw]`,
  `p_discharge[t] ∈ [0, MaxDischargePowerKw]` und `soc[t]` in kWh.
- Leistungsgrenzen kommen ausschließlich aus `BatteryAsset`:
  `p_charge <= MaxChargePowerKw`,
  `p_discharge <= MaxDischargePowerKw`.
- SOC-Bilanz nutzt kWh:
  `soc[t+1] = soc[t] + (ChargeEfficiency * p_charge[t] -
  p_discharge[t] / DischargeEfficiency) * Δt_h`.
- SOC-Grenzen werden aus `MinSocPercent`, `MaxSocPercent` und
  `CapacityKwh` abgeleitet; ein M2-Minimalvorschlag ist
  `initial_soc = (MinSocPercent + MaxSocPercent) / 2 * CapacityKwh`.
- Terminal-SOC ist im M2-Minimalvorschlag frei, um keine
  Horizon-End-Floor/Ceil-Effekte einzubauen.
- Objective in M2 ist nur Day-Ahead-Energiekosten:
  `energy_cost = Σ price[t] * (p_charge[t] - p_discharge[t]) * Δt_h /
  1000` bei `PriceUnit = "EUR/MWh"`.
- Schedule-Output folgt der Domain-Konvention:
  `target_power_kw[t] = p_discharge[t] - p_charge[t]`
  (Entladen positiv, Laden negativ).
- Tarif-, Reserve-, Degradations-, Ramp- und P/Q-Fähigkeitskosten bleiben
  Folgearbeit, sofern sie nicht explizit in OP-05 gehoben werden.

**Offene Designentscheidungen**

- Initial SOC: M2-Konstante `(MinSocPercent + MaxSocPercent) / 2` vs.
  Request-Feld; Request-Feld wäre eine Port-Erweiterung, Telemetrie-Feed
  ist eher M3.
- Charge/Discharge-Exklusivität: LP-Relaxation vs. MILP mit binärem
  Indikator; bei `η < 1` sollte das LP-Optimum Doppelfluss vermeiden,
  ein Test muss diese Annahme absichern.
- Preis-Einheit: nur `EUR/MWh` akzeptieren mit
  `Failed/"unsupported price unit"` vs. mehrere Units übersetzen.
- Terminal-SOC: frei vs. zyklusbalanciert `soc[N] == soc[0]`; frei kann
  am Horizon-Ende leerfahren, balanciert ist realistischer, reduziert
  aber Erlöse.
- Solver: OR-Tools GLOP vs. HiGHS/CBC; reines LP braucht kein MILP.
- Konfigurierbarkeit: zunächst ein Solver mit kleinem Options-Typ vs.
  sofortige Multi-Solver-Abstraction.

**Resultatvertrag**

- `Optimal` und `Feasible` erzeugen einen `Schedule` mit genau einem
  Fenster pro Schritt; `ScheduleReference` im `OptimizationRun` zeigt auf
  dieselbe `(AssetId, Type, Version)`.
- Nicht-lösbare Läufe (`Infeasible`, `Unbounded`, `Failed`) erzeugen
  keinen Schedule, werden aber als `OptimizationRun` vollständig
  zurückgegeben und später durch den Use Case persistiert.
- `ObjectiveBreakdown` enthält mindestens `energy_cost`; zusätzliche
  Komponenten sind erst zulässig, wenn ihr physikalischer
  Unit-Denominator dokumentiert und getestet ist.
- `ConstraintViolations` bleibt für harte Modellverletzungen leer; bei
  Status `Infeasible` steht die Ursache in `TerminationReason` und
  optional in `Warnings`.

**Testfokus**

- Adapter-Unit-Tests ohne Host: Options-Validation, Status-Mapping,
  Cancellation und Fehlerpfade.
- Modelltests mit kleinen deterministischen Horizonten: konstante Preise,
  30-min- und 1-h-Schritte, `E = P * Δt`, SOC-Update mit Wirkungsgrad,
  Power-Limits, unsupported PriceUnit und infeasible Initial-SOC.
- DI-Tests: ohne Config bleibt NoOp aktiv; mit konfiguriertem Backend
  wird der produktive `IScheduleOptimizer` aufgelöst.
- Architekturtests bleiben grün: Adapter darf Application/Domain
  referenzieren, aber nicht API/Worker/Persistence.

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
| RM-M2-OP-OPEN-01 | HiGHS via NuGet vs. OR-Tools GLOP/CBC vs. gRPC-Sidecar — welcher Bindungspfad zuerst? | Noch offen; M2 bevorzugt einen LP-fähigen In-Process-NuGet-Adapter, gRPC-Sidecar bleibt Option für custom solver pipelines. |
| RM-M2-OP-OPEN-02 | Wie stark wird `IScheduleOptimizer` mit `MarketCommitment`-Strafkosten und Tarifmodell (LH-MKT-008) gekoppelt? | M2 minimal: Day-Ahead-Energiekosten als Objective; Tarif/Reserve folgen mit RM-M2-04 Configurable Objective. |
| RM-M2-OP-OPEN-03 | Replay-Reproduzierbarkeit: deterministische Solver-Konfiguration als M2-Anforderung oder als Soll? | Soll für M2; Solver-Determinismus hängt vom Backend ab und ist nicht überall garantiert. |
| RM-M2-OP-OPEN-04 | API-Trigger: synchrone Antwort mit Solverlauf vs. asynchroner Job mit Status-Endpunkt? | Asynchron — `POST` legt Run an und gibt RunId zurück; `GET /optimization/runs/{id}` liefert Status. Solverlaufzeiten passen nicht in HTTP-Request-Budgets. |
| RM-M2-OP-OPEN-05 | Schedule-Replace ist nicht atomar gegen parallele Optimize-Calls auf Multi-Replica-Hosts (Review S1). | M3: optimistic-concurrency in `IScheduleRepository.Replace(schedule, expectedBaseVersion)` mit `WHERE version = @expected` in der Dapper-Variante; bei Versionskonflikt wird ein eigener `OptimizationSolverStatus`-Pfad ausgelöst (Failed mit Reason `concurrent-version-conflict`). M2 nutzt den per-(asset,type)-Semaphore in `DefaultScheduleOptimizationUseCase` — ausreichend für Single-Host-Deployments. |

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
