# bess-ems

Battery Energy Management System (EMS) für Battery Energy Storage Systems (BESS).

`bess-ems` plant, überwacht und steuert Batteriespeicher unter Berücksichtigung
von Marktmechanismen, Echtzeitmessdaten, technischen Grenzwerten und
Sicherheitsanforderungen. Das System ist modular aufgebaut, containerisiert und
so konzipiert, dass Marktoptimierung und technische Regelung strikt getrennt
sind.

> **Status:** Entwurf — derzeit nur Spezifikation, noch keine Implementierung.
> Maßgebliches Dokument: [`spec/lastenheft.md`](spec/lastenheft.md).

---

## Kernidee

```text
Messdaten
→ Validierung
→ Snapshot
→ State Machine
→ Markt-/Fahrplanauflösung
→ Regelleistungspriorisierung (falls aktiviert)
→ Constraint Limiter
→ Ramp Limiter
→ Command
→ Protokolladapter
```

Die zentrale Architekturregel:

> Optimierung liefert Wunschwerte.
> Der technische Regelkreis entscheidet, was sicher gefahren wird.

Optimierungsergebnisse werden niemals direkt an Batteriesysteme gesendet,
sondern stets über State Machine, Constraint Limiter und Ramp Limiter geführt.

---

## Funktionsumfang

Das System adressiert unter anderem:

- Day-Ahead-Markt, Intraday-Markt, Regelleistung
- Lade- und Entladeregelung mit Leistungsrampen
- Zustandsmaschinen für Betriebs- und Sicherheitszustände
- PID-Regelung, MPC / State-Space-Modelle (perspektivisch)
- LP / MILP / heuristische Optimierung über austauschbares Solver-Interface
- Feldkommunikation über Modbus TCP, MQTT und (nach MVP) OPC-UA
- Echtzeitnahe Messdatenverarbeitung mit Datenqualitätsbewertung
- Persistenz, Auditierbarkeit und Monitoring

---

## Technologie-Stack

| Bereich                        | Technologie                                     |
| ------------------------------ | ----------------------------------------------- |
| Hauptplattform                 | C# / .NET                                       |
| Performance-kritischer Kern    | C / C++ (optionaler Native Core über C-ABI)     |
| Persistenz (MVP)               | PostgreSQL (TimescaleDB nach MVP)               |
| Messaging                      | MQTT, Modbus TCP, OPC-UA (nach MVP)             |
| Betrieb                        | Linux, Docker, Docker Compose                   |
| Observability                  | Strukturierte Logs, Metriken, OpenTelemetry     |

---

## Architekturschichten

- Domain Layer
- Market Layer
- Optimization Layer
- Realtime Layer
- Control Layer
- Protocol Adapter Layer
- Infrastructure Layer
- API Layer
- Native Core Layer (optional)

Protokolladapter enthalten ausschließlich Transformationen zwischen externen
Signalen und internen Modellen — keine Markt-, Optimierungs- oder
Regelentscheidungen.

---

## Vorzeichenkonvention

Wirkleistung am Batteriespeicher:

- `> 0 kW` → Entladen / Einspeisen
- `< 0 kW` → Laden / Bezug
- `0 kW` → kein aktiver Lade- oder Entladebefehl

Diese Konvention wird intern einheitlich verwendet. Abweichende
Gerätekonventionen werden ausschließlich in Protokolladaptern abgebildet.

---

## Sicherheitsprinzipien

Sicherer Zustand im MVP bedeutet:

- kein aktiver Lade- oder Entladebefehl
- keine Weiterleitung veralteter oder ungültiger Commands
- Ausgabe eines `0 kW`-Commands, eines expliziten Stop-Commands oder
  Deaktivierung der Ausgabe
- persistierter und geloggter Grund

Prioritätsreihenfolge im Regelkreis:

1. Emergency Stop
2. Batterie-, Wechselrichter- und Netzgrenzen
3. Regelleistungsaktivierung
4. verbindliche Marktverpflichtungen
5. Intraday-Fahrplan
6. Day-Ahead-Fahrplan
7. lokale Optimierung

Softwareseitige Stop- und Sicherheitsfunktionen ersetzen keinen hardwareseitigen
Not-Aus, keine BMS-Schutztechnik und keine herstellerspezifischen
Wechselrichter-Schutzfunktionen. Harte Echtzeit- und zertifizierungsrelevante
Funktionen sind außerhalb des Docker-basierten EMS abzubilden.

---

## MVP-Abgrenzung

### Bestandteil des MVP

- C#/.NET Worker Service
- Domain-Modell, Realtime Snapshot Store, State Machine
- Constraint Limiter, Ramp Limiter
- Optimization-Interface (ohne produktiven Solver)
- MQTT- und Modbus-TCP-Adapter
- statischer Fahrplanimport, einfache Day-Ahead-Fahrplanverfolgung
- PostgreSQL-Persistenz
- Health-, Status-, Command-, Fahrplan- und Operator-Stop-API
  (schreibend mit AuthN/AuthZ und Audit-Log)
- strukturierte Logs
- Docker Compose
- Unit Tests für Kernlogik

### Nach MVP

OPC-UA-Adapter, Intraday-Reoptimierung, Regelleistungsreservierung
und -aktivierung, MILP-Optimierung, Native C/C++ Control Core,
OpenTelemetry-Tracing, Replay-Testumgebung.

### Spätere Erweiterungen

MPC, State-Space-Modelle, Kalman-Filter, native Solver-Sidecar, TimescaleDB,
Operator UI, Multi-Asset-Flottensteuerung, Kubernetes-Deployment,
zertifizierungsnahe Regelleistungsintegration.

---

## Repository-Struktur

```text
.
├── README.md             # dieses Dokument
├── LICENSE               # MIT-Lizenz
└── spec/
    ├── idea.md           # Projektidee / Hintergrund
    ├── lastenheft.md     # Lastenheft (V-Modell-ähnliche Anforderungsstruktur)
    └── architecture.md   # Architekturentwurf (Schichten, Module, Datenfluss)
```

Implementierungsmodule (Worker, API, Domain, Adapter, Infrastructure, Native
Core) folgen gemäß Lastenheft.

---

## Vorgehensmodell

V-Modell-ähnliche Anforderungsstruktur mit Kennungen und Rückverfolgbarkeit von
Anforderung → Design → Implementierung → Test. Anforderungen sind im
Lastenheft mit Präfixen (z. B. `LH-CTRL-002`, `LH-SAFE-001`) eindeutig
referenzierbar.

---

## Weiterführend

- [`spec/lastenheft.md`](spec/lastenheft.md) — vollständige Anforderungen,
  Abnahmekriterien, Rückverfolgbarkeitstabellen, Risiken und offene Punkte
- [`spec/architecture.md`](spec/architecture.md) — Architekturentwurf:
  Schichten, Module, Datenfluss, Native-Core-Strategie, Rückverfolgbarkeit
- [`spec/idea.md`](spec/idea.md) — Projektidee und Hintergrund

---

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](LICENSE).
