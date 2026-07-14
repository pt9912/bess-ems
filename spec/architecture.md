# Architektur: bess-ems

**Projektname:** bess-ems
**Dokumenttyp:** Architekturbeschreibung
**Format:** Markdown
**Version:** 0.3.0
**Status:** Entwurf
**Bezug:** [`lastenheft.md`](lastenheft.md)

---

## 1. Zweck

Dieses Dokument beschreibt die technische Architektur des Systems `bess-ems`.
Es übersetzt die Anforderungen aus dem Lastenheft in Schichten, Module,
Schnittstellen und Datenflüsse. Es legt fest, wo C#/.NET und wo C/C++
eingesetzt werden, wie Marktlogik und Regelung getrennt sind und welche
Schnittstellen die Trennung tragen.

Das Dokument ergänzt das Lastenheft, ersetzt es nicht. Anforderungen
referenzieren ihre `LH-*`-Kennung; Architekturkomponenten erhalten
`AR-*`-Kennungen für die Rückverfolgbarkeit.

---

## 2. Architekturprinzipien

| Kennung   | Prinzip                                                         | Bezug         |
| --------- | --------------------------------------------------------------- | ------------- |
| AR-P-001  | Strikte Trennung von Marktoptimierung und technischer Regelung  | LH-ZIEL-002   |
| AR-P-002  | Modulare, schichtenbasierte Architektur                         | LH-ARCH-001/2 |
| AR-P-003  | Adapter enthalten keine Geschäfts-, Markt- oder Regelentscheidungen | LH-ARCH-003 |
| AR-P-004  | Optimierer schreiben nie direkt auf Geräte                      | LH-ARCH-004   |
| AR-P-005  | Einheitliche interne Modelle (Telemetrie, Command, Snapshot)    | LH-DOM-002/3  |
| AR-P-006  | Einheitliche Vorzeichenkonvention intern                        | LH §4.1       |
| AR-P-007  | Sicherer Fallback bei jeder Unsicherheit (kein Default-Aktiv)   | LH-CTRL-007, LH-SAFE-* |
| AR-P-008  | Konfigurations-, nicht codegetriebene Geräte- und Marktparameter | LH-CONF-001  |
| AR-P-009  | Native Core ist optional, nicht zentral; austauschbar gegen .NET | LH-ARCH-006, LH-NF-002 |
| AR-P-010  | Containerisiert lauffähig auf Linux                             | LH-NF-003/4   |
| AR-P-011  | Hexagonal: Driving/Driven-Trennung, Dependency Rule, Architektur-Tabus per Boundary-Test | LH-ARCH-001..005 |

---

## 3. Systemkontext

```text
              ┌────────────────────┐   ┌──────────────────────┐
              │ Marktpreisquellen  │   │ Fahrplanquellen      │
              │ (Day-Ahead/Intra)  │   │ (Operator/extern)    │
              └─────────┬──────────┘   └──────────┬───────────┘
                        │                         │
                        ▼                         ▼
   ┌───────────────────────────────────────────────────────────────┐
   │                          bess-ems                             │
   │                                                               │
   │   API (REST/HTTP) ◄──── Operator UI / API-Client              │
   │                                                               │
   │   Worker (Regelkreis, Markt, Adapter-Orchestrierung)          │
   │                                                               │
   │   Native Core (optional, performance-kritisch)                │
   └─────┬───────────────────┬──────────────────┬──────────────────┘
         │                   │                  │
   ┌─────▼─────┐      ┌──────▼──────┐    ┌──────▼──────────────┐
   │ Modbus TCP│      │ MQTT Broker │    │ OPC-UA              │
   └─────┬─────┘      └──────┬──────┘    └──────┬──────────────┘
         │                   │                  │
   ┌─────▼───────────────────▼──────────────────▼──────────────┐
   │   BMS / Wechselrichter / Zähler / PV-Messung / RL-Signal  │
   └───────────────────────────────────────────────────────────┘

   Persistenz: PostgreSQL (TimescaleDB optionaler Folgeausbau)
   Monitoring: strukturierte Logs, Metriken, OpenTelemetry-Traces
```

Bezug: LH-KTX-001, LH-KTX-002, LH-PERSIST-005.

---

## 4. Architekturstruktur

Das System wird zugleich aus zwei Sichten beschrieben:

- **§4.1 Schichtenmodell** — logische Schichten der Verantwortung
  (LH-ARCH-002).
- **§4.2 Hexagonale Sicht** — strukturelle Trennung in fachlichen
  Hexagon-Kern und auswechselbare Adapter mit Driving/Driven-Klassifikation.

Beide Sichten beschreiben dasselbe System; sie sind komplementär. §4.1
ordnet Verantwortlichkeiten und Datenfluss, §4.2 legt die zulässigen
Compile-Time-Abhängigkeiten fest. Bei einer scheinbaren Kollision zwischen
Schichtenmodell und hexagonaler Sicht gilt die Dependency Rule aus §4.2.

### 4.1 Schichtenmodell

```text
┌─────────────────────────────────────────────────────────────┐
│ API Layer                                                   │  HTTP/REST, AuthN/AuthZ
├─────────────────────────────────────────────────────────────┤
│ Application / Worker Orchestration                          │  Regelzyklus, Scheduler
├─────────────────────────────────────────────────────────────┤
│ Market Layer        │ Optimization Layer                    │  Day-Ahead / Intraday / Solver-IF
├─────────────────────┴───────────────────────────────────────┤
│ Control Layer                                               │  State Machine, Limiter, PID
├─────────────────────────────────────────────────────────────┤
│ Realtime Layer                                              │  Snapshot Store, Validierung
├─────────────────────────────────────────────────────────────┤
│ Domain Layer                                                │  Asset, Telemetrie, Command
├─────────────────────────────────────────────────────────────┤
│ Protocol Adapter Layer                                      │  Modbus, MQTT, OPC-UA
├─────────────────────────────────────────────────────────────┤
│ Infrastructure Layer                                        │  Persistenz, Logging, Config
├─────────────────────────────────────────────────────────────┤
│ Native Core Layer (optional)                                │  C/C++ über C-ABI
└─────────────────────────────────────────────────────────────┘
```

