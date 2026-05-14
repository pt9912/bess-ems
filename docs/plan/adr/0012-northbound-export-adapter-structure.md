# ADR 0012 - Northbound-Export bleibt unimplementiert (Trigger-basiert)

**Status:** Accepted - `bess-ems` implementiert heute keinen
Northbound-Export-Pfad. Die Struktur-Frage (eigener Adapter vs.
Telemetry-Adapter-Untermodul) wird beim ersten konkreten Konsumenten
entschieden, nicht spekulativ. Schliesst `AR-OPEN-011` und liefert
`LH-CONF-004` den Trigger-Anchor.
**Datum:** 2026-05-14
**Bezug:**
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§18 (`AR-OPEN-011`),
[`../../../spec/lastenheft.md`](../../../spec/lastenheft.md)
§22 (`LH-CONF-004` Export- und Northbound-Konfiguration), §27.2
(Implementierungs-Status),
[`0009-api-service-extraction-criteria.md`](0009-api-service-extraction-criteria.md)
(gleiche Trigger-basierte Logik fuer API-Auskopplung),
[`0011-application-monolithic-module.md`](0011-application-monolithic-module.md)
(gleiche Trigger-basierte Logik fuer Application-Split)

---

## 1. Kontext

`AR-OPEN-011` aus dem Architekturentwurf stand seit Projektstart offen:
"Northbound Export als eigener Adapter oder als
Telemetry-Adapter-Untermodul?". `LH-CONF-004` benennt den
Lastenheft-seitigen Erwartungsraum:

- aktivierbare Exportziele mit Status pro Ziel
- exportierte Assets und Punkte
- Protokolle MQTT, Modbus TCP oder HTTP
- Upload- oder Polling-Intervall, AuthN/TLS-Optionen
- Runtime-Reload ohne Neustart

Stand 2026-05-14:

- Es existiert **keine** Northbound-Export-Implementierung in `src/`.
- `BatteryEms.Adapters.Telemetry` ist ein **Inbound**-Adapter (er
  konsumiert Telemetrie aus Modbus/MQTT/OPC-UA und stellt sie der
  Application bereit), kein Outbound.
- Es gibt keinen konkreten Northbound-Konsumenten (Data Warehouse,
  Customer Portal, Aufsichts-Schnittstelle), der einen Pfad heute
  rechtfertigen wuerde.

Die strukturelle Frage aus `AR-OPEN-011` ist deshalb genau heute
nicht beantwortbar: Ob "eigener Adapter" oder "Telemetry-Adapter-
Untermodul" besser passt, haengt vom Lastprofil und der
Konsumenten-Topologie ab (Push vs. Pull, Stream vs. Batch,
Ziel-Diversitaet, Re-Use von Auth-/Transport-Stacks). Ohne diesen
Kontext ist jede Strukturwahl eine Wette.

---

## 2. Entscheidung

| Achse                       | Entscheidung                                                                                          |
| --------------------------- | ----------------------------------------------------------------------------------------------------- |
| Heutiger Implementierungs-Stand | Kein Northbound-Export. `LH-CONF-004` bleibt unimplementiert.                                     |
| Struktur-Entscheidung       | Vertagt bis zum ersten konkreten Konsumenten - dann via Folge-ADR.                                    |
| Default-Annahme bei Trigger | Telemetry-Adapter-Untermodul oder eigener Adapter wird **nicht** vorab festgelegt.                    |
| Verworfen fuer jetzt        | Spekulative Anlage eines `BatteryEms.Adapters.NorthboundExport`-Projekts ohne Konsument.              |

Begruendung: Diese ADR folgt derselben Logik wie
[ADR 0009](0009-api-service-extraction-criteria.md) (API-Auskopplung)
und [ADR 0011](0011-application-monolithic-module.md) (Application-
Split) - eine vorzeitige Strukturentscheidung erkauft Komplexitaet
(Projekt, Boundary-Tests, Konfigurations-Schema, Status-Endpoint)
ohne erkennbaren Nutzen, weil der bessere Designinput von einem
konkreten Konsumenten kommt, der heute nicht existiert.

