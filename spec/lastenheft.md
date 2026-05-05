# Lastenheft: bess-ems

**Projektname:** bess-ems  
**System:** Battery Energy Management System für Batteriespeicher  
**Dokumenttyp:** Lastenheft  
**Format:** Markdown  
**Version:** 0.1.2
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
- Regelleistungs-Aktivierungssignale nach MVP
- Datenbank
- Monitoring-System
- Operator-UI oder API-Client

**Priorität:** Muss  
**Abnahmekriterium:** Für jeden im jeweiligen Release unterstützten externen Systemtyp existiert eine dokumentierte Schnittstelle oder ein Adapterkonzept.

---

### LH-KTX-002 — Kommunikationsprotokolle

Das System muss Batteriesysteme und zugehörige Feldgeräte im MVP über Modbus TCP und MQTT anbinden können. OPC-UA soll als nachgelagerter Adapter vorbereitet werden.

- Modbus TCP
- MQTT
- OPC-UA nach MVP

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

### 4.1 Fachliche Konventionen

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

## 8. Marktanforderungen

### LH-MKT-001 — Day-Ahead-Fahrplan

Das System muss Day-Ahead-Fahrpläne verarbeiten können.

**Priorität:** Muss  
**Beschreibung:** Das System muss im MVP Day-Ahead-Fahrpläne importieren und im Regelkreis verwenden können. Die automatische Erzeugung von Day-Ahead-Fahrplänen auf Basis von Preisen, Prognosen und technischen Grenzen folgt mit dem Optimierungsmodul nach dem MVP.

**Abnahmekriterium:** Ein Day-Ahead-Fahrplan kann gespeichert, gelesen und im Regelkreis verwendet werden.

---

### LH-MKT-002 — Intraday-Reoptimierung

Das System soll Intraday-Anpassungen nach dem MVP unterstützen.

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

Das System soll reservierte Leistung für Regelleistung nach dem MVP verwalten können.

**Priorität:** Soll  
**Beschreibung:** Reservierte Lade- und Entladeleistung muss für andere Märkte blockiert werden können.  
**Abnahmekriterium:** Day-Ahead- und Intraday-Optimierung verletzen keine reservierten Regelleistungsbereiche.

---

### LH-MKT-005 — Regelleistungsaktivierung

Das System soll Aktivierungssignale für Regelleistung nach dem MVP verarbeiten können.

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

## 9. Optimierungsanforderungen

### LH-OPT-001 — Fahrplanoptimierung

Das System soll nach dem MVP Fahrpläne für Lade- und Entladeleistung optimieren können.

**Priorität:** Soll  
**Abnahmekriterium:** Für einen definierten Zeitraum wird eine Zeitreihe mit Leistungssollwerten und SOC-Zielwerten erzeugt.

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

Für dieses Lastenheft bedeutet sicherer Zustand im MVP:

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

Das System soll nach dem MVP OPC-UA Nodes lesen können.

**Priorität:** Soll  
**Abnahmekriterium:** Node-Werte können gelesen und in interne Telemetrie umgewandelt werden.

---

### LH-OPCUA-002 — OPC-UA Schreiben

Das System soll nach dem MVP OPC-UA Nodes schreiben können.

**Priorität:** Soll  
**Abnahmekriterium:** Leistungssollwerte können über konfigurierte NodeIds geschrieben werden.

---

### LH-OPCUA-003 — OPC-UA Subscriptions

Das System soll OPC-UA Subscriptions unterstützen.

**Priorität:** Soll  
**Abnahmekriterium:** Änderungen ausgewählter Nodes können ereignisbasiert verarbeitet werden.

---

### LH-OPCUA-004 — OPC-UA StatusCode

Das System soll nach dem MVP OPC-UA StatusCodes auswerten.

**Priorität:** Soll  
**Abnahmekriterium:** Werte mit schlechtem StatusCode werden als ungültig markiert.

---

### LH-OPCUA-005 — OPC-UA Security

Das System soll nach dem MVP OPC-UA Security Modes unterstützen.

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
**Abnahmekriterium:** Day-Ahead-Fahrpläne sind im MVP versioniert ablegbar. Intraday- und Regelleistungs-Fahrpläne sind versioniert ablegbar, sobald diese Funktionen umgesetzt werden.

---

### LH-PERSIST-004 — Speicherung von Operator-Kommandos