**Interpretation:** Dieses Schichtenmodell ist eine logische
Verantwortungs- und Datenflusssicht. Es erlaubt nicht automatisch, dass
eine höher dargestellte Schicht konkrete Implementierungen tieferer
Schichten referenziert. Konkrete Code-Abhängigkeiten werden über Ports,
Adapter und Composition Root in §4.2 geregelt.

Bezug: LH-ARCH-002.

### 4.2 Hexagonale Sicht (Driving / Driven Ports)

Der fachliche Kern (Hexagon) enthält Domain und Application; alles, was
Außenwelt berührt — Protokolle, Persistenz, Telemetrie, Solver, Native
Core, HTTP — lebt in Adaptern. Adapter implementieren Ports, die der
Kern definiert. Der Regelkreis aus §6 läuft strikt **Driving Port → Use
Case → Driven Ports → Driven Adapter**.

```text
        ┌──────────────────────────────────────────────────┐
        │              Driving Adapters                    │
        │  HTTP API   Worker-Loop   Operator-CLI (opt.)    │
        └─────────┬─────────────┬─────────────┬────────────┘
                  │             │             │
        ┌─────────▼─────────────▼─────────────▼────────────┐
        │             Driving Ports                        │
        │  IControlCycleUseCase, IOperatorCommandUseCase,  │
        │  IBatteryStatusQuery, IScheduleQuery, …          │
        └─────────────────────┬────────────────────────────┘
                              │
        ┌─────────────────────▼────────────────────────────┐
        │           Application Hexagon (Use Cases)        │
        │   Regelzyklus  ·  Markt-/Fahrplanauflösung       │
        │   Optimierung-IF  ·  Limiter-Komposition         │
        └─────────────────────┬────────────────────────────┘
                              │
        ┌─────────────────────▼────────────────────────────┐
        │             Domain (Hexagon-Kern)                │
        │   BatteryAsset, Telemetry, Command, Schedule,    │
        │   StateMachine, Limiter, Vorzeichenkonvention    │
        │   — frameworkfrei                                │
        └─────────────────────┬────────────────────────────┘
                              │
        ┌─────────────────────▼────────────────────────────┐
        │             Driven Ports                         │
        │ IBatteryTelemetrySource, IBatteryCommandSink,    │
        │ ICommandRepository, IScheduleRepository,         │
        │ IAuditLog, IScheduleOptimizer, IClock,           │
        │ IOptimizationRunRepository, IDispatchOptimizer,  │
        │ IControlKernel, ITelemetryExporter               │
        └─────────┬───────────┬─────────────┬──────────────┘
                  │           │             │
        ┌─────────▼──┐ ┌──────▼──────┐ ┌────▼─────────────┐
        │  Modbus    │ │   MQTT      │ │  Postgres /      │
        │  OPC-UA    │ │   Mosquitto │ │  EF Core         │
        │  Adapter   │ │   Adapter   │ │  Adapter         │
        └────────────┘ └─────────────┘ └──────────────────┘
                  │           │             │
        ┌─────────▼───────────▼─────────────▼──────────────┐
        │  Native-Interop (P/Invoke), OTel-Exporter,       │
        │  Solver-Bindings (HiGHS/OR-Tools/gRPC-Sidecar)   │
        └──────────────────────────────────────────────────┘
```

#### Verzeichnisstruktur (.NET-Solution)

Bess-ems folgt dem **driving/driven**-Stil. „Driving" markiert die
Aufrufrichtung von außen in den Kern,
„Driven" die Aufrufrichtung vom Kern nach außen. Die Verzeichnisstruktur
ist verbindlich für den Solution-Aufbau (RM-M1-01):

```text
bess-ems/
├── src/
│   ├── hexagon/
│   │   ├── BatteryEms.Domain/                # Entitäten, Value Objects, Limiter, State Machine
│   │   └── BatteryEms.Application/           # Use Cases, Driving + Driven Port-Interfaces
│   ├── adapters/
│   │   ├── driving/
│   │   │   ├── BatteryEms.Api/               # HTTP/REST, AuthN/AuthZ, Audit
│   │   │   └── BatteryEms.Worker/            # Hosted Service: Regelzyklus
│   │   └── driven/
│   │       ├── BatteryEms.Adapters.Modbus/
│   │       ├── BatteryEms.Adapters.Mqtt/
│   │       ├── BatteryEms.Adapters.OpcUa/    # ab M4
│   │       ├── BatteryEms.Adapters.Persistence/  # Postgres, Repositories, Migrationen
│   │       ├── BatteryEms.Adapters.Telemetry/    # OTel, Prometheus, Logging-Exporter
│   │       ├── BatteryEms.Adapters.Optimization/ # Solver-Bindings, Schedule-/Dispatch-Optimierung
│   │       └── BatteryEms.Adapters.NativeInterop/ # ab M3, P/Invoke + Fallback-Routing
│   └── infrastructure/
│       └── BatteryEms.Infrastructure/        # Cross-cutting: Config-Loader, DI-Wiring, Health
├── native/
│   └── battery_control_core/                 # ab M3, eigene C-Bibliothek mit C-ABI-Header
└── tests/
    ├── hexagon/
    │   ├── BatteryEms.Domain.Tests/
    │   └── BatteryEms.Application.Tests/
    └── adapters/
        ├── driving/
        │   └── BatteryEms.Api.Tests/
        └── driven/
            ├── BatteryEms.Adapters.Modbus.Tests/
            ├── BatteryEms.Adapters.Mqtt.Tests/
            └── …
```

`BatteryEms.Infrastructure` ist bewusst kein Adapter, sondern
Composition-Root: es kennt `hexagon/` und `adapters/`, aber kein anderer
Pfad kennt es. DI-Wiring, Konfigurations-Loader und Healthchecks leben
hier.

#### Driving Ports (vom Kern angeboten)

