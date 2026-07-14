# Lastenheft: bess-ems

**Projektname:** bess-ems  
**System:** Battery Energy Management System für Batteriespeicher  
**Dokumenttyp:** Lastenheft  
**Format:** Markdown  
**Version:** 0.1.5
**Status:** Entwurf  
**Zielplattform:** Linux, Docker, C#/.NET, C/C++ für performance-kritische Komponenten  
**Vorgehensmodell:** V-Modell-ähnliche Anforderungsstruktur mit Kennungen und Rückverfolgbarkeit  

---

## 1. Zweck des Dokuments

Dieses Lastenheft beschreibt die fachlichen, technischen und nicht-funktionalen Anforderungen an das System `bess-ems`.

Das System soll ein Energy Management System für Battery Energy Storage Systems bereitstellen. Es soll Batteriespeicher unter Berücksichtigung von Marktmechanismen, Echtzeitmessdaten, technischen Grenzwerten und Sicherheitsanforderungen steuern.

Berücksichtigt werden insbesondere:

- Day-Ahead-Markt
- Intraday-Markt
- Regelleistung
- Lade- und Entladeregelung
- Leistungsrampen
- Zustandsmaschinen
- PID-Regelung
- MPC / State-Space-Modelle
- LP / MILP / heuristische Optimierung
- Modbus TCP
- OPC-UA
- MQTT
- Echtzeitnahe Messdatenverarbeitung
- C#/.NET als Hauptplattform
- C/C++ für performance-kritische Komponenten
- Docker/Linux-Betrieb

---

## 2. Zielsetzung

### LH-ZIEL-001 — Aufbau eines modularen BESS-EMS

Das System muss ein modular aufgebautes Battery Energy Management System bereitstellen, das Batteriespeicher planen, überwachen und steuern kann.

**Priorität:** Muss  
**Quelle:** Projektziel  
**Abnahmekriterium:** Das System besitzt getrennte Module für Domain-Modell, Marktlogik, Optimierung bzw. Optimierungsinterface, Echtzeitregelung, Protokolladapter, Persistenz und Monitoring.

---

### LH-ZIEL-002 — Trennung von Marktoptimierung und technischer Regelung

Das System muss Marktoptimierung und technische Ausführung strikt trennen.

**Priorität:** Muss  
**Quelle:** Architekturvorgabe  
**Abnahmekriterium:** Optimierungsergebnisse werden niemals direkt an Batteriesysteme gesendet, sondern immer über State Machine, Constraint Limiter und Ramp Limiter geführt.

---

### LH-ZIEL-003 — Betrieb auf Linux mit Docker

Das System muss auf Linux-basierten Docker-Umgebungen lauffähig sein.

**Priorität:** Muss  
**Quelle:** Zielplattform  
**Abnahmekriterium:** Das System kann über Docker Compose lokal gestartet und über Container Images deployt werden.

---

## 3. Systemkontext

### LH-KTX-001 — Externe Systeme

Das System muss mit folgenden externen Systemen interagieren können:

- Batteriesystem / BMS
- Wechselrichter
- Netz- oder Standortzähler
- PV-Erzeugungsmessung
- Marktpreisquellen
- Fahrplanquellen
- Regelleistungs-Aktivierungssignale
- Datenbank
- Monitoring-System
- Operator-UI oder API-Client

**Priorität:** Muss  
**Abnahmekriterium:** Für jeden im jeweiligen Release unterstützten externen Systemtyp existiert eine dokumentierte Schnittstelle oder ein Adapterkonzept.

---

### LH-KTX-002 — Kommunikationsprotokolle

Das System bindet Batteriesysteme und zugehörige Feldgeräte über Modbus TCP, MQTT und OPC-UA an.

- Modbus TCP
- MQTT
- OPC-UA

**Priorität:** Muss  
**Abnahmekriterium:** Für Modbus TCP und MQTT existieren Adapterkonzepte mit Lese- und Schreibpfad. Für OPC-UA ist die spätere Integration über dasselbe interne Adapterinterface dokumentiert.

---

## 4. Begriffe und Abkürzungen

| Begriff            | Bedeutung                                                       |
| ------------------ | --------------------------------------------------------------- |
| BESS               | Battery Energy Storage System                                   |
| EMS                | Energy Management System                                        |
| BMS                | Battery Management System                                       |
| SOC                | State of Charge                                                 |
| SOH                | State of Health                                                 |
| Day-Ahead          | Strommarkt mit Fahrplanplanung für den Folgetag                 |
| Intraday           | Kurzfristige Marktanpassung innerhalb des Liefertages           |
| Regelleistung      | Bereitstellung von Leistungsreserve zur Netzstabilisierung      |
| PID                | Proportional-Integral-Differential-Regler                       |
| MPC                | Model Predictive Control                                        |
| LP                 | Linear Programming                                              |
| MILP               | Mixed Integer Linear Programming                                |
| OPC-UA             | Industrielles Kommunikationsprotokoll                           |
| MQTT               | Publish/Subscribe-Protokoll                                     |
| Modbus TCP         | Industrielles Register-basiertes TCP-Protokoll                  |
| Native Core        | Performance-kritischer C/C++-Systemkern                         |
| Constraint Limiter | Begrenzungslogik für technische Betriebsgrenzen                 |
| Ramp Limiter       | Begrenzung der Änderungsgeschwindigkeit von Leistungssollwerten |

### LH-DOM-007 — Vorzeichenkonvention

Das System muss eine einheitliche Vorzeichenkonvention für Wirkleistung verwenden:

- positive Wirkleistung am Batteriespeicher bedeutet Entladen / Einspeisen aus der Batterie
- negative Wirkleistung am Batteriespeicher bedeutet Laden / Bezug in die Batterie
- 0 kW bedeutet kein aktiver Lade- oder Entladebefehl

Alle Fahrpläne, Optimierungsergebnisse, Limiter, Commands, Persistenzdaten und Protokolladapter müssen diese Konvention intern verwenden. Abweichende Gerätekonventionen müssen ausschließlich in Protokolladaptern umgesetzt werden.

**Abnahmekriterium:** Unit Tests weisen nach, dass SOC-Grenzen, Ramp Limiter und Protokolladapter die Vorzeichenkonvention konsistent anwenden.

---

## 5. Kennungssystem

Die Anforderungen verwenden folgende Kennungssystematik:

| Präfix     | Bedeutung                       |
| ---------- | ------------------------------- |
| LH-ZIEL    | Projektziele                    |
| LH-KTX     | Systemkontext                   |
| LH-ARCH    | Architektur                     |
| LH-DOM     | Domain-Modell                   |
| LH-MKT     | Marktanforderungen              |
| LH-OPT     | Optimierung                     |
| LH-CTRL    | Regelung                        |
| LH-SM      | Zustandsmaschine                |
| LH-SAFE    | Sicherheit                      |
| LH-RT      | Echtzeitdaten                   |
| LH-PROT    | Protokolle                      |
| LH-MODB    | Modbus TCP                      |
| LH-OPCUA   | OPC-UA                          |
| LH-MQTT    | MQTT                            |
| LH-NATIVE  | C/C++ Native Core               |
| LH-PERSIST | Persistenz                      |
| LH-API     | API                             |
| LH-MON     | Monitoring                      |
| LH-CONF    | Konfiguration                   |
| LH-NF      | Nicht-funktionale Anforderungen |
| LH-TEST    | Testanforderungen               |
| LH-DEPLOY  | Deployment                      |
| LH-OPS     | Betrieb                         |
| LH-TRACE   | Rückverfolgbarkeit              |
| LH-RISK    | Risiken                         |
| LH-OPEN    | Offene Punkte                   |

Prioritäten:

| Priorität | Bedeutung                                   |
| --------- | ------------------------------------------- |
| Muss      | zwingend erforderlich                       |
| Soll      | wichtig, aber nicht zwingend für ersten MVP |
| Kann      | optional oder spätere Erweiterung           |

Bedingte Prioritäten wie `Muss, falls Native Core eingesetzt wird` oder `Muss für produktiven Betrieb` gelten nur für den genannten Einsatzkontext.

---

## 6. Architektur-Anforderungen

### LH-ARCH-001 — Modulare Systemarchitektur

Das System muss modular aufgebaut sein.

**Priorität:** Muss  
**Beschreibung:** Die fachlichen Komponenten müssen getrennt entwickelt, getestet und ausgetauscht werden können.  
**Abnahmekriterium:** Es existieren getrennte Module oder Projekte für Domain, Control, Markets, Optimization bzw. Optimization-Interfaces, Realtime, Protocols, Infrastructure, Worker und API. Im MVP darf das Optimization-Modul als austauschbares Interface ohne produktiven Solver umgesetzt sein.

---

### LH-ARCH-002 — Schichtentrennung

Das System muss mindestens folgende logische Schichten besitzen:

- Domain Layer
- Market Layer
- Optimization Layer
- Realtime Layer
- Control Layer
- Protocol Adapter Layer
- Infrastructure Layer
- API Layer
- Native Core Layer, falls native Komponenten eingesetzt werden