Das System muss manuelle Eingriffe speichern können.

**Priorität:** Muss  
**Abnahmekriterium:** Operator, Zeitpunkt, Befehl und Begründung werden auditierbar gespeichert.

---

### LH-PERSIST-005 — Datenbank

Das System muss im MVP PostgreSQL verwenden. TimescaleDB darf nach dem MVP als kompatible Erweiterung für Zeitreihendaten eingesetzt werden.

**Priorität:** Muss

**Abnahmekriterium:** Datenbankzugriff ist konfigurierbar und im MVP mit PostgreSQL containerisiert betreibbar. Eine spätere TimescaleDB-Nutzung erfordert keine Änderung der fachlichen Persistenzmodelle.

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

## 20. API-Anforderungen

Im MVP müssen mindestens Health, Batterie-Status, aktueller Command, Fahrplanabfrage und Operator Stop über API verfügbar sein. Schreibende Endpunkte müssen bereits im MVP authentifiziert, autorisiert und auditierbar sein. API-Endpunkte zur Optimierungsauslösung folgen mit dem Optimierungsmodul nach dem MVP.

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

**Abnahmekriterium:** Ein Optimierungslauf kann ausgelöst und sein Ergebnis gespeichert werden.

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
**Abnahmekriterium:** Day-Ahead- und statische Fahrplanlogik sind im MVP testbar. Intraday- und Regelleistungslogik sind testbar, sobald diese Funktionen umgesetzt werden.

---

### LH-TEST-003 — Integrationstests für Protokolladapter

Das System soll Integrationstests für Protokolladapter besitzen.

**Priorität:** Soll  
**Abnahmekriterium:** Modbus- und MQTT-Adapter können im MVP gegen Simulatoren getestet werden. OPC-UA-Adapter sind gegen Simulatoren testbar, sobald OPC-UA umgesetzt wird.

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

Das System soll im Container getestet werden.

**Priorität:** Soll  
**Abnahmekriterium:** Die Anwendung startet im Container und führt Healthchecks erfolgreich aus.

---

## 27. V-Modell-ähnliche Rückverfolgbarkeit

### 27.1 Anforderung zu Design

| Lastenheft-Kennung | Design-Artefakt                      | Geltung                    |
| ------------------ | ------------------------------------ | -------------------------- |
| LH-ARCH-001        | Systemarchitektur                    | MVP                        |
| LH-DOM-001         | Domain Model                         | MVP                        |
| LH-MKT-001         | Market Module                        | MVP                        |
| LH-MKT-003         | Market Commitment Model              | MVP                        |
| LH-OPT-001         | Optimization Interface Design        | MVP als Interface          |
| LH-CTRL-001        | Control Loop Design                  | MVP                        |
| LH-SM-001          | State Machine Design                 | MVP                        |
| LH-PROT-001        | Adapter Interface Design             | MVP                        |
| LH-NATIVE-001      | Native Core Design                   | nach MVP / falls eingesetzt |
| LH-PERSIST-001     | Database Schema                      | MVP                        |
| LH-PERSIST-006     | Retention- und Datenvolumenkonzept   | MVP                        |
| LH-API-001         | API Specification                    | MVP                        |
| LH-API-007         | Authentifizierungs- und Autorisierungskonzept | MVP             |
| LH-MON-001         | Observability Design                 | MVP                        |

---

### 27.2 Anforderung zu Implementierung

| Lastenheft-Kennung | Implementierung                | Geltung                    |
| ------------------ | ------------------------------ | -------------------------- |
| LH-DOM-001         | `BatteryAsset`                 | MVP                        |
| LH-DOM-002         | `BatteryTelemetry`             | MVP                        |
| LH-DOM-003         | `BatteryCommand`               | MVP                        |
| LH-MKT-003         | `MarketCommitment`             | MVP                        |
| LH-CTRL-002        | `ConstraintLimiter`            | MVP                        |
| LH-CTRL-003        | `RampLimiter`                  | MVP                        |
| LH-SM-001          | `BatteryStateMachine`          | MVP                        |
| LH-RT-001          | `RealtimeSnapshotStore`        | MVP                        |
| LH-MODB-001        | `ModbusBatteryTelemetrySource` | MVP                        |
| LH-MQTT-001        | `MqttBatteryTelemetryStream`   | MVP                        |
| LH-OPCUA-001       | `OpcUaBatteryTelemetrySource`  | nach MVP                   |
| LH-NATIVE-001      | `battery_control_core`         | nach MVP / falls eingesetzt |
| LH-API-001         | `/health` Endpoint             | MVP                        |
| LH-API-002         | `/battery/{assetId}/status`    | MVP                        |
| LH-API-003         | `/battery/{assetId}/command/current` | MVP                  |
| LH-API-004         | `/markets/schedules/current`   | MVP                        |
| LH-API-006         | `/operator/stop`               | MVP                        |
| LH-API-007         | Schreibschutz für Operator-Endpunkte | MVP                    |