| Driving Port (Use Case)         | Eingang aus               | Verantwortung                                                  | LH-Bezug         |
| ------------------------------- | ------------------------- | -------------------------------------------------------------- | ---------------- |
| `IControlCycleUseCase`          | Worker-Loop               | Ein Regelzyklus: Snapshot lesen → State Machine → Limiter → Command | LH-CTRL-001/007 |
| `IOperatorCommandUseCase`       | HTTP API                  | Operator-Stop, manuelle Sollwerte, Quittierung von FAULT       | LH-API-006/007, LH-OPS-004 |
| `IBatteryStatusQuery`           | HTTP API                  | aktueller Status, letzter Command, Datenqualität               | LH-API-002/003   |
| `IScheduleQuery`                | HTTP API                  | aktiven/historischen Fahrplan abfragen                         | LH-API-004       |
| `IScheduleImport`               | HTTP API / Worker         | Day-Ahead-/Intraday-Fahrplan importieren                       | LH-MKT-001/002   |
| `IScheduleOptimizationUseCase`  | HTTP API                  | Horizon-Optimierung auslösen und Ergebnis speichern            | LH-API-005, LH-OPT-001/007/009 |
| `IIntradayReoptimizationUseCase` | HTTP API                 | Intraday-Resthorizont reoptimieren und Fahrplan ersetzen       | LH-MKT-002, LH-API-005 |
| `IRegelleistungActivationUseCase` | Aktivierungs-Adapter    | Regelleistungsaktivierung validieren, deduplizieren und für Dispatch bereitstellen | LH-MKT-005/006 |
| `IHealthQuery`                  | HTTP API                  | Health-Endpunkt                                                | LH-API-001       |
| `IRegelleistungHealthQuery`     | HTTP API                  | Health der Regelleistungs-Aktivierungspipeline                 | LH-MKT-005, LH-MON-002 |

#### Driven Ports (vom Kern aufgerufen)

| Driven Port                       | Implementiert in                                | Verantwortung                                              | LH-Bezug              |
| --------------------------------- | ----------------------------------------------- | ---------------------------------------------------------- | --------------------- |
| `IBatteryTelemetrySource`         | Modbus / MQTT / OPC-UA                          | Telemetrie liefern, Datenqualität setzen                   | LH-RT-002, LH-PROT-001 |
| `IBatteryCommandSink`             | Modbus / MQTT / OPC-UA                          | Commands schreiben, Schreibbegrenzung                      | LH-SAFE-007, LH-PROT-001 |
| `ITelemetryRepository`            | Persistence                                     | Telemetrie speichern, historisch abfragen                  | LH-PERSIST-001        |
| `ICommandRepository`              | Persistence                                     | Commands speichern, Reason erhalten                        | LH-PERSIST-002        |
| `IScheduleRepository`             | Persistence                                     | Fahrpläne versioniert speichern                            | LH-PERSIST-003        |
| `IOperatorAuditLog`               | Persistence                                     | Operator-Aktionen auditierbar speichern                    | LH-PERSIST-004, LH-OPS-004 |
| `IScheduleOptimizer`              | Optimization (LP ab M2; MILP/heuristisch optional) | Fahrpläne über Horizon erzeugen oder aktualisieren       | LH-OPT-001..009       |
| `IDispatchOptimizer`              | Optimization (Schedule-Following; MPC später)   | Single-Step-Dispatch im Regelzyklus                        | LH-OPT-007, LH-CTRL-005 |
| `IPriceSeriesSource`              | Price-Source-Adapter / Import                    | quellenneutrale Preisreihen für Optimierung liefern        | LH-MKT-008, LH-OPEN-003 |
| `IOptimizationRunRepository`       | Persistence                                     | Optimierungsläufe und Objective Breakdown speichern         | LH-PERSIST-007        |
| `IActivationDispatchSource`        | Application / Markets                          | aktive Regelleistungsaktivierung für den Dispatch-Tick halten | LH-MKT-005/006        |
| `IControlKernel`                  | Application (`ManagedControlKernel`) und NativeInterop (`NativeFallbackControlKernel`, ab M3) | Constraint/Ramp/PID ausführen; Native bevorzugt mit deterministischem Managed-Fallback bei nativem Fehler | LH-NATIVE-001/004     |
| `IClock`                          | Infrastructure                                  | UTC-Zeit, deterministisch in Tests                         | LH-MKT-007            |
| `ITelemetryExporter`              | Telemetry                                       | Logs/Metrics/Traces nach außen                             | LH-MON-001/002/003    |
| `IConfigurationProvider`          | Infrastructure                                  | validierte Konfiguration bereitstellen                     | LH-CONF-001..003      |

#### Dependency Rule (verbindlich)

Abhängigkeiten zeigen **immer nach innen**: Driving Adapter →
Application → Domain ← Application ← Driven Adapter. Konkret:

- `BatteryEms.Domain` referenziert nichts (auch keine Application).
- `BatteryEms.Application` referenziert nur `BatteryEms.Domain`.
- `adapters/driven/*` und `adapters/driving/*` referenzieren beide
  `BatteryEms.Application` (für Ports) und ggf. `BatteryEms.Domain`
  (für Wertobjekte).
- **Adapter referenzieren niemals andere Adapter.** Falls ein Adapter
  Funktionalität eines anderen braucht, geht es über einen Port.
- `BatteryEms.Infrastructure` darf alles unter `src/` referenzieren —
  kein anderer Pfad darf `Infrastructure` referenzieren.

#### Architektur-Tabus (Compile-/Build-time-Check)

Pro Hexagon-Modul gilt ein hartes Import-Verbot. Diese Tabus werden in
M1 als Boundary-Test (`BatteryEms.ArchitectureTests` mit NetArchTest oder
ArchUnitNET) durchgesetzt; Verstöße brechen den Build (LH-NF-006,
LH-ARCH-002).

| Modul                              | Verboten                                                                |
| ---------------------------------- | ----------------------------------------------------------------------- |
| `BatteryEms.Domain`                | ASP.NET, EF Core, Npgsql, MQTTnet, NModbus, OPC Foundation, OTel, Serilog, gRPC, P/Invoke, `System.Net.Http`, `Microsoft.Extensions.*` außer `Logging.Abstractions`/`Options` |
| `BatteryEms.Application`           | gleiche Verbote wie Domain; zusätzlich keine Adapter-Referenzen, kein konkreter Solver |
| `adapters/driven/*`                | Referenzen auf `adapters/driving/*` und auf andere `adapters/driven/*`-Module |
| `adapters/driving/*`               | Referenzen auf `adapters/driven/*` und auf andere `adapters/driving/*`-Module |
| `BatteryEms.Adapters.NativeInterop` | direkter Zugriff auf andere Adapter; nur Application-Ports und Domain  |

