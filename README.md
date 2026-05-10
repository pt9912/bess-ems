# bess-ems

Battery Energy Management System (EMS) für Battery Energy Storage Systems (BESS).

`bess-ems` plant, überwacht und steuert Batteriespeicher unter Berücksichtigung
von Marktmechanismen, Echtzeitmessdaten, technischen Grenzwerten und
Sicherheitsanforderungen. Das System ist modular aufgebaut, containerisiert und
so konzipiert, dass Marktoptimierung und technische Regelung strikt getrennt
sind.

> **Status:** M1, M2 und M3 sind abgeschlossen ([docs/plan/planning/in-progress/roadmap.md](docs/plan/planning/in-progress/roadmap.md))

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

| Bereich                     | Technologie                                      |
| --------------------------- | ------------------------------------------------ |
| Hauptplattform              | C# / .NET 10                                     |
| API                         | ASP.NET Core Minimal API                         |
| Performance-kritischer Kern | C / C++ (optionaler Native Core über C-ABI)      |
| Persistenz (MVP)            | PostgreSQL über Dapper + Npgsql                  |
| Messaging / Feldbus         | MQTTnet, FluentModbus; OPC-UA nach MVP           |
| Simulator                   | Go-Feldsimulator für Modbus/MQTT                 |
| Betrieb / Gates             | Linux, Docker, Dockerfile-Stages, Makefile       |
| Observability               | Strukturierte Logs; Prometheus/OTel schrittweise |

---

## Architekturschichten

Ausführliche Definitionen, Verantwortlichkeiten und Grenzen sind in
[`spec/architecture.md`](spec/architecture.md) dokumentiert.

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

Der Go-Feldsimulator unter `simulators/bess-field-sim` ist ein Testwerkzeug:
MQTT läuft dort bewusst anonym und plaintext über `tcp://` ohne TLS oder
Credentials. Dies ist nur für lokale Mosquitto-/CI-Simulationen gedacht und
nicht für Produktions-Broker.

---

## MVP-Abgrenzung

### Zielumfang des MVP

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


---

## Repository-Struktur

Die verbindliche Modul- und Schichtenstruktur ist in
[`spec/architecture.md`](spec/architecture.md) beschrieben. Die wichtigsten
Einstiegspunkte im Repository sind:

- `BatteryEms.sln` — .NET-Solution
- `src/hexagon/` — Domain und Application Core
- `src/adapters/` — Driving Adapter (API, Worker) und Driven Adapter
  (Modbus, MQTT, Persistence, Optimization, Telemetry)
- `src/infrastructure/` — Infrastruktur-Implementierungen
- `tests/` — Architektur-, Unit- und Integrationstests
- `config/` — JSON-Schemas und Beispielkonfigurationen
- `simulators/bess-field-sim/` — Go-Feldsimulator
- `docs/plan/` — Roadmap, aktive und offene Detailpläne


---

## Lokale Gates

Die aktiven Gate-Ziele sind im `Makefile` sichtbar:

```bash
make help
make lint
make arch-check
make test
make test-safety
make coverage-gate
make simulator-test
make test-integration
```

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
- [`docs/plan/planning/in-progress/roadmap.md`](docs/plan/planning/in-progress/roadmap.md) —
  Meilensteine M1–M6 mit Liefergegenständen und LH-Rückverfolgbarkeit
- [`docs/user/quality.md`](docs/user/quality.md) — verbindliche
  Qualitäts- und Messpfade (statische Analyse C#/.NET + C/C++, Tests,
  Coverage, Vertrags-Gates, Native-Parity, CI/Release)
- [`docs/archive/idea.md`](docs/archive/idea.md) — historische Native-Core-
  Ideenskizze, nicht normativ

---

## Lizenz

Veröffentlicht unter der [MIT-Lizenz](LICENSE).
