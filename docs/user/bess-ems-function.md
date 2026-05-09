# Funktion eines BESS EMS

## Zweck

Dieses Dokument illustriert die Rolle eines Battery Energy Storage System
Energy Management System (`BESS EMS`). Es beschreibt aus Anwendersicht,
welche Informationen ein EMS verarbeitet, welche Entscheidungen es trifft
und welche Sollwerte es an Batterie- und Leistungselektronik weitergibt.

Das EMS ist dabei nicht die Batterie selbst und auch nicht das
Sicherheits-BMS. Es ist die koordinierende Steuerungs- und
Optimierungsschicht zwischen Markt, Netz, lokaler Anlage und
Batteriesystem.

---

## 1. Gesamtbild

```mermaid
%%{init: {'theme':'base','themeVariables':{'background':'#f8fafc','primaryColor':'#dbeafe','primaryTextColor':'#0f172a','primaryBorderColor':'#1e40af','lineColor':'#8f872a','secondaryColor':'#fef3c7','tertiaryColor':'#dcfce7','noteBkgColor':'#fef3c7','noteTextColor':'#0f172a','noteBorderColor':'#a16207','actorBkg':'#dbeafe','actorBorder':'#1e40af','actorTextColor':'#0f172a','actorLineColor':'#475569','signalColor':'#0f172a','signalTextColor':'#0f172a','sequenceNumberColor':'#ffffff','labelTextColor':'#0f172a','loopTextColor':'#0f172a','edgeLabelBackground':'#f8fafc'}}}%%
flowchart LR
  Grid[Netzanschlusspunkt<br/>PCC] --> Measurements[Messwerte<br/>Leistung, Spannung,<br/>Frequenz]
  Market[Markt / Fahrplan<br/>Preise, Gebote,<br/>Abrufe] --> EMS[BESS EMS]
  Forecast[Prognosen<br/>Last, PV/Wind,<br/>Verfügbarkeit] --> EMS
  Measurements --> EMS
  BMS[BMS<br/>SOC, SOH,<br/>Limits, Freigaben,<br/>Alarme] --> EMS

  EMS --> ScheduleOptimizer[Horizon-Optimierung<br/>Fahrplan / Zielverlauf]
  ScheduleOptimizer --> Dispatcher[Echtzeit-Dispatch<br/>aktueller Sollwert,<br/>Safety- und Ramp-Limits]
  BMS --> Dispatcher

  Dispatcher --> PCS[PCS / Wechselrichter<br/>P/Q-Sollwerte]

  Battery[Batterie] <--> BMS
  Battery <--> PCS
  PCS <--> Grid

  PCS --> Measurements
  BMS --> Measurements
```

Die EMS-Funktion lässt sich in sechs wiederkehrende Schritte gliedern:

1. **Messen:** aktuelle Anlagen-, Batterie- und Netzwerte erfassen.
2. **Bewerten:** technische Grenzen, Alarme, SOC/SOH und Datenqualität
   prüfen.
3. **Optimieren:** Fahrpläne oder Zielverläufe über einen Horizont
   bestimmen. Dieser Schritt läuft nicht zwingend in jedem
   Regelzyklus, sondern kann zeit-, ereignis- oder operatorgetrieben
   aktualisieren.
4. **Dispatchen:** den aktuell gültigen Sollwert auswählen, priorisieren
   und durch Safety- und Ramp-Limits begrenzen.
5. **Ausgeben:** konkrete Wirk- und Blindleistungssollwerte an den PCS
   beziehungsweise den Feldadapter senden.
6. **Nachregeln:** Istwerte beobachten und im nächsten Zyklus
   korrigieren.

---

## 2. Eingaben