**Priorität:** Muss  
**Abnahmekriterium:** Quellcode und Dokumentation bilden diese Schichten nachvollziehbar ab.

---

### LH-ARCH-003 — Keine Marktlogik in Protokolladaptern

Protokolladapter dürfen keine Markt-, Optimierungs- oder Regelentscheidungen enthalten.

**Priorität:** Muss  
**Abnahmekriterium:** Adapter transformieren ausschließlich externe Signale in interne Modelle und interne Commands in externe Schreiboperationen.

---

### LH-ARCH-004 — Keine direkte Geräteansteuerung aus Optimierung

Optimierungsmodule dürfen keine direkten Schreiboperationen auf Batterien, Wechselrichter oder Feldgeräte ausführen.

**Priorität:** Muss  
**Abnahmekriterium:** Optimierer erzeugen ausschließlich Fahrpläne, Zielwerte oder Optimierungsergebnisse.

---

### LH-ARCH-005 — Austauschbare Kommunikationsadapter

Kommunikationsadapter müssen austauschbar sein.

**Priorität:** Muss  
**Abnahmekriterium:** Alle implementierten Feldkommunikationsadapter implementieren gemeinsame interne Interfaces. Im MVP gilt dies für Modbus TCP und MQTT; OPC-UA verwendet bei Umsetzung dasselbe Interface.

---

### LH-ARCH-006 — Native Core als optionaler Beschleuniger

Performance-kritische Komponenten sollen optional über einen C/C++ Native Core implementierbar sein.

**Priorität:** Soll  
**Abnahmekriterium:** Die .NET-Anwendung kann native Funktionen über eine stabile Schnittstelle verwenden oder durch eine .NET-Implementierung ersetzen.

---

## 7. Domain-Anforderungen

### LH-DOM-001 — Batteriespeicher-Modell

Das System muss ein fachliches Modell für Batteriespeicher bereitstellen.

**Priorität:** Muss  
**Mindestattribute:**

- AssetId
- Kapazität in kWh
- maximale Ladeleistung
- maximale Entladeleistung
- minimaler SOC
- maximaler SOC
- Lade-Wirkungsgrad
- Entlade-Wirkungsgrad
- maximale Rampenrate
- Betriebsstatus

**Abnahmekriterium:** Das Modell kann in Regelung, Optimierung und Persistenz verwendet werden.

---

### LH-DOM-002 — Telemetrie-Modell

Das System muss ein einheitliches internes Telemetrie-Modell besitzen.

**Priorität:** Muss  
**Mindestattribute:**

- Timestamp
- AssetId
- SOC
- SOH
- Wirkleistung
- Blindleistung
- DC-Spannung
- DC-Strom
- Temperatur
- Verfügbarkeit
- Fehlerstatus
- Datenqualität

**Abnahmekriterium:** Alle Protokolladapter liefern dieses interne Modell.

---

### LH-DOM-003 — Command-Modell

Das System muss ein einheitliches internes Command-Modell besitzen.

**Priorität:** Muss  
**Mindestattribute:**

- CommandId
- Timestamp
- AssetId
- Modus
- Wirkleistungssollwert
- optionaler Blindleistungssollwert
- ValidUntil
- Reason
- Source

**Abnahmekriterium:** Alle Schreibadapter verwenden dieses Modell als Eingabe.

---

### LH-DOM-004 — Datenqualität

Jeder Messwert muss mit einer Datenqualität bewertet werden.

**Priorität:** Muss  
**Datenqualität muss mindestens enthalten:**

- gültig / ungültig
- veraltet / aktuell
- substituiert / original
- Protokollfehler ja/nein
- Begründung

**Abnahmekriterium:** Der Echtzeitregler kann bei ungültiger oder veralteter Datenqualität in einen sicheren Zustand wechseln.

---

### LH-DOM-005 — Gerätepunkt-Modell

Das System muss Geräte- und Protokollpunkte fachlich beschreiben können.

**Priorität:** Muss

**Mindestattribute:**

- eindeutiger Schlüssel
- Anzeigename
- Einheit
- Datentyp
- Wertebereich oder Plausibilitätsbereich
- Skalierung und Offset, sofern protokollabhängig
- Lese- oder Schreibrichtung
- Exportfähigkeit
- optionaler Alarm- oder Plausibilitätsauslöser
- optionale Werteerklärung für Status- und Enum-Werte

**Abnahmekriterium:** Modbus- und MQTT-Mappings können Punktmetadaten so beschreiben, dass Telemetrie, API, Monitoring und spätere Exportdienste dieselbe fachliche Bedeutung verwenden.

---

### LH-DOM-006 — Geräte-Capabilities

Das System soll Batteriesysteme nicht nur als generischen Speicher, sondern über fachliche Capabilities abbilden können.

**Priorität:** Soll

**Mindest-Capabilities nach Bedarf:**

- Battery/BMS: SOC, SOH, Zelltemperaturen, Zellspannungen, Lade-/Entladefreigaben
- PCS/Inverter: Wirkleistung, Blindleistung, Frequenz, Betriebszustand, Setpoints
- EnergyStore: aggregierte Speicherfähigkeit über BMS- und PCS-Daten
- Grid/Meter: Netzbezug, Einspeisung, Leistung, Energiezähler

**Abnahmekriterium:** Regler und Adapter können prüfen, ob ein Asset eine benötigte Capability unterstützt, ohne herstellerspezifische Details im Regelkreis zu kennen.

---

## 8. Marktanforderungen

### LH-MKT-001 — Day-Ahead-Fahrplan

Das System muss Day-Ahead-Fahrpläne verarbeiten können.

**Priorität:** Muss  
**Beschreibung:** Das System importiert Day-Ahead-Fahrpläne und verwendet sie im Regelkreis. Die automatische Erzeugung von Day-Ahead-Fahrplänen auf Basis von Preisen, Prognosen und technischen Grenzen erfolgt über das Optimierungsmodul.

**Abnahmekriterium:** Ein Day-Ahead-Fahrplan kann gespeichert, gelesen und im Regelkreis verwendet werden.

---

### LH-MKT-002 — Intraday-Reoptimierung

Das System unterstützt Intraday-Anpassungen.

**Priorität:** Soll  
**Beschreibung:** Das System soll bei Preis-, Prognose- oder Zustandsänderungen eine Korrektur bestehender Fahrpläne ermöglichen.  
**Abnahmekriterium:** Ein bestehender Fahrplan kann für einen Resthorizont neu bewertet und angepasst werden.

---

### LH-MKT-003 — Marktverpflichtungen

Das System muss verbindliche Marktverpflichtungen abbilden können.

**Priorität:** Muss

**Mindestattribute:**

- Markt
- Zeitraum
- zugesagte Leistung
- Strafkosten oder Abweichungskosten
- Verbindlichkeitsstatus

**Abnahmekriterium:** Regelung und, falls vorhanden, Optimierung können Marktverpflichtungen berücksichtigen.

---

### LH-MKT-004 — Regelleistungsreservierung

Das System verwaltet reservierte Leistung für Regelleistung.

**Priorität:** Soll  
**Beschreibung:** Reservierte Lade- und Entladeleistung muss für andere Märkte blockiert werden können.  
**Abnahmekriterium:** Day-Ahead- und Intraday-Optimierung verletzen keine reservierten Regelleistungsbereiche.

---

### LH-MKT-005 — Regelleistungsaktivierung

Das System verarbeitet Aktivierungssignale für Regelleistung.

**Priorität:** Soll  
**Beschreibung:** Aktivierungssignale müssen priorisiert in den Regelkreis eingehen.  
**Abnahmekriterium:** Bei aktiver Regelleistungsanforderung wird der normale Fahrplan übersteuert, sofern Sicherheitsgrenzen eingehalten werden.

---

### LH-MKT-006 — Priorisierung von Markt- und Sicherheitsanforderungen

Das System muss für alle im jeweiligen Release aktivierten Funktionen folgende Prioritätsreihenfolge einhalten:

1. Emergency Stop
2. Batterie-, Wechselrichter- und Netzgrenzen
3. Regelleistungsaktivierung
4. verbindliche Marktverpflichtungen
5. Intraday-Fahrplan
6. Day-Ahead-Fahrplan
7. lokale Optimierung

**Priorität:** Muss

**Abnahmekriterium:** Diese Priorität ist im Regelkreis und in Tests für die aktivierten Eingangsquellen nachweisbar.

---

### LH-MKT-007 — Zeitmodell für Fahrpläne

Das System muss für Fahrpläne und Marktverpflichtungen ein eindeutiges Zeitmodell verwenden.

**Priorität:** Muss  
**Mindestfestlegungen:**

- interne Speicherung von Zeitpunkten in UTC
- explizite Zeitzone für Import, Export und Anzeige
- halboffene Intervalle `[Start, Ende)` für Fahrplanzeiträume
- konfigurierte Zeitschrittweite pro Fahrplantyp
- definierte Behandlung von Sommerzeitumstellungen
- Marktgebiet oder Preiszone als Attribut von Marktpreisen und Verpflichtungen