#### Mapping zum Schichtenmodell

| Schicht (§4.1)            | Hexagonale Zuordnung                                |
| ------------------------- | --------------------------------------------------- |
| API Layer                 | Driving Adapter (`BatteryEms.Api`)                  |
| Application / Worker      | Driving Adapter (`BatteryEms.Worker`) + Application (`BatteryEms.Application`) |
| Market / Optimization     | Application Use Cases + `IScheduleOptimizer`- und `IDispatchOptimizer`-Driven-Ports |
| Control                   | Domain (Limiter, State Machine) + Application (Komposition) |
| Realtime                  | Application (Snapshot Store) + Driven Adapter (Telemetrie-Quellen) |
| Domain                    | `BatteryEms.Domain` (Hexagon-Kern)                  |
| Protocol Adapter          | Driven Adapter (`BatteryEms.Adapters.{Modbus,Mqtt,OpcUa}`) |
| Infrastructure            | `BatteryEms.Infrastructure` (Composition Root) + Driven Adapter (Persistence, Telemetry) |
| Native Core               | Driven Adapter (`BatteryEms.Adapters.NativeInterop`) + native Bibliothek unter `native/` |

Bezug: LH-ARCH-001..005, LH-NF-006/007/008.

---

## 5. Komponentensicht

### 5.1 .NET-Module

Die Modulnamen folgen der Verzeichnisstruktur aus §4.2. Die Spalte
„Hexagon" ordnet jedes Modul der hexagonalen Klassifikation zu.
Application-interne Funktionsbereiche (Realtime, Control, Markets,
Optimization-Interface) leben innerhalb von `BatteryEms.Application` als
Namespaces; eine spätere Aufspaltung in eigene .NET-Projekte ist optional
und trigger-basiert.

| Modul                                    | Hexagon              | Verantwortung                                                | LH-Bezug             |
| ---------------------------------------- | -------------------- | ------------------------------------------------------------ | -------------------- |
| `BatteryEms.Domain`                      | Hexagon-Kern         | Entitäten, Wertobjekte, Vorzeichenkonvention, State Machine, Limiter | LH-DOM-*, LH-SM-*, LH-CTRL-002/003, §4.1 |
| `BatteryEms.Application`                 | Hexagon-Application  | Use Cases, Driving + Driven Port-Interfaces, Snapshot Store, Markt-/Fahrplanauflösung, Optimierungs-Interface | LH-CTRL-001/007, LH-RT-*, LH-MKT-*, LH-OPT-001 |
| `BatteryEms.Api`                         | Driving Adapter      | REST-API, AuthN/AuthZ, Operator-Endpunkte, Audit             | LH-API-*             |
| `BatteryEms.Worker`                      | Driving Adapter      | Hosted Service: Regelzyklus, Scheduler                       | LH-CTRL-001, LH-OPS-* |
| `BatteryEms.Adapters.Modbus`             | Driven Adapter       | Modbus-TCP-Adapter (Lesen + Schreiben)                       | LH-MODB-*            |
| `BatteryEms.Adapters.Mqtt`               | Driven Adapter       | MQTT-Telemetrie + Command-Publish                            | LH-MQTT-*            |
| `BatteryEms.Adapters.OpcUa`              | Driven Adapter (M4)  | OPC-UA Lesen, Schreiben, Subscriptions                       | LH-OPCUA-*           |
| `BatteryEms.Adapters.Persistence`        | Driven Adapter       | Repositories (EF Core/Dapper), Migrationen, Retention        | LH-PERSIST-*         |
| `BatteryEms.Adapters.Telemetry`          | Driven Adapter       | OTel-Tracing, Prometheus-Metriken, Logging-Exporter          | LH-MON-*             |
| `BatteryEms.Adapters.Optimization`       | Driven Adapter       | Solver-Bindings für Horizon-Optimierung; Schedule-Following-/Single-Step-Dispatch | LH-OPT-001..009 |
| `BatteryEms.Adapters.NativeInterop`      | Driven Adapter (M3)  | P/Invoke-Bindings, ABI-Check, Fallback-Routing               | LH-NATIVE-*          |
| `BatteryEms.Infrastructure`              | Composition Root     | DI-Wiring, Konfigurations-Loader, Health, Startvalidierung   | LH-CONF-*, LH-OPS-001 |
| `BatteryEms.ArchitectureTests`           | Test-Modul           | Boundary-Tests für Dependency Rule und Architektur-Tabus aus §4.2 | LH-NF-006, LH-ARCH-002 |

### 5.2 Native-Core-Komponenten (optional, ab Phase 2)

| Komponente                   | Zweck                                            | LH-Bezug         |
| ---------------------------- | ------------------------------------------------ | ---------------- |
| `battery_control_core` (lib) | Constraint Limiter, Ramp Limiter, PID, Plausi    | LH-NATIVE-001    |
| `state_space_core` (lib/sidecar) | State-Space, Kalman, MPC-Kernel              | LH-CTRL-005/6    |
| `optimization_core` (sidecar) | gRPC-Wrapper um nativen Solver                  | LH-OPT-006       |

Bezug: LH-NATIVE-002 (stabile C-ABI), LH-NATIVE-005 (ABI-Versionierung).

---

## 6. Datenfluss: Regelkreis

```text
   ┌──────────────────────────────────────────────────────────┐
   │                  Realtime Layer                          │
   │                                                          │
   │  Modbus  ─┐                                              │
   │           ├──► Telemetrie-Validierung ──► Snapshot Store │
   │  MQTT  ───┤      (Plausi, Aging, Quality)                │
   │  OPC-UA ──┘                                              │
   └─────────────────────────┬────────────────────────────────┘
                             │ Snapshot (konsistent, datenqualitätsbewertet)
                             ▼
   ┌──────────────────────────────────────────────────────────┐
   │                   Control Layer                          │
   │                                                          │
   │  ┌─── State Machine ───┐                                 │
   │  │  INIT/READY/IDLE/   │                                 │
   │  │  CHARGING/DISCH./   │                                 │
   │  │  LIMITED/FAULT/     │                                 │
   │  │  EMERGENCY_STOP     │                                 │
   │  └─────────┬───────────┘                                 │
   │            ▼                                             │
   │  Markt-/Fahrplanauflösung ◄──── Schedule (Markets)       │
   │            ▼                                             │
   │  Regelleistungspriorisierung ◄──────────── RL-Aktivierung │
   │            ▼                                             │
   │  Constraint Limiter ────┐                                │
   │            ▼            │ Native Core (optional)         │
   │  Ramp Limiter ──────────┘                                │
   │            ▼                                             │
   │  Command (mit Reason, ValidUntil)                        │
   └─────────────────────────┬────────────────────────────────┘
                             ▼
   ┌──────────────────────────────────────────────────────────┐
   │            Protocol Adapter Layer (Schreibpfad)          │
   │  Schreibbegrenzung (final) ──► Modbus / MQTT / OPC-UA    │
   └──────────────────────────────────────────────────────────┘
```

