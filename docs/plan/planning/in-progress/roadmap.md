# Roadmap: bess-ems

**Dokumenttyp:** Planung / Roadmap
**Status:** In Arbeit
**Bezug:** [`spec/lastenheft.md`](../../../../spec/lastenheft.md)
(§28 MVP-Abgrenzung), [`spec/architecture.md`](../../../../spec/architecture.md)
(§13 Native-Core-Phasenmodell), [`docs/user/quality.md`](../../../user/quality.md)
(Gate-Aktivierung pro Meilenstein)

---

## Zweck

Dieses Dokument beschreibt die geplante Umsetzungsreihenfolge für `bess-ems`
in Meilensteinen. Es ist die Brücke zwischen Lastenheft (was) und
Architektur (wie) hin zu konkreter Arbeit (wann, in welcher Reihenfolge).

Jeder Meilenstein listet Liefergegenstände, die zugehörigen
Lastenheft-Kennungen und Abnahmekriterien. Kennungen `RM-Mn-xx` ermöglichen
die spätere Verlinkung aus PRs, Issues und ADRs.

Diese Roadmap ist die **Statusseite** des Projekts. Sie duplikiert nicht
die Anforderungen (die stehen normativ im Lastenheft), sondern verfolgt
*wo wir stehen, was als nächstes kommt und welche Risiken offen sind*.
Detail-DoD-Tracking pro Meilenstein lebt in einem eigenen
`plan-RM-Mn.md`; offene Entwürfe liegen unter `open/`, aktive Pläne
unter `in-progress/`.

### Status-Legende

| Symbol | Bedeutung   |
| ------ | ----------- |
| ✅     | abgeschlossen |
| 🟡     | in Arbeit  |
| ⬜     | geplant    |
| ⬛     | obsolet / verworfen |

---

## Aktueller Stand

> **Stand:** 2026-05-09
> **Abgeschlossen:** M1 (alle 24 Liefergegenstände grün) und M2 (alle
> 10 Liefergegenstände RM-M2-01..10 grün). `make fullbuild`
> reproduzierbar grün, Compose-Stack (bess-ems + Postgres + Mosquitto
> + bess-field-sim) liefert `/health = ok` inkl. Postgres-Probe.
> **Phase M3 abgeschlossen:** alle RM-M3-01..13 ✅. Hervorhebungen
> der jüngsten Slices: Sprach-Pivot der Kernel-Implementierung von
> C++ auf C (`catch (...)`-Block + libstdc++-Linkage entfallen,
> `.so` bei 15 KB ohne `NEEDED`-Einträge), doctest 2.4.11 als
> Test-Framework via `FetchContent` mit URL_HASH-Pinning, vier
> Native-Quality-Gates (`native-lint`, `native-sanitizer`,
> `native-coverage-gate`, `native-coverage-exclusions`) auf 100 %
> Line-Coverage ohne Exclusion, versionierter Replay-Parity-
> Datensatz unter `tests/fixtures/native_parity/cases.v1.json` mit
> 25 Cases, sowie der PID-Native-Slice (RM-M3-13) mit additiver
> ABI-Minor-Bump 0.1 → 0.2: vier neue PID-Structs, neuer Export
> `battery_control_core_pid_step`, vier neue Reason-Codes, voller
> Anti-Windup-/Deadband-Vertrag aus dem managed
> `PidController.Step` 1:1 abgebildet inklusive negativ-Ki-
> Direction-Logik, plus 21 Wire-Tests durch P/Invoke gegen die
> echte `.so`. `make gates` und `make ci` sind komplett grün.
> **M3-D2 abgeschlossen** ([`../done/plan-RM-M3-D2.md`](../done/plan-RM-M3-D2.md)):
> `AddBessNativeControl(IConfiguration)`-Extension registriert
> `IControlKernel` als Singleton, Default `Enabled=false` →
> `ManagedControlKernel`, `Enabled=true` mit echter `.so` →
> `NativeFallbackControlKernel`, deterministischer Managed-
> Fallback bei Native-Fehler, opt-in `AbortOnAbiMismatch` als
> Production-Policy. ADRs [0003](../../adr/0003-native-kernel-language.md)
> (Sprache C) und [0004](../../adr/0004-native-kernel-process-isolation.md)
> (In-Process P/Invoke) sind die Architektur-Anker. **Offen
> (alle trigger-getrieben, keine Trigger heute aktiv):** vier
> Follow-up-Items zentral geführt in
> [`../open/note-RM-M3-followups.md`](../open/note-RM-M3-followups.md)
> — M3-D3 PID-Routing (sobald ein PID-Konsument im Regelzyklus
> existiert; `PidController.Step` ist heute nicht verdrahtet),
> Production-Profil-Defaults zentralisieren (Operations-Reibung
> als Trigger), NativeControl-Health-Endpoint
> (Operator-Anforderung), Out-of-Process/Sprach-Pivot (ADR
> 0003/0004 Trigger). Plus die orthogonalen
> RM-M3-FUP-01/03/04-Carve-outs (Migrationen, OP-OPEN-06,
> Replay-Folge-Slices), ebenfalls trigger-getrieben offen.
> RM-M3-FUP-02 ✅ (OP-OPEN-05 — optimistic Schedule-Replace-CAS)
> ist abgeschlossen.
>
> **M4 gestartet:** [`plan-RM-M4.md`](plan-RM-M4.md) ist nach
> `in-progress/` migriert. **RM-M4-02 ✅** (Reservierungs-Modell +
> Solver-Constraints) ist abgeschlossen: Domain-Trio
> `ReserveProduct`/`ReserveDirection`/`ReserveBand`, Driven Port
> `IReserveRepository` + `InMemoryReserveRepository`,
> Use-Case-Verdrahtung über `ScheduleOptimizationRequest.Reserves`,
> OR-Tools deduziert Charge-/Discharge-Caps (Symmetric beidseitig,
> Up/Down einseitig) und terminiert Über-Commit mit
> `reserve-exceeds-capacity`; 9 Domain- + 11 OR-Tools-Pins inkl.
> FCR-symmetrischem und AFRR-Up/Down-Profiltest. Andere M4-Pakete
> (RM-M4-01 Intraday, RM-M4-03 Aktivierungssignal, RM-M4-04..08
> OPC-UA/MQTT) bleiben abhängigkeits-/trigger-getrieben offen.
>
> **M2-Welle 1 (Optimization-Slice, abgeschlossen):**
> [`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
> hat alle Arbeitspakete OP-01..09 grün gezogen — Domain mit
> LH-OPT-009-Payload und Invarianten, `IScheduleOptimizer`-Driven-Port,
> `IScheduleOptimizationUseCase` als API-Driving-Port, In-Memory- und
> Dapper-Variante des `IOptimizationRunRepository`, OR-Tools-GLOP-LP
> als produktiver Solver-Adapter (config-getrieben aktiviert),
> Prometheus-Metriken für Run-Counter/Runtime/Objective/Constraint-
> Violations, `POST /markets/day-ahead/optimize` operator-policy-
> guarded, sowie Replay-/Reproduzierbarkeitstests (LH-OPT-009).
> Drei Review-Pässe, alle Findings adressiert oder mit präzise
> dokumentiertem M3-Trigger eingefroren (OP-OPEN-05 ✅ via
> RM-M3-FUP-02; OP-OPEN-06 trigger-getrieben offen).
>
> **M2-Welle 2 (Restliche M2-Items, abgeschlossen):**
> RM-M2-01 (Marktcommitment-Priorisierung mit `MarketCommitmentPriority`
> + `ScheduleFollowingDispatchOptimizer`), RM-M2-02 (Zeitmodell-
> Erweiterung mit `MarketCommitment.MarketBidArea` + `ScheduleTimeGrid`
> + DST-Pipeline-Tests), RM-M2-04 (Configurable Objective mit
> `degradation_cost` + `soc_target_penalty` als zwei LP-Patterns),
> RM-M2-06 (OTel-Tracing für `bess.control_cycle.execute` /
> `bess.command_dispatch.write` / `bess.schedule_optimization.run`,
> SDK 1.15.3 mit zwei Vulns gefixt), RM-M2-08 (PID-Regler-Primitive
> `PidController` als pure Function — LH-CTRL-004 „Soll", produktive
> Verdrahtung folgt mit konkreten Konsumenten), RM-M2-10 (Telemetrie-
> Replay-Harness mit Reproduzierbarkeit / Golden-Trace / Missing- und
> Stale-Recovery). Mehrere Review-Pässe pro Slice, alle Findings
> adressiert oder als Carve-out mit Trigger in den jeweiligen
> RM-M2-Tabellenzeilen festgehalten.
>
> **M2-Folgewellen (abgeschlossen):**
> [`HIL-simulator.md`](../done/HIL-simulator.md) (HIL gegen
> `bess-hil-simulator:local`, HIL-01..09 ✅) und
> [`plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md)
> (versionierte Persistence-Migrations, MIG-01..06 ✅) sind beide
> gepusht. Der Migrationspfad ist startbar für die ersten echten
> Schema-Änderungen; der HIL-Pfad fährt Closed-Loop gegen das
> dynamische PCS/PQ-Modell. Plus mehrere Carve-out-Slices innerhalb der RM-M2-Zeilen
> (LH-OPT-004 #4 Marktverpflichtungs-Strafkosten unter RM-M2-04,
> Adapter-Spans / DB-Spans / Trace-ID-Persistenz unter RM-M2-06,
> JSON-Loader / Operator-Replay-CLI / Multi-Asset-Replay unter
> RM-M2-10), je mit eigenem Trigger in der Tabellenzeile.

