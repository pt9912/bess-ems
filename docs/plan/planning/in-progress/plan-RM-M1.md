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
| ✅      | RM-M1-08 | Optimierungsinterface                           | RM-M1-02                                 | `IDispatchOptimizer` und `NoOpDispatchOptimizer` sind austauschbar verdrahtet.                                                                                                                                                  |
| ✅      | RM-M1-07 | Regelzyklus/Fallback                            | RM-M1-03..06, RM-M1-08                   | 1-s-Regelzyklus erzeugt bei stale/ungültigem Snapshot einen sicheren Command.                                                                                                                                                   |
| ⬜      | RM-M1-18 | Konfiguration/Mappings                          | RM-M1-01, RM-M1-02                       | Config-Loader, JSON-Schemas, Beispiel-Mappings und Startup-Validierung sind Contract-Gates; das Adapter-Mapping-Schema trägt die Pflichtfelder aus Abschnitt „Mapping-Schema-Pflichtfelder".                                    |
| ⬜      | RM-M1-09 | Modbus-TCP-Adapter                              | RM-M1-18                                 | Lesen, Schreiben, Mapping, Timeout und Fehlerstatus laufen gegen den Go-Simulator aus [`plan-RM-M1-simulator.md`](plan-RM-M1-simulator.md).                                                                                      |
| ⬜      | RM-M1-10 | MQTT-Adapter                                    | RM-M1-18                                 | Telemetrieempfang, Command-Publish, ACK-Korrelation und Topic-Konvention laufen gegen Mosquitto und die Go-basierten MQTT-Szenarien aus [`plan-RM-M1-simulator.md`](plan-RM-M1-simulator.md).                                      |
| ⬜      | RM-M1-11 | Adapter-Schreibbegrenzung                       | RM-M1-09, RM-M1-10                       | Finale Schreibbegrenzung greift unmittelbar vor Versand für Modbus und MQTT.                                                                                                                                                    |
| ⬜      | RM-M1-12 | Fahrplan, MarketCommitment, Zeitmodell          | RM-M1-02, RM-M1-08                       | Import, Speicherung, UTC/DST-Zeitmodell und Day-Ahead-Verfolgung sind inklusive DST-Regression abgedeckt.                                                                                                                       |
| ⬜      | RM-M1-13 | PostgreSQL-Persistenz                           | RM-M1-02, RM-M1-12                       | Telemetrie, Commands, Fahrpläne und Audit werden versioniert gespeichert.                                                                                                                                                       |
| ⬜      | RM-M1-14 | Retention-Konfiguration                         | RM-M1-13                                 | Retention- und Datenvolumenparameter sind konfigurierbar, getestet und dokumentiert.                                                                                                                                            |
| ⬜      | RM-M1-15 | API                                             | RM-M1-07, RM-M1-12, RM-M1-13             | Health, Status, Current Command, Schedules und Operator-Stop sind als OpenAPI-3.1-Vertrag vorhanden.                                                                                                                            |
| ⬜      | RM-M1-16 | AuthN/AuthZ/Audit                               | RM-M1-15                                 | Schreibende Endpunkte sind geschützt; unberechtigte Zugriffe liefern 401/403 und Audit-Einträge.                                                                                                                                |
| ⬜      | RM-M1-17 | Observability                                   | RM-M1-07, RM-M1-13                       | JSON-Logs mit Reason-Feld und Prometheus-Metriken sind exportierbar und getestet.                                                                                                                                               |
| ⬜      | RM-M1-19 | Container/Compose                               | RM-M1-09, RM-M1-10, RM-M1-13, RM-M1-15   | Dockerfile und Compose starten `bess-ems`, PostgreSQL und MQTT-Broker lokal reproduzierbar.                                                                                                                                     |
| ⬜      | RM-M1-20 | Quality-Gates                                   | wächst je Welle; Abschluss nach RM-M1-19 | Lint (inkl. Code-Metriken via CA1501/1502/1505/1506 in `.editorconfig`), Unit, Safety, Integration, Contract, Container und Coverage laufen reproduzierbar grün. Jede Welle aktiviert ihre Gates sofort; fehlende Gates werden nicht bis zum M1-Ende aufgeschoben.                                 |

