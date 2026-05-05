# Roadmap: bess-ems

**Dokumenttyp:** Planung / Roadmap
**Status:** Offen
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

> **Stand:** 2026-05-05
> **Aktive Phase:** keine — Implementierung noch nicht begonnen.
> Vorhanden sind Spezifikation (`spec/lastenheft.md`,
> `spec/architecture.md`), Qualitäts-/Gate-Definition
> (`docs/user/quality.md`) und diese Roadmap.
> **Nächster konkreter Schritt:** Aktivierung von **M1**
> (MVP — sichere Regelpipeline). Ein offener Detailplan liegt in
> [`plan-RM-M1.md`](plan-RM-M1.md). Mit Aktivierung wird dieser nach
> `docs/plan/planning/in-progress/plan-RM-M1.md` verschoben und diese
> Roadmap nach `docs/plan/planning/in-progress/roadmap.md` verschoben.

---

## Übersicht

| Status | Meilenstein | Titel                              | Phase | Detailplan |
| ------ | ----------- | ---------------------------------- | ----- | ---------- |
| ⬜     | M1          | MVP — sichere Regelpipeline        | 1     | [Entwurf](plan-RM-M1.md) |
| ⬜     | M2          | Marktausbau und Optimierung        | 1 → 2 | folgt mit Aktivierung |
| ⬜     | M3          | Native Control Core (Library)      | 2     | folgt mit Aktivierung |
| ⬜     | M4          | Regelleistung und OPC-UA           | 2     | folgt mit Aktivierung |
| ⬜     | M5          | MPC, Solver-Sidecar, Replay        | 3     | folgt mit Aktivierung |
| ⬜     | M6          | Skalierung, UI, Edge / Multi-Asset | 4     | folgt mit Aktivierung |

Phase bezieht sich auf [`idea.md`](../../../../spec/idea.md) und
[`architecture.md`](../../../../spec/architecture.md) §13.

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
| ⬜     | RM-M1-01   | C#/.NET Solution-Skeleton mit Projekten gemäß Architektur §5.1      | LH-NF-001, LH-ARCH-001    |
| ⬜     | RM-M1-02   | Domain-Modell (BatteryAsset, Telemetry, Command, DataQuality)       | LH-DOM-001..004           |
| ⬜     | RM-M1-03   | Realtime Snapshot Store, Datenfusion, Aging                         | LH-RT-001..004            |
| ⬜     | RM-M1-04   | State Machine (INIT…EMERGENCY_STOP) inkl. Quittierungslogik         | LH-SM-001..003            |
| ⬜     | RM-M1-05   | Constraint Limiter (.NET) mit SOC-, Power-, Verfügbarkeitsgrenzen   | LH-CTRL-002, LH-SAFE-002/3 |
| ⬜     | RM-M1-06   | Ramp Limiter (.NET)                                                  | LH-CTRL-003               |
| ⬜     | RM-M1-07   | Regelzyklus 1 s, sicherer Fallback bei stale/ungültigem Snapshot    | LH-CTRL-001/007, LH-RT-003 |
| ⬜     | RM-M1-08   | Optimization-Interface + `NoOpDispatchOptimizer`                    | LH-OPT-001 (als Interface) |
| ⬜     | RM-M1-09   | Modbus-TCP-Adapter (Lesen + Schreiben, Mapping über Config)         | LH-MODB-001..005          |
| ⬜     | RM-M1-10   | MQTT-Adapter (Telemetrie-Empfang, Command-Publish, Topic-Konvention) | LH-MQTT-001..003         |
| ⬜     | RM-M1-11   | Schreibbegrenzung im Adapter unmittelbar vor Versand                | LH-SAFE-007               |
| ⬜     | RM-M1-12   | Statischer Fahrplanimport, `MarketCommitment`-Modell, UTC/DST-Zeitmodell + Day-Ahead-Verfolgung | LH-MKT-001/003/006/007 |
| ⬜     | RM-M1-13   | PostgreSQL-Persistenz: Telemetrie, Commands, Fahrpläne, Audit       | LH-PERSIST-001..005       |
| ⬜     | RM-M1-14   | Retention-/Datenvolumen-Konfiguration (dokumentiert)                | LH-PERSIST-006            |
| ⬜     | RM-M1-15   | API: Health, Status, Current Command, Schedules, Operator-Stop      | LH-API-001..004, LH-API-006 |
| ⬜     | RM-M1-16   | AuthN/AuthZ + Audit-Log für schreibende Endpunkte                   | LH-API-007                |
| ⬜     | RM-M1-17   | Strukturierte JSON-Logs mit Reason-Feld + Prometheus-Metrikexport   | LH-MON-001/002/004        |
| ⬜     | RM-M1-18   | Konfigurations-Loader, JSON-Schemas für Adapter-Mappings + Startvalidierung | LH-CONF-001..003, LH-OPS-001 |
| ⬜     | RM-M1-19   | Dockerfile + Docker Compose (`bess-ems` mit Worker/API, Postgres, MQTT-Broker) | LH-DEPLOY-001..003, LH-NF-003/4 |
| ⬜     | RM-M1-20   | Quality-Gates: Lint, Unit/Safety/Integration/Contract/Container, Coverage | LH-TEST-001/003/006/007, LH §4.1 |
| ⬜     | RM-M1-21   | Makefile als Orchestrierungsschicht über die Docker-Stages aus `docs/user/quality.md`: `.DEFAULT_GOAL=help`, Override-Variablen (`COVERAGE_THRESHOLD`, `LIZARD_MAX_*`, `IMAGE`), Composite-Targets (`gates`, `ci`, `runtime`, `fullbuild`), `-gate`/`-report`-Trennung | LH-DEPLOY-001/002, LH-TEST-001/006/007 |