---

## Übersicht

| Status | Meilenstein | Titel                              | Phase | Detailplan |
| ------ | ----------- | ---------------------------------- | ----- | ---------- |
| ✅     | M1          | MVP — sichere Regelpipeline        | 1     | [Abgeschlossen](../done/plan-RM-M1.md) |
| ✅     | M2          | Marktausbau und Optimierung        | 1 → 2 | Abgeschlossen ([Optimization-Slice](../done/plan-RM-M2-optimization.md), RM-M2-01..10, [Migrations-Tooling](../done/plan-RM-M2-migration.md), [HIL](../done/HIL-simulator.md)) |
| ✅     | M3          | Native Control Core (Library)      | 2     | [`../done/plan-RM-M3.md`](../done/plan-RM-M3.md) — alle RM-M3-01..13 ✅ inkl. C-Pivot, doctest, vier Native-Quality-Gates, replay-basierter Parity, Doku-Sync und PID-Slice mit ABI-Minor-Bump 0.1→0.2; M3-D2 produktive Routing-Aktivierung ✅ ([`../done/plan-RM-M3-D2.md`](../done/plan-RM-M3-D2.md)); offene Follow-up-Slices (acht Items in zwei Blöcken) in [`../open/note-RM-M3-followups.md`](../open/note-RM-M3-followups.md) — Block A M3-Closure-Out-of-Scope, Block B M2-Folgewellen mit M3-Trigger; alle trigger-getrieben, kein aktiver Trigger heute |
| 🟡     | M4          | Regelleistung und OPC-UA           | 2     | [`plan-RM-M4.md`](plan-RM-M4.md) — RM-M4-02 (Reservierungs-Modell + Solver-Constraints) als erste aktive Slice, andere Pakete trigger-/abhängigkeitsgetrieben offen |
| ⬜     | M5          | MPC, Solver-Sidecar, Replay        | 3     | folgt mit Aktivierung |
| ⬜     | M6          | Skalierung, UI, Edge / Multi-Asset | 4     | folgt mit Aktivierung |

Phase bezieht sich auf [`architecture.md`](../../../../spec/architecture.md)
§13. Die frühere Native-Core-Ideenskizze liegt archiviert unter
[`docs/archive/idea.md`](../../../archive/idea.md).

---

## M1 — MVP: sichere Regelpipeline

**Ziel:** Ein .NET-only EMS, das Telemetrie liest, Day-Ahead-Fahrpläne und
MarketCommitments verfolgt, technisch begrenzt und sicher Commands erzeugt
— vollständig containerisiert als ein eigenes `bess-ems`-OCI-Image mit
integrierter Worker/API-Komponente, Persistenz, Metriken und
AuthN/AuthZ-geschütztem Operator-Stop.

