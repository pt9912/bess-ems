# Plan RM-M1: MVP sichere Regelpipeline

**Dokumenttyp:** Detailplan / DoD-Tracking
**Status:** Offen / Entwurf
**Meilenstein:** RM-M1
**Bezug:** [`roadmap.md`](roadmap.md), [`spec/lastenheft.md`](../../../../spec/lastenheft.md), [`spec/architecture.md`](../../../../spec/architecture.md), [`docs/user/quality.md`](../../../user/quality.md)

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

| Welle | Fokus | Ergebnis |
| ----- | ----- | -------- |
| 1 | Foundation | Solution, Makefile, Docker-Stages und Basis-Gates stehen. |
| 2 | Domain und Control | Domainmodell, Snapshot Store, State Machine, Limiter und Fallbacks sind deterministisch getestet. |
| 3 | Config und Adapter | Modbus/MQTT-Mappings, Simulatorpfade und adapterseitige Schreibbegrenzung sind gate-relevant. |
| 4 | Märkte, Persistenz und API | Day-Ahead, MarketCommitments, UTC/DST-Zeitmodell, PostgreSQL und M1-API sind integriert. |
| 5 | Observability und Abschluss | Logs, Metriken, Compose, Contract-Gates, Coverage und Fullbuild schließen M1 ab. |

Die Wellen sind eine empfohlene Reihenfolge. Innerhalb einer Welle dürfen
Arbeitspakete parallel laufen, solange ihre Abhängigkeiten erfüllt sind.

---

## Arbeitspakete

| Status | ID | Paket | Abhängigkeiten | DoD |
| ------ | -- | ----- | -------------- | --- |
| ⬜ | RM-M1-01 | Solution-Skeleton | keine | Projekte gemäß Architektur sind angelegt, `dotnet build -warnaserror` läuft in der Lint-Stage, gemeinsame Analyzer sind eingebunden. |
| ⬜ | RM-M1-21 | Makefile-Orchestrierung | RM-M1-01 | `make help`, `make gates`, `make ci`, `make runtime`, `make fullbuild`, Override-Variablen und Gate-/Report-Trennung sind implementiert. |
| ⬜ | RM-M1-02 | Domain-Modell | RM-M1-01 | `BatteryAsset`, `BatteryTelemetry`, `BatteryCommand`, `DataQuality`, `MarketCommitment` und Vorzeichenkonvention sind unit-getestet. |
| ⬜ | RM-M1-03 | Realtime Snapshot Store | RM-M1-02 | Datenfusion, Aging, Plausibilisierung, Snapshot-Qualität und stale-Erkennung sind unit-getestet. |
| ⬜ | RM-M1-04 | State Machine | RM-M1-02 | `INIT`, `READY`, `RUNNING`, `FAULT`, `EMERGENCY_STOP`, Quittierung und Operator-Stop-Pfade sind abgedeckt. |
| ⬜ | RM-M1-05 | Constraint Limiter | RM-M1-02, RM-M1-03 | SOC-, Power-, Temperatur- und Verfügbarkeitsgrenzen begrenzen Commands deterministisch. |
| ⬜ | RM-M1-06 | Ramp Limiter | RM-M1-02 | Rampenbegrenzung ist deterministisch, vorzeichenfest und testbar. |
| ⬜ | RM-M1-08 | Optimierungsinterface | RM-M1-02 | `IDispatchOptimizer` und `NoOpDispatchOptimizer` sind austauschbar verdrahtet. |
| ⬜ | RM-M1-07 | Regelzyklus/Fallback | RM-M1-03..06, RM-M1-08 | 1-s-Regelzyklus erzeugt bei stale/ungültigem Snapshot einen sicheren Command. |
| ⬜ | RM-M1-18 | Konfiguration/Mappings | RM-M1-01, RM-M1-02 | Config-Loader, JSON-Schemas, Beispiel-Mappings und Startup-Validierung sind Contract-Gates. |
| ⬜ | RM-M1-09 | Modbus-TCP-Adapter | RM-M1-18 | Lesen, Schreiben, Mapping, Timeout und Fehlerstatus laufen gegen Simulator. |
| ⬜ | RM-M1-10 | MQTT-Adapter | RM-M1-18 | Telemetrieempfang, Command-Publish und Topic-Konvention laufen gegen Mosquitto. |
| ⬜ | RM-M1-11 | Adapter-Schreibbegrenzung | RM-M1-09, RM-M1-10 | Finale Schreibbegrenzung greift unmittelbar vor Versand für Modbus und MQTT. |
| ⬜ | RM-M1-12 | Fahrplan, MarketCommitment, Zeitmodell | RM-M1-02, RM-M1-08 | Import, Speicherung, UTC/DST-Zeitmodell und Day-Ahead-Verfolgung sind inklusive DST-Regression abgedeckt. |
| ⬜ | RM-M1-13 | PostgreSQL-Persistenz | RM-M1-02, RM-M1-12 | Telemetrie, Commands, Fahrpläne und Audit werden versioniert gespeichert. |
| ⬜ | RM-M1-14 | Retention-Konfiguration | RM-M1-13 | Retention- und Datenvolumenparameter sind konfigurierbar, getestet und dokumentiert. |
| ⬜ | RM-M1-15 | API | RM-M1-07, RM-M1-12, RM-M1-13 | Health, Status, Current Command, Schedules und Operator-Stop sind als OpenAPI-3.1-Vertrag vorhanden. |
| ⬜ | RM-M1-16 | AuthN/AuthZ/Audit | RM-M1-15 | Schreibende Endpunkte sind geschützt; unberechtigte Zugriffe liefern 401/403 und Audit-Einträge. |
| ⬜ | RM-M1-17 | Observability | RM-M1-07, RM-M1-13 | JSON-Logs mit Reason-Feld und Prometheus-Metriken sind exportierbar und getestet. |
| ⬜ | RM-M1-19 | Container/Compose | RM-M1-09, RM-M1-10, RM-M1-13, RM-M1-15 | Dockerfile und Compose starten `bess-ems`, PostgreSQL und MQTT-Broker lokal reproduzierbar. |
| ⬜ | RM-M1-20 | Quality-Gates | alle M1-Pakete | Lint, Unit, Safety, Integration, Contract, Container und Coverage laufen reproduzierbar grün. |