### Abnahmekriterien

- `docker compose up` startet das Gesamtsystem lokal.
- Ein simulierter BMS/Wechselrichter (Modbus/MQTT) liefert Telemetrie, das
  System publiziert Commands, ohne SOC-/Power-/Rampengrenzen zu verletzen.
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

---

## M2 — Marktausbau und Optimierung (.NET)

**Ziel:** Erweiterte Marktlogik auf dem M1-Zeitmodell, einfacher
LP-Optimierer (.NET-Interface, optional OR-Tools/HiGHS), Tracing und Replay.

### Liefergegenstände

| Status | ID         | Inhalt                                                              | LH-Bezug                |
| ------ | ---------- | ------------------------------------------------------------------- | ----------------------- |
| ⬜     | RM-M2-01   | Erweiterte Marktcommitment-Priorisierung und Optimierungsintegration | LH-MKT-003/006          |
| ⬜     | RM-M2-02   | Erweiterte Zeitmodell-Nutzung für Optimierungshorizonte und Marktintervalle | LH-MKT-007              |
| ⬜     | RM-M2-03   | LP-Implementierung des `IDispatchOptimizer` (Solver-Auswahl per Config) | LH-OPT-001..006     |
| ⬜     | RM-M2-04   | Zielfunktion konfigurierbar (Energiebezug, Einspeise, Strafkosten)  | LH-OPT-004              |
| ⬜     | RM-M2-05   | Optimierungs-API (`POST /markets/day-ahead/optimize`)               | LH-API-005              |
| ⬜     | RM-M2-06   | OpenTelemetry-Tracing für Snapshot → Control → Adapter              | LH-MON-003              |
| ⬜     | RM-M2-07   | Erweiterte Prometheus-Metriken für Solverzeit und Optimierungsläufe | LH-MON-002              |
| ⬜     | RM-M2-08   | PID-Regler (.NET) mit Anti-Windup, Output-Clamping, Totband         | LH-CTRL-004             |
| ⬜     | RM-M2-09   | Erweiterte Persistenz für Optimierungsläufe und Solverstatus        | LH-PERSIST-002/003      |
| ⬜     | RM-M2-10   | Replay-Test-Harness (Telemetrie-Wiedergabe, Command-Vergleich)      | LH-TEST-004             |

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

