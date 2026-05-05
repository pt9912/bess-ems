# Architektur: bess-ems

**Projektname:** bess-ems
**Dokumenttyp:** Architekturbeschreibung
**Format:** Markdown
**Version:** 0.1.1
**Status:** Entwurf
**Bezug:** [`lastenheft.md`](lastenheft.md), [`idea.md`](idea.md)

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
   │   API (REST/gRPC) ◄──── Operator UI / API-Client              │
   │                                                               │
   │   Worker (Regelkreis, Markt, Adapter-Orchestrierung)          │
   │                                                               │
   │   Native Core (optional, performance-kritisch)                │
   └─────┬───────────────────┬──────────────────┬──────────────────┘
         │                   │                  │
   ┌─────▼─────┐      ┌──────▼──────┐    ┌──────▼──────────────┐
   │ Modbus TCP│      │ MQTT Broker │    │ OPC-UA (nach MVP)   │
   └─────┬─────┘      └──────┬──────┘    └──────┬──────────────┘
         │                   │                  │
   ┌─────▼───────────────────▼──────────────────▼──────────────┐
   │   BMS / Wechselrichter / Zähler / PV-Messung / RL-Signal  │
   └───────────────────────────────────────────────────────────┘

   Persistenz: PostgreSQL (TimescaleDB nach MVP)
   Monitoring: strukturierte Logs, Metriken, OpenTelemetry-Traces
```

Bezug: LH-KTX-001, LH-KTX-002, LH-PERSIST-005.

---

## 4. Schichtenarchitektur

```text
┌─────────────────────────────────────────────────────────────┐
│ API Layer                                                   │  HTTP/gRPC, AuthN/AuthZ
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

**Abhängigkeitsregel:** obere Schichten kennen untere; untere Schichten kennen
nur Domain-Abstraktionen, niemals konkrete Adapter.

Bezug: LH-ARCH-002.

---

## 5. Komponentensicht

### 5.1 .NET-Module

| Modul                              | Verantwortung                                                | LH-Bezug             |
| ---------------------------------- | ------------------------------------------------------------ | -------------------- |
| `BatteryEms.Api`                   | REST-API, AuthN/AuthZ, Operator-Endpunkte, Audit             | LH-API-*             |
| `BatteryEms.Worker`                | Hosting, Regelzyklus, Scheduler, Hosted Services             | LH-CTRL-001, LH-OPS-*|
| `BatteryEms.Domain`                | Domain-Modell, Wertobjekte, Vorzeichenkonvention             | LH-DOM-*             |
| `BatteryEms.Realtime`              | Snapshot Store, Datenfusion, Datenqualität, Aging            | LH-RT-*, LH-DOM-004  |
| `BatteryEms.Control`               | State Machine, Constraint Limiter, Ramp Limiter, PID         | LH-CTRL-*, LH-SM-*   |
| `BatteryEms.Markets`               | Day-Ahead, Intraday (n. MVP), Verpflichtungen, Zeitmodell    | LH-MKT-*             |
| `BatteryEms.Optimization`          | Optimierungs-Interface, Solver-Abstraktion, Zielfunktion     | LH-OPT-*             |
| `BatteryEms.Optimization.NoOp`     | Pass-Through-Optimierer für MVP                              | LH-OPT-001 (MVP)     |
| `BatteryEms.Protocols.Abstractions`| Adapter-Interfaces, Datenqualitätsmapping                    | LH-PROT-*, LH-ARCH-005 |
| `BatteryEms.Protocols.Modbus`      | Modbus-TCP-Adapter                                           | LH-MODB-*            |
| `BatteryEms.Protocols.Mqtt`        | MQTT-Adapter (Telemetrie + Commands)                         | LH-MQTT-*            |
| `BatteryEms.Protocols.OpcUa`       | OPC-UA-Adapter (nach MVP)                                    | LH-OPCUA-*           |
| `BatteryEms.Persistence`           | Repositories, EF Core / Dapper, Migrationen, Retention       | LH-PERSIST-*         |
| `BatteryEms.Infrastructure`        | Logging, Metriken, Tracing, Config-Loader, Validierung       | LH-MON-*, LH-CONF-*  |
| `BatteryEms.NativeInterop`         | P/Invoke-Bindings, ABI-Check, Fallback-Routing               | LH-NATIVE-*          |

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
   │  Regelleistungspriorisierung (n. MVP) ◄── RL-Aktivierung │
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
  └─ OperatingState

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
  ├─ Market (DayAhead/Intraday/RL-Produkt)
  ├─ Window [Start, End)
  ├─ PowerKw
  ├─ Penalty
  └─ BindingState

Schedule
  ├─ Type (DayAhead/Intraday/RLReserve)
  ├─ AssetId
  ├─ TimeStep, Zone, Version
  └─ Set<TimeSlot{[t0,t1), powerKw, socTarget?}>