---

## Modulziele

| Modul / Pfad | Erwartung in M1 |
| ------------ | --------------- |
| `BatteryEms.Domain` | Domainobjekte, Wertebereiche, Vorzeichenkonvention und MarketCommitments. |
| `BatteryEms.Realtime` | Snapshot Store, Aging, DataQuality und Validierung. |
| `BatteryEms.Control` | State Machine, Constraint Limiter, Ramp Limiter und sicherer Fallback. |
| `BatteryEms.Markets` | Day-Ahead-Import, Zeitmodell, MarketCommitment-Auswahl und aktive Fahrplanlogik. |
| `BatteryEms.Optimization` | Interface und `NoOpDispatchOptimizer`; kein produktiver Solver. |
| `BatteryEms.Protocols.Abstractions` | Gemeinsame Adapterinterfaces und adapterneutrales Command-Modell. |
| `BatteryEms.Protocols.Modbus` | Modbus-TCP Lese-/Schreibpfad mit Mapping und Simulator-Tests. |
| `BatteryEms.Protocols.Mqtt` | MQTT-Telemetrie, Command-Publish und Mosquitto-Integration. |
| `BatteryEms.Persistence` | PostgreSQL-Schema, Repositories, Retention und Audit. |
| `BatteryEms.Api` | M1-Endpunkte, OpenAPI, AuthZ-Negativtests und Operator-Stop. |
| `BatteryEms.Infrastructure` | Logging, Metrics, Config Loading, JSON-Schema-Validierung. |
| `BatteryEms.Worker` | Hosting, Regelzyklus, Adapter-Wiring und Healthcheck. |
| `config/schema/` | JSON-Schemas für Asset, Limits, Modbus- und MQTT-Mappings. |
| `config/examples/` | Validierbare Beispielkonfigurationen und Mapping-Fixtures. |

---

## Gate-Matrix

| Gate | Muss prüfen | M1-Bezug |
| ---- | ----------- | -------- |
| `make lint` | Build mit Warnungen als Fehler, Format, Roslyn-/Style-/Threading-Analyzer | RM-M1-01, RM-M1-21 |
| `make test` | Domain, Control, Zeitmodell, Snapshot, Vorzeichenkonvention | RM-M1-02..08, RM-M1-12 |
| `make test-safety` | Emergency Stop, stale Snapshot, ungültige Daten, SOC-/Power-Grenzen, Schreiblimit | RM-M1-04..07, RM-M1-11 |
| `make test-integration` | Modbus, MQTT, PostgreSQL, API über Testserver | RM-M1-09, RM-M1-10, RM-M1-13, RM-M1-15 |
| `make test-container` | Runtime-Image, Compose-Start, Healthcheck | RM-M1-19 |
| `make coverage-gate` | 90 Prozent Line-Coverage für M1-Codebereiche | RM-M1-20 |
| `make build` | Runtime-Image ohne SDK, nicht-root User, Port 8080, Healthcheck | RM-M1-19 |
| `make gates` | Aggregiert alle M1-Pflichtgates ohne Report-Erzeugung | RM-M1-21 |
| `make ci` | CI-kompatibler Lauf der verbindlichen M1-Gates in dokumentierter Reihenfolge | RM-M1-20, RM-M1-21 |
| `make runtime` | Runtime-Smoke: Compose-Start, `/health`-Prüfung und Shutdown | RM-M1-19, RM-M1-21 |
| `make fullbuild` | Fresh-clone-naher Komplettlauf inkl. Gates, Build und Runtime-Smoke | RM-M1-20, RM-M1-21 |