**Abnahmekriterium:** Fahrplanimport, Persistenz und Regelkreis interpretieren Zeitintervalle identisch; Tests decken mindestens eine Sommerzeitumstellung ab.

---

### LH-MKT-008 — Tarif- und Preiszeitfenster

Das System soll Preis- und Tarifmodelle für Optimierung und lokale Strategien abbilden können.

**Priorität:** Soll

**Mindestumfang:**

- Preiszone oder Marktgebiet
- Preisart, z. B. Bezug, Einspeisung, Netzentgelt, Peak, Valley, Flat
- Zeitfenster mit Minutenauflösung
- Priorität bei überlappenden Preisregeln
- Gültigkeitsdatum oder langfristige Gültigkeit
- Arbeits-/Wochenend- und benutzerdefinierte Kalendertage
- Cross-Day-Zeitfenster, z. B. 22:00 bis 06:00

**Abnahmekriterium:** Für einen gegebenen Zeitpunkt kann das System eindeutig den gültigen Preis oder die gültige Tarifregel bestimmen.

---

### LH-MKT-009 — Marktprodukt-Differenzierung

Das System soll Marktprodukte fachlich differenziert abbilden, sobald diese im jeweiligen Release verwendet werden.

**Priorität:** Soll

**Mindestprodukte nach Ausbau:**

| Kennung | Marktprodukt |
| ------- | ------------ |
| LH-MKT-009_DAE | Day-Ahead Energie |
| LH-MKT-009_IE | Intraday Energie |
| LH-MKT-009_FCR_RC | FCR Reservekapazität |
| LH-MKT-009_AFRR_POS_RC | aFRR positive Reservekapazität |
| LH-MKT-009_AFRR_NEG_RC | aFRR negative Reservekapazität |
| LH-MKT-009_AFRR_POS_AE | aFRR positive Aktivierungsenergie |
| LH-MKT-009_AFRR_NEG_AE | aFRR negative Aktivierungsenergie |
| LH-MKT-009_MFRR_POS_RC | mFRR positive Reservekapazität |
| LH-MKT-009_MFRR_NEG_RC | mFRR negative Reservekapazität |
| LH-MKT-009_MFRR_POS_AE | mFRR positive Aktivierungsenergie |
| LH-MKT-009_MFRR_NEG_AE | mFRR negative Aktivierungsenergie |

**Abnahmekriterium:** Marktverpflichtungen, Preise und Optimierungsinputs unterscheiden Marktprodukt, Richtung, Einheit und Zeitschritt eindeutig.

---

### Entscheidung zu LH-OPEN-002 — Regelleistungsprodukte

Für die Produktplanung werden konkret die im deutschen Regelreservemarkt relevanten Produktfamilien FCR, aFRR und mFRR berücksichtigt.

**Planungsannahme Stand 2026-05-09:**

| Kennung | Produktprofil | Marktbezug | Richtung | Zeitraster | Leistungsinterpretation |
| ------- | ------------- | ---------- | -------- | ---------- | ----------------------- |
| LH-MKT-009_FCR_RC | FCR Reservekapazität | Regelleistung | symmetrisch, positive und negative Leistung müssen gemeinsam vorgehalten werden | 4-h-Produkt; interne Reservierungsauflösung mindestens 15 min | bidirektional reserviertes Leistungsband um den Baseline-Fahrplan; keine getrennte Aktivierungsenergie im EMS-Marktmodell |
| LH-MKT-009_AFRR_POS_RC | aFRR positive Reservekapazität | Regelleistung | positiv, Einspeisung erhöhen oder Bezug senken | 4-h-Produkt; interne Reservierungsauflösung mindestens 15 min | entladeseitige Reserve, die Day-Ahead und Intraday nicht verplanen dürfen |
| LH-MKT-009_AFRR_NEG_RC | aFRR negative Reservekapazität | Regelleistung | negativ, Einspeisung senken oder Bezug erhöhen | 4-h-Produkt; interne Reservierungsauflösung mindestens 15 min | ladeseitige Reserve, die Day-Ahead und Intraday nicht verplanen dürfen |
| LH-MKT-009_AFRR_POS_AE | aFRR positive Aktivierungsenergie | Regelarbeit | positiv | 15-min-Abrechnungsraster; Aktivierung im Regelkreis als zeitgestempelter Sollwert | aktivierter Leistungs-Sollwert vor Marktverpflichtungen und Fahrplänen, nach Safety-Limits |
| LH-MKT-009_AFRR_NEG_AE | aFRR negative Aktivierungsenergie | Regelarbeit | negativ | 15-min-Abrechnungsraster; Aktivierung im Regelkreis als zeitgestempelter Sollwert | aktivierter Leistungs-Sollwert vor Marktverpflichtungen und Fahrplänen, nach Safety-Limits |
| LH-MKT-009_MFRR_POS_RC | mFRR positive Reservekapazität | Regelleistung | positiv | 4-h-Produkt; interne Reservierungsauflösung mindestens 15 min | entladeseitige Reserve, die Day-Ahead und Intraday nicht verplanen dürfen |
| LH-MKT-009_MFRR_NEG_RC | mFRR negative Reservekapazität | Regelleistung | negativ | 4-h-Produkt; interne Reservierungsauflösung mindestens 15 min | ladeseitige Reserve, die Day-Ahead und Intraday nicht verplanen dürfen |
| LH-MKT-009_MFRR_POS_AE | mFRR positive Aktivierungsenergie | Regelarbeit | positiv | 15-min-Abrechnungsraster | fachlich relevantes Arbeitsprodukt; produktive MOLS-/MARI-Aktivierung bleibt Folgeausbau |
| LH-MKT-009_MFRR_NEG_AE | mFRR negative Aktivierungsenergie | Regelarbeit | negativ | 15-min-Abrechnungsraster | fachlich relevantes Arbeitsprodukt; produktive MOLS-/MARI-Aktivierung bleibt Folgeausbau |

**Priorisierung:** Das System muss Reserve-Constraints produktneutral modellieren können. FCR und aFRR sind als erste reale Produktprofile nachzuweisen. mFRR wird im Marktproduktmodell und in den Reservierungsdaten vorbereitet, aber nicht als produktiver Aktivierungspfad vorausgesetzt.

**Abnahmekriterium:** Die Produktfamilie, Richtung, Einheit und der Zeitschritt sind für FCR, aFRR und mFRR eindeutig modellierbar. Produktive Aktivierung bleibt deaktiviert, bis das konkrete Produktprofil inklusive Präqualifikations-, Schnittstellen-, Latenz- und Jitter-Annahmen freigegeben ist.

## 9. Optimierungsanforderungen

### LH-OPT-001 — Fahrplanoptimierung

Das System optimiert Fahrpläne für Lade- und Entladeleistung über einen definierten Horizont.

**Priorität:** Soll

**Beschreibung:** Horizon-Optimierung erzeugt eine Zeitreihe aus Leistungssollwerten, optionalen SOC-Zielwerten und Solver-/Qualitätsinformationen. Sie ist von der zyklischen Echtzeit-Dispatch-Entscheidung im Regelkreis getrennt.

**Abnahmekriterium:** Für einen definierten Zeitraum wird eine versionierbare Zeitreihe mit Leistungssollwerten, SOC-Zielwerten und Solverstatus erzeugt.

---

### LH-OPT-002 — Unterstützte Optimierungsverfahren

Das System soll folgende Verfahren unterstützen oder vorbereiten:

- LP
- MILP
- heuristische Verfahren
- MPC

**Priorität:** Soll  
**Abnahmekriterium:** Die Optimierung ist über ein gemeinsames Interface abstrahiert.

---

### LH-OPT-003 — MILP-Modellierung

Das System soll MILP für Dispatch-Optimierung unterstützen.

**Priorität:** Soll  
**Beschreibung:** Gleichzeitiges Laden und Entladen muss durch Binärvariablen ausgeschlossen werden können.  
**Abnahmekriterium:** Ein MILP-Modell kann Lade- und Entladezustände pro Zeitschritt getrennt abbilden.

---

### LH-OPT-004 — Zielfunktion

Die Optimierung muss, falls ein Optimierungsmodul eingesetzt wird, mehrere Kosten- und Strafkostenbestandteile berücksichtigen können.

**Priorität:** Muss, falls Optimierung eingesetzt wird  
**Mögliche Bestandteile:**

- Energiebezugskosten
- Einspeiseerlöse
- Batteriealterungskosten
- Abweichungskosten von Marktverpflichtungen
- Strafkosten für SOC-Zielabweichung
- Strafkosten für Reserveverletzung
- Peak-Shaving-Ziele

**Abnahmekriterium:** Die Zielfunktion ist konfigurierbar oder erweiterbar.

---

### LH-OPT-005 — Nebenbedingungen

Die Optimierung muss, falls ein Optimierungsmodul eingesetzt wird, technische Nebenbedingungen berücksichtigen.

**Priorität:** Muss, falls Optimierung eingesetzt wird  
**Mindestbedingungen:**