### Liefergegenstände

| Status | ID         | Inhalt                                                              | LH-Bezug                  |
| ------ | ---------- | ------------------------------------------------------------------- | ------------------------- |
| ✅     | RM-M1-01   | C#/.NET Solution-Skeleton mit Projekten gemäß Architektur §4.2 / §5.1 (hexagonale Verzeichnisstruktur) | LH-NF-001, LH-ARCH-001    |
| ✅     | RM-M1-02   | Domain-Modell (BatteryAsset, Telemetry, Command, DataQuality)       | LH-DOM-001..004           |
| ✅     | RM-M1-03   | Realtime Snapshot Store, Datenfusion, Aging                         | LH-RT-001..004            |
| ✅     | RM-M1-04   | State Machine (INIT…EMERGENCY_STOP) inkl. Quittierungslogik         | LH-SM-001..003            |
| ✅     | RM-M1-05   | Constraint Limiter (.NET) mit SOC-, Power-, Verfügbarkeitsgrenzen   | LH-CTRL-002, LH-SAFE-002/3 |
| ✅     | RM-M1-06   | Ramp Limiter (.NET)                                                  | LH-CTRL-003               |
| ✅     | RM-M1-07   | Regelzyklus 1 s, sicherer Fallback bei stale/ungültigem Snapshot    | LH-CTRL-001/007, LH-RT-003 |
| ✅     | RM-M1-08   | Optimization-Interface: `IDispatchOptimizer`/`NoOpDispatchOptimizer` für Echtzeit-Single-Step-Dispatch im Regelzyklus; `IScheduleOptimizer` ist Architekturgrenze ohne M1-Implementierung | LH-OPT-001 (als Interface), LH-OPT-007 |
| ✅     | RM-M1-09   | Modbus-TCP-Adapter (Lesen + Schreiben, Mapping über Config)         | LH-MODB-001..005          |
| ✅     | RM-M1-10   | MQTT-Adapter (Telemetrie-Empfang, Command-Publish, Topic-Konvention) | LH-MQTT-001..003         |
| ✅     | RM-M1-11   | Schreibbegrenzung im Adapter unmittelbar vor Versand                | LH-SAFE-007               |
| ✅     | RM-M1-12   | Statischer Fahrplanimport, `MarketCommitment`-Modell, UTC/DST-Zeitmodell + Day-Ahead-Verfolgung | LH-MKT-001/003/006/007 |
| ✅     | RM-M1-13   | PostgreSQL-Persistenz: Dapper + Npgsql 9, idempotente DDL, Repos für Telemetrie/Commands/Fahrpläne/Audit, Postgres-Sidecar im Compose-Integrationstest. DI-/Runtime-Wiring bleibt RM-M1-15/19. | LH-PERSIST-001..005       |
| ✅     | RM-M1-14   | Retention-/Datenvolumen-Konfiguration (dokumentiert)                | LH-PERSIST-006            |
| ✅     | RM-M1-15   | API: Health, Status, Current Command, Schedules, Operator-Stop      | LH-API-001..004, LH-API-006 |
| ✅     | RM-M1-16   | AuthN/AuthZ + Audit-Log für schreibende Endpunkte                   | LH-API-007                |
| ✅     | RM-M1-17   | Strukturierte JSON-Logs mit Reason-Feld + Prometheus-Metrikexport   | LH-MON-001/002/004        |
| ✅     | RM-M1-18   | Konfigurations-Loader, JSON-Schemas für Adapter-Mappings + Startvalidierung | LH-CONF-001..003, LH-OPS-001 |
| ✅     | RM-M1-19   | Dockerfile + Docker Compose (`bess-ems` mit Worker/API, Postgres, MQTT-Broker) | LH-DEPLOY-001..003, LH-NF-003/4 |
| ✅     | RM-M1-20   | Quality-Gates: Lint, Unit/Safety/Integration/Contract/Container, Coverage | LH-TEST-001/003/006/007, LH §4.1 |
| ✅     | RM-M1-21   | Makefile als Orchestrierungsschicht über die Docker-Stages aus `docs/user/quality.md`: `.DEFAULT_GOAL=help`, Override-Variablen (`COVERAGE_THRESHOLD`, `LIZARD_MAX_*`, `IMAGE`), Composite-Targets (`gates`, `ci`, `runtime`, `fullbuild`), `-gate`/`-report`-Trennung | LH-DEPLOY-001/002, LH-TEST-001/006/007 |
| ✅     | RM-M1-22   | Hexagonale Verzeichnis- und Modulstruktur gemäß Architektur §4.2 (`src/hexagon/`, `src/adapters/{driving,driven}/`, `src/infrastructure/`); Driving/Driven-Klassifikation pro Modul | LH-ARCH-001..005, LH-NF-006 |
| ✅     | RM-M1-23   | Boundary-Test-Modul `BatteryEms.ArchitectureTests` mit Dependency Rule und Architektur-Tabus aus §4.2 (Domain frameworkfrei, Application kein Adapter, Adapter zitieren keine anderen Adapter); Verstöße brechen den Build | LH-ARCH-002, LH-NF-006 |
| ✅     | RM-M1-24   | Go-basierter Blackbox-Simulator `simulators/bess-field-sim` für Modbus/MQTT, Szenario-Fixtures und Runtime-Smoke | LH-TEST-003/006/007, LH-PROT-001 |

### Abnahmekriterien

- `docker compose up` startet das Gesamtsystem lokal.
- Ein simulierter BMS/Wechselrichter (Modbus/MQTT) liefert Telemetrie, das
  System publiziert Commands, ohne SOC-/Power-/Rampengrenzen zu verletzen.
  Simulatorumfang und Szenarien sind in
  [`plan-RM-M1-simulator.md`](../done/plan-RM-M1-simulator.md) festgelegt; die
  Implementierung erfolgt als eigenstaendiger Go-Service.
- Bei stale Snapshot, Emergency Stop oder Operator-Stop wird ein sicherer
  Zustand erreicht und ist im Audit-Log nachvollziehbar.