---

## 3. Trigger fuer einen Northbound-Slice

Ein Northbound-Export-Slice soll geplant werden, sobald
**mindestens einer** der folgenden Punkte konkret eintritt. Trigger
sind absichtlich anwendungsfall-getrieben, damit "wir sollten doch
mal exportieren" kein Trigger ist:

- **Konsumenten-Trigger:** Ein externer Konsument
  (Data Warehouse, Customer Portal, Energy-Marktplatz, Aufsichts-
  Schnittstelle) verlangt einen messbaren Daten-Stream oder
  -Snapshot und das Lastprofil ist beschrieben (Frequenz,
  Volumen, Authentifizierung, Transport).
- **Regulatorischer Trigger:** Eine gesetzliche oder
  zertifizierungs-relevante Berichtspflicht (z. B. Netzbetreiber-
  Reporting, Marktteilnehmer-Pflichten) verlangt strukturierten
  Datenexport.
- **Cross-System-Trigger:** Ein paralleles internes System
  (Asset-Manager, Trading-System, Monitoring-Plattform) braucht
  bess-ems-Daten ueber eine etablierte Schnittstelle und der
  bestehende `/metrics`-Pfad reicht nicht aus.
- **Mehrziel-Trigger:** Mehr als zwei Konsumenten gleichzeitig
  brauchen denselben Datenstrom in unterschiedlichen Formaten -
  was die Frage "eigener Adapter" gegenueber "Untermodul" konkret
  beantwortbar macht.

---

## 4. Mindestvoraussetzungen fuer einen Slice

Falls ein Trigger zuendet, gelten dieselben Disziplin-Anforderungen
wie bei ADR 0009 §4 und ADR 0011 §4:

- Eigene Folge-ADR mit der konkreten Strukturwahl
  (eigener Adapter `BatteryEms.Adapters.NorthboundExport` vs.
  Untermodul innerhalb eines bestehenden Adapters), Begruendung
  des Triggers und Auswirkung auf
  `BatteryEms.ArchitectureTests`-Boundary-Regeln.
- Konkrete Konsumenten-Beschreibung als Eingangs-Artefakt:
  Lastprofil, Datenmodell, Protokoll, Authentifizierungsmodell,
  Re-Connect-/Backpressure-Semantik.
- Konfigurations-Schema fuer die Ziele aus `LH-CONF-004`
  (Mindestumfang: Aktivierung, Assets/Punkte, Protokoll,
  Intervall, AuthN/TLS, Status, Runtime-Reload).
- Status-Endpoint pro Exportziel (analog zu `/health/regelleistung`
  aus RM-M4-03).
- Architektur-Tabu: Northbound-Adapter darf den internen Regelkreis
  nicht beeinflussen (Lesepfad-only auf interne State-Sources;
  Pflicht-Tabu-Test in `ArchitectureTabusTests`).

---

## 5. Konsequenzen

- `AR-OPEN-011` wechselt im Architekturentwurf §18 auf "Geschlossen
  mit ADR 0012 - kein Northbound-Export heute; Struktur-Entscheidung
  trigger-basiert."
- `LH-CONF-004` behaelt in §27.2 den Status
  `🔲 ADR 0012` (statt zuvor `🔲 AR-OPEN-011`) - der Verweis zeigt
  jetzt auf eine geschlossene Entscheidung mit klaren Triggern, nicht
  mehr auf eine offene Architektur-Frage.
- Bis zum ersten Trigger entsteht **kein** `BatteryEms.Adapters.NorthboundExport`-
  Projekt, **keine** `NorthboundExportConfiguration`-Klasse und **kein**
  Konfigurations-Schema fuer Exportziele.
- Die ADR ist kein Bekenntnis "niemals Northbound"; sie ist ein
  Bekenntnis "Northbound erst, wenn ein konkreter Konsument existiert,
  dessen Bedarf die Strukturwahl entscheidbar macht".