- SOC-Minimum
- SOC-Maximum
- maximale Ladeleistung
- maximale Entladeleistung
- Wirkungsgrade
- Rampengrenzen
- Regelleistungsreserve
- Marktverpflichtungen

**Abnahmekriterium:** Optimierungsergebnisse verletzen diese Grenzen nicht oder markieren die Lösung als unzulässig.

---

### LH-OPT-006 — Solver-Abstraktion

Das System soll Solver austauschbar integrieren können.

**Priorität:** Soll  
**Mögliche Solver:**

- OR-Tools
- HiGHS
- CBC
- SCIP
- Gurobi
- CPLEX

**Abnahmekriterium:** Optimierer sind nicht hart an einen konkreten Solver gekoppelt.

---

### LH-OPT-007 — Trennung von Horizon-Optimierung und Echtzeit-Dispatch

Das System muss zwischen Fahrplanoptimierung über einen Horizont und zyklischem Dispatch im Regelkreis unterscheiden.

**Priorität:** Muss, falls Optimierung eingesetzt wird

**Beschreibung:** Ein Horizon-Optimierer erzeugt oder aktualisiert Fahrpläne. Der Echtzeit-Dispatch wählt im Regelzyklus den aktuell gültigen Sollwert, kombiniert ihn mit aktiven Marktverpflichtungen und unterwirft ihn Sicherheits- und Rampengrenzen.

**Abnahmekriterium:** Ein Optimierungsergebnis kann gespeichert werden, ohne unmittelbar ein Feldgerät anzusteuern; der Regelkreis verwendet daraus nur den jeweils gültigen Sollwert.

---

### LH-OPT-008 — Einheiten- und Zeitschritt-Konsistenz

Optimierungsmodelle müssen Leistung, Energie, Preise und Zeitschritte eindeutig behandeln.

**Priorität:** Muss, falls Optimierung eingesetzt wird

**Mindestfestlegungen:**

- Leistung intern in kW
- Energie intern in kWh
- Preise mit explizitem Nenner, z. B. EUR/MWh oder EUR/kWh
- Zeitschritt in Stunden
- Umrechnung von Leistung zu Energie nur über `E = P * delta_t`
- Exportspalten mit fachlich korrekter Einheit

**Abnahmekriterium:** Tests oder Modellvalidierungen weisen nach, dass ein konstanter Leistungsfahrplan über einen bekannten Zeitschritt die erwartete Energiemenge ergibt.

---

### LH-OPT-009 — Optimierungsergebnis und Objective Breakdown

Optimierungsläufe müssen nachvollziehbare Ergebnisdaten liefern.

**Priorität:** Soll

**Mindestumfang:**

- eindeutige RunId
- verwendete Input-Versionen
- Solvername und Solverstatus
- Optimierungshorizont und Zeitschritt
- Zielfunktionswert
- aufgeschlüsselte Kosten- und Erlöskomponenten
- erkannte Constraint-Verletzungen oder Unzulässigkeit
- Laufzeit und Abbruchgrund
- erzeugter oder aktualisierter Fahrplan

**Abnahmekriterium:** Ein gespeicherter Optimierungslauf kann fachlich erklärt und bei gleichen Inputs reproduziert oder als nicht reproduzierbar begründet werden.

---

## 10. Regelungsanforderungen

### LH-CTRL-001 — Zyklischer Regelkreis

Das System muss einen zyklischen, echtzeitnahen Regelkreis bereitstellen.

**Priorität:** Muss  
**Standardzyklus:** 1 Sekunde  
**Abnahmekriterium:** Der Regelkreis kann periodisch Snapshots lesen, Zielwerte berechnen und Commands publizieren. Unter Normalbedingungen wird ein Regelzyklus innerhalb von 1 Sekunde abgeschlossen.

---

### LH-CTRL-002 — Constraint Limiter

Das System muss jeden Zielwert vor Ausgabe technisch begrenzen.

**Priorität:** Muss  
**Grenzen:**

- SOC-Minimum
- SOC-Maximum
- Ladeleistungsgrenze
- Entladeleistungsgrenze
- Verfügbarkeit BMS
- Verfügbarkeit Wechselrichter
- Emergency Stop
- Temperaturgrenzen
- Protokoll- und Datenqualität

**Abnahmekriterium:** Kein Command überschreitet bekannte technische Grenzen.

---

### LH-CTRL-003 — Ramp Limiter

Das System muss Leistungsänderungen begrenzen.

**Priorität:** Muss  
**Beschreibung:** Leistungssollwerte dürfen im Normalbetrieb nur gemäß konfigurierter Rampenrate verändert werden.  
**Abnahmekriterium:** Tests weisen nach, dass die Differenz zweier aufeinanderfolgender Sollwerte die erlaubte Rampenrate nicht überschreitet.

---

### LH-CTRL-004 — PID-Regelung

Das System soll PID-Regler für ausgewählte lokale Regelziele bereitstellen.

**Priorität:** Soll  
**Mögliche Regelziele:**

- Netzanschlusspunkt auf Zielwert regeln
- Peak-Shaving
- Frequenzabweichung ausregeln
- Exportbegrenzung

**Abnahmekriterium:** PID-Regler besitzt Anti-Windup, Output-Clamping und optionales Totband.

---

### LH-CTRL-005 — MPC-Unterstützung

Das System soll Model Predictive Control unterstützen oder vorbereiten.

**Priorität:** Kann  
**Abnahmekriterium:** Ein MPC-Kern kann über das Optimierungsinterface oder den Native Core integriert werden.

---

### LH-CTRL-006 — State-Space-Modelle

Das System soll State-Space-Modelle für dynamische Systemzustände unterstützen.

**Priorität:** Kann  
**Mögliche Zustände:**

- SOC
- Batterietemperatur
- interne Batterie-Dynamik
- DC-Zwischenkreis
- Alterungszustand

**Abnahmekriterium:** Das System besitzt ein erweiterbares Modellinterface für diskrete Zustandsmodelle.

---

### LH-CTRL-007 — Fallback bei ungültigem Snapshot

Das System muss bei ungültigem oder veraltetem Snapshot einen sicheren Fallback ausführen.

**Priorität:** Muss  
**Abnahmekriterium:** Bei fehlenden, ungültigen oder veralteten Messdaten wird kein aktiver Lade- oder Entladebefehl ausgegeben. Der resultierende Command ist `0 kW`, ein expliziter Stop-Command oder eine deaktivierte Ausgabe mit begründetem Reason-Feld.

---

## 11. Zustandsmaschinen-Anforderungen

### LH-SM-001 — Explizite Betriebszustände

Das System muss eine explizite Zustandsmaschine für den Batteriespeicher besitzen.

**Priorität:** Muss  
**Mindestzustände:**

- INIT
- STANDBY
- READY
- IDLE
- CHARGING
- DISCHARGING
- LIMITED
- FAULT
- EMERGENCY_STOP
- MAINTENANCE

**Abnahmekriterium:** Jeder Regelzyklus verarbeitet den aktuellen Zustand und mögliche Zustandsübergänge.

---

### LH-SM-002 — Sicherheitszustände haben Vorrang

Sicherheitszustände müssen alle normalen Betriebszustände übersteuern.

**Priorität:** Muss  
**Abnahmekriterium:** Aus jedem Zustand ist ein Übergang nach FAULT oder EMERGENCY_STOP möglich.

---

### LH-SM-003 — Quittierung von Fehlerzuständen

Fehlerzustände dürfen nur nach definierter Quittierung oder Wiederherstellungsbedingung verlassen werden.

**Priorität:** Muss  
**Abnahmekriterium:** FAULT geht nicht automatisch in READY über, ohne dass definierte Bedingungen erfüllt sind.

---

## 12. Sicherheitsanforderungen

Für dieses Lastenheft bedeutet sicherer Zustand:

- kein aktiver Lade- oder Entladebefehl
- keine Weiterleitung veralteter oder ungültiger Commands
- Ausgabe eines `0 kW`-Commands, eines expliziten Stop-Commands oder Deaktivierung der Ausgabe
- persistierter und geloggter Grund für den Sicherheitsfall

Falls ein angebundenes Feldgerät einen herstellerspezifischen sicheren Zustand verlangt, muss dieser über das jeweilige Gerätemapping dokumentiert und im Adapter umgesetzt werden.

Softwareseitige Stop- und Sicherheitsfunktionen des EMS ersetzen keinen hardwareseitigen Not-Aus, keine Schutztechnik des BMS und keine herstellerspezifischen Schutzfunktionen des Wechselrichters. Harte Not-Aus-Ketten und zertifizierungsrelevante Schutzfunktionen müssen außerhalb des EMS oder über dedizierte Edge-/Herstellersteuerungen realisiert und im Anlagenkonzept abgegrenzt werden.

### LH-SAFE-001 — Emergency Stop

Das System muss ein softwareseitiges Emergency-Stop-Signal mit höchster Priorität behandeln.