Vertrags-Gates:

- [ ] OpenAPI-Vertrag ist wohlgeformt und deckt alle M1-Endpunkte ab.
- [ ] AuthZ-Negativtests für schreibende Endpunkte liefern 401/403.
- [ ] Adapter-Mapping-Schemas validieren alle Beispiele unter `config/examples/`.
- [ ] Vorzeichenkonvention ist durch dedizierte Tests abgesichert.
- [ ] Ungültige Startkonfiguration verhindert aktiven Regelbetrieb.
- [ ] Metrikexport ist in Tests abrufbar und enthält die M1-Pflichtmetriken.

---

## Akzeptanzdaten

| Datensatz | Mindestinhalt | Zweck |
| --------- | ------------- | ----- |
| `config/examples/asset.single-bess.json` | Ein BatteryAsset mit SOC-, Power-, Temperatur- und Rampengrenzen | Startup- und Mapping-Validierung |
| `config/examples/adapters/modbus.simulator.json` | Registerprofil für SOC, Power, Temperatur, Verfügbarkeit und Command-Write | Modbus-Integration |
| `config/examples/adapters/mqtt.simulator.json` | Topics für Telemetrie, Command und Command-ACK | MQTT-Integration |
| `tests/fixtures/schedules/day-ahead-basic.json` | 24h-Day-Ahead-Fahrplan in UTC | Fahrplanimport und Regelkreis |
| `tests/fixtures/schedules/day-ahead-dst-transition.json` | DST-Übergang mit eindeutigen UTC-Zeitpunkten | Zeitmodell-Regression |
| `tests/fixtures/telemetry/safe-fallback.json` | stale Snapshot, invalid SOC, BMS-Ausfall, Operator-Stop | Safety-Tests |

Die Dateinamen sind Zielkonventionen. Wenn die spätere Implementierung andere
Formate braucht, muss dieser Abschnitt mit dem jeweiligen PR angepasst werden.

---

## Abnahmekriterien

- [ ] `docker compose up` startet das Gesamtsystem lokal.
- [ ] Simulierter BMS/Wechselrichter liefert Modbus/MQTT-Telemetrie.
- [ ] Das System publiziert Commands ohne SOC-, Power- oder Rampengrenzen zu verletzen.
- [ ] Stale Snapshot, Emergency Stop und Operator-Stop führen in den sicheren Zustand.
- [ ] Audit-Log und Reason-Felder machen Stop- und Fallback-Entscheidungen nachvollziehbar.
- [ ] Day-Ahead-Fahrplan kann importiert, gespeichert und mit UTC/DST-Zeitmodell verfolgt werden.
- [ ] Health, Status, Current Command, Schedules und Operator-Stop sind über API nutzbar.
- [ ] Metriken sind exportierbar und in Tests validiert.
- [ ] `make ci` und `make runtime` laufen reproduzierbar grün.
- [ ] Alle M1-Gates aus der Gate-Matrix sind reproduzierbar grün.

---

## Blocker und Entscheidungen

| Kennung | Entscheidung | Blockiert | Status |
| ------- | ------------ | --------- | ------ |
| RM-OPEN-02 | Erste Herstellerintegration und daraus folgende Modbus-/MQTT-Beispielprofile | finale Adapter-Fixtures | Offen |
| RM-OPEN-04 | Authentifizierungsverfahren: API-Token, OIDC oder mTLS | RM-M1-16, OpenAPI-Security-Schema | Offen |
| RM-OPEN-07 | Release-Pipeline-Gates vor M1-Abschluss und erstem Tag `v0.1.0` konkretisieren | Abschluss von RM-M1 | Offen |

Ein Blocker darf nur in einen späteren Meilenstein verschoben werden, wenn
Roadmap, Lastenheft-Bezug und betroffene Gates im gleichen PR angepasst werden.

---

## Abschlussbedingung

RM-M1 ist abgeschlossen, wenn alle Arbeitspakete erledigt, alle Gates aus der
Gate-Matrix grün, alle M1-Abnahmekriterien erfüllt und alle M1-Blocker
geschlossen oder explizit in einen späteren Meilenstein verschoben sind.