Standard-Zyklus: 1 s (LH-CTRL-001, LH-RT-004). Ein Zyklus liest einen
konsistenten Snapshot, läuft synchron durch State Machine → Limiter →
Command → Adapter und persistiert Ergebnis und Reason.

Bezug: LH-CTRL-002/3, LH-SAFE-007 (Schreibbegrenzung vor Versand),
LH-MON-004 (Reason).

---

## 7. Domain-Modell (Skizze)

```text
BatteryAsset
  ├─ AssetId
  ├─ CapacityKwh
  ├─ MaxChargePowerKw / MaxDischargePowerKw
  ├─ MinSocPercent / MaxSocPercent
  ├─ ChargeEfficiency / DischargeEfficiency
  ├─ MaxRampKwPerSecond
  ├─ Capabilities (BMS, PCS, EnergyStore, Meter, ...)
  └─ OperatingState

DevicePointDefinition
  ├─ Key, DisplayName
  ├─ Unit, ValueType
  ├─ Direction (Read/Write/ReadWrite)
  ├─ Min/Max, Scale, Offset
  ├─ Exportable
  ├─ Alarm/Plausibility Rule?
  └─ ValueExplanation? (Enum/Status)

BatteryTelemetry
  ├─ Timestamp (UTC), AssetId
  ├─ Soc, Soh
  ├─ ActivePowerKw, ReactivePowerKvar
  ├─ DcVoltage, DcCurrent
  ├─ TemperatureCelsius
  ├─ Availability, FaultStatus
  └─ DataQuality (valid, stale, substituted, protocolError, reason)

BatteryCommand
  ├─ CommandId, Timestamp
  ├─ AssetId
  ├─ Mode (Stop/Charge/Discharge/Idle)
  ├─ ActivePowerKw, ReactivePowerKvar?
  ├─ ValidUntil
  ├─ Reason (strukturiert)
  └─ Source (Schedule, Operator, RL, Safety, ...)

MarketCommitment
  ├─ Product (DayAhead/Intraday/FCR/aFRR...)
  ├─ Window [Start, End)
  ├─ PowerKw
  ├─ Direction (Charge/Discharge/ReserveUp/ReserveDown?)
  ├─ Penalty
  └─ BindingState

MarketPrice / TariffRule
  ├─ Product / PriceType
  ├─ Zone
  ├─ Validity DateRange
  ├─ TimeRange (weekday/custom/monthly, cross-day)
  ├─ Price + Unit
  └─ Priority

Schedule
  ├─ Type (DayAhead/Intraday/RLReserve)
  ├─ AssetId
  ├─ TimeStep, Zone, Version
  └─ Set<TimeSlot{[t0,t1), powerKw, socTarget?}>

OptimizationRun
  ├─ RunId, AssetId
  ├─ Horizon [Start, End), TimeStep
  ├─ InputVersions
  ├─ SolverName, SolverStatus
  ├─ ObjectiveValue + Breakdown
  ├─ Warnings / Violations
  ├─ Runtime / TerminationReason
  └─ ProducedScheduleVersion?
```

Vorzeichenkonvention: positiv = Entladen, negativ = Laden, 0 = neutral.
Konvention gilt in allen Modellen, Limitern, Persistenz und Commands;
Geräteumrechnung erfolgt ausschließlich in den Adaptern (LH §4.1).

Bezug: LH-DOM-001..006, LH-MKT-003/7/8/9, LH-OPT-007/8/9.

---

## 8. Schnittstellen

### 8.1 Interne Adapter-Interfaces

```csharp
public interface IBatteryTelemetrySource
{
    IAsyncEnumerable<BatteryTelemetry> ReadAsync(CancellationToken ct);
    AdapterStatus Status { get; }
}

public interface IBatteryCommandSink
{
    Task<CommandDispatchResult> WriteAsync(BatteryCommand cmd, CancellationToken ct);
}
```

Modbus, MQTT und OPC-UA implementieren dieselben Interfaces (LH-PROT-001,
LH-ARCH-005). Schreibadapter wenden die finale Schreibbegrenzung an
(LH-SAFE-007).

### 8.2 Optimierungs-Interface

Horizon-Optimierung und Echtzeit-Dispatch sind getrennte Ports. Der
Horizon-Optimierer erzeugt versionierbare Fahrpläne; der Dispatch-Optimizer
entscheidet im Regelzyklus nur über den aktuellen Sollwert.

```csharp
public interface IScheduleOptimizer
{
    Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request, CancellationToken ct);
}

public interface IDispatchOptimizer
{
    Task<DispatchResult> OptimizeAsync(
        DispatchRequest request, CancellationToken ct);
}
```

Der Basispfad löst importierte oder optimierte Fahrpläne über
`IScheduleTracker` auf. `IDispatchOptimizer` liefert daraus einen
regelkreisfähigen Single-Step-Wert und fällt bei fehlender gültiger
Vorgabe auf einen sicheren Idle-/Stop-Wert zurück. `IScheduleOptimizer`
stellt seit M2 LP-basierte Horizon-Optimierung bereit; MILP- und
heuristische Varianten bleiben austauschbare Solver-Ausprägungen. MPC
kann später `IDispatchOptimizer` für Single-Step-Entscheidungen oder
`IScheduleOptimizer` für rollierende Horizonte implementieren
(LH-OPT-001..009).

### 8.3 Preisquellen und Open-Source-Policy