- Day-Ahead-Fahrplan kann importiert, gespeichert und mit konsistentem
  UTC-/DST-Zeitmodell im Regelkreis verfolgt werden.
- M1-Gates aus `docs/user/quality.md` sind reproduzierbar grün:
  `make lint`, `make test`, `make test-safety`, `make test-integration`,
  `make test-container`, `make coverage-gate`, `make build`.
- `make help` listet alle Targets und Override-Variablen; `make gates`
  aggregiert die M1-Gates; `make ci` läuft die CI-kompatible Gate-Reihenfolge;
  `make runtime` prüft Compose/Healthcheck; `make fullbuild` läuft
  fresh-clone-nah bis Runtime-Smoke.
- OpenAPI-, Adapter-Mapping-, Vorzeichen- und Startvalidierungs-Gates
  brechen den Build bei Vertragsverletzungen.
- `BatteryEms.ArchitectureTests` setzt Dependency Rule und
  Architektur-Tabus aus §4.2 durch (Domain frameworkfrei, Application
  ohne Adapter-Referenzen, keine Adapter-zu-Adapter-Referenzen).

---

## M2 — Marktausbau und Optimierung (.NET)

**Ziel:** Erweiterte Marktlogik auf dem M1-Zeitmodell, einfacher
LP-Optimierer (.NET-Interface, optional OR-Tools/HiGHS), Tracing und Replay.