---

### 27.3 Anforderung zu Test

| Lastenheft-Kennung | Testtyp          | Geltung                    |
| ------------------ | ---------------- | -------------------------- |
| LH-CTRL-002        | Unit Test        | MVP                        |
| LH-CTRL-003        | Unit Test        | MVP                        |
| LH-MKT-003         | Unit Test        | MVP                        |
| LH-MKT-007         | Unit/Integration Test | MVP                   |
| LH-SAFE-001        | Safety Test      | MVP                        |
| LH-SAFE-004        | Integration Test | MVP                        |
| LH-API-006         | API/Control Integration Test | MVP              |
| LH-API-007         | API Security Test | MVP                       |
| LH-RT-003          | Unit Test        | MVP                        |
| LH-MODB-005        | Integration Test | MVP                        |
| LH-MQTT-005        | Integration Test | MVP                        |
| LH-OPCUA-004       | Integration Test | nach MVP                   |
| LH-NATIVE-002      | Interop Test     | nach MVP / falls eingesetzt |
| LH-DEPLOY-001      | Container Test   | MVP                        |

---

## 28. MVP-Abgrenzung

### 28.1 Muss im MVP enthalten sein

- C#/.NET Worker Service
- Domain-Modell
- Realtime Snapshot Store
- State Machine
- Constraint Limiter
- Ramp Limiter
- Optimization-Interface ohne produktiven Solver
- MQTT Adapter
- Modbus TCP Adapter
- statischer Fahrplanimport
- einfache Day-Ahead-Fahrplanverfolgung
- Marktverpflichtungsmodell für verbindliche Fahrplanvorgaben
- PostgreSQL-Persistenz
- Health API
- Batterie-Status API
- Aktueller-Command API
- Fahrplanabfrage API
- Operator-Stop API mit Authentifizierung, Autorisierung und Audit-Log
- strukturierte Logs
- Dockerfile
- Docker Compose
- Unit Tests für Kernlogik

---

### 28.2 Soll nach MVP folgen

- OPC-UA Adapter
- Intraday-Reoptimierung
- Regelleistungsreservierung
- Regelleistungsaktivierung
- MILP-Optimierung
- Native C/C++ Control Core
- OpenTelemetry Tracing
- Replay-Testumgebung

---

### 28.3 Spätere Erweiterungen

- MPC
- State-Space-Modelle
- Kalman-Filter
- native Solver-Sidecar
- TimescaleDB
- Operator UI
- Multi-Asset-Flottensteuerung
- Kubernetes Deployment
- Zertifizierungsnahe Regelleistungsintegration

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

## 30. Offene Punkte

| Kennung     | Frage                                                                           | Status |
| ----------- | ------------------------------------------------------------------------------- | ------ |
| LH-OPEN-001 | Welche konkreten Batteriesysteme / Hersteller sollen zuerst unterstützt werden? | Offen  |
| LH-OPEN-002 | Welche Regelleistungsprodukte sind konkret relevant?                            | Offen  |
| LH-OPEN-003 | Welche Marktpreisquellen sollen angebunden werden?                              | Offen  |
| LH-OPEN-004 | Welche produktive Zykluszeit gilt je Anlagenklasse und Betriebsmodus?           | Offen  |
| LH-OPEN-005 | Soll ein Operator UI Bestandteil des Projekts sein?                             | Offen  |
| LH-OPEN-006 | Ab welchem Datenvolumen soll TimescaleDB statt reinem PostgreSQL genutzt werden? | Offen  |
| LH-OPEN-007 | Welche Native-Core-Komponenten sollen zuerst umgesetzt werden?                  | Offen  |

---

## 31. Zusammenfassung

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