```

Vorzeichenkonvention: positiv = Entladen, negativ = Laden, 0 = neutral.
Konvention gilt in allen Modellen, Limitern, Persistenz und Commands;
Geräteumrechnung erfolgt ausschließlich in den Adaptern (LH §4.1).

Bezug: LH-DOM-001/2/3, LH-MKT-003/7.

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

```csharp
public interface IDispatchOptimizer
{
    Task<DispatchResult> OptimizeAsync(
        DispatchRequest request, CancellationToken ct);
}
```

Im MVP: `NoOpDispatchOptimizer` reicht den importierten Day-Ahead-Fahrplan
durch (LH-OPT-001 als Interface). Spätere Solver-Implementierungen
(LP/MILP/MPC) implementieren dasselbe Interface (LH-OPT-002/006).

### 8.3 Externe API (MVP)

| Endpoint                                | Methode | Schutz                                      | LH-Bezug   |
| --------------------------------------- | ------- | ------------------------------------------- | ---------- |
| `/health`                               | GET     | intern/lokal; produktiv via Reverse Proxy   | LH-API-001 |
| `/battery/{assetId}/status`             | GET     | read-only intern; produktiv TLS/optional AuthN | LH-API-002 |
| `/battery/{assetId}/command/current`    | GET     | read-only intern; produktiv TLS/optional AuthN | LH-API-003 |
| `/markets/schedules/current`            | GET     | read-only intern; produktiv TLS/optional AuthN | LH-API-004 |
| `/operator/stop`                        | POST    | AuthN+AuthZ + Audit                         | LH-API-006/007 |
| `/markets/day-ahead/optimize`           | POST    | nach MVP                                    | LH-API-005 |

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
3. Regelleistungsaktivierung (n. MVP)   (LH-MKT-005)
4. Verbindliche Marktverpflichtungen    (LH-MKT-003)
5. Intraday-Fahrplan (n. MVP)           (LH-MKT-002)
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
(LH-SAFE-001 Hinweis, LH-RISK-001).

---

## 11. Persistenz

| Bereich            | Detail                                                  | LH-Bezug         |
| ------------------ | ------------------------------------------------------- | ---------------- |
| RDBMS              | PostgreSQL (MVP), TimescaleDB optional (n. MVP)         | LH-PERSIST-005   |
| Telemetrie         | Zeitstempel, AssetId, Werte, DataQuality, Quelle        | LH-PERSIST-001   |
| Commands           | jeder ausgegebene Command mit Reason und Source         | LH-PERSIST-002   |
| Fahrpläne          | versioniert (Day-Ahead MVP; Intraday/RL nach MVP)       | LH-PERSIST-003   |
| Operator-Audit     | Operator, Zeit, Aktion, Begründung, Ergebnis            | LH-PERSIST-004, LH-OPS-004 |
| Retention          | konfigurierbar, getrennt je Datentyp, kein Auto-Delete von Audit | LH-PERSIST-006 |
| Persistenzfehler   | definiertes Verhalten, kein undefinierter Regelbetrieb  | LH-PERSIST-006   |

Migrations-Strategie: EF Core Migrations oder FluentMigrator, idempotent
beim Worker-Start anwendbar.

---

## 12. Konfiguration

- Quelle: YAML/JSON-Dateien + Environment Variables, hierarchisch überlagernd.
- Bereiche: Assets, Adapter, Mappings, Limits, Rampen, Marktparameter,
  Optimierungsparameter, Sicherheitsparameter (LH-CONF-001).
- Mappings (Modbus, MQTT, später OPC-UA) versioniert in `config/`
  (LH-CONF-002).
- Validierung beim Start; bei Fehlern kein aktiver Regelbetrieb (LH-CONF-003,
  LH-OPS-001).

```text
config/
├─ assets/{assetId}.yaml
├─ adapters/modbus/{deviceProfile}.yaml
├─ adapters/mqtt/{deviceProfile}.yaml
├─ adapters/opcua/{deviceProfile}.yaml      # n. MVP
├─ control/limits.yaml
├─ control/ramps.yaml
├─ markets/zones.yaml
└─ safety/profiles.yaml
```

---

## 13. Native-Core-Strategie

### 13.1 Phasenmodell

```text
Phase 1 (MVP)        : .NET-only, kein Native Core
Phase 2 (post-MVP)   : Native Library via P/Invoke
                       (Constraint, Ramp, PID, schnelle Plausi)
Phase 3 (later)      : Native Sidecar via gRPC
                       (MPC, State-Space, Solver-Anbindung)
Phase 4 (optional)   : Shared Memory / CPU Pinning / Edge Controller
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

`BatteryEms.NativeInterop` exportiert dasselbe Interface wie die
.NET-Referenzimplementierung. Bei fehlender Bibliothek, ABI-Mismatch oder
Native-Fehler greift automatisch die .NET-Variante; der Regelkreis bleibt
funktionsfähig (LH-ARCH-006).

---

## 14. Beobachtbarkeit