**Detailplan:** Der Schedule-Optimizer- und Run-Persistenz-Slice
(RM-M2-03/05/07/09 und Anschluss an LH-OPT-007/008/009 + LH-PERSIST-007)
ist in [`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
detailliert und mit OP-01..09 abgeschlossen. RM-M2-04 (Configurable
Objective) ist abgeschlossen: das M2-minimale `energy_cost`-Objective
(OP-OPEN-02) ist um `degradation_cost` und `soc_target_penalty`
gehoben; drei weitere LH-OPT-004-Komponenten (Marktverpflichtung,
Reserve, Peak-Shaving) hängen jeweils an einer eigenen Vorbedingung —
Begründung in der RM-M2-04-Tabellenzeile unten.

**Folgewelle:** Der Hardware-in-the-Loop-Pfad gegen das externe
`bess-hil-simulator:local`-Image ist abgeschlossen. Detail-Plan:
[`HIL-simulator.md`](../done/HIL-simulator.md). HIL-01..09 sind
gelandet, der Closed-Loop-Demo läuft via `make test-hil-modbus`
und über `deploy/compose.hil.yml` gegen die echte PCS/PQ-Dynamik
des Schwesterprojekt-Simulators. Modbus-Adapter-Erweiterungen aus
dem HIL-Plan (Input Registers, Word-Order, Q-Setpoint) sind
gleichzeitig Vorarbeit für RM-M4 (OPC-UA / Vendor-Profile mit
MW-Skalierung). HIL ist und bleibt **kein M2-Pflichtgate** —
`make gates` und `make test-integration` bleiben auf den
Go-`bess-field-sim` ausgerichtet.

### Liefergegenstände

| Status | ID         | Inhalt                                                              | LH-Bezug                |
| ------ | ---------- | ------------------------------------------------------------------- | ----------------------- |
| ✅     | RM-M2-01   | Erweiterte Marktcommitment-Priorisierung — `MarketCommitmentPriority` (Domain) rankt Commitments nach LH-MKT-006-Reihenfolge (#3 RegelLeistung > #4 verbindliche Markt­verpflichtung > #5 Intraday > #6 DayAhead; Released/Violated gefiltert). `ScheduleFollowingDispatchOptimizer` (Application) wählt das höchst-priorisierte Commitment und gibt dessen `PowerKw` als Setpoint; ohne aktives Commitment fällt er auf Idle zurück. Im Production-DI-Default ersetzt der neue Optimizer den `NoOpDispatchOptimizer`; Test-Hosts können Letzteren weiterhin explizit verdrahten. LH-MKT-003 (Modell) ist mit dem bestehenden `MarketCommitment`-Record bereits seit M1 abgedeckt; LH-MKT-006-Akzeptanz „im Regelkreis nachweisbar" ist mit dem neuen Optimizer-Pfad erfüllt. **Optimierungsintegration** (Strafkosten im LP) bleibt explizit offen: `MarketCommitment.Penalty` wird vom Tracker weiterhin als 0 emittiert; der Penalty-Quellpfad (Schedule-Eintrag, Commitment-Konfiguration, Tarifregel?) und die LP-Slack-Modellierung sind als Folge-Slice unter RM-M2-04 dokumentiert (RM-M2-04-OPT-MARKT). **RegelLeistung-Aktivierung** (Frequency-Response) bleibt bei RM-M4-03; M2-01 nimmt den statischen `PowerKw` aus dem Reserve-Schedule als Setpoint. | LH-MKT-003/006          |
| ✅     | RM-M2-02   | Erweiterte Zeitmodell-Nutzung — `MarketCommitment` trägt jetzt `MarketBidArea` (LH-MKT-007 #6 für Verpflichtungen erfüllt; Vorarbeit für RM-M2-04-OPT-MARKT). `ScheduleTimeGrid.DefaultTimeStep(ScheduleType)` als Domain-Helper deckt LH-MKT-007 #4 (DayAhead=1h, Intraday=15min, RegelLeistungReserve=15min); kein Lock — Caller können jeden positiven TimeSpan wählen. DST-Coverage erweitert: zusätzlich zum bestehenden `Schedule.WindowCovering`-Test über Spring-Forward gibt es jetzt einen `DefaultScheduleTracker`-DST-Test und drei OR-Tools-Pipeline-Tests (Horizon-Step-Count, Bit-exakte Determinismus über DST, Window-Offsets bleiben UTC). M1-Coverage für #1 UTC-Speicherung, #2 ISO-8601-Export und #3 halboffene Intervalle bleibt gültig. **Bewusst draussen** als eigene Folge-Slices: konsolidierter `MarketPriceSeries`-Type mit Preiszone (LH-MKT-007 #6 für Preise selbst — würde alle Optimize-Aufrufer touchen), Display-Timezone für API-Anzeige (LH-MKT-007 #2 für Anzeige — heute UTC-only via ISO-8601), Schedule-Format-Loader mit Timezone-Annotation (LH-MKT-007 #2 für Import — Material wenn ENTSO-E-XML / Day-Ahead-CSV-Import dazukommt). | LH-MKT-007              |
| ✅     | RM-M2-03   | LP-Implementierung des `IScheduleOptimizer` für Horizon-Optimierung (Solver-Auswahl per Config) | LH-OPT-001..009     |
| ✅     | RM-M2-04   | Zielfunktion konfigurierbar — OP-OPEN-02 (M2-minimal `energy_cost`) ist um zwei zusätzliche LP-Komponenten gehoben: `degradation_cost` (linearer Throughput-Proxy für LH-OPT-004 „Batteriealterungskosten") und `soc_target_penalty` (zwei Slack-Variablen pro Schritt für LH-OPT-004 „SOC-Zielabweichung"). Damit sind zwei verschiedene LP-Patterns (gewichtete Beiträge, Hilfsvariablen) am OR-Tools-Adapter verprobt; die Akzeptanz „Zielfunktion ist konfigurierbar oder erweiterbar" ist erfüllt. Konfiguration über `ScheduleSolverOptions.DegradationCost`/`SocTargetPenalty` (DI-Builder), Default bleibt unverändert (nur `energy_cost`). Optimization-Adapter-Tests 48 → 63, Coverage 98.64% line / 99.12% branch / 96.96% method. **Bewusst draussen gelassen** mit jeweils konkretem Trigger und Heimat: **Marktverpflichtungs-Abweichungen** (LH-OPT-004 #4) sind als Folge-Slice *zu RM-M2-04* vorgemerkt (RM-M2-04-OPT-MARKT, kein eigener Top-Level-Liefergegenstand): die Voraussetzung — `MarketCommitment.Penalty` operativ befüllt + Commitment-Lookup für den Optimierungs-Horizont — wird von RM-M2-01 gelegt; sobald das steht, ergänzt der Folge-Slice eine vierte Komponente am OR-Tools-Adapter (`commitment_deviation_penalty`) mit zwei Slack-Variablen pro Commitment-Schritt analog zum `soc_target_penalty`-Pattern. **Reserveverletzung** (#5) braucht ein Reserve-Domain-Modell (FCR/aFRR/mFRR-Produkttyp, Bidirektionalität, Aktivierungsdauer), das im Repo heute nicht existiert; das Reservemodell selbst ist M4-Territorium (RM-M4-02), die LP-Strafkosten-Komponente folgt analog zu Marktverpflichtungs-Abweichungen erst danach. **Peak-Shaving** (#6) ist LP-modellierbar (`peak ≥ p_charge[t]` mit N Constraints) braucht aber operator-seitige Klärung (Peak-Definition: Import-only vs. Netto, Bezugszeitraum vs. Horizon, statisch vs. gestaffelt). | LH-OPT-004              |
| ✅     | RM-M2-05   | Optimierungs-API (`POST /markets/day-ahead/optimize`)               | LH-API-005              |
| ✅     | RM-M2-06   | OpenTelemetry-Tracing für Snapshot → Control → Adapter — drei Span-Boundaries decken die LH-MON-003-Akzeptanz „Optimierung, Snapshot-Erzeugung und Command-Ausgabe nachvollziehbar" ab: `bess.control_cycle.execute` pro Asset+Tick im `ControlCycleHostedService` (umschließt Snapshot-Read, Dispatch, Constraint/Ramp, Command-Emission); `bess.command_dispatch.write` als Child-Span um `IBatteryCommandSink.WriteAsync` (Outcome-Attribut macht Failed-Dispatch-Pfade im Trace sichtbar); `bess.schedule_optimization.run` im `DefaultScheduleOptimizationUseCase` als Top-Level-Span aus dem API-Trigger. Span-Attribute analog LH-MON-001-Logfeldern (`bess.asset_id`, `bess.decision`, `bess.command_mode`, `bess.power_kw`, `bess.run_id`, `bess.solver_status`). OTel-SDK + OTLP-Exporter via neue `AddBessTracing`-Methode in Adapters.Telemetry, OTLP-Endpoint per `OTEL_EXPORTER_OTLP_ENDPOINT` env var (Default leer → Spans laufen ohne Crash ins Leere). ActivitySource-Konstanten in `Application.Observability` (BCL-`System.Diagnostics`, kein OTel-SDK-Coupling im Hexagon). Tests via `ActivityListener` ohne SDK-Dependency. **Bewusst draussen** als eigene Folge-Slices: **Modbus/MQTT-interne Spans** (Adapter-internes Tracing wie „connect"/„write-register-bank") ist Protokoll-Debugging, nicht LH-MON-003-Akzeptanz — `command_dispatch.write` an der Application-Grenze deckt den geforderten Sichtbarkeitsbereich, Adapter-Spans nachziehbar ohne Application-Touch sobald Bedarf besteht; **Npgsql/Dapper-DB-Spans** (NuGet `OpenTelemetry.Instrumentation.Npgsql`) sind separater Slice, nicht LH-MON-003-blockierend; **Sampling-Strategie / Production-Tuning** (Tail-/Probability-Sampling): Default ist 100%-Sampling für M2, Last-abhängiges Tuning ist Operations-Bereich; **Persistierte Trace-IDs in `commands`/`optimization_runs`** würden Schema-Änderung bedeuten und gehören damit zu [`plan-RM-M2-migration.md`](plan-RM-M2-migration.md), Aktivierung sobald dieser Plan zündet. | LH-MON-003              |
| ✅     | RM-M2-07   | Erweiterte Prometheus-Metriken für Solverzeit und Optimierungsläufe | LH-MON-002              |
| ✅     | RM-M2-08   | PID-Regler (.NET) mit Anti-Windup, Output-Clamping, Totband — Domain-Primitive `PidController` (functional `Step(state, options, …)`) mit Conditional-Integration-Anti-Windup, symmetrischem Output-Clamping und optionalem absoluten Totband. LH-CTRL-004 ist „Soll" und der Regelzyklus konsumiert das Primitive bisher nicht; produktive Verdrahtung folgt mit konkreten Konsumenten (PCC-Regelung, Peak-Shaving, Frequenz-Stützung, Export-Begrenzung). | LH-CTRL-004             |
| ✅     | RM-M2-09   | Erweiterte Persistenz für Optimierungsläufe und Solverstatus (`IOptimizationRunRepository`, `OptimizationRun` mit Objective Breakdown) | LH-PERSIST-007          |
| ✅     | RM-M2-10   | Replay-Test-Harness (Telemetrie-Wiedergabe, Command-Vergleich) — Solver-seitiger Replay (LH-OPT-009) ist mit OP-09 erbracht; Telemetrie-Replay-Harness für den Regelzyklus-Pfad ergänzt. **Implementiert:** Fixture-Shape `TelemetryReplayRecord(Timestamp, Telemetry?, ReceivedAt?)` als hardcoded Array in den Tests (kein File-Loader im M2-Scope); `TelemetryReplayHarness`-Utility in `BatteryEms.Application.Tests` baut einen frischen `ControlCycleUseCase` mit deterministischen Dependencies (`FakeClock`, `InMemorySnapshotStore`, `InMemoryScheduleRepository`, `ScheduleFollowingDispatchOptimizer` oder `NoOpDispatchOptimizer`, NoOp-Metrics), pumpt pro Eintrag das Telemetrie in den Snapshot-Store + ruft `ExecuteAsync`, sammelt die emittierten Commands. Vier Tests: Reproduzierbarkeits-Test (Fixture zweimal → bit-exakt identische Sequenz, analog OP-09), Golden-Trace-Test gegen hardcoded `expected: BatteryCommand[]` (fängt Refactoring-Drift im Limiter-/Mode-Verhalten), Missing-Snapshot-Recovery-Test (kein Telemetry → safe-stop „no-snapshot" → fresh Telemetry → normal Dispatch), Stale-Snapshot-Recovery-Test (Telemetry mit `receivedAt > MaxAge` → safe-stop „snapshot-aged-…" → fresh Telemetry → normal Dispatch). **Bewusst draussen** als eigene Folge-Slices: **JSON-File-Loader unter `tests/fixtures/replay/`** für externe Fixtures (Operator-Replay-Workflow) hängt am CLI-Tool-Slice; **CLI-Tool / make-Target für Operator-Replay** (`make replay path/to/fixture.json`) ist operator-facing Tooling, nicht LH-TEST-004-Akzeptanz; **Multi-Asset-Replay-Koordination** ist M3+ (M2 single-asset-fokussiert; ein Coordinator über mehrere `ControlCycleUseCase`-Instanzen würde eigene Logik brauchen); **Compare-against-Production-Replay** (Telemetrie aus der Live-DB ziehen + replayen) verlangt Persistence-Side-Plumbing (`DapperTelemetryRepository` → Stream); **Replay-fähige Adapter-Mocks** für Modbus/MQTT — das Replay läuft unterhalb der Adapter (Snapshot-Store direkt befüllt, Sink ist No-Op-Capture), Adapter-internes Tracing/Replay ist eigener Slice parallel zur RM-M2-06-Carve-out-Logik. | LH-TEST-004             |

### Abnahmekriterien

- Optimierungslauf liefert verifizierbare Zeitreihe von Sollwerten, die
  Limiter nicht verletzt.
- Marktverpflichtungen werden im Regelkreis priorisiert (LH-MKT-006).
- M1-DST-Regressionsfall bleibt grün; Optimierungshorizonte und
  Marktintervalle werden konsistent interpretiert.
- M2-Gates aus `docs/user/quality.md` sind aktiv: `make test-replay`
  läuft gegen versionierte Goldens, und Test-, Coverage- und Lint-Reports
  werden als CI-Artefakte veröffentlicht.

---

## M3 — Native Control Core (Library)

**Ziel:** Phase 2 aus Architektur §13: native Bibliothek
`battery_control_core` für Constraint, Ramp, PID via P/Invoke. .NET-Variante
bleibt Fallback und Referenz.

### Liefergegenstände

| Status | ID         | Inhalt                                                              | LH-Bezug                   |
| ------ | ---------- | ------------------------------------------------------------------- | -------------------------- |
| ✅     | RM-M3-01   | C-ABI `battery_control_core.h` (Snapshot/Limits/Command Structs)    | LH-NATIVE-002/003          |
| ✅     | RM-M3-02   | Implementierung Constraint + Ramp + Statuscode-Fehlerpfade (Sprach-Pivot C++ → C unter RM-M3-09) | LH-NATIVE-001/004 |
| ✅     | RM-M3-03   | ABI-Versionsfunktion + Startup-Check in .NET                        | LH-NATIVE-005              |
| ✅     | RM-M3-04   | P/Invoke-Bindings (`BatteryEms.Adapters.NativeInterop`)              | LH-NATIVE-001              |
| ✅     | RM-M3-05   | Routing: Native bevorzugt, .NET-Fallback bei Fehler/Abwesenheit     | LH-ARCH-006, LH-NF-002     |
| ✅     | RM-M3-06   | Multi-Stage Dockerfile mit Native-Build-Stage (Teil 1 Build-Stage; Teil 2 Runtime-Image-Pfad `/app/native/` + Container-Smoke) | LH-DEPLOY-003/004, LH-NATIVE-006 |
| ✅     | RM-M3-07   | Interop-Tests (Struct Layout, ABI, non-finite-Contract gegen echte `.so`) | LH-TEST-005          |
| ✅     | RM-M3-08   | Native-Unit-Tests via doctest 2.4.11 (FetchContent + URL_HASH SHA256-Pinning) | LH-TEST-001        |
| ✅     | RM-M3-09   | Native-Quality-Gates: `native-lint` (clang-tidy), Sanitizer (ASan + UBSan), `native-coverage-gate` (gcovr, 100 % line) | LH-TEST-005, LH-NATIVE-* |
| ✅     | RM-M3-10   | Native/.NET-Parity-Gate über versionierten Replay-Datensatz `cases.v1.json` (25 Cases) | LH-ARCH-006, LH-TEST-005 |
| ✅     | RM-M3-11   | Makefile-Erweiterung um native Targets (`native-lint`, `test-native-interop`, `test-native-parity`, `native-coverage-gate`, `native-coverage-report`, `native-coverage-exclusions`); `gates`/`ci` ziehen native Gates mit | LH-NATIVE-*, LH-TEST-005 |
| ✅     | RM-M3-12   | Doku-/Contract-Sync fuer Native-Policy und Adaptername (quality.md, architecture.md, roadmap.md, plan-RM-M3.md auf kanonischen Adaptername `BatteryEms.Adapters.NativeInterop`, Header-Pfad `native/battery_control_core/include/battery_control_core.h`, Coverage-Scope `native/battery_control_core/src/`, Port `IControlKernel`, Sprache C; M3-Default-Policy = Managed-Fallback, opt-in `AbortOnAbiMismatch` als Production-Policy synchron) | LH-NATIVE-004/005, LH-OPS-001 |
| ✅     | RM-M3-13   | PID Native-Slice nach stabiler Constraint/Ramp-Parity (ABI 0.1→0.2 additiv, 4 neue Structs, 4 Reason-Codes, neuer Export `battery_control_core_pid_step`, 100 % Coverage, 21 Wire-Tests durch P/Invoke) | LH-NATIVE-001/004, LH-TEST-005 |

### Abnahmekriterien

- Native und .NET-Pfad liefern für Replay-Datensatz identische Commands
  bis auf dokumentierte Toleranzen.
- Fehlende oder inkompatible `.so` führt zu sauberem Fallback, kein
  Crash, geloggter Reason.
- M3-Gates aus `docs/user/quality.md` sind aktiv: Native-Lint,
  Native-Interop, Native-Parity, Sanitizer und Native-Coverage.

---

## M4 — Regelleistung und OPC-UA

**Ziel:** Intraday-Reoptimierung, Regelleistungsreservierung und
-aktivierung, OPC-UA-Adapter über dasselbe Adapter-Interface.

### Liefergegenstände

| Status | ID         | Inhalt                                                              | LH-Bezug                |
| ------ | ---------- | ------------------------------------------------------------------- | ----------------------- |
| ⬜     | RM-M4-01   | Intraday-Reoptimierung (Resthorizont)                               | LH-MKT-002              |
| ✅     | RM-M4-02   | Reservierungs-Modell für Regelleistung + Solver-Constraints — Domain `ReserveProduct`/`ReserveDirection`/`ReserveBand` (FCR↔Symmetric, AFRR/MFRR↔Up oder Down, halboffenes Fenster, PowerKw als Magnitude). Driven Port `IReserveRepository` + `InMemoryReserveRepository`; `ScheduleOptimizationRequest.Reserves` (Default leer ⇒ M2-Pfad bit-identisch); `DefaultScheduleOptimizationUseCase` ruft `FindActive` und reicht durch. OR-Tools deduziert per Step die Caps (Symmetric beidseitig, Up nur Discharge, Down nur Charge); Über-Commit terminiert mit `reserve-exceeds-capacity` statt LP-infeasible. 9 Domain- + 11 OR-Tools-Tests inkl. FCR-symmetrischer Profiltest, AFRR-positiv- und AFRR-negativ-Profiltest, MFRR-Modellierbarkeit, ScheduleType-Theory (DayAhead+Intraday). **Bewusst draußen:** LP-Strafkosten für Reserveverletzung (RM-M2-04-OPT-RESERVE-Folge), persistente Dapper-`IReserveRepository`, API für Reserve-Pflege, Penalty/Pricing — alles eigene Slices wenn realer Konsument das fordert. | LH-MKT-004              |
| ⬜     | RM-M4-03   | Regelleistungs-Aktivierungssignal-Verarbeitung mit Priorisierung    | LH-MKT-005, LH-MKT-006  |
| ⬜     | RM-M4-04   | OPC-UA-Adapter (Lesen, Schreiben, Subscriptions, StatusCode)        | LH-OPCUA-001..004       |
| ⬜     | RM-M4-05   | OPC-UA-Security (Zertifikate, Security Mode/Policy)                 | LH-OPCUA-005            |
| ⬜     | RM-M4-06   | MQTT QoS und Command-ACK-Korrelation                                | LH-MQTT-004/005         |
| ⬜     | RM-M4-07   | Versionierte OPC-UA-Mappings in Config                              | LH-CONF-002             |
| ⬜     | RM-M4-08   | Integrationstests OPC-UA gg. Simulator                              | LH-TEST-003 (n. MVP-Teil) |

### Abnahmekriterien

- Bei aktiver Regelleistungsanforderung übersteuert der Regelkreis den
  normalen Fahrplan, ohne Sicherheitsgrenzen zu verletzen.
- OPC-UA-Adapter integriert sich ohne Änderung der zentralen Regelpipeline.

---

## M5 — MPC, Solver-Sidecar, Replay-Plattform

**Ziel:** Phase 3 aus Architektur §13: native Sidecars für MPC und
Solver-nahe Optimierung; ausgebaute Replay- und Vergleichsplattform.

### Liefergegenstände

| Status | ID         | Inhalt                                                              | LH-Bezug                |
| ------ | ---------- | ------------------------------------------------------------------- | ----------------------- |
| ⬜     | RM-M5-01   | gRPC-Sidecar `optimization-core` (LP/MILP/MPC)                      | LH-OPT-002/003/006      |
| ⬜     | RM-M5-02   | MPC-Kernel (State-Space, Kalman, Vorhersagehorizont)                | LH-CTRL-005/006         |
| ⬜     | RM-M5-03   | Hochfrequente Telemetrie-Filterung im Native Core (optional)        | LH-NATIVE-001           |
| ⬜     | RM-M5-04   | Replay-Plattform mit Datensatz-Verwaltung und Sollwertvergleich     | LH-TEST-004             |
| ⬜     | RM-M5-05   | Erweiterte Metriken / Solverstatus / Command-Latenz                 | LH-MON-002              |
| ⬜     | RM-M5-06   | Container-Orchestrierungstests (Worker + Sidecar)                   | LH-TEST-007             |

### Abnahmekriterien

- MPC-Lauf erzeugt zulässige Trajektorien, die Limiter nicht verletzen.
- Sidecar-Crash beeinträchtigt den Regelkreis nicht; Fallback bleibt
  funktionsfähig.

---

## M6 — Skalierung, UI, Edge / Multi-Asset

**Ziel:** Phase 4 aus Architektur §13: Operator-UI, Multi-Asset-Hosting,
Kubernetes-Deployment, Edge-Anbindung. Inhalte mit hohem Diskussionsbedarf
— Konkretisierung folgt nach M5-Erfahrungswerten.

### Kandidaten

| Status | ID         | Inhalt                                                              | LH-Bezug             |
| ------ | ---------- | ------------------------------------------------------------------- | -------------------- |
| ⬜     | RM-M6-01   | Operator UI (Web)                                                   | LH-OPEN-005          |
| ⬜     | RM-M6-02   | Multi-Asset-Flottensteuerung                                        | §28.3                |
| ⬜     | RM-M6-03   | Kubernetes-Deployment + Helm Charts                                 | §28.3                |
| ⬜     | RM-M6-04   | TimescaleDB-Integration als Persistenz-Erweiterung                  | LH-PERSIST-005, LH-OPEN-006 |
| ⬜     | RM-M6-05   | Edge-Controller-Integration für harte Echtzeitkomponenten           | LH-RISK-001          |
| ⬜     | RM-M6-06   | Zertifizierungsnahe Regelleistungsintegration                       | §28.3                |

---

## Querschnittsthemen

| Thema                       | Anmerkung                                                        |
| --------------------------- | ---------------------------------------------------------------- |
| ADRs                        | Wichtige Entscheidungen unter `docs/plan/adr/` festhalten         |
| Sicherheitsregression       | Sicherheitsfall-Tests laufen ab M1 in jeder CI-Pipeline (LH-TEST-006) |
| Native-Reference-Parität    | .NET-Referenzregler bleibt parallel gepflegt zum Native Core     |
| Konfigurations-Schemata     | JSON-Schemata unter `config/schema/` + Validatoren mitwachsen lassen |
| Vorzeichenkonvention        | In jedem neuen Modul aktiv testen (LH §4.1)                      |

---

## Offene Punkte zur Roadmap

| Kennung    | Frage                                                          | Status |
| ---------- | -------------------------------------------------------------- | ------ |
| RM-OPEN-01 | Konkrete Zeitachse / Kalenderwochen pro Meilenstein?           | Offen  |
| RM-OPEN-02 | Welche Hersteller-Integration zuerst (siehe LH-OPEN-001)?      | Offen  |
| RM-OPEN-03 | Solver-Auswahl für M2 (HiGHS vs. OR-Tools default)?            | Offen  |
| RM-OPEN-04 | Authentifizierung in M1 (API-Token, OIDC)?                     | Geschlossen mit RM-M1-16 — API-Token + Operator-Rolle live; OIDC/mTLS bleiben Folge-ADR. |
| RM-OPEN-05 | Reihenfolge M3 vs. M4 — Native zuerst oder Markt-/RL zuerst?   | Offen  |
| RM-OPEN-06 | Kriterien für spätere API-Extraktion nach dem MVP (siehe AR-OPEN-001)? | Offen  |
| RM-OPEN-07 | Folge-ADR für Release-Pipeline-Gates; vor Abschluss von M1 und vor erstem Tag `v0.1.0` schließen? | Geschlossen mit ADR 0002 — `.github/workflows/release.yml` ist Gate-only vor Publishing; kein freigegebener Tag ohne grünen Release-Workflow. |

---

## Verlinkung

- Lastenheft-Anforderungen: [`spec/lastenheft.md`](../../../../spec/lastenheft.md)
- Architekturentwurf: [`spec/architecture.md`](../../../../spec/architecture.md)
- Qualitäts- und Messpfade: [`docs/user/quality.md`](../../../user/quality.md)
- Archivierte Native-Core-Ideenskizze: [`docs/archive/idea.md`](../../../archive/idea.md)

---

## Wartung dieses Dokuments

- Statusspalten in „Übersicht" und in den Liefergegenstands-Tabellen pro
  Meilenstein nach jedem abgeschlossenen Schritt aktualisieren
  (⬜ → 🟡 → ✅). Verworfene Liefergegenstände auf ⬛ setzen statt
  zu löschen, damit die Roadmap die historische Entscheidung erhält.
- Beim **Aktivieren** eines Meilensteins:
  1. Diese Datei nach `docs/plan/planning/in-progress/roadmap.md`
     verschieben.
  2. Den Detailplan `plan-RM-Mn.md` nach
     `docs/plan/planning/in-progress/plan-RM-Mn.md` verschieben oder dort
     anlegen, falls noch kein offener Entwurf existiert.
  3. Aktive Phase im „Aktueller Stand"-Block eintragen und auf den aktiven
     Detailplan verweisen.
  4. Rückverweise aus anderen Dokumenten auf den neuen Roadmap-Pfad prüfen
     und bei Bedarf aktualisieren.
- Beim **Abschließen** eines Meilensteins:
  1. Status in beiden Tabellen auf ✅ setzen (Übersicht und
     Liefergegenstände).
  2. Den zugehörigen `plan-RM-Mn.md` nach `docs/plan/planning/done/`
     verschieben und in der „Übersicht"-Tabelle in der
     Detailplan-Spalte verlinken.
  3. „Aktueller Stand" auf den nächsten Meilenstein umstellen.
- „Aktueller Stand" wird nach jedem signifikanten Fortschritt neu
  geschrieben, nicht inkrementell — die Liste bleibt kurz.
- Bei Inkonsistenz zwischen Lastenheft (`LH-*`) und Roadmap-Eintrag
  gewinnt das Lastenheft. Die Roadmap wird angepasst; ein Lastenheft-Patch
  erfolgt nur, wenn die normative Anforderung selbst falsch oder veraltet ist.