| Quelle             | Typische Daten                                               | Zweck im EMS                                              |
| ------------------ | ------------------------------------------------------------ | --------------------------------------------------------- |
| Netzanschlusspunkt | Wirkleistung, Blindleistung, Spannung, Frequenz              | Rückmeldung, ob der Zielwert am PCC erreicht wird         |
| Batterie/BMS       | SOC, SOH, Zell-/Rack-Limits, Temperaturen, Freigaben, Alarme | Schutzgrenzen und verfügbare Lade-/Entladeleistung        |
| PCS/Wechselrichter | aktueller P/Q-Wert, Status, Fehler, Verfügbarkeit            | Umsetzung und Plausibilisierung des Dispatchs             |
| Markt/Fahrplan     | Day-Ahead-Fahrplan, Intraday-Änderungen, Abrufe              | wirtschaftliche oder vertragliche Zielvorgabe             |
| Prognosen          | Last, Erzeugung, Preise, Verfügbarkeit                       | vorausschauende Optimierung statt reiner Momentanregelung |
| Operator           | Betriebsmodus, Sperren, manuelle Limits, Freigaben           | Prioritäten und betriebliche Leitplanken                  |

### 2.1 Rollen der Kernkomponenten

- **BMS (Battery Management System)**  
  Batterieüberwachung/-steuerung, Alarmierung bei abnormalen Zuständen, und
  Bereitstellung von SOC, SOH, Limits sowie Freigaben für das EMS.
- **Auxiliary System**  
  Überwacht Umgebungs- und Sicherheitszustände wie Temperatur,
  unautorisierten Zugriff, USV-Zustand und HVAC und meldet Auffälligkeiten.
- **PCS (Power Conversion System)**  
  Setzt EMS-Kommandos um, steuert Lade- und Entladeleistung und
  übernimmt die AC/DC-Umsetzung.
- **EMS (Energy Management System)**  
  Steuert den Leistungsfluss, überwacht den SoC im Betrieb, unterstützt
  Remote Monitoring und KPI-Management sowie lokales und cloud-basiertes
  Logging und Management.

```mermaid
flowchart LR
  subgraph "BESS-Landschaft"
    BMS[BMS<br/>Batteriezustand & Limits]
    AUX[Auxiliary<br/>Umgebung, USV, HVAC, Zutritt]
    PCS[PCS<br/>AC/DC-Umrichter]
    EMS[EMS<br/>Dispatch & Optimierung]
    BAT[Batterie]
  end

  subgraph "Monitoring-Ebene"
    SCADA[Remote Monitoring<br/>und KPI-Dashboard]
    CLOUD[Cloud-/Historian-Lager<br/>Logging]
  end

  BAT --> BMS
  BAT --> PCS
  PCS --> BMS
  AUX --> BMS
  AUX --> EMS
  BMS --> EMS
  PCS --> EMS
  EMS --> PCS
  EMS --> BMS
  EMS --> SCADA
  EMS --> CLOUD
  SCADA --> EMS

  Grid[Netzanschlusspunkt<br/>PCC] --> PCS
  PCS --> Grid
```

---

## 3. Entscheidungen

Das EMS entscheidet nicht nur, **ob** geladen oder entladen wird, sondern
auch **wie stark**, **wie lange** und **unter welchen Grenzen**.

Typische Entscheidungslogik:

- Das EMS verarbeitet kontinuierlich eintreffende Datenströme (Messwerte,
  Prognosen, Markt- und Statusinformationen), um Lade- und Entladezyklen
  laufend zu optimieren.

- Bei niedrigen Preisen oder lokaler Überschusserzeugung laden.
- Bei hohen Preisen oder Lastspitzen entladen.
- Einen Fahrplan am Netzanschlusspunkt einhalten.
- Reserve für Regelenergie oder Notfallbetrieb freihalten.
- Rampen, SOC-Fenster, Temperaturgrenzen und Anlagenverfügbarkeit
  respektieren.
- Bei schlechter Datenqualität oder kritischen Alarmen in einen
  sicheren Zustand wechseln.

Die Optimierung ist dabei zweigeteilt:

- **Horizon-Optimierung:** erzeugt oder aktualisiert Fahrpläne und
  Zielverläufe für einen Zeitraum, ohne direkt Feldgeräte anzusteuern.
- **Echtzeit-Dispatch:** wählt im Regelzyklus den gerade gültigen
  Sollwert, kombiniert ihn mit aktiven Prioritäten und begrenzt ihn
  durch technische Schutz- und Rampenregeln.

Das Ergebnis ist immer ein Abgleich aus Ziel und Randbedingungen:

```text
Zielwert = wirtschaftliches / netzdienliches Ziel
         begrenzt durch Batterie-Limits
         begrenzt durch Wechselrichter-Limits
         begrenzt durch Sicherheits- und Betriebsregeln
```

---

## 4. Ausgaben

Das Ergebnis des EMS ist ein begrenzter Dispatch an die nachgelagerten
technischen Systeme. Das BMS liefert dafür Schutzgrenzen, Freigaben und
Batteriezustand; P/Q-Sollwerte werden an PCS beziehungsweise
Feldadapter ausgegeben.

| Zielsystem              | Ausgabe                            | Bedeutung                                  |
| ----------------------- | ---------------------------------- | ------------------------------------------ |
| PCS / Wechselrichter    | `P`-Sollwert                       | Laden oder Entladen der Batterie           |
| PCS / Wechselrichter    | `Q`-Sollwert                       | Blindleistungsbereitstellung, falls aktiv  |
| Persistenz / Monitoring | Telemetrie, Commands, Audit-Events | Nachvollziehbarkeit und Betriebsauswertung |
| Operator-Oberfläche     | Status, Fehler, aktueller Modus    | Transparenz für Leitwarte und Betrieb      |

Zur Vermeidung von Mehrdeutigkeit gilt im weiteren Verlauf die interne
BESS-EMS-Konvention dieses Dokuments:

- `+P` bedeutet Entladen bzw. Einspeisen an das Netz
- `-P` bedeutet Laden bzw. Bezug in die Batterie
- `0 kW` bedeutet kein aktiver Lade- oder Entladebefehl
- `+Q` bzw. `-Q` folgen der je nach Netzanschlusspunkt vereinbarten
  Blindleistungsdefinition

Abweichende Gerätekonventionen werden in Protokolladaptern umgesetzt,
damit Fahrpläne, Optimierung, Limiter, Commands und Persistenz intern
dieselbe Konvention verwenden.

---

## 5. Typische Betriebsarten

| Betriebsart                | Ziel                                                        |
| -------------------------- | ----------------------------------------------------------- |
| Fahrplanbetrieb            | Ein vorgegebener Leistungsfahrplan wird am PCC nachgefahren |
| Peak Shaving               | Lastspitzen werden durch Entladen begrenzt                  |
| Eigenverbrauchsoptimierung | Lokale Erzeugung wird gespeichert und später lokal genutzt  |
| Arbitrage                  | Laden bei niedrigen Preisen, Entladen bei hohen Preisen     |
| Frequenzstützung           | Leistung wird zur Stabilisierung der Netzfrequenz angepasst |
| Reservehaltung             | Ein SOC-Korridor wird für spätere Abrufe freigehalten       |
| Manueller Betrieb          | Operator gibt Modus oder Sollwert innerhalb der Limits vor  |

---

## 6. Sicherheitsprinzip

Das EMS optimiert den Betrieb, ersetzt aber keine Schutzfunktionen. BMS,
PCS und Schutztechnik behalten ihre eigenen harten Grenzen. Das EMS muss
diese Grenzen kennen und respektieren, aber die letzte Schutzinstanz
liegt in den technischen Schutzsystemen.

Praktisch bedeutet das:

- Keine Sollwerte außerhalb freigegebener Batterie- oder PCS-Limits.
- Kein Normalbetrieb bei kritischen Alarmen.
- Begrenzung von Rampen und Sprüngen.
- Konkreter Notfall-/Fallbackmodus bei Störungen (z. B. Datenqualitäts-
  degradiert, Kommunikationsausfall, kritischer Alarm): sichere
  Leistungs-Sollwerte ausgeben (`P=0`, `Q=0`) und auf manuellen
  Freigabestatus warten.
- Nachvollziehbare Command- und Audit-Historie.
- Definiertes Verhalten bei Kommunikations- oder Datenqualitätsfehlern.

## 6.1 Notfall- und Wiederanlaufablauf (vereinfacht)

```mermaid
%%{init: {'theme':'base','themeVariables':{'background':'#f8fafc','primaryColor':'#dbeafe','primaryTextColor':'#0f172a','primaryBorderColor':'#1e40af','lineColor':'#8f872a','secondaryColor':'#fef3c7','tertiaryColor':'#dcfce7','noteBkgColor':'#fef3c7','noteTextColor':'#0f172a','noteBorderColor':'#a16207','actorBkg':'#dbeafe','actorBorder':'#1e40af','actorTextColor':'#0f172a','actorLineColor':'#475569','signalColor':'#0f172a','signalTextColor':'#0f172a','sequenceNumberColor':'#ffffff','labelTextColor':'#0f172a','loopTextColor':'#0f172a','edgeLabelBackground':'#f8fafc'}}}%%
flowchart TD
  A[Normaler Regelbetrieb] --> B{Zustand gesund?}
  B -- ja --> C[Optimierung und Dispatch aktiv]
  C --> B
  B -- nein --> D[Safe-Monitoring]
  D --> E[P und Q auf 0]
  E --> F[Alarmkontext loggen und Operator informieren]
  F --> G{Manuelle Freigabe und Leitplanken erfüllt?}
  G -- ja --> H[Grenzwerte prüfen, SOC SOH PCS BMS]
  H --> I[Wiederanlauf mit Rampenlimit]
  I --> B
  G -- nein --> E
  D -->|Timeout| B
```

## 6.2 Typische Auslöser für den sicheren Zustand

| Ereignis (Beispiel)                               | Typische Auswirkung                        | Reaktion im Safe-Mode                              |
| ------------------------------------------------- | ------------------------------------------ | -------------------------------------------------- |
| Kritischer BMS-Alarm                              | Batterie- oder Zellschutz greift           | `P=0`, `Q=0`, keine aktiven Dispatch-Befehle       |
| PCS-Fehler, Freigabe weg                          | Leistungsabgabe kann nicht sicher erfolgen | Leistung auf 0, Anlagenzustand sichern             |
| Kommunikationsverlust EMS↔BMS/PCS                 | Zustand/limits nicht verlässlich           | Fallback-Befehle halten, Monitoring alarmieren     |
| Unplausible Messung (z. B. Sprünge, Timeout)      | Optimierung wird unzuverlässig             | Fallback beibehalten, Operator informieren         |
| Datenqualität unter Grenzwert                     | Risiko falscher Dispatch-Berechnung        | Kein automatischer Re-Enable bis Freigabe          |
| Scharfes Netzereignis (Frequenz-/Spannungsabwurf) | Netzvorgaben / Schutzlogik erzwingen       | Lokale Grenzwerte respektieren, ggf. 0er Sollwerte |

Ein Rückkehrkriterium ist immer: **kritische Bedingungen beseitigt**,  
**manuelle Freigabe liegt vor** und **Grenzprüfungen (SOC/SOH, Temperatur,
BMS/PCS-Limits) sind bestanden**.

---

## 7. Kurzform

```text
Messen -> Bewerten -> Optimieren -> Dispatchen -> Ausgeben -> Nachregeln
```

Ein BESS EMS ist damit die Schicht, die aus technischen Messwerten,
Batteriezustand, Markt-/Fahrplanvorgaben und Betriebsregeln konkrete,
sichere und nachvollziehbare Leistungsbefehle für das Batteriesystem
ableitet.