---

## Modulziele

Modulnamen folgen Architektur §4.2 / §5.1 (driving/driven). Application-
interne Funktionsbereiche (Realtime, Control, Markets, Optimization-IF)
leben in M1 als Namespaces innerhalb von `BatteryEms.Application`.

| Modul / Pfad                                           | Hexagon-Klasse      | Erwartung in M1                                                                                                                    |
| ------------------------------------------------------ | ------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `src/hexagon/BatteryEms.Domain`                        | Hexagon-Kern        | Entitäten, Wertebereiche, Vorzeichenkonvention, MarketCommitments, Limiter, State Machine — frameworkfrei.                         |
| `src/hexagon/BatteryEms.Application`                   | Hexagon-Application | Use Cases, Driving + Driven Ports, Snapshot Store, Aging, Markt-/Fahrplanauflösung, Optimierungs-Interface (`IDispatchOptimizer`). |
| `src/adapters/driving/BatteryEms.Api`                  | Driving Adapter     | M1-Endpunkte, OpenAPI 3.1, AuthZ-Negativtests, Operator-Stop.                                                                      |
| `src/adapters/driving/BatteryEms.Worker`               | Driving Adapter     | Hosted Service, 1-s-Regelzyklus, Adapter-Wiring über DI.                                                                           |
| `src/adapters/driven/BatteryEms.Adapters.Modbus`       | Driven Adapter      | Modbus-TCP Lese-/Schreibpfad mit Mapping und Simulator-Tests.                                                                      |
| `src/adapters/driven/BatteryEms.Adapters.Mqtt`         | Driven Adapter      | MQTT-Telemetrie, Command-Publish, Mosquitto-Integration.                                                                           |
| `src/adapters/driven/BatteryEms.Adapters.Persistence`  | Driven Adapter      | PostgreSQL-Schema, Repositories, Retention, Audit.                                                                                 |
| `src/adapters/driven/BatteryEms.Adapters.Telemetry`    | Driven Adapter      | JSON-Logging, Prometheus-Metrikexport.                                                                                             |
| `src/adapters/driven/BatteryEms.Adapters.Optimization` | Driven Adapter      | `NoOpDispatchOptimizer`; kein produktiver Solver in M1.                                                                            |
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

## Gate-Matrix

| Gate                    | Muss prüfen                                                                       | M1-Bezug                               |
| ----------------------- | --------------------------------------------------------------------------------- | -------------------------------------- |
| `make lint`             | Build mit Warnungen als Fehler plus Code-Metriken-/SOLID-Gate via `Microsoft.CodeAnalysis.NetAnalyzers` (`AnalysisLevel=latest-all` in `Directory.Build.props`, Severities in `.editorconfig`): CA1501/1502/1505/1506 (SRP/Maintainability), CA1000/1001/1012/1033/1040/1715 (OCP/LSP/ISP/DIP). | RM-M1-01, RM-M1-20, RM-M1-21           |
| `make arch-check`       | Dependency Rule und Architektur-Tabus aus Architektur §4.2 (Boundary-Tests)       | RM-M1-22, RM-M1-23                     |
| `make test`             | Domain, Control, Zeitmodell, Snapshot, Vorzeichenkonvention                       | RM-M1-02..08, RM-M1-12                 |
| `make test-safety`      | Emergency Stop, stale Snapshot, ungültige Daten, SOC-/Power-Grenzen, Schreiblimit | RM-M1-04..07, RM-M1-11                 |
| `make simulator-test`   | Go-Simulator baut, Szenario-Fixtures und DTO-/Schema-Vertraege sind gueltig       | RM-M1-09, RM-M1-10, RM-M1-18            |
| `make test-integration` | Modbus, MQTT, PostgreSQL, API über Testserver                                     | RM-M1-09, RM-M1-10, RM-M1-13, RM-M1-15 |
| `make test-container`   | Runtime-Image, Compose-Start, Healthcheck                                         | RM-M1-19                               |
| `make coverage-gate`    | 90 Prozent Line-Coverage für M1-Codebereiche                                      | RM-M1-20                               |
| `make build`            | Runtime-Image ohne SDK, nicht-root User, Port 8080, Healthcheck                   | RM-M1-19                               |
| `make gates`            | Aggregiert alle M1-Pflichtgates ohne Report-Erzeugung                             | RM-M1-21                               |
| `make ci`               | CI-kompatibler Lauf der verbindlichen M1-Gates in dokumentierter Reihenfolge      | RM-M1-20, RM-M1-21                     |
| `make runtime`          | Runtime-Smoke: Compose-Start, `/health`-Prüfung und Shutdown                      | RM-M1-19, RM-M1-21                     |
| `make fullbuild`        | Fresh-clone-naher Komplettlauf inkl. Gates, Build und Runtime-Smoke               | RM-M1-20, RM-M1-21                     |