**Priorität:** Muss  
**Abnahmekriterium:** Bei aktivem Emergency Stop wird spätestens im nächsten Regelzyklus ein sicherer Stop-Command erzeugt oder die Ausgabe deaktiviert. Emergency Stop übersteuert Fahrplan-, Markt-, Optimierungs- und Operator-Zielwerte. Falls eine kürzere Reaktionszeit als das konfigurierte Regelzyklusintervall erforderlich ist, muss diese Anforderung durch Hardware, BMS, Wechselrichter oder Edge-Controller erfüllt und außerhalb des Docker-basierten EMS abgegrenzt werden.

---

### LH-SAFE-002 — Kein Laden außerhalb SOC-Grenzen

Das System darf keine Ladeleistung anfordern, wenn der maximale SOC erreicht oder überschritten ist.

**Priorität:** Muss  
**Abnahmekriterium:** Bei SOC >= SOC_MAX ist der resultierende Ladeanteil 0.

---

### LH-SAFE-003 — Kein Entladen außerhalb SOC-Grenzen

Das System darf keine Entladeleistung anfordern, wenn der minimale SOC erreicht oder unterschritten ist.

**Priorität:** Muss  
**Abnahmekriterium:** Bei SOC <= SOC_MIN ist der resultierende Entladeanteil 0.

---

### LH-SAFE-004 — Kommunikationsverlust

Das System muss Kommunikationsverlust zu BMS, Wechselrichter oder relevanten Messgeräten erkennen.

**Priorität:** Muss  
**Abnahmekriterium:** Bei Kommunikationsverlust wird spätestens nach Überschreitung des konfigurierten maximalen Messwertalters ein sicherer Zustand eingenommen.

---

### LH-SAFE-005 — Veraltete Commands

Das System darf veraltete Commands nicht ausführen oder weiterleiten.

**Priorität:** Muss  
**Abnahmekriterium:** Commands mit überschrittenem ValidUntil werden verworfen.

---

### LH-SAFE-006 — Datenplausibilität

Das System muss unplausible Messwerte erkennen.

**Priorität:** Muss  
**Beispiele:**

- SOC < 0 %
- SOC > 100 %
- unrealistische Temperatur
- unrealistische Leistungswerte
- widersprüchliche Statuswörter
- Zeitstempel aus der Zukunft
- zu alte Zeitstempel

**Abnahmekriterium:** Unplausible Messwerte erhalten ungültige Datenqualität.

---

### LH-SAFE-007 — Schreibbegrenzung vor Feldkommunikation

Schreibwerte müssen unmittelbar vor dem Senden nochmals begrenzt werden.

**Priorität:** Muss  
**Abnahmekriterium:** Jeder Protokolladapter prüft den finalen Schreibwert gegen zulässige Grenzwerte.

---

## 13. Echtzeitdaten-Anforderungen

### LH-RT-001 — Realtime Snapshot Store

Das System muss einen Snapshot Store für aktuelle Mess- und Statusdaten bereitstellen.

**Priorität:** Muss  
**Abnahmekriterium:** Der Regelkreis liest konsistente Snapshots aus dem Snapshot Store.

---

### LH-RT-002 — Zusammenführung mehrerer Datenquellen

Das System muss Messdaten aus mehreren aktivierten Quellen zu einem konsistenten Snapshot zusammenführen.

**Priorität:** Muss  
**Quellen:**

- Batterie/BMS
- Wechselrichter
- Netz-/Standortzähler
- PV-Messung
- Regelleistungsaktivierung
- Markt-/Fahrplansignal

**Abnahmekriterium:** Snapshots enthalten Zeitstempel und Qualitätsbewertung für die enthaltenen Daten.

---

### LH-RT-003 — Maximales Messwertalter

Das System muss ein maximales Messwertalter konfigurieren können.

**Priorität:** Muss  
**Standardwert:** 3 Sekunden  
**Abnahmekriterium:** Überschreitet ein Messwert das maximale Alter, wird der Snapshot als stale bewertet.

---

### LH-RT-004 — Echtzeitnahe Verarbeitung

Das System muss Messdaten echtzeitnah verarbeiten.

**Priorität:** Muss  
**Zielwert:** Regelzyklus 1 Sekunde  
**Abnahmekriterium:** Unter Normalbedingungen wird ein vollständiger Regelzyklus innerhalb des konfigurierten Zyklusintervalls abgeschlossen. Das System beansprucht keine harte Echtzeitfähigkeit; Anforderungen mit härterem Zeitverhalten müssen über Edge-Controller oder herstellerspezifische Steuerungen abgegrenzt werden.

---

### LH-RT-005 — Event- und Polling-Unterstützung

Das System soll sowohl ereignisbasierte als auch zyklische Messdatenerfassung unterstützen.

**Priorität:** Soll  
**Beispiele:**

- MQTT Subscribe
- OPC-UA Subscription
- Modbus TCP Polling

**Abnahmekriterium:** Adapter können unterschiedliche Erfassungsarten in denselben Snapshot Store schreiben.

---

## 14. Protokollanforderungen allgemein

### LH-PROT-001 — Einheitliches Adapterinterface

Alle Protokolladapter müssen ein gemeinsames internes Interface implementieren.

**Priorität:** Muss  
**Abnahmekriterium:** Der Regelkreis ist unabhängig vom verwendeten Feldprotokoll.

---

### LH-PROT-002 — Protokollfehler

Protokollfehler müssen in Datenqualität oder Adapterstatus abgebildet werden.

**Priorität:** Muss  
**Abnahmekriterium:** Ein Timeout, Verbindungsfehler oder fehlerhafter Statuscode führt zu entsprechendem Quality Flag.

---

### LH-PROT-003 — Wiederverbindung

Protokolladapter müssen Wiederverbindungsstrategien unterstützen.

**Priorität:** Muss  
**Abnahmekriterium:** Nach temporärem Verbindungsverlust versucht der Adapter automatisch, die Verbindung wiederherzustellen.

---

### LH-PROT-004 — Keine retained Commands ohne Freigabe

Das System darf keine dauerhaft gespeicherten Befehle verwenden, wenn dadurch alte Commands später wirksam werden können.

**Priorität:** Muss  
**Abnahmekriterium:** MQTT retained Commands sind standardmäßig deaktiviert.

---

## 15. Modbus-TCP-Anforderungen

### LH-MODB-001 — Modbus TCP Lesen

Das System muss Modbus-TCP-Register zyklisch lesen können.

**Priorität:** Muss  
**Abnahmekriterium:** Registerwerte können gelesen, skaliert und in interne Telemetrie umgewandelt werden.

---

### LH-MODB-002 — Modbus TCP Schreiben

Das System muss Modbus-TCP-Register oder Coils schreiben können.

**Priorität:** Muss  
**Abnahmekriterium:** Leistungssollwerte können als skalierte Registerwerte geschrieben werden.

---

### LH-MODB-003 — Registermapping über Konfiguration

Modbus-Register müssen über Konfiguration gemappt werden.

**Priorität:** Muss  
**Konfigurierbar:**

- Host
- Port
- UnitId
- Registertyp
- Adresse
- Datentyp
- Skalierung
- Offset
- Endianness

**Abnahmekriterium:** Registeradressen sind nicht hart im Quellcode verdrahtet.

---

### LH-MODB-004 — Endianness

Das System muss unterschiedliche Byte- und Word-Reihenfolgen unterstützen.

**Priorität:** Muss  
**Abnahmekriterium:** 32-bit- und Float-Werte können korrekt gemäß Geräte-Mapping interpretiert werden.

---

### LH-MODB-005 — Timeout

Modbus-Kommunikation muss Timeouts unterstützen.

**Priorität:** Muss  
**Abnahmekriterium:** Bei Timeout wird der Messwert ungültig und der Adapterstatus entsprechend gesetzt.

---

## 16. OPC-UA-Anforderungen

### LH-OPCUA-001 — OPC-UA Lesen

Das System liest OPC-UA Nodes.

**Priorität:** Soll  
**Abnahmekriterium:** Node-Werte können gelesen und in interne Telemetrie umgewandelt werden.

---

### LH-OPCUA-002 — OPC-UA Schreiben

Das System schreibt OPC-UA Nodes.

**Priorität:** Soll  
**Abnahmekriterium:** Leistungssollwerte können über konfigurierte NodeIds geschrieben werden.

---

### LH-OPCUA-003 — OPC-UA Subscriptions

Das System soll OPC-UA Subscriptions unterstützen.

**Priorität:** Soll  
**Abnahmekriterium:** Änderungen ausgewählter Nodes können ereignisbasiert verarbeitet werden.

---

### LH-OPCUA-004 — OPC-UA StatusCode

Das System wertet OPC-UA StatusCodes aus.

**Priorität:** Soll  
**Abnahmekriterium:** Werte mit schlechtem StatusCode werden als ungültig markiert.

---

### LH-OPCUA-005 — OPC-UA Security

Das System unterstützt OPC-UA Security Modes.

**Priorität:** Soll  
**Abnahmekriterium:** Zertifikate, Security Mode und Security Policy können konfiguriert werden.