| Aspekt    | Umsetzung                                                          | LH-Bezug   |
| --------- | ------------------------------------------------------------------ | ---------- |
| Logs      | strukturierte JSON-Logs mit AssetId, Komponente, Reason, Decision  | LH-MON-001 |
| Metriken  | Regelzyklusdauer, Snapshot-Güte, Fehlerquote, SOC, Power, Solverzeit | LH-MON-002 |
| Tracing   | OpenTelemetry-Spans über Snapshot → Control → Adapter (n. MVP)     | LH-MON-003 |
| Reason    | jeder Command führt strukturiertes Reason-Feld (Codes + Detail)    | LH-MON-004, LH-NF-008 |

Export: Prometheus-Endpoint, OTLP für Traces, stdout-Log (Container-konform).

---

## 15. Deployment-Sicht

```text
docker-compose.yml
├─ bess-ems               # MVP: ein eigenes OCI-Image mit Worker + API
├─ postgres               # PostgreSQL
├─ mosquitto              # MQTT-Broker (lokal/Test)
└─ monitoring (optional)  # Prometheus, Grafana, Tempo/Jaeger
```

Nach dem MVP kann die API bei Bedarf als eigener `bess-ems-api`-Service
aus demselben Codebestand oder als separates Image ausgekoppelt werden
(AR-OPEN-001).

Bezug: LH-DEPLOY-001/2/3, LH-NF-003/4.

MVP-Dockerfile: .NET-Build-Stage + schlankes Runtime-Image ohne Native Core.
Falls Native Core aktiviert wird, ergänzt ein optionaler nativer Build-Stage
die `.so` und deponiert sie in `/usr/local/lib` (LH-DEPLOY-004).

---

## 16. Testarchitektur

| Stufe              | Inhalt                                                 | LH-Bezug    |
| ------------------ | ------------------------------------------------------ | ----------- |
| Unit               | Domain, State Machine, Limiter, Snapshot-Validierung    | LH-TEST-001 |
| Unit (Markt)       | Day-Ahead-Logik, Zeitmodell, Sommerzeit                 | LH-TEST-002 |
| Integration        | Modbus/MQTT-Adapter gegen Simulatoren                   | LH-TEST-003 |
| Replay             | historische Telemetrie → reproduzierbare Commands       | LH-TEST-004 |
| Native Interop     | Struct-Layout, ABI-Version, Fehlercodes, Werte-Parität  | LH-TEST-005 |
| Sicherheitsfälle   | Emergency Stop, BMS-Ausfall, stale snapshot, ungültiger Command | LH-TEST-006 |
| Container          | Image-Boot, Healthcheck; Native Library geladen, falls Native Core eingesetzt wird | LH-TEST-007 |

Empfehlung: .NET-Referenzregler parallel zum Native Core pflegen und in
Replay-Tests gegeneinander vergleichen, um ABI- und Rundungsfehler früh
zu finden.

---

## 17. Rückverfolgbarkeit Architektur ↔ Lastenheft

| Architekturkomponente         | LH-Anforderung(en)                            |
| ----------------------------- | --------------------------------------------- |
| Schichten 1–9                 | LH-ARCH-001/002                               |
| Adapter-Interface             | LH-PROT-001, LH-ARCH-005                      |
| Optimierungs-Interface        | LH-ARCH-004, LH-OPT-001/002/006               |
| Snapshot Store                | LH-RT-001/002/003/005                         |
| Control Layer (Limiter, SM)   | LH-CTRL-001/002/003/007, LH-SM-001/002/003    |
| Sicherheitsfallback           | LH-SAFE-001..007, LH-CTRL-007                 |
| Native Core                   | LH-NATIVE-001..006, LH-ARCH-006, LH-NF-002    |
| Persistenz                    | LH-PERSIST-001..006                           |
| API + AuthN/AuthZ             | LH-API-001..008                               |
| Observability                 | LH-MON-001..004, LH-NF-008                    |
| Konfiguration                 | LH-CONF-001..003                              |
| Deployment                    | LH-DEPLOY-001..004, LH-NF-003/004             |
| Tests                         | LH-TEST-001..007                              |

---

## 18. Offene architektonische Punkte

| Kennung    | Frage                                                               | Status |
| ---------- | ------------------------------------------------------------------- | ------ |
| AR-OPEN-001 | API-Komponente eigener Service oder im Worker integriert?         | Offen  |
| AR-OPEN-002 | gRPC vs. REST-only für externe Optimierungs-Sidecars in Phase 3?  | Offen  |
| AR-OPEN-003 | Persistenz-Stack: EF Core, Dapper oder Mischung?                  | Offen  |
| AR-OPEN-004 | Fahrplanimport-Format (CSV, JSON, ENTSO-E, proprietär)?           | Offen  |
| AR-OPEN-005 | Konkrete Topic-/Registerprofile für die ersten Hersteller?        | Offen, Bezug LH-OPEN-001 |
| AR-OPEN-006 | Strategie für Multi-Asset-Hosting (Worker-pro-Asset vs. shared)?  | Offen  |
| AR-OPEN-007 | Authentifizierungsverfahren (API-Token, OIDC, mTLS)?              | Offen  |

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
