# Plan RM-M1: MVP sichere Regelpipeline

**Dokumenttyp:** Detailplan / DoD-Tracking
**Status:** In Arbeit
**Meilenstein:** RM-M1
**Bezug:** [`roadmap.md`](roadmap.md),
[`plan-RM-M1-simulator.md`](plan-RM-M1-simulator.md),
[`spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`spec/architecture.md`](../../../../spec/architecture.md),
[`docs/user/quality.md`](../../../user/quality.md)

---

## Ziel

RM-M1 liefert ein .NET-only `bess-ems`, das Telemetrie aus Modbus/MQTT liest,
Day-Ahead-Fahrpläne und MarketCommitments verarbeitet, sichere Commands
erzeugt und lokal als ein eigenes `bess-ems`-OCI-Image mit integrierter
Worker/API-Komponente, PostgreSQL und MQTT-Broker betreibbar ist.

Dieser Plan trackt die Umsetzungsschritte und die Definition of Done für M1.
Die Roadmap bleibt die Statusseite; dieser Detailplan ist die Arbeitsliste.

---

## Nicht-Ziele

- Kein produktiver LP/MILP/MPC-Solver.
- Kein Native Control Core.
- Kein OPC-UA-Adapter.
- Keine Intraday- oder Regelleistungslogik.
- Keine Operator-UI.
- Keine TimescaleDB-Umstellung.

---

## Sequenz

| Welle | Fokus                       | Ergebnis                                                                                          | Abschluss-Gates                                                          |
| ----- | --------------------------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| 0     | Aktivierung                 | Roadmap und Detailplan liegen unter `in-progress/`; Status ist auf M1 gesetzt.                    | Markdown-Linkcheck, `git diff --check`                                   |
| 1     | Foundation                  | Solution, Makefile, Docker-Stages und Basis-Gates stehen.                                         | `make lint`, `make arch-check`, `make gates` (Foundation-Auswahl)        |
| 2     | Domain und Control          | Domainmodell, Snapshot Store, State Machine, Limiter und Fallbacks sind deterministisch getestet. | `make test`, `make test-safety`, `make coverage-gate`                    |
| 3     | Config und Adapter          | Modbus/MQTT-Mappings, Go-Simulatorpfade und adapterseitige Schreibbegrenzung sind gate-relevant.  | `make simulator-test`, `make test-integration`, Contract-Gates für Mapping und Startvalidierung |
| 4     | Märkte, Persistenz und API  | Day-Ahead, MarketCommitments, UTC/DST-Zeitmodell, PostgreSQL und M1-API sind integriert.          | `make test-integration`, OpenAPI/AuthZ-Gates, `make coverage-gate`       |
| 5     | Observability und Abschluss | Logs, Metriken, Compose, Contract-Gates, Coverage und Fullbuild schließen M1 ab.                  | `make ci`, `make runtime`, `make fullbuild`                              |

Die Wellen sind eine empfohlene Reihenfolge. Innerhalb einer Welle dürfen
Arbeitspakete parallel laufen, solange ihre Abhängigkeiten erfüllt sind.

---

## Aktivierungs-Checkliste

- [x] `docs/plan/planning/open/roadmap.md` nach
  `docs/plan/planning/in-progress/roadmap.md` verschieben.
- [x] `docs/plan/planning/open/plan-RM-M1.md` nach
  `docs/plan/planning/in-progress/plan-RM-M1.md` verschieben.
- [x] Roadmap-Status auf M1 in Arbeit setzen und Detailplan-Link auf
  `in-progress/plan-RM-M1.md` aktualisieren.
- [x] Rückverweise in `docs/user/quality.md`, README und offenen Planlinks
  auf den neuen Roadmap-Pfad prüfen.
- [x] Welle 1 als ersten Umsetzungsumfang festlegen und alle übrigen
  Arbeitspakete unverändert auf ⬜ lassen.
- [x] Baseline-Commit für die Aktivierung erstellen, bevor Code entsteht.

---

## Arbeitspakete

| Status | ID       | Paket                                           | Abhängigkeiten                           | DoD                                                                                                                                                                                                                             |
| ------ | -------- | ----------------------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ✅      | RM-M1-01 | Solution-Skeleton                               | keine                                    | Projekte gemäß Architektur §4.2 / §5.1 (hexagonale Verzeichnisstruktur) sind angelegt, `dotnet build -warnaserror` läuft in der Lint-Stage, gemeinsame Analyzer sind eingebunden.                                               |
| ✅      | RM-M1-22 | Hexagonale Verzeichnis- und Modulstruktur       | RM-M1-01                                 | `src/hexagon/`, `src/adapters/{driving,driven}/`, `src/infrastructure/` sind angelegt; jedes Modul ist Driving/Driven/Composition-Root klassifiziert (siehe §5.1).                                                              |
| ✅      | RM-M1-23 | Boundary-Tests (`BatteryEms.ArchitectureTests`) | RM-M1-22                                 | NetArchTest- oder ArchUnitNET-Suite setzt Dependency Rule und Architektur-Tabus aus §4.2 durch (Domain frameworkfrei, Application ohne Adapter-Refs, keine Adapter-zu-Adapter); `make arch-check` bricht den Build bei Verstoß. |
| ✅      | RM-M1-21 | Makefile-Orchestrierung                         | RM-M1-01                                 | `make help`, `make gates`, `make ci`, `make runtime`, `make fullbuild`, Override-Variablen und Gate-/Report-Trennung sind implementiert; `make gates` zieht `make arch-check` mit.                                              |
| ✅      | RM-M1-02 | Domain-Modell                                   | RM-M1-01                                 | `BatteryAsset`, `BatteryTelemetry`, `BatteryCommand`, `DataQuality`, `MarketCommitment` und Vorzeichenkonvention sind unit-getestet.                                                                                            |
| ✅      | RM-M1-03 | Realtime Snapshot Store                         | RM-M1-02                                 | Datenfusion, Aging, Plausibilisierung, Snapshot-Qualität und stale-Erkennung sind unit-getestet.                                                                                                                                |
| ✅      | RM-M1-04 | State Machine                                   | RM-M1-02                                 | `INIT`, `STANDBY`, `READY`, `IDLE`, `CHARGING`, `DISCHARGING`, `LIMITED`, `FAULT`, `EMERGENCY_STOP`, `MAINTENANCE`, Quittierung und Operator-Stop-Pfade sind abgedeckt.                                                         |
| ✅      | RM-M1-05 | Constraint Limiter                              | RM-M1-02, RM-M1-03                       | SOC-, Power-, Temperatur- und Verfügbarkeitsgrenzen begrenzen Commands deterministisch.                                                                                                                                         |
| ✅      | RM-M1-06 | Ramp Limiter                                    | RM-M1-02                                 | Rampenbegrenzung ist deterministisch, vorzeichenfest und testbar.                                                                                                                                                               |
| ✅      | RM-M1-08 | Optimierungsinterface                           | RM-M1-02                                 | `IDispatchOptimizer`/`NoOpDispatchOptimizer` decken den Echtzeit-Single-Step-Dispatch im 1-s-Regelzyklus ab und sind austauschbar verdrahtet (LH-OPT-007). `IScheduleOptimizer` bleibt Architekturgrenze ohne konkrete M1-Implementierung; produktive Horizon-Optimierung folgt mit RM-M1-F04 / Nach-MVP. |
| ✅      | RM-M1-07 | Regelzyklus/Fallback                            | RM-M1-03..06, RM-M1-08                   | 1-s-Regelzyklus erzeugt bei stale/ungültigem Snapshot einen sicheren Command.                                                                                                                                                   |
| ✅      | RM-M1-18 | Konfiguration/Mappings                          | RM-M1-01, RM-M1-02                       | Config-Loader, JSON-Schemas, Beispiel-Mappings und Startup-Validierung sind Contract-Gates; das Adapter-Mapping-Schema trägt die Pflichtfelder aus Abschnitt „Mapping-Schema-Pflichtfelder".                                    |
| ✅      | RM-M1-09 | Modbus-TCP-Adapter                              | RM-M1-18                                 | Lesen, Schreiben, Mapping, Timeout und Fehlerstatus laufen gegen den Go-Simulator aus [`plan-RM-M1-simulator.md`](plan-RM-M1-simulator.md). Unit-Slice (`ModbusTelemetrySource`/`ModbusCommandSink` + `FluentModbusClient`) plus Integration-Roundtrip via `make test-integration` (docker compose mit Simulator-Sidecar). |
| ✅      | RM-M1-10 | MQTT-Adapter                                    | RM-M1-18                                 | Telemetrieempfang, Command-Publish, ACK-Korrelation und Topic-Konvention laufen gegen Mosquitto und die Go-basierten MQTT-Szenarien aus [`plan-RM-M1-simulator.md`](plan-RM-M1-simulator.md). Unit-Slice (`MqttTelemetrySource`/`MqttCommandSink` + `MqttNetClient`) plus Integration-Roundtrip via `make test-integration` (docker compose mit Mosquitto- und Simulator-Sidecar; SIM-M1-11 Echo-ACK schließt die Korrelation). |
| ✅      | RM-M1-11 | Adapter-Schreibbegrenzung                       | RM-M1-09, RM-M1-10                       | Finale Schreibbegrenzung greift unmittelbar vor Versand für Modbus und MQTT. `Domain.AdapterWriteLimiter` clamped Mode-vs-Power und `[-MaxChargePowerKw, +MaxDischargePowerKw]`; beide Sinks rufen ihn vor Encode/Publish und surfacen den Clamp via `CommandDispatchResult.Reason` (`adapter-limited:*`).         |
| ✅      | RM-M1-12 | Fahrplan, MarketCommitment, Zeitmodell          | RM-M1-02, RM-M1-08                       | Import, Speicherung, UTC/DST-Zeitmodell und Day-Ahead-Verfolgung sind inklusive DST-Regression abgedeckt. `Domain.Schedule` (halboffene `[Start, End)`-Windows in UTC) + `JsonFileConfigurationLoader.LoadSchedule` mit Schema-erzwungenem `Z`-Suffix; `IScheduleRepository`/`InMemoryScheduleRepository` halten den Tagesfahrplan, `IScheduleTracker`/`DefaultScheduleTracker` liefert dem `ControlCycleUseCase` die aktiven `MarketCommitment`s. Fixtures `tests/fixtures/schedules/day-ahead-{basic,dst-transition}.json`; DST-Regression deckt 2026-03-29 (CET→CEST) ab. PostgreSQL-Backing folgt mit RM-M1-13. |
| ✅      | RM-M1-13 | PostgreSQL-Persistenz                           | RM-M1-02, RM-M1-12                       | Telemetrie, Commands, Fahrpläne und Audit werden versioniert gespeichert. `Adapters.Persistence` mit Dapper + Npgsql 9; idempotente `BessDbSchema`-DDL via `BessDbInitializer`; Repos `DapperTelemetryRepository`, `DapperCommandRepository`, `DapperScheduleRepository`, `DapperOperatorAuditLog` implementieren die Application-Ports. Postgres-Sidecar in `tests/integration/compose.yml`; 5 Roundtrip-Integration-Tests beweisen Append, Query (`[from, until)` halboffen), Schedule-Replace-Atomicity, Audit-Append-Only und DDL-Idempotenz. Wiring an `ControlCycleUseCase` folgt mit RM-M1-15/19 (Composition Root). |
| ✅      | RM-M1-14 | Retention-Konfiguration                         | RM-M1-13                                 | Retention- und Datenvolumenparameter sind konfigurierbar, getestet und dokumentiert. `Domain.RetentionPolicy` (4× `TimeSpan?`, null = forever; `AuditPreserved` als sicherer Default) + JSON-Schema/Loader (`config/schema/retention.schema.json`, Beispiel `config/examples/retention.json`). `Application.Persistence.IRetentionRepository` + `RetentionRunUseCase` orchestrieren cutoff-basierte Löschungen; Audit nur bei explizit gesetzter Retention. `Adapters.Persistence.DapperRetentionRepository` setzt das per `DELETE … WHERE recorded_at < @Cutoff` (Schedules via `NOT EXISTS`-Pattern + FK-CASCADE auf `schedule_windows`). 4 Domain- + 4 Application- + 4 Loader-Tests + 1 Postgres-Integrations-Roundtrip. Runbook [`docs/user/persistence.md`](../../../user/persistence.md) dokumentiert Retention, Audit-Sonderfall und Persistenzfehler-Verhalten (LH-PERSIST-006). Periodischer Trigger folgt mit RM-M1-19. |
| ✅      | RM-M1-15 | API                                             | RM-M1-07, RM-M1-12, RM-M1-13             | Health, Status, Current Command, Schedules und Operator-Stop sind als OpenAPI-3.1-Vertrag vorhanden. ASP.NET Core Minimal-API in `BatteryEms.Api` mit `Microsoft.AspNetCore.OpenApi` (Snake-Case-JSON, Enum-Konverter); 15a liefert die Read-Endpoints (`/health`, `/battery/{id}/status`, `/battery/{id}/command/current`, `/markets/schedules/current?assetId=`) gegen die hexagonalen In-Memory-Stores plus 5 Endpoint-Tests via `WebApplicationFactory<Program>`. 15b ergänzt `IOperatorStopRegistry`/`InMemoryOperatorStopRegistry` (Domain.Control), `IOperatorStopUseCase`/`DefaultOperatorStopUseCase` (Application.Api) und den `POST /operator/stop` mit Validierungs-400 + Acknowledgment-200; `ControlCycleUseCase` liest die Registry pro Zyklus und kürzt auf `SafeStop` mit Reason `operator-stop:<reason>` und `CommandSource.Operator`. Production-DI/Worker-Wiring (Dapper-Adapters, Composition Root) folgt mit RM-M1-19; AuthN/AuthZ + Audit landen in RM-M1-16. |
| ✅      | RM-M1-16 | AuthN/AuthZ/Audit                               | RM-M1-15                                 | Schreibende Endpunkte sind geschützt; unberechtigte Zugriffe liefern 401/403 und Audit-Einträge. API-Token-AuthN über `Authorization: Bearer <token>` (`BatteryEms.Api.Auth.ApiTokenAuthenticationHandler`) mit eager geprüfter Token-Tabelle aus `IConfiguration`-Section `ApiTokens` (Token → Operator → Rolle); Operator-Policy via `RequireAuthorization("operator")`. Nur Schreibpfad ist geschützt — Read-Endpunkte folgen Architektur §6 ("read-only intern; produktiv TLS/optional AuthN"). Audit (LH-API-007 + LH-OPS-004) deckt alle vier Outcomes ab: `accepted`/`invalid` aus dem Endpoint (Operator aus Claim, TargetAsset aus Body), `unauthorized`/`forbidden` aus den Challenge-/Forbidden-Hooks des Auth-Handlers (path-gefiltert auf `/operator/stop`). `InMemoryOperatorAuditLog` als M1-Drop-in für `IOperatorAuditLog`; produktive Verkabelung an `DapperOperatorAuditLog` folgt mit RM-M1-19. `OperatorStopRequestBody` enthält den Operator nicht mehr — er stammt aus dem Token, damit ein Caller ihn nicht über den Body fälschen kann. Tests: 5× InMemory-Audit + 7× ApiTokenRegistry + 6× Endpoint (accepted/invalid/401-no-header/401-bad-token/403/200 mit Audit-Verifikation). TLS/Reverse-Proxy bleibt RM-M1-19 / LH-API-008. |
| ✅      | RM-M1-17 | Observability                                   | RM-M1-07, RM-M1-13                       | JSON-Logs mit Reason-Feld und Prometheus-Metriken sind exportierbar und getestet. **JSON-Logs (LH-MON-001):** `Microsoft.Extensions.Logging.Console`-`AddJsonConsole` (UTC-Timestamp + Scopes) zentral via `Api/Observability/LoggingRegistration.ConfigureBessJsonLogging`; `ControlCycleUseCase` erzeugt strukturierte Log-Events (`asset_id`, `mode`, `power_kw`, `decision`, `reason`) über den `LoggerMessage`-Source-Generator. Reason-Feld auf jedem Command bleibt LH-MON-004-konform (war schon Domain-Pflicht). **Prometheus-Metriken (LH-MON-002):** `Application.Observability.IControlCycleMetrics`-Port (framework-frei) + `NoOpControlCycleMetrics`-Default; `BatteryEms.Adapters.Telemetry/Prometheus/PrometheusControlCycleMetrics` mit Histogrammen (`bess_control_cycle_duration_seconds`, `bess_command_latency_seconds`), Countern (`bess_invalid_snapshots_total`, `bess_communication_errors_total`, `bess_safe_stops_total`) und Gauges (`bess_active_power_kw`, `bess_soc_percent`) — Label `asset_id` (+ `reason`/`component`). API-Host serviert `/metrics` direkt über `prometheus-net.AspNetCore` (Default-Process-Metriken); fachliche ControlCycle-Metriken laufen mit dem Worker-Wiring in RM-M1-19 zusammen. **Tracing (LH-MON-003):** explizit *Soll/nach-MVP*, kein M1-Pflichtanteil. Tests: 6× ControlCycle-Spy (Branch-Instrumentierung), 1× NoOp-Smoke, 5× Prometheus-Adapter (Scrape-Roundtrip + Blank-Validation), 2× Telemetry-Registration, 1× `/metrics`-Endpoint im API. Coverage-Gate für Adapters.Telemetry aktiv (92.2 %). Adapter-seitige Kommunikationsfehler-Counter sind als Port verfügbar; Aufrufstellen aus Modbus/MQTT folgen mit RM-M1-19. |
| ✅      | RM-M1-19 | Container/Compose                               | RM-M1-09, RM-M1-10, RM-M1-13, RM-M1-15   | Dockerfile und Compose starten `bess-ems`, PostgreSQL und MQTT-Broker lokal reproduzierbar. **19c (geliefert):** `Adapters.Modbus.ModbusRegistration.AddBessModbus` + `Adapters.Mqtt.MqttRegistration.AddBessMqtt` (DI-Extensions registrieren `IModbusClient`/`IMqttClient` als Singleton, `IBatteryTelemetrySource` + `IBatteryCommandSink` teilen die Connection); `BessHostBuilder` lädt Modbus-/MQTT-Mapping via `JsonFileConfigurationLoader` und wired den passenden Adapter — sonst bleibt der NoOp-Default; `BatteryEms.Worker.TelemetryIngestionHostedService` pumpt `IBatteryTelemetrySource.ReadAsync` in den `ISnapshotStore` (Backoff bei Crash, kein Loop-Stop). `IHealthQuery` erweitert um `Components`-Map; `Adapters.Persistence.DapperHealthQuery` ersetzt die Default-Probe sobald `AddBessPersistence` läuft und prüft Postgres mit `SELECT 1` (Timeout 2 s) → `/health` antwortet 503 + Component-Detail bei DB-Ausfall. `compose.yml` schaltet im Demo-Stack den MQTT-Adapter ein (Mapping + Broker-Hostname); `/health` returnt `{"status":"ok","components":{"database":"ok"}}` im Smoke-Test. Tests: 2× ModbusRegistration + 2× MqttRegistration + 2× TelemetryIngestionHostedService (Drain + Adapter-Failure-Recovery). **19b (geliefert):** Multi-Stage `Dockerfile` mit `publish`- und `runtime`-Stage (`mcr.microsoft.com/dotnet/aspnet:10.0`, non-root `USER app`, `curl`-`HEALTHCHECK` auf `/health`, Port 8080, `dotnet BatteryEms.Host.dll`-Entrypoint); `deploy/compose.yml` (Repo-Root, getrennt vom Test-Compose) startet `bess-ems` + `postgres` + `mosquitto` + `bess-field-sim` lokal reproduzierbar (LH-DEPLOY-001/002/003); `make build` baut das Runtime-Image, `make runtime` (Alias `make test-container`) fährt den Compose-Stack mit `--wait` hoch, probt `/health` und räumt wieder auf. `IScheduleTracker` wurde im DI-Wiring nachgezogen (war im InMemory-Pfad noch nicht registriert). Beispiel-`appsettings.json` im Host-Project setzt sichere Defaults (Schema-/Asset-Pfade, Worker-CycleInterval=1 s, leere Token-Liste); `compose.yml` setzt Postgres-Connection-String + Demo-Operator-Token via Environment.

**19a (geliefert):** `BatteryEms.Host` (neuer Composition-Root-Project, einziger Ort mit Kreuz-Layer-Refs); `BatteryEms.Worker.ControlCycleHostedService` (1-Hz `PeriodicTimer`-Loop mit Fan-out über `IBatteryAssetRegistry.GetAll()`, Sink-Aufruf, `ICommandRepository.AppendAsync`, Communication-Error-Counter bei Adapter-/Use-Case-Fehlern; Loop läuft weiter); `WorkerOptions` (Section `Worker`, Default 1 s); `WorkerRegistration.AddBessWorker`; `Adapters.Persistence.PersistenceRegistration.AddBessPersistence` (NpgsqlDataSource + Repos + DDL-Initializer); `Adapters.Optimization.OptimizationRegistration.AddBessOptimization`; `Application.IO.NoOp{BatteryTelemetrySource,BatteryCommandSink}` als sicherer Default (`SafeStop` wegen `no-snapshot`, Sink quittiert ohne Hardware) bis 19c die echten Modbus/MQTT-Adapter wired; `BessHostBuilder.BuildApp` lädt Asset/Schedule/Retention via `JsonFileConfigurationLoader` beim Start und seedet die Registry — fehlende Pflichtkonfig bricht den Start (LH-OPS-001 + LH-CONF-003); Postgres-DDL läuft eager beim Start, wenn `Bess:PersistenceConnectionString` gesetzt ist. arch-check: neuer `HostNamespace` als verbotene Referenz für Domain/Application/Driving-/Driven-Adapter — Host darf alles. Coverage-Gate für `BatteryEms.Worker` aktiv (93.5 %). Tests: 3× Hosted-Service (Fan-out, Sink-Failure, Cycle-Exception keep-running), 3× WorkerRegistration (DI-resolve, Null-args, Default-CycleInterval), 2× NoOp-Adapter (Telemetry-Empty + Sink-Ack). **Offen für 19b:** Runtime-Image-Stage + repo-root `compose.yml` + `make build`/`make runtime`/`make test-container`. **Offen für 19c:** echte Modbus/MQTT-Wiring (Mapping-File-Loader im Host) und `/health`-Probes (DB + Adapter). **Auf RM-M1-20:** Aggregat-Gates `make ci` / `make fullbuild` (siehe Gates-Tabelle). |
| ⬜      | RM-M1-20 | Quality-Gates                                   | wächst je Welle; Abschluss nach RM-M1-19 | Lint (inkl. Code-Metriken via CA1501/1502/1505/1506 in `.editorconfig`), Unit, Safety, Integration, Contract, Container und Coverage laufen reproduzierbar grün. Jede Welle aktiviert ihre Gates sofort; fehlende Gates werden nicht bis zum M1-Ende aufgeschoben.                                 |

---

## Modulziele

Modulnamen folgen Architektur §4.2 / §5.1 (driving/driven). Application-
interne Funktionsbereiche (Realtime, Control, Markets, Optimization-IF)
leben in M1 als Namespaces innerhalb von `BatteryEms.Application`.

| Modul / Pfad                                           | Hexagon-Klasse      | Erwartung in M1                                                                                                                    |
| ------------------------------------------------------ | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `src/hexagon/BatteryEms.Domain`                        | Hexagon-Kern        | Entitäten, Wertebereiche, Vorzeichenkonvention, MarketCommitments, Limiter, State Machine — frameworkfrei.                         |
| `src/hexagon/BatteryEms.Application`                   | Hexagon-Application | Use Cases, Driving + Driven Ports, Snapshot Store, Aging, Markt-/Fahrplanauflösung. Optimierungs-Interface in M1: `IDispatchOptimizer` (Echtzeit-Single-Step im Regelzyklus); `IScheduleOptimizer` ist Architekturgrenze ohne M1-Implementierung (LH-OPT-007). |
| `src/adapters/driving/BatteryEms.Api`                  | Driving Adapter     | M1-Endpunkte, OpenAPI 3.1, AuthZ-Negativtests, Operator-Stop.                                                                      |
| `src/adapters/driving/BatteryEms.Worker`               | Driving Adapter     | Hosted Service, 1-s-Regelzyklus, Adapter-Wiring über DI.                                                                           |
| `src/adapters/driven/BatteryEms.Adapters.Modbus`       | Driven Adapter      | Modbus-TCP Lese-/Schreibpfad mit Mapping und Simulator-Tests.                                                                      |
| `src/adapters/driven/BatteryEms.Adapters.Mqtt`         | Driven Adapter      | MQTT-Telemetrie, Command-Publish, Mosquitto-Integration.                                                                           |
| `src/adapters/driven/BatteryEms.Adapters.Persistence`  | Driven Adapter      | PostgreSQL-Schema, Repositories, Retention, Audit.                                                                                 |
| `src/adapters/driven/BatteryEms.Adapters.Telemetry`    | Driven Adapter      | JSON-Logging, Prometheus-Metrikexport.                                                                                             |
| `src/adapters/driven/BatteryEms.Adapters.Optimization` | Driven Adapter      | `NoOpDispatchOptimizer` für den Echtzeit-Single-Step im Regelzyklus; kein produktiver Schedule-Optimizer/Solver in M1 (LH-OPT-007 Architekturgrenze).                                                                            |
| `src/infrastructure/BatteryEms.Infrastructure`         | Composition Root    | DI-Wiring, Config-Loader, Healthchecks, Startup-Validierung.                                                                       |
| `simulators/bess-field-sim`                            | Blackbox-Simulator  | Go-Service fuer Modbus/MQTT-Feldgeraetesimulation; koppelt ueber Schemas und Fixtures, nicht ueber C#-Domainklassen.              |
| `tests/BatteryEms.ArchitectureTests`                   | Test-Modul          | Boundary-Tests für Dependency Rule und Architektur-Tabus aus §4.2.                                                                 |
| `config/schema/`                                       | Configuration       | JSON-Schemas für Asset, Limits, Modbus- und MQTT-Mappings.                                                                         |
| `config/examples/`                                     | Configuration       | Validierbare Beispielkonfigurationen und Mapping-Fixtures.                                                                         |

---

## Mapping-Schema-Pflichtfelder

Das Adapter-Mapping-Schema in RM-M1-18 muss die folgenden Felder pro
Register- bzw. Topic-Eintrag tragen, damit reale Vendor-Profile post-M1
ohne Schemabruch abbildbar sind. Die Felder werden bereits in den
herstellerneutralen Simulatorprofilen aus M1 verlangt; Beispielprofile
ohne diese Felder verletzen das Contract-Gate.

| Feld                  | Pflicht                                           | Werteform                                                           | Zweck                                                                                                                                                                                                                                                    |
| --------------------- | ------------------------------------------------- | ------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `address`             | ja                                                | int / string (für MQTT-Topic)                                       | Adress-/Topic-Identifikation.                                                                                                                                                                                                                            |
| `type`                | ja                                                | enum: `uint16 \| int16 \| uint32 \| int32 \| float32 \| string`     | Decode-Layer.                                                                                                                                                                                                                                            |
| `scale_factor`        | ja                                                | numeric (Default 1)                                                 | Roh-zu-Engineering-Skalierung.                                                                                                                                                                                                                           |
| `range`               | ja                                                | `[min, max]`                                                        | Plausibilisierung, stale-/invalid-Erkennung.                                                                                                                                                                                                             |
| `writable`            | ja                                                | bool                                                                | Trennung Lese-/Schreibpfad, AuthZ-Eingriffspunkt.                                                                                                                                                                                                        |
| `enum`                | optional                                          | Mapping `int → string`                                              | Symbolische Zustände (z. B. State-Codes).                                                                                                                                                                                                                |
| `write_cadence`       | ja                                                | enum: `cyclic \| once_per_day \| one_shot \| heartbeat \| cooldown` | Hardware-Schutz und Liveness-Pflichten. Belege: SMA Sunny Island (`cyclic`/`once_per_day`), Socomec SunSpec Model 715 (`heartbeat` jede Sekunde), Socomec Model 802 DISCONNECT (`cooldown` 5 Minuten). Quellen siehe `../open/note-vendor-shortlist.md`. |
| `auth_required`       | ja                                                | enum: `none \| network \| token`                                    | Vendor-Auth-Vorbedingung für Schreibzugriffe. Belege: Victron (`none`), Socomec (`network` — Firewall/IP-Allowlist), SMA Sunny Island (`token` — Grid Guard).                                                                                            |
| `firmware_constraint` | optional                                          | Min-Firmware-Filter                                                 | Firmwareabhängige Datentyp-/Skalierungswechsel (z. B. Sungrow SH-Serie).                                                                                                                                                                                 |
| `unit_id_discovery`   | ja, auf Adapter-Ebene                             | enum: `static \| dynamic \| sunspec`                                | Bedient feste Unit-IDs (SMA), dynamische Zuweisung (Victron CCGX/Cerbo) und SunSpec-Anker-Discovery (Socomec).                                                                                                                                           |
| `sunspec_model`       | optional, Pflicht bei `unit_id_discovery=sunspec` | Modell-ID (z. B. `1`, `701`, `704`, `715`, `802`, `803`)            | Erlaubt einem Adapter, ein Mapping direkt gegen die SunSpec-Spezifikation aufzulösen statt gegen vendor-spezifische Adressen.                                                                                                                            |

Die Pflichtfelder gehören in das JSON-Schema unter `config/schema/`. Die
Beispielmappings unter `config/examples/adapters/` müssen alle
Pflichtfelder befüllen; die Startup-Validierung lehnt unvollständige
Mappings ab. Hintergrund und Quellenlage stehen in
[`../open/note-vendor-shortlist.md`](../open/note-vendor-shortlist.md).

---

## Spezifikations-Folgepunkte

Die folgenden Punkte halten Nacharbeiten aus der geschärften Spezifikation
fest. Bereits implementierte M1-Flächen bekommen Anpasspunkte; noch nicht
implementierte Flächen werden als neue Folgepunkte geführt, ohne damit
automatisch produktiven Optimierungs- oder Export-Scope in M1 zu ziehen.

| Status | ID          | Scope            | Punkt                                      | Bezug                       | DoD |
| ------ | ----------- | ---------------- | ------------------------------------------ | --------------------------- | --- |
| ✅      | RM-M1-F01   | M1-Anpassung     | Dispatch-Optimizer-Begriffe schärfen       | RM-M1-08, LH-OPT-007        | Plan, Modulziele und Roadmap benennen `IDispatchOptimizer`/`NoOpDispatchOptimizer` klar als Echtzeit-Dispatch im Regelzyklus; `IScheduleOptimizer` bleibt als Architekturgrenze ohne konkrete M1-Implementierung ausgewiesen. |
| ✅      | RM-M1-F02   | M1-Anpassung     | Persistenzstatus in Roadmap nachziehen     | RM-M1-13, LH-PERSIST-001..007 | Roadmap-Status und M1-Liefergegenstand für RM-M1-13 spiegeln den implementierten Dapper/Npgsql-Stand inklusive Integrationstest-Scope; noch offenes DI-/Runtime-Wiring bleibt RM-M1-15/19 zugeordnet. |
| ✅      | RM-M1-F03   | neuer M1-Punkt   | Gerätepunkt-Basis in Adapter-Mappings      | RM-M1-18, LH-DOM-005, LH-CONF-002 | `config/schema/device-point.schema.json` definiert `device_point_base` ($defs) mit Schlüssel (required), Anzeigename, Einheit, Exportfähigkeit, Alarm-Schwellen und Wert­erklärung. Modbus- und MQTT-Mapping-Schemas binden diese Basis via `allOf + $ref + unevaluatedProperties:false` ein. `Application.Configuration.DevicePointMetadata` (+ `DevicePointAlarm`) hängt als optionale `init`-Property an `ModbusRegisterMapping`/`MqttTopicMapping`; `JsonFileConfigurationLoader` registriert das Sub-Schema in der `SchemaRegistry` und befüllt die DTOs. Beispiel-Mappings tragen `display_name`/`unit` für die wichtigsten Telemetrie-Punkte; `temperature_celsius` zeigt den Alarm-Pfad. Loader-Tests decken: Metadaten-Roundtrip, fehlender `name` schlägt am Schema fehl, `value_explanation` als protokollneutrale Werteerklärung, unbekannte Felder werden weiterhin abgewiesen. |
| ✅      | RM-M1-F04   | nach-MVP-Punkt   | Schedule-Optimizer und Run-Persistenz planen | LH-OPT-001/007/009, LH-PERSIST-007 | [`../open/plan-RM-M2-optimization.md`](../open/plan-RM-M2-optimization.md) ist der offene Detailplan für `IScheduleOptimizer`-Implementierung, `OptimizationRun`/`IOptimizationRunRepository`, Solverstatus und Objective-Breakdown. M1 bleibt bei Import/Tracking von Fahrplänen und Echtzeit-Dispatch; die Roadmap-Sektion M2 verweist auf den Plan. |

---

## Gate-Matrix

Die `Status`-Spalte spiegelt den **Tagesstand** (✅ aktiv, ⏳ teilweise
scharf, ⬜ pending). Die `Muss prüfen`-Spalte beschreibt den
**M1-Zielzustand** — sie wandert nicht mit, wenn ein Gate erst teilweise
scharf ist; stattdessen verweist die Status-Spalte und ggf. die Detail-
zeile selbst auf den aktuellen Scope.

| Status | Gate                           | Muss prüfen                                                                                                                                                                                                                                 | M1-Bezug                                |
| :----: | ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------- |
| ✅     | `make lint`                    | Build mit Warnungen als Fehler plus Code-Metriken-/SOLID-Gate via `Microsoft.CodeAnalysis.NetAnalyzers` (`AnalysisLevel=latest-all` in `Directory.Build.props`, Severities in `.editorconfig`): CA1501/1502/1505/1506 (SRP/Maintainability), CA1000/1001/1012/1033/1040/1715 (OCP/LSP/ISP/DIP). | RM-M1-01, RM-M1-20, RM-M1-21            |
| ✅     | `make arch-check`              | Dependency Rule und Architektur-Tabus aus Architektur §4.2 (Boundary-Tests)                                                                                                                                                                 | RM-M1-22, RM-M1-23                      |
| ✅     | `make test`                    | Domain, Control, Zeitmodell, Snapshot, Vorzeichenkonvention                                                                                                                                                                                 | RM-M1-02..08, RM-M1-12                  |
| ✅     | `make test-safety`             | Emergency Stop, stale Snapshot, ungültige Daten, SOC-/Power-Grenzen, Schreiblimit                                                                                                                                                           | RM-M1-04..07, RM-M1-11                  |
| ✅     | `make simulator-test`          | Go-Simulator baut, Szenario-Fixtures und DTO-/Schema-Verträge sind gültig                                                                                                                                                                   | RM-M1-09, RM-M1-10, RM-M1-18            |
| ✅     | `make simulator-lint`          | golangci-lint v2 mit SOLID-leaning Profil + depguard-Boundary-Regeln (model/scenario halten Protokoll-/Runtime-Imports raus)                                                                                                                | RM-M1-09, RM-M1-10, RM-M1-20            |
| ✅     | `make simulator-race`          | `go test -race` (`CGO=1`) auf den Goroutine-tragenden Paketen `internal/{modbus,mqtt,runtime}`                                                                                                                                              | RM-M1-09, RM-M1-10, RM-M1-20            |
| ✅     | `make simulator-coverage-gate` | 90 Prozent Line-Coverage für die Go-Simulator-Pakete (`internal/...`)                                                                                                                                                                       | RM-M1-09, RM-M1-10, RM-M1-20            |
| ⏳     | `make test-integration`        | Modbus, MQTT, PostgreSQL, API über Testserver. Heute scharf: Modbus-Roundtrip (RM-M1-09), MQTT-Roundtrip (Telemetry + Command/ACK, RM-M1-10) und Persistenz-Roundtrips (Telemetry/Command/Schedule/Audit, RM-M1-13) via docker compose mit Mosquitto-, Go-Simulator- und Postgres-Sidecar. API folgt mit RM-M1-15.                                                | RM-M1-09, RM-M1-10, RM-M1-13, RM-M1-15  |
| ✅     | `make test-container`          | Runtime-Image, Compose-Start, Healthcheck (Alias `make runtime` in M1-19b; aktiviert via `BatteryEms.Host`-Image, `deploy/compose.yml`, /health-Probe).                                                                                       | RM-M1-19                                |
| ✅     | `make coverage-gate`           | 90 Prozent Line-Coverage für die M1-.NET-Module (Domain, Application, Infrastructure, Adapter mit Produktivcode)                                                                                                                            | RM-M1-20                                |
| ✅     | `make build`                   | Runtime-Image ohne SDK, nicht-root User, Port 8080, Healthcheck. Multi-Stage Dockerfile (`publish` + `runtime`), `mcr.microsoft.com/dotnet/aspnet:10.0`-Basis, USER `app` (1654), `curl`-basierter `HEALTHCHECK` gegen `/health`.            | RM-M1-19                                |
| ✅     | `make gates`                   | Aggregiert alle scharfen M1-Pflichtgates ohne Report-Erzeugung                                                                                                                                                                              | RM-M1-21                                |
| ⬜     | `make ci`                      | CI-kompatibler Lauf der verbindlichen M1-Gates in dokumentierter Reihenfolge                                                                                                                                                                | RM-M1-20, RM-M1-21                      |
| ✅     | `make runtime`                 | Runtime-Smoke: Compose-Start, `/health`-Prüfung und Shutdown. `deploy/compose.yml` startet `bess-ems` + `postgres` + `mosquitto` + `bess-field-sim` mit `--wait`/`--wait-timeout 60`; `curl /health` aus dem Container heraus; `down -v --remove-orphans` zum Schluss.                                                                                                                                                                                | RM-M1-19, RM-M1-21                      |
| ⬜     | `make fullbuild`               | Fresh-clone-naher Komplettlauf inkl. Gates, Build und Runtime-Smoke                                                                                                                                                                         | RM-M1-20, RM-M1-21                      |

### Gate-Aktivierung nach Wellen

| Welle | Neu verpflichtend                                                                  | Darf noch fehlen                                            |
| ----- | ---------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| 1     | `make lint`, `make arch-check`, `make gates` mit Foundation-Auswahl                | fachliche Adapter, Persistenz, API, Runtime-Smoke           |
| 2     | `make test`, `make test-safety`, Coverage für Domain/Application                   | Adapter-Integration, OpenAPI, Container-Smoke               |
| 3     | `make simulator-{lint,test,race,coverage-gate}`, `make test-integration` für Modbus, MQTT und Startvalidierung; Mapping-Schema-Gate | API-Vertrag, Persistenzvollständigkeit, Runtime-Smoke       |
| 4     | API-/AuthZ-/OpenAPI-Gates, Persistenztests, Zeitmodell-/DST-Regressionsfälle       | Fullbuild und Runtime-Smoke als Abschlussgate               |
| 5     | `make ci`, `make runtime`, `make fullbuild`, vollständiges Coverage-Gate           | nichts; offene Abweichungen brauchen ADR oder Roadmap-Patch |

Gates werden mit ihrer Aktivierungswelle in `make gates` und `make ci`
eingehängt. Temporär nicht aktive Gates müssen im Makefile sichtbar bleiben
und mit einer klaren Meldung auf ihre Aktivierungswelle verweisen.

Vertrags-Gates:

- [ ] OpenAPI-Vertrag ist wohlgeformt und deckt alle M1-Endpunkte ab.
- [x] AuthZ-Negativtests für schreibende Endpunkte liefern 401/403.
- [ ] Adapter-Mapping-Schemas validieren alle Beispiele unter `config/examples/`.
- [ ] Vorzeichenkonvention ist durch dedizierte Tests abgesichert.
- [ ] Ungültige Startkonfiguration verhindert aktiven Regelbetrieb.
- [ ] Metrikexport ist in Tests abrufbar und enthält die M1-Pflichtmetriken.
- [ ] Hexagonale Architektur-Tabus aus §4.2 sind durchgesetzt:
  Domain frameworkfrei, Application ohne Adapter-Refs, keine
  Adapter-zu-Adapter-Referenzen, Infrastructure als Composition Root.

---

## Akzeptanzdaten

| Datensatz                                                | Mindestinhalt                                                                                                                                                                                                                                                          | Zweck                                                                                           |
| -------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `config/examples/asset.single-bess.json`                 | Ein BatteryAsset mit SOC-, Power-, Temperatur- und Rampengrenzen                                                                                                                                                                                                       | Startup- und Mapping-Validierung                                                                |
| `config/examples/adapters/modbus.simulator.json`         | Registerprofil für SOC, Power, Temperatur, Verfügbarkeit und Command-Write; herstellerneutral, alle Pflichtfelder aus „Mapping-Schema-Pflichtfelder" befüllt                                                                                                           | Modbus-Integration, Validierung des vendor-spezifischen Pfads                                   |
| `config/examples/adapters/modbus.sunspec-simulator.json` | SunSpec-konformes Profil mit `unit_id_discovery=sunspec` und mindestens den Modellen 1 (Common), 701 (DER AC Measurement), 704 (DER AC Controls), 715 (DER Ctl, inkl. `write_cadence=heartbeat`) und 802 (Battery Base, inkl. `write_cadence=cooldown` für DISCONNECT) | SunSpec-Pfad-Validierung; deckt Querschnittsstrategie aus `../open/note-vendor-shortlist.md` ab |
| `config/examples/adapters/mqtt.simulator.json`           | Topics für Telemetrie, Command und Command-ACK                                                                                                                                                                                                                         | MQTT-Integration                                                                                |
| `tests/fixtures/schedules/day-ahead-basic.json`          | 24h-Day-Ahead-Fahrplan in UTC                                                                                                                                                                                                                                          | Fahrplanimport und Regelkreis                                                                   |
| `tests/fixtures/schedules/day-ahead-dst-transition.json` | DST-Übergang mit eindeutigen UTC-Zeitpunkten                                                                                                                                                                                                                           | Zeitmodell-Regression                                                                           |
| `tests/fixtures/telemetry/safe-fallback.json`            | stale Snapshot, invalid SOC, BMS-Ausfall, Operator-Stop                                                                                                                                                                                                                | Safety-Tests                                                                                    |

Die Dateinamen sind Zielkonventionen. Wenn die spätere Implementierung andere
Formate braucht, muss dieser Abschnitt mit dem jeweiligen PR angepasst werden.
Die Simulator-Szenarien und Protokollanforderungen sind in
[`plan-RM-M1-simulator.md`](plan-RM-M1-simulator.md) beschrieben.

---

## Abnahmekriterien

- [ ] `docker compose up` startet das Gesamtsystem lokal.
- [ ] Simulierter BMS/Wechselrichter liefert Modbus/MQTT-Telemetrie.
- [ ] Das System publiziert Commands ohne SOC-, Power- oder Rampengrenzen zu verletzen.
- [ ] Stale Snapshot, Emergency Stop und Operator-Stop führen in den sicheren Zustand.
- [ ] Audit-Log und Reason-Felder machen Stop- und Fallback-Entscheidungen nachvollziehbar.
- [ ] `make arch-check` setzt Architektur-Tabus aus Architektur §4.2 durch
  und bricht den Build bei Verstoß.
- [ ] Day-Ahead-Fahrplan kann importiert, gespeichert und mit UTC/DST-Zeitmodell verfolgt werden.
- [ ] Health, Status, Current Command, Schedules und Operator-Stop sind über API nutzbar.
- [ ] Metriken sind exportierbar und in Tests validiert.
- [ ] `make ci` und `make runtime` laufen reproduzierbar grün.
- [ ] Alle M1-Gates aus der Gate-Matrix sind reproduzierbar grün.

---

## Blocker und Entscheidungen

| Kennung    | Entscheidung                                                                   | Blockiert ab                  | Default, falls nicht entschieden                                                                                        | Status |
| ---------- | ------------------------------------------------------------------------------ | ----------------------------- | ----------------------------------------------------------------------------------------------------------------------- | ------ |
| RM-OPEN-02 | Erste Herstellerintegration und daraus folgende Modbus-/MQTT-Beispielprofile   | RM-M1-09, RM-M1-10            | Herstellerneutrale Simulatorprofile bleiben verbindlich; reale Herstellerprofile werden nach M1 als Folgepaket geplant. | Offen  |
| RM-OPEN-04 | Authentifizierungsverfahren: API-Token, OIDC oder mTLS                         | RM-M1-15, spätestens RM-M1-16 | API-Token mit rollenbasierter Operator-Rolle für M1; OIDC/mTLS bleiben Erweiterung nach ADR.                            | Geschlossen mit RM-M1-16 (Bearer-Token + Operator-Policy live; OIDC/mTLS bleiben Folge-ADR) |
| RM-OPEN-07 | Release-Pipeline-Gates vor M1-Abschluss und erstem Tag `v0.1.0` konkretisieren | RM-M1-20 Abschluss            | Kein `v0.1.0`-Tag ohne Folge-ADR; M1 darf ohne Release-Tag abgeschlossen werden.                                        | Offen  |

Ein Blocker darf nur in einen späteren Meilenstein verschoben werden, wenn
Roadmap, Lastenheft-Bezug und betroffene Gates im gleichen PR angepasst werden.

---

## Abschlussbedingung

RM-M1 ist abgeschlossen, wenn alle Arbeitspakete erledigt, alle Gates aus der
Gate-Matrix grün, alle M1-Abnahmekriterien erfüllt und alle M1-Blocker
geschlossen oder explizit in einen späteren Meilenstein verschoben sind.