Marktpreisquellen sind über einen quellenneutralen Port vom Optimierer
getrennt. Der Optimierer arbeitet mit normalisierten Preisreihen pro
Zeitschritt; er kennt keine Anbieter-API, Authentifizierung,
Rate-Limits oder Caching-Regeln.

```csharp
public interface IPriceSeriesSource
{
    Task<PriceSeries> LoadAsync(
        PriceSeriesRequest request, CancellationToken ct);
}
```

Open-Source-Default ist Import/API statt Anbieterbindung:

- Preisreihen können per API, Fahrplanimport oder Konfiguration
  eingebracht werden.
- Im Repository liegen keine API-Keys, keine gecachten Marktdaten mit
  unklarer Lizenz und keine Scraper gegen Marktportale.
- Tests verwenden synthetische oder eindeutig frei nutzbare Preisreihen.
- Externe Quellen werden als optionale Adapter implementiert und müssen
  Datenlizenz, Nutzungsbedingungen, Auth/API-Key-Anforderungen,
  Rate-Limits sowie erlaubtes Caching dokumentieren.
- Eine konkrete Anbieterintegration erfordert eine eigene
  Quellenentscheidung; bis dahin bleibt der Port source-agnostic.

Bezug: LH-OPEN-003, LH-MKT-008, LH-OPT-001.

### 8.4 Externe API

| Endpoint                                | Methode | Schutz                                      | LH-Bezug   |
| --------------------------------------- | ------- | ------------------------------------------- | ---------- |
| `/health`                               | GET     | intern/lokal; produktiv via Reverse Proxy   | LH-API-001 |
| `/health/regelleistung`                 | GET     | intern/lokal; produktiv via Reverse Proxy   | LH-MKT-005, LH-MON-002 |
| `/battery/{assetId}/status`             | GET     | read-only intern; produktiv TLS/optional AuthN | LH-API-002 |
| `/battery/{assetId}/command/current`    | GET     | read-only intern; produktiv TLS/optional AuthN | LH-API-003 |
| `/markets/schedules/current`            | GET     | read-only intern; produktiv TLS/optional AuthN | LH-API-004 |
| `/operator/stop`                        | POST    | AuthN+AuthZ + Audit                         | LH-API-006/007 |
| `/markets/day-ahead/optimize`           | POST    | AuthN+AuthZ + Audit                         | LH-API-005 |
| `/markets/intraday/reoptimize`          | POST    | AuthN+AuthZ + Audit                         | LH-MKT-002, LH-API-005 |
| `/optimization/runs/{runId}`            | GET     | read-only intern; produktiv TLS/optional AuthN | LH-PERSIST-007, LH-OPT-009 |

Produktiv: TLS-Terminierung oder dokumentierter Reverse-Proxy-Betrieb
(LH-API-008).

---

## 9. State Machine

```text
                ┌──────┐
                │ INIT │
                └──┬───┘
                   ▼
              ┌─────────┐ ─────────────────┐
              │ STANDBY │                  │
              └────┬────┘                  │
                   ▼                       │
              ┌─────────┐                  │
              │ READY   │ ◄─── Quittierung │
              └────┬────┘                  │
        ┌──────────┼─────────┐             │
        ▼          ▼         ▼             │
   ┌─────────┐ ┌──────┐ ┌─────────────┐    │
   │ IDLE    │ │CHARG.│ │ DISCHARGING │    │
   └────┬────┘ └──┬───┘ └──────┬──────┘    │
        └─────────┼────────────┘           │
                  ▼                        │
            ┌──────────┐                   │
            │ LIMITED  │                   │
            └────┬─────┘                   │
                 ▼                         │
        ┌──────────────────┐  ┌────────────┴────┐
        │ FAULT            │  │ MAINTENANCE     │
        └────────┬─────────┘  └─────────────────┘
                 ▼
        ┌──────────────────┐
        │ EMERGENCY_STOP   │   ◄── aus jedem Zustand erreichbar
        └──────────────────┘
```

`FAULT` und `EMERGENCY_STOP` übersteuern alle Betriebszustände
(LH-SM-002). `FAULT → READY` nur nach definierter Quittierung
(LH-SM-003).

Bezug: LH-SM-001..003, LH-SAFE-001.

---

## 10. Sicherheit und Fallback-Strategien

### 10.1 Prioritätsreihenfolge im Regelkreis

```text
1. Emergency Stop                       (LH-SAFE-001)
2. Batterie-/Wechselrichter-/Netzgrenzen (LH-CTRL-002, LH-SAFE-002/3)
3. Regelleistungsaktivierung            (LH-MKT-005)
4. Verbindliche Marktverpflichtungen    (LH-MKT-003)
5. Intraday-Fahrplan                    (LH-MKT-002)
6. Day-Ahead-Fahrplan                   (LH-MKT-001)
7. Lokale Optimierung                   (LH-OPT-*)
```

(LH-MKT-006)

### 10.2 Sicherer Zustand

Sicherer Zustand = `0 kW` oder explizites Stop-Command oder deaktivierte
Ausgabe + persistierter Reason. Tritt ein bei:

- ungültigem oder veraltetem Snapshot (LH-CTRL-007, LH-RT-003)
- Kommunikationsverlust (LH-SAFE-004)
- abgelaufenem Command-`ValidUntil` (LH-SAFE-005)
- unplausiblen Messwerten (LH-SAFE-006)
- aktivem Emergency Stop (LH-SAFE-001)
- Native-Core-Statusfehler (LH-NATIVE-004)

### 10.3 Abgrenzung zu Hardware-Schutz

Softwareseitige Stop-Funktionen ersetzen keinen Hardware-Not-Aus, keine
BMS-Schutztechnik und keine Wechselrichter-Schutzfunktionen. Harte
Echtzeit-/Schutzanforderungen werden außerhalb des Docker-EMS abgegrenzt
(LH-SAFE-001 Hinweis, LH-RISK-001). RM-M6-05 normiert diese Grenze so:
Das EMS bleibt supervisory/1-s-Dispatch; Edge-/Herstellercontroller,
BMS/PCS und Hardware-Schutzketten uebernehmen sub-cycle oder
zertifizierungsnahe Schutzaufgaben.

---

## 11. Persistenz