---

## 17. MQTT-Anforderungen

### LH-MQTT-001 — MQTT Telemetrie-Empfang

Das System muss MQTT-Telemetriedaten empfangen können.

**Priorität:** Muss  
**Abnahmekriterium:** JSON-Payloads werden deserialisiert und in interne Telemetrie umgewandelt.

---

### LH-MQTT-002 — MQTT Command Publishing

Das System muss Commands über MQTT publizieren können.

**Priorität:** Muss  
**Abnahmekriterium:** BatteryCommand wird als JSON auf ein konfiguriertes Topic publiziert.

---

### LH-MQTT-003 — MQTT Topic-Konvention

Das System muss eine konfigurierbare Topic-Struktur unterstützen.

**Priorität:** Muss  
**Standardstruktur:**

- `battery/{assetId}/telemetry`
- `battery/{assetId}/status`
- `battery/{assetId}/command`
- `battery/{assetId}/command/ack`
- `battery/{assetId}/fault`

**Abnahmekriterium:** Topics können pro Asset konfiguriert werden.

---

### LH-MQTT-004 — MQTT QoS

Das System soll MQTT QoS konfigurieren können.

**Priorität:** Soll  
**Empfehlung:** QoS 1 für Commands  
**Abnahmekriterium:** QoS-Level ist pro Publisher/Subscriber konfigurierbar.

---

### LH-MQTT-005 — Command Acknowledgement

MQTT-Commands sollen bestätigt werden können.

**Priorität:** Soll  
**Abnahmekriterium:** Commands besitzen CommandId und können mit ACK-Nachricht korreliert werden.

---

## 18. Native-Core-Anforderungen

### LH-NATIVE-001 — C/C++ für performance-kritische Komponenten

Das System soll C/C++ für performance-kritische Komponenten unterstützen.

**Priorität:** Soll  
**Mögliche Komponenten:**

- Constraint Limiter
- Ramp Limiter
- PID-Regler
- State-Space-Modelle
- Kalman-Filter
- MPC-Kernel
- schnelle Plausibilitätsprüfung
- native Solver-Anbindung

**Abnahmekriterium:** Native Komponenten können aus .NET heraus verwendet werden.

---

### LH-NATIVE-002 — Stabile C-ABI

Native Komponenten müssen über eine stabile C-ABI verfügbar sein.

**Priorität:** Muss, falls Native Core eingesetzt wird  
**Abnahmekriterium:** Es werden keine C++-Klassen oder Exceptions über die Sprachgrenze exportiert.

---

### LH-NATIVE-003 — Keine Speicherallokation über Sprachgrenzen

Native Komponenten dürfen keinen Speicher an .NET übergeben, der von .NET freigegeben werden müsste.

**Priorität:** Muss, falls Native Core eingesetzt wird  
**Abnahmekriterium:** Datenaustausch erfolgt über primitive Typen und explizit layoutete Structs.

---

### LH-NATIVE-004 — Native Fehlercodes

Native Komponenten müssen Fehler über Statuscodes melden.

**Priorität:** Muss, falls Native Core eingesetzt wird  
**Abnahmekriterium:** Fehler führen in .NET zu sicherem Fallback.

---

### LH-NATIVE-005 — ABI-Versionierung

Native Komponenten müssen eine ABI-Version bereitstellen.

**Priorität:** Muss, falls Native Core eingesetzt wird  
**Abnahmekriterium:** Die .NET-Anwendung prüft beim Start die ABI-Version.

---

### LH-NATIVE-006 — Container-Build

Native Komponenten müssen im Docker-Build reproduzierbar gebaut werden können.

**Priorität:** Muss, falls Native Core eingesetzt wird  
**Abnahmekriterium:** Docker Multi-Stage Build erzeugt .NET-Anwendung und native `.so`-Bibliothek.

---

## 19. Persistenz-Anforderungen

### LH-PERSIST-001 — Speicherung von Messdaten

Das System muss Messdaten speichern können.

**Priorität:** Muss  
**Daten:**

- Telemetrie
- Statusinformationen
- Datenqualität
- Quelle
- Zeitstempel

**Abnahmekriterium:** Messdaten können historisch abgefragt werden.

---

### LH-PERSIST-002 — Speicherung von Commands

Das System muss erzeugte Commands speichern können.

**Priorität:** Muss  
**Abnahmekriterium:** Jeder ausgegebene Command ist mit Zeitstempel, Zielwert und Grund nachvollziehbar.

---

### LH-PERSIST-003 — Speicherung von Fahrplänen

Das System muss die im jeweiligen Release unterstützten Fahrpläne speichern können.

**Priorität:** Muss  
**Abnahmekriterium:** Day-Ahead-, Intraday- und Regelleistungs-Fahrpläne sind versioniert ablegbar.

---

### LH-PERSIST-004 — Speicherung von Operator-Kommandos

Das System muss manuelle Eingriffe speichern können.

**Priorität:** Muss  
**Abnahmekriterium:** Operator, Zeitpunkt, Befehl und Begründung werden auditierbar gespeichert.

---

### LH-PERSIST-005 — Datenbank

Das System verwendet PostgreSQL. TimescaleDB darf als kompatible Erweiterung für Zeitreihendaten eingesetzt werden.

**Priorität:** Muss

**Abnahmekriterium:** Datenbankzugriff ist konfigurierbar und mit PostgreSQL containerisiert betreibbar. TimescaleDB-Nutzung erfordert keine Änderung der fachlichen Persistenzmodelle.

---

### LH-PERSIST-006 — Aufbewahrung und Datenvolumen

Das System muss Aufbewahrungsregeln für historische Betriebsdaten definieren können.

**Priorität:** Muss

**Mindestumfang:**

- getrennte Aufbewahrungsdauer für Telemetrie, Commands, Fahrpläne, Operator-Kommandos und Audit-Ereignisse
- konfigurierbare Begrenzung oder Archivierung hochfrequenter Messdaten
- dokumentiertes Verhalten bei voller Datenbank oder fehlgeschlagenem Persistenzzugriff
- keine automatische Löschung auditrelevanter Daten ohne explizite Konfiguration

**Abnahmekriterium:** Retention- und Datenvolumenregeln sind konfigurierbar dokumentiert; Tests oder Betriebschecks weisen nach, dass ein Persistenzfehler nicht zu undefiniertem Regelverhalten führt.

---

### LH-PERSIST-007 — Speicherung von Optimierungsläufen

Das System soll Optimierungsläufe und deren Ergebnisse speichern können, sobald Optimierung produktiv eingesetzt wird.

**Priorität:** Soll

**Mindestdaten:**

- RunId
- AssetId
- Input-Versionen
- Horizon Start/Ende und Zeitschritt
- Solver und Solverstatus
- Objective Breakdown
- erzeugte Fahrplanversion
- Laufzeit, Abbruchgrund und Warnungen

**Abnahmekriterium:** Ein Optimierungslauf ist nachträglich mit seiner erzeugten Fahrplanversion und den verwendeten Inputs verknüpfbar.

---

## 20. API-Anforderungen

Mindestens Health, Batterie-Status, aktueller Command, Fahrplanabfrage und Operator Stop sind über API verfügbar. Schreibende Endpunkte sind authentifiziert, autorisiert und auditierbar. Endpunkte zur Optimierungsauslösung sind über das Optimierungsmodul verfügbar.

### LH-API-001 — Health Endpoint

Das System muss einen Health Endpoint bereitstellen.

**Priorität:** Muss  
**Beispiel:** `GET /health`  
**Abnahmekriterium:** Der Endpoint liefert Betriebszustand der Anwendung.

---

### LH-API-002 — Batterie-Status

Das System muss aktuellen Batteriestatus über API bereitstellen.

**Priorität:** Muss  
**Beispiel:** `GET /battery/{assetId}/status`  
**Abnahmekriterium:** API liefert aktuellen Snapshot oder Statusauszug.

---

### LH-API-003 — Aktueller Command

Das System muss den zuletzt erzeugten Command anzeigen können.

**Priorität:** Muss  
**Beispiel:** `GET /battery/{assetId}/command/current`  
**Abnahmekriterium:** API liefert letzten Command inklusive Reason.

---

### LH-API-004 — Fahrplanabfrage

Das System muss aktuelle und historische Fahrpläne abrufen können.

**Priorität:** Muss  
**Beispiel:** `GET /markets/schedules/current`  
**Abnahmekriterium:** Aktiver Fahrplan ist über API verfügbar.

---

### LH-API-005 — Optimierungsauslösung

Das System soll Optimierungsläufe über API starten können.

**Priorität:** Soll  
**Beispiele:**

- `POST /markets/day-ahead/optimize`
- `POST /markets/intraday/reoptimize`

**Abnahmekriterium:** Ein Optimierungslauf kann ausgelöst werden und liefert mindestens RunId, Status, Horizon, Solverstatus und erzeugte Fahrplanversion oder Fehlergrund.

---

### LH-API-006 — Operator Stop

Das System muss einen manuellen Stop über API ermöglichen.