**Ziel:** Phase 2 aus `idea.md` / Architektur §13: native Bibliothek
`battery_control_core` für Constraint, Ramp, PID via P/Invoke. .NET-Variante
bleibt Fallback und Referenz.

### Liefergegenstände

| Status | ID         | Inhalt                                                              | LH-Bezug                   |
| ------ | ---------- | ------------------------------------------------------------------- | -------------------------- |
| ⬜     | RM-M3-01   | C-ABI `battery_control_core.h` (Snapshot/Limits/Command Structs)    | LH-NATIVE-002/003          |
| ⬜     | RM-M3-02   | C++-Implementierung Constraint + Ramp + Statuscode-Fehlerpfade      | LH-NATIVE-001/004          |
| ⬜     | RM-M3-03   | ABI-Versionsfunktion + Startup-Check in .NET                        | LH-NATIVE-005              |
| ⬜     | RM-M3-04   | P/Invoke-Bindings (`BatteryEms.NativeInterop`)                      | LH-NATIVE-001              |
| ⬜     | RM-M3-05   | Routing: Native bevorzugt, .NET-Fallback bei Fehler/Abwesenheit     | LH-ARCH-006, LH-NF-002     |
| ⬜     | RM-M3-06   | Multi-Stage Dockerfile mit Native-Build-Stage                       | LH-DEPLOY-003/004, LH-NATIVE-006 |
| ⬜     | RM-M3-07   | Interop-Tests (Struct Layout, ABI, Werte-Parität gg. .NET-Referenz) | LH-TEST-005                |
| ⬜     | RM-M3-08   | C++-Unit-Tests (Constraint, Ramp, NaN/Inf, Vorzeichen, neg. dt)     | LH-TEST-001                |
| ⬜     | RM-M3-09   | Native-Quality-Gates: `native-lint`, Sanitizer, Native-Coverage     | LH-TEST-005, LH-NATIVE-*   |
| ⬜     | RM-M3-10   | Native/.NET-Parity-Gate über Replay-Datensatz                       | LH-ARCH-006, LH-TEST-005   |
| ⬜     | RM-M3-11   | Makefile-Erweiterung um native Targets (`native-lint`, `test-native-interop`, `test-native-parity`, `native-coverage-gate`, `native-coverage-report`, `native-coverage-exclusions`); `gates`/`ci` ziehen native Gates mit | LH-NATIVE-*, LH-TEST-005   |

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
| ⬜     | RM-M4-02   | Reservierungs-Modell für Regelleistung + Solver-Constraints         | LH-MKT-004              |
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
| RM-OPEN-04 | Authentifizierung in M1 (API-Token, OIDC)?                     | Offen  |
| RM-OPEN-05 | Reihenfolge M3 vs. M4 — Native zuerst oder Markt-/RL zuerst?   | Offen  |
| RM-OPEN-06 | Kriterien für spätere API-Extraktion nach dem MVP (siehe AR-OPEN-001)? | Offen  |
| RM-OPEN-07 | Folge-ADR für Release-Pipeline-Gates; vor Abschluss von M1 und vor erstem Tag `v0.1.0` schließen? | Offen  |

---

## Verlinkung

- Lastenheft-Anforderungen: [`spec/lastenheft.md`](../../../../spec/lastenheft.md)
- Architekturentwurf: [`spec/architecture.md`](../../../../spec/architecture.md)
- Qualitäts- und Messpfade: [`docs/user/quality.md`](../../../user/quality.md)
- Projektidee/Hintergrund: [`spec/idea.md`](../../../../spec/idea.md)

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