| Bereich            | Detail                                                  | LH-Bezug         |
| ------------------ | ------------------------------------------------------- | ---------------- |
| RDBMS              | PostgreSQL; TimescaleDB optionaler Folgeausbau          | LH-PERSIST-005   |
| Telemetrie         | Zeitstempel, AssetId, Werte, DataQuality, Quelle        | LH-PERSIST-001   |
| Commands           | jeder ausgegebene Command mit Reason und Source         | LH-PERSIST-002   |
| Fahrpläne          | versioniert für Day-Ahead, Intraday und Regelleistung   | LH-PERSIST-003   |
| Optimierungsläufe  | RunId, Inputs, Solverstatus, Objective Breakdown, erzeugte Fahrplanversion | LH-PERSIST-007 |
| Operator-Audit     | Operator, Zeit, Aktion, Begründung, Ergebnis            | LH-PERSIST-004, LH-OPS-004 |
| Retention          | konfigurierbar, getrennt je Datentyp, kein Auto-Delete von Audit | LH-PERSIST-006 |
| Persistenzfehler   | definiertes Verhalten, kein undefinierter Regelbetrieb  | LH-PERSIST-006   |

Migrations-Strategie: versionierter Pfad ab M2 (RM-M2-MIG-05) —
DDL aus einer neutralen `schema.yaml` per `d-migrate` (Build-Time)
generiert, zur Laufzeit per `DbUp` mit Tracking-Tabelle
`__schema_versions` angewendet. EF Core Migrations und FluentMigrator
sind als Alternativen geprüft und mit Begründung ausgeschlossen worden. `BessDbMigrator.MigrateAsync` ist
idempotent beim Worker-Start anwendbar und setzt vor DbUp einen
`pg_advisory_lock(hashtextextended('bess-ems:migrations', 0))`, sodass
mehrere Repliken sicher boot-rennen können (RM-M2-MIG-OPEN-06).

---

## 12. Konfiguration

- Quelle: YAML/JSON-Dateien + Environment Variables, hierarchisch überlagernd.
- Bereiche: Assets, Capabilities, Device Points, Adapter, Mappings, Limits,
  Rampen, Markt-/Tarifparameter, Optimierungsparameter, Sicherheitsparameter
  und Northbound-Exports (LH-CONF-001/004).
- Mappings (Modbus, MQTT, OPC-UA) versioniert in `config/`
  (LH-CONF-002).
- Validierung beim Start; bei Fehlern kein aktiver Regelbetrieb (LH-CONF-003,
  LH-OPS-001).

```text
config/
├─ assets/{assetId}.yaml
├─ adapters/modbus/{deviceProfile}.yaml
├─ adapters/mqtt/{deviceProfile}.yaml
├─ adapters/opcua/{deviceProfile}.yaml
├─ device-points/{profile}.yaml
├─ control/limits.yaml
├─ control/ramps.yaml
├─ markets/zones.yaml
├─ markets/tariffs.yaml
├─ exports/{target}.yaml                    # optionaler Folgeausbau
└─ safety/profiles.yaml
```

---

## 13. Native-Core-Strategie

### 13.1 Phasenmodell

```text
Phase 1 (M1/M2)      : .NET-only, kein Native Core
Phase 2 (M3)         : Native Library via P/Invoke
                       (Constraint, Ramp, PID, schnelle Plausi)
Phase 3 (M5)         : Native/externes Sidecar via gRPC
                       (MPC, State-Space, Solver-Anbindung)
Phase 4 (M6)         : Multi-Asset, UI, Kubernetes, Timescale-Option,
                       Edge-/Zertifizierungsgates ohne harte RT-Zusage
```

### 13.2 Bibliothek vs. Sidecar — Entscheidungskriterien

| Kriterium                        | Library (P/Invoke)        | Sidecar (gRPC)          |
| -------------------------------- | ------------------------- | ----------------------- |
| Latenz                           | sehr niedrig              | mittel                  |
| Crash-Isolation                  | nein (Prozessabsturz)     | ja                      |
| Deployment                       | ein Container             | zwei Prozesse           |
| Geeignet für                     | Limiter, Rampen, PID      | MPC, Solver, große Kerne |
| ABI-Stabilität                   | hoch erforderlich         | nur Protobuf-Vertrag    |

### 13.3 ABI-Regeln

- Stabile C-ABI, keine C++-Klassen/Exceptions exportieren (LH-NATIVE-002).
- Keine Speicherallokation über die Sprachgrenze (LH-NATIVE-003).
- Fehler über Statuscodes (LH-NATIVE-004).
- ABI-Version über Funktion abfragbar; Worker prüft beim Start
  (LH-NATIVE-005).
- Native Komponenten reproduzierbar im Docker-Multi-Stage-Build
  (LH-NATIVE-006, LH-DEPLOY-003/4).

### 13.4 Fallback

`BatteryEms.Adapters.NativeInterop` (`NativeFallbackControlKernel`)
implementiert denselben `IControlKernel`-Driven-Port wie die
.NET-Referenzimplementierung (`ManagedControlKernel`). Bei
fehlender Bibliothek, ABI-Mismatch oder nativem Fehler aus
validem .NET-Kontext (`BCC_STATUS_INVALID_INPUT` /
`BCC_STATUS_NON_FINITE` / `BCC_STATUS_NEGATIVE_DT` /
`BCC_STATUS_UNSUPPORTED_STATE`) ruft der Adapter im selben Tick
die Managed-Referenz und nutzt deren Ergebnis (Source =
`NativeFallbackToManaged`); der Regelkreis bleibt funktionsfähig
(LH-ARCH-006).

Diese Default-Policy gilt verbindlich für M3. Eine produktive
Deployment-Variante darf zusätzlich
`NativeControlOptions.AbortOnAbiMismatch=true` setzen — dann führt
ein ABI-Mismatch beim Startup-Check zu einem harten Fehler statt
zum Managed-Fallback. Die Abort-Policy ist explizit Opt-in,
hat einen eigenen Integrationstest und überspielt nicht den
Default-Fallback-Vertrag (siehe `docs/user/quality.md` §5.2).

---

## 14. Beobachtbarkeit