**Priorität:** Muss  
**Beispiel:** `POST /operator/stop`  
**Abnahmekriterium:** Der Regelkreis erzeugt nach Operator Stop keinen aktiven Lade-/Entladebefehl mehr.

---

### LH-API-007 — Authentifizierung und Autorisierung

Schreibende API-Endpunkte müssen gegen unberechtigten Zugriff geschützt werden.

**Priorität:** Muss  
**Mindestumfang:**

- authentifizierte Zugriffe für Operator-Kommandos
- rollen- oder rechtebasierte Freigabe für schreibende Aktionen
- Ablehnung unbekannter oder unzureichend berechtigter Clients
- Audit-Log für angenommene und abgelehnte schreibende Operator-Aktionen

**Abnahmekriterium:** Schreibende Endpunkte wie `POST /operator/stop` können ohne gültige Berechtigung nicht ausgeführt werden.

---

### LH-API-008 — Transport- und Netzwerkschutz

Produktive API-Kommunikation muss gegen ungeschützte Netzzugriffe abgesichert werden.

**Priorität:** Muss für produktiven Betrieb  
**Abnahmekriterium:** Für produktive Deployments ist TLS-Terminierung oder ein dokumentierter Betrieb hinter einem abgesicherten Reverse Proxy vorgesehen. Unsichere lokale Entwicklungsendpunkte sind klar als Entwicklungsmodus gekennzeichnet.

---

## 21. Monitoring- und Observability-Anforderungen

### LH-MON-001 — Logging

Das System muss strukturierte Logs erzeugen.

**Priorität:** Muss  
**Abnahmekriterium:** Logs enthalten Zeitstempel, AssetId, Komponente, Entscheidung und Reason.

---

### LH-MON-002 — Metriken

Das System muss technische und fachliche Metriken bereitstellen.

**Priorität:** Muss  
**Beispiele:**

- Regelzyklusdauer
- Anzahl ungültiger Snapshots
- Kommunikationsfehler
- aktuelle Leistung
- SOC
- Command-Latenz
- Optimierungslaufzeit
- Solverstatus

**Abnahmekriterium:** Metriken sind für Monitoring exportierbar.

---

### LH-MON-003 — Tracing

Das System soll OpenTelemetry Tracing unterstützen.

**Priorität:** Soll  
**Abnahmekriterium:** Kritische Abläufe wie Optimierung, Snapshot-Erzeugung und Command-Ausgabe sind nachvollziehbar.

---

### LH-MON-004 — Entscheidungsbegründung

Jeder erzeugte Command muss eine fachliche Begründung enthalten.

**Priorität:** Muss  
**Abnahmekriterium:** Commands enthalten ein Reason-Feld.

---

## 22. Konfigurationsanforderungen

### LH-CONF-001 — Externe Konfiguration

Das System muss über externe Konfiguration parametrierbar sein.

**Priorität:** Muss  
**Konfigurierbar:**

- Assets
- Geräte-Capabilities
- Gerätepunkt-Metadaten
- Protokolladapter
- Registermappings
- NodeIds, falls OPC-UA eingesetzt wird
- MQTT Topics
- technische Grenzen
- Rampen
- Marktparameter
- Optimierungsparameter
- Sicherheitsparameter

**Abnahmekriterium:** Änderungen an Anlagenparametern erfordern keine Codeänderung.

---

### LH-CONF-002 — Versionierte Gerätemappings

Gerätemappings müssen versionierbar sein.

**Priorität:** Muss  
**Abnahmekriterium:** Mappings der implementierten Adapter sind als Konfigurationsdateien versioniert ablegbar. Im MVP gilt dies für Modbus TCP und MQTT; OPC-UA-Mappings folgen mit dem OPC-UA-Adapter.

---

### LH-CONF-003 — Validierung der Konfiguration

Das System muss Konfiguration beim Start validieren.

**Priorität:** Muss  
**Abnahmekriterium:** Ungültige oder unvollständige Konfiguration verhindert einen unsicheren Start.

---

### LH-CONF-004 — Export- und Northbound-Konfiguration

Das System stellt externe Datenexporte konfigurierbar bereit.

**Priorität:** Soll

**Mindestumfang:**

- aktivierbare Exportziele
- exportierte Assets und Punkte
- Protokoll, z. B. MQTT, Modbus TCP oder HTTP
- Upload- oder Polling-Intervall
- Authentifizierung und Transportverschlüsselung, falls vom Zielprotokoll unterstützt
- Runtime-Reload ohne Neustart, falls sicher möglich
- Status pro Exportziel

**Abnahmekriterium:** Ein Exportziel kann aktiviert, deaktiviert und im Status abgefragt werden, ohne den internen Regelkreis zu verändern.

---

## 23. Nicht-funktionale Anforderungen

### LH-NF-001 — Programmiersprache Hauptsystem

Das Hauptsystem muss in C#/.NET implementiert werden.

**Priorität:** Muss  
**Abnahmekriterium:** Worker, API, Domain, Marktlogik und Orchestrierung sind in C#/.NET umgesetzt.

---

### LH-NF-002 — Performance-kritische Komponenten

Performance-kritische Komponenten dürfen in C/C++ implementiert werden.

**Priorität:** Soll  
**Abnahmekriterium:** C/C++-Komponenten können über P/Invoke, gRPC Sidecar oder vergleichbare Schnittstelle integriert werden.

---

### LH-NF-003 — Betriebssystem

Das System muss unter Linux lauffähig sein.

**Priorität:** Muss  
**Abnahmekriterium:** Anwendung läuft in Linux-Containern.

---

### LH-NF-004 — Containerisierung

Das System muss containerisiert bereitgestellt werden können.

**Priorität:** Muss  
**Abnahmekriterium:** Dockerfile und Docker Compose sind vorhanden.

---

### LH-NF-005 — Verfügbarkeit

Das System soll bei temporären Kommunikationsfehlern weiter betrieben werden, sofern ein sicherer Zustand gewährleistet ist.

**Priorität:** Soll  
**Abnahmekriterium:** Kommunikationsfehler führen nicht zu undefiniertem Verhalten.

---

### LH-NF-006 — Wartbarkeit

Das System muss wartbar und modular testbar sein.

**Priorität:** Muss  
**Abnahmekriterium:** Kernlogik ist unabhängig von Infrastruktur testbar.

---

### LH-NF-007 — Erweiterbarkeit

Das System soll um weitere Protokolle, Märkte und Optimierungsverfahren erweiterbar sein.

**Priorität:** Soll  
**Abnahmekriterium:** Neue Adapter oder Optimierer können ohne Änderung der zentralen Regelpipeline ergänzt werden.

---

### LH-NF-008 — Nachvollziehbarkeit

Entscheidungen des Systems müssen nachvollziehbar sein.

**Priorität:** Muss  
**Abnahmekriterium:** Für jeden Command sind Eingangsdaten, Zielwert, Begrenzungen und Reason rekonstruierbar.

---

### LH-NF-009 — Sicherheit gegen Fehlbedienung

Operator-Kommandos müssen validiert werden.

**Priorität:** Muss  
**Abnahmekriterium:** Ungültige manuelle Sollwerte werden abgelehnt oder begrenzt.

---

## 24. Deployment-Anforderungen

### LH-DEPLOY-001 — Dockerfile

Das System muss ein Dockerfile enthalten.

**Priorität:** Muss  
**Abnahmekriterium:** Das Dockerfile baut die Anwendung reproduzierbar.

---

### LH-DEPLOY-002 — Docker Compose

Das System muss eine Docker-Compose-Umgebung für lokale Entwicklung enthalten.

**Priorität:** Muss  
**Komponenten:**

- bess-ems-worker
- API-Komponente, entweder als eigener bess-ems-api-Service oder im Worker integriert
- PostgreSQL
- MQTT Broker
- optional Monitoring Stack

**Abnahmekriterium:** Lokaler Start ist mit `docker compose up` möglich.

---

### LH-DEPLOY-003 — Multi-Stage Build

Das Dockerfile soll Multi-Stage Builds verwenden.

**Priorität:** Soll  
**Abnahmekriterium:** Build- und Runtime-Image sind getrennt.

---

### LH-DEPLOY-004 — Native Build

Wenn C/C++-Komponenten vorhanden sind, müssen diese im Container-Build gebaut oder eingebunden werden können.

**Priorität:** Muss, falls Native Core eingesetzt wird  
**Abnahmekriterium:** Native `.so`-Bibliothek ist im Runtime-Container verfügbar.

---

## 25. Betriebsanforderungen

### LH-OPS-001 — Sicherer Start

Das System darf nur starten, wenn Mindestkonfigurationen gültig sind.

**Priorität:** Muss  
**Abnahmekriterium:** Fehlende Sicherheitsgrenzen verhindern aktiven Regelbetrieb.

---

### LH-OPS-002 — Sicherer Stopp

Das System muss kontrolliert gestoppt werden können.

**Priorität:** Muss  
**Abnahmekriterium:** Beim Stop wird ein definierter sicherer Command erzeugt oder die Ausgabe deaktiviert.