### Gate-Aktivierung nach Wellen

| Welle | Neu verpflichtend                                                                  | Darf noch fehlen                                            |
| ----- | ---------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| 1     | `make lint`, `make arch-check`, `make gates` mit Foundation-Auswahl                | fachliche Adapter, Persistenz, API, Runtime-Smoke           |
| 2     | `make test`, `make test-safety`, Coverage für Domain/Application                   | Adapter-Integration, OpenAPI, Container-Smoke               |
| 3     | `make simulator-test`, `make test-integration` für Modbus, MQTT und Startvalidierung; Mapping-Schema-Gate | API-Vertrag, Persistenzvollständigkeit, Runtime-Smoke       |
| 4     | API-/AuthZ-/OpenAPI-Gates, Persistenztests, Zeitmodell-/DST-Regressionsfälle       | Fullbuild und Runtime-Smoke als Abschlussgate               |
| 5     | `make ci`, `make runtime`, `make fullbuild`, vollständiges Coverage-Gate           | nichts; offene Abweichungen brauchen ADR oder Roadmap-Patch |

Gates werden mit ihrer Aktivierungswelle in `make gates` und `make ci`
eingehängt. Temporär nicht aktive Gates müssen im Makefile sichtbar bleiben
und mit einer klaren Meldung auf ihre Aktivierungswelle verweisen.

Vertrags-Gates:

- [ ] OpenAPI-Vertrag ist wohlgeformt und deckt alle M1-Endpunkte ab.
- [ ] AuthZ-Negativtests für schreibende Endpunkte liefern 401/403.
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
| RM-OPEN-04 | Authentifizierungsverfahren: API-Token, OIDC oder mTLS                         | RM-M1-15, spätestens RM-M1-16 | API-Token mit rollenbasierter Operator-Rolle für M1; OIDC/mTLS bleiben Erweiterung nach ADR.                            | Offen  |
| RM-OPEN-07 | Release-Pipeline-Gates vor M1-Abschluss und erstem Tag `v0.1.0` konkretisieren | RM-M1-20 Abschluss            | Kein `v0.1.0`-Tag ohne Folge-ADR; M1 darf ohne Release-Tag abgeschlossen werden.                                        | Offen  |

Ein Blocker darf nur in einen späteren Meilenstein verschoben werden, wenn
Roadmap, Lastenheft-Bezug und betroffene Gates im gleichen PR angepasst werden.

---

## Abschlussbedingung

RM-M1 ist abgeschlossen, wenn alle Arbeitspakete erledigt, alle Gates aus der
Gate-Matrix grün, alle M1-Abnahmekriterien erfüllt und alle M1-Blocker
geschlossen oder explizit in einen späteren Meilenstein verschoben sind.