| Aspekt    | Umsetzung                                                          | LH-Bezug   |
| --------- | ------------------------------------------------------------------ | ---------- |
| Logs      | strukturierte JSON-Logs mit AssetId, Komponente, Reason, Decision  | LH-MON-001 |
| Metriken  | Regelzyklusdauer, Snapshot-Güte, Fehlerquote, SOC, Power, Solverzeit | LH-MON-002 |
| Tracing   | OpenTelemetry-Spans über Snapshot → Control → Adapter              | LH-MON-003 |
| Reason    | jeder Command führt strukturiertes Reason-Feld (Codes + Detail)    | LH-MON-004, LH-NF-008 |

Export: Prometheus-Endpoint, OTLP für Traces, stdout-Log (Container-konform).

---

## 15. Deployment-Sicht

```text
docker-compose.yml
├─ bess-ems               # ein OCI-Image mit Worker + API
├─ postgres               # PostgreSQL
├─ mosquitto              # MQTT-Broker (lokal/Test)
└─ monitoring (optional)  # Prometheus, Grafana, Tempo/Jaeger
```

Der kombinierte Worker/API-Host ist die Default-Topologie. Eine spaetere
API-Auskopplung als eigener `bess-ems-api`-Service aus demselben
Codebestand oder als separates Image ist trigger-basiert;
sie ist kein impliziter `replicaCount > 1`-Skalierungsschritt fuer den
gemeinsamen Host.

Bezug: LH-DEPLOY-001/2/3, LH-NF-003/4.

Dockerfile: .NET-Build-Stage + schlankes Runtime-Image. Falls Native Core
aktiviert wird, ergänzt ein optionaler nativer Build-Stage die `.so` und
deponiert sie in `/usr/local/lib` (LH-DEPLOY-004).

---

## 16. Testarchitektur

| Stufe              | Inhalt                                                 | LH-Bezug    | Geltung                    |
| ------------------ | ------------------------------------------------------ | ----------- | -------------------------- |
| Unit               | Domain, State Machine, Limiter, Snapshot-Validierung    | LH-TEST-001 | M1                         |
| Unit (Markt)       | Day-Ahead-Logik, Zeitmodell, Sommerzeit                 | LH-TEST-002 | M1/M2                      |
| Integration        | Modbus/MQTT-Adapter gegen Simulatoren                   | LH-TEST-003 | M1                         |
| Replay             | historische Telemetrie → reproduzierbare Commands       | LH-TEST-004 | M2                         |
| Native Interop     | Struct-Layout, ABI-Version, Fehlercodes, Werte-Parität  | LH-TEST-005 | M3 / falls eingesetzt      |
| Sicherheitsfälle   | Emergency Stop, BMS-Ausfall, stale snapshot, ungültiger Command | LH-TEST-006 | M1                 |
| Container          | Image-Boot, Healthcheck; Native Library geladen, falls Native Core eingesetzt wird | LH-TEST-007 | M1 / Native optional |

Empfehlung: .NET-Referenzregler parallel zum Native Core pflegen und in
Replay-Tests gegeneinander vergleichen, um ABI- und Rundungsfehler früh
zu finden.

---

## 17. Rückverfolgbarkeit Architektur ↔ Lastenheft

| Architekturkomponente         | LH-Anforderung(en)                            |
| ----------------------------- | --------------------------------------------- |
| Schichten 1–9                 | LH-ARCH-001/002                               |
| Hexagonale Sicht (§4.2)       | LH-ARCH-001..005, LH-NF-006/007/008           |
| Architektur-Tabus / Boundary-Tests | LH-ARCH-002, LH-NF-006                   |
| Adapter-Interface             | LH-PROT-001, LH-ARCH-005                      |
| Device Point / Capability Model | LH-DOM-005/006, LH-CONF-002                 |
| Tarif- und Marktproduktmodell | LH-MKT-008/009                                |
| Optimierungs-Interfaces       | LH-ARCH-004, LH-OPT-001/002/006/007/008/009   |
| Snapshot Store                | LH-RT-001/002/003/005                         |
| Control Layer (Limiter, SM)   | LH-CTRL-001/002/003/007, LH-SM-001/002/003    |
| Sicherheitsfallback           | LH-SAFE-001..007, LH-CTRL-007                 |
| Native Core                   | LH-NATIVE-001..006, LH-ARCH-006, LH-NF-002    |
| Persistenz                    | LH-PERSIST-001..007                           |
| API + AuthN/AuthZ             | LH-API-001..008                               |
| Observability                 | LH-MON-001..004, LH-NF-008                    |
| Konfiguration                 | LH-CONF-001..004                              |
| Deployment                    | LH-DEPLOY-001..004, LH-NF-003/004             |
| Tests                         | LH-TEST-001..007                              |

---

## 18. Offene architektonische Punkte

| Kennung    | Frage                                                               | Status |
| ---------- | ------------------------------------------------------------------- | ------ |
| AR-OPEN-004 | Fahrplanimport-Format (CSV, JSON, ENTSO-E, proprietär)?           | Offen  |
| AR-OPEN-007 | Authentifizierungsverfahren (API-Token, OIDC, mTLS)?              | Teilweise geschlossen — API-Token + Operator-Rolle sind mit RM-M1-16 live; OIDC und mTLS sind Folge-ADR-Trigger (konsistent mit RM-OPEN-04 in der Roadmap). |
| AR-OPEN-012 | MPC-Backend-Topologie: in-process Local-First vs. Sidecar-First vs. Bi-Modal? | Reserviert — heute geschlossen auf Local-First mit OSQP; Sidecar-Erweiterung ist F-M5-12-Folgearbeit mit fünf Triggern (siehe `docs/plan/planning/open/note-RM-M5-followups.md`). |

---

## 19. Zusammenfassung

`bess-ems` ist als geschichtetes, modular-erweiterbares System konzipiert.
Der Regelkreis ist die invariante Spine; Markt, Optimierung und Native
Core sind austauschbar daran angedockt. Die wichtigste Architekturregel
bleibt:

```text
Optimierung liefert Wunschwerte.
Der technische Regelkreis entscheidet, was sicher gefahren wird.
```

Diese Trennung wird durch klare Schichten, einheitliche interne Modelle,
adapterneutrale Schreibpfade und einen optionalen, austauschbaren Native
Core getragen.