---

### LH-OPS-003 — Wiederanlauf

Das System soll nach Neustart den letzten bekannten sicheren Zustand berücksichtigen.

**Priorität:** Soll  
**Abnahmekriterium:** Nach Neustart wird nicht unkontrolliert ein alter Fahrplanbefehl ausgegeben.

---

### LH-OPS-004 — Auditierbarkeit

Betriebsrelevante Entscheidungen müssen auditierbar sein.

**Priorität:** Muss  
**Abnahmekriterium:** Fahrplanänderungen, Operator-Kommandos, Fehler und Commands werden gespeichert.

---

## 26. Testanforderungen

### LH-TEST-001 — Unit Tests für Regelung

Das System muss Unit Tests für Regelungskomponenten besitzen.

**Priorität:** Muss  
**Komponenten:**

- Constraint Limiter
- Ramp Limiter
- State Machine
- PID-Regler, falls eingesetzt
- Snapshot Validation

**Abnahmekriterium:** Tests decken Normalfälle und Grenzfälle ab.

---

### LH-TEST-002 — Unit Tests für Marktlogik

Das System muss Unit Tests für Markt- und Fahrplanlogik besitzen.

**Priorität:** Muss  
**Abnahmekriterium:** Day-Ahead-, statische Fahrplan-, Intraday- und Regelleistungslogik sind testbar.

---

### LH-TEST-003 — Integrationstests für Protokolladapter

Das System muss Integrationstests für Protokolladapter besitzen.

**Priorität:** Muss
**Abnahmekriterium:** Modbus-, MQTT- und OPC-UA-Adapter sind gegen Simulatoren testbar.

---

### LH-TEST-004 — Replay-Tests

Das System soll historische Messdaten wiedergeben und gegen den Regelkreis testen können.

**Priorität:** Soll  
**Abnahmekriterium:** Messdaten-Replay erzeugt reproduzierbare Commands.

---

### LH-TEST-005 — Native Interop Tests

Wenn Native Core eingesetzt wird, müssen Interop-Tests vorhanden sein.

**Priorität:** Muss, falls Native Core eingesetzt wird  
**Abnahmekriterium:** Struct Layout, ABI-Version, Fehlercodes und Ergebniswerte werden getestet.

---

### LH-TEST-006 — Sicherheitsfall-Tests

Das System muss Sicherheitsfälle testen.

**Priorität:** Muss  
**Sicherheitsfälle:**

- Emergency Stop
- BMS nicht verfügbar
- Wechselrichter nicht verfügbar
- SOC ungültig
- Temperatur ungültig
- veralteter Snapshot
- Kommunikationsverlust
- ungültiger Command

**Abnahmekriterium:** Jeder Sicherheitsfall führt zu definiertem sicherem Verhalten.

---

### LH-TEST-007 — Container-Tests

Das System muss im Container getestet werden.

**Priorität:** Muss
**Abnahmekriterium:** Die Anwendung startet im Container und führt Healthchecks erfolgreich aus.

---

## 27. V-Modell-ähnliche Rückverfolgbarkeit

### 27.1 Anforderung zu Design

| Lastenheft-Kennung | Design-Artefakt                                |
| ------------------ | ---------------------------------------------- |
| LH-ARCH-001        | Systemarchitektur                              |
| LH-DOM-001         | Domain Model                                   |
| LH-DOM-005         | Device Point Model                             |
| LH-DOM-006         | Capability Model                               |
| LH-MKT-001         | Market Module                                  |
| LH-MKT-003         | Market Commitment Model                        |
| LH-MKT-008         | Tariff/Price Model                             |
| LH-MKT-009         | Market Product Model                           |
| LH-OPT-001         | Optimization Interface Design                  |
| LH-OPT-007         | Schedule vs Dispatch Boundary Design           |
| LH-OPT-008         | Optimization Unit Convention                   |
| LH-OPT-009         | Optimization Result Model                      |
| LH-CTRL-001        | Control Loop Design                            |
| LH-SM-001          | State Machine Design                           |
| LH-PROT-001        | Adapter Interface Design                       |
| LH-NATIVE-001      | Native Core Design                             |
| LH-PERSIST-001     | Database Schema                                |
| LH-PERSIST-006     | Retention- und Datenvolumenkonzept             |
| LH-PERSIST-007     | Optimization Run Persistence Design            |
| LH-API-001         | API Specification                              |
| LH-API-007         | Authentifizierungs- und Autorisierungskonzept  |
| LH-MON-001         | Observability Design                           |
| LH-MON-002         | Metrics Design                                 |
| LH-CONF-001        | Configuration Design                           |
| LH-CONF-002        | Device Mapping Design                          |
| LH-CONF-003        | Configuration Validation Design                |
| LH-CONF-004        | Northbound Export Configuration Design         |

---

### 27.3 Anforderung zu Test

| Lastenheft-Kennung | Testtyp                       |
| ------------------ | ----------------------------- |
| LH-CTRL-002        | Unit Test                     |
| LH-CTRL-003        | Unit Test                     |
| LH-MKT-003         | Unit Test                     |
| LH-MKT-007         | Unit/Integration Test         |
| LH-MKT-008         | Unit Test                     |
| LH-OPT-008         | Unit/Model Test               |
| LH-OPT-009         | Integration Test              |
| LH-SAFE-001        | Safety Test                   |
| LH-SAFE-004        | Integration Test              |
| LH-API-006         | API/Control Integration Test  |
| LH-API-007         | API Security Test             |
| LH-API-001         | API Contract Test             |
| LH-API-002         | API Contract Test             |
| LH-API-003         | API Contract Test             |
| LH-API-004         | API Contract Test             |
| LH-PERSIST-006     | Persistence/Retention Test    |
| LH-MON-001         | Logging Test                  |
| LH-MON-002         | Metrics Export Test           |
| LH-CONF-001        | Configuration Loading Test    |
| LH-CONF-002        | Device Mapping Test           |
| LH-CONF-003        | Startup Configuration Test    |
| LH-CONF-004        | Integration Test              |
| LH-RT-003          | Unit Test                     |
| LH-MODB-005        | Integration Test              |
| LH-MQTT-005        | Integration Test              |
| LH-OPCUA-004       | Integration Test              |
| LH-NATIVE-002      | Interop Test                  |
| LH-DEPLOY-001      | Container Test                |

---

## 29. Risiken

### LH-RISK-001 — Regelleistung und Echtzeitfähigkeit

Regelleistung, Emergency Stop und zertifizierungsrelevante Schutzfunktionen können regulatorisch und technisch höhere Anforderungen haben als ein Docker-basierter .NET-Regelkreis sicher erfüllen kann.

**Bewertung:** Hoch  
**Maßnahme:** Klare Produktabgrenzung, hardwareseitige Schutzketten und ggf. Edge-Controller oder Herstellersteuerung für harte Echtzeit verwenden.

---

### LH-RISK-002 — Herstellerabhängige Protokollmappings

Modbus-Register, OPC-UA NodeIds und MQTT-Payloads unterscheiden sich je Hersteller und Firmware.

**Bewertung:** Hoch  
**Maßnahme:** Mappings extern konfigurieren und versionieren.

---

### LH-RISK-003 — Native Core Komplexität

C/C++ erhöht Performance, aber auch Risiko für Speicherfehler, ABI-Probleme und Debugging-Aufwand.

**Bewertung:** Mittel  
**Maßnahme:** Kleine native Schnittstelle, C-ABI, Interop-Tests, Fallback-Implementierung in .NET.

---

### LH-RISK-004 — Optimierungsmodell zu früh zu komplex

Ein zu früher Fokus auf MILP/MPC kann die robuste technische Ausführung verzögern.

**Bewertung:** Mittel  
**Maßnahme:** Erst sichere Regelpipeline implementieren, danach Optimierung ausbauen.

---

## 31. Offene Punkte

| Kennung     | Frage                                                                           | Status |
| ----------- | ------------------------------------------------------------------------------- | ------ |
| LH-OPEN-003 | Welche Marktpreisquellen sollen angebunden werden?                              | Offen — Open-Source-Default ist quellenneutraler Preisreihen-Import/API; konkrete externe Quellen nur als optionale Adapter nach Lizenz-/Nutzungsprüfung |

---

## 32. Zusammenfassung

`bess-ems` soll ein modulares, containerisiertes Battery Energy Management System werden.

Der Kern des Systems ist nicht der Optimierer allein, sondern die sichere Kette:

```text
Messdaten
→ Validierung
→ Snapshot
→ State Machine
→ Markt-/Fahrplanauflösung
→ Regelleistungspriorisierung, falls aktiviert
→ Constraint Limiter
→ Ramp Limiter
→ Command
→ Protokolladapter
```

Die wichtigste Architekturregel lautet:

```text
Optimierung liefert Wunschwerte.
Der technische Regelkreis entscheidet, was sicher gefahren wird.
```

Diese Trennung ist zwingend, damit das System bei realen Batteriespeichern robust, testbar und betriebssicher bleibt.
