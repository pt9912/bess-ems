# Notiz: Vendor-Shortlist für RM-OPEN-02

**Dokumenttyp:** Vorabklärung / Decision-Input
**Status:** Als Entscheidungsinput übernommen; LH-OPEN-001 ist im Lastenheft geklärt
**Bezug:** [`plan-RM-M1.md`](../done/plan-RM-M1.md) (RM-OPEN-02), [`roadmap.md`](../in-progress/roadmap.md)

---

## Zweck

Diese Notiz sammelt Vendor-Befunde, damit der RM-OPEN-02-Default
(„herstellerneutrale Simulatorprofile in M1, reale Profile als Folgepaket")
nach M1 mit Evidenz aufgelöst werden kann. Sie ist **kein Bestandteil** der
M1-Lieferung. Vendor-Auswahl, ADRs und reale Profile entstehen erst nach
RM-M1-Abschluss.

---

## Marktübersicht (Klassifizierung)

Die folgende Tabelle ist Marktorientierung. Eine Bewertung „Doku öffentlich"
und „Lizenz kommerziell nutzbar" wurde nur für die Hersteller eingetragen,
deren Dokumentation im Rahmen dieser Notiz tatsächlich gesichtet wurde.
„?" bedeutet: nicht geprüft.

| Hersteller | Geräteklasse | Typische Integration | Doku öffentlich | Lizenz kommerziell | Reference-Stufe |
| ---------- | ------------ | -------------------- | --------------- | ------------------ | --------------- |
| Victron Energy (CCGX/Cerbo GX) | EMS-Gateway über VE.Bus/VE.Direct/CAN | Modbus TCP, dbus-Mapping | ja (XLSX) | offen, breite Community-Praxis | **A** — Reference Vendor |
| SMA Sunny Island (SI4.4M / SI6.0H / SI8.0H) | Battery Inverter (AC-gekoppelt) | Modbus TCP/UDP, Grid Guard | ja (PDF; Registerliste separat als HTML) | konservativer Lizenztext, Interface aber öffentlich beworben | **B** — Auth-/Cyclic-Constraints relevant |
| SMA EDMx (Data Manager) | EMS-/PV-Gateway | Modbus TCP, Aggregation Multicluster | ja (PDF) | wie Sunny Island | **B** — Aggregations-Layer für Sunny-Island-Cluster |
| Sungrow (SH-Serie inkl. SBR-Batterie) | Hybrid-Wechselrichter mit Batteriespeicher | Modbus TCP, EMS/SCADA | community-dokumentiert (ioBroker-Gist), Hersteller-Protokoll auf Anfrage | restriktiv: „prohibited … for commercial purposes by any means" (Logger1000-Manual) | **C** — Lizenz-Caveat |
| Sungrow Logger1000A/B | Datalogger / EMS-Gateway | Modbus, Hersteller-Cloud | User Manual ja, Communication Protocol separat | wie oben | **C** |
| Tesla Megapack | Utility-Scale BESS | API/Hersteller-Gateway, SCADA/EMS | ? (typisch hinter NDA/Projektvertrag) | ? | nicht bewertet |
| Fluence (Gridstack/Gridstack Pro) | Grid-Scale BESS, Turnkey | proprietäres EMS/SCADA, Projektintegration | ? | ? | nicht bewertet |
| BYD Energy Storage / Battery-Box | C&I und größere Speicherlösungen | BMS/Wechselrichter-Kombination | ? | ? | nicht bewertet |
| Wärtsilä Energy Storage | Großspeicher und Grid-Anwendungen | Hersteller-EMS, Projekt-/SCADA-Integration | ? | ? | nicht bewertet |
| Saft | industrielle und netzgekoppelte Batteriesysteme | meist projektspezifisch | ? | ? | nicht bewertet |
| CATL | Zellen, Racks, Container, BESS | häufig über Systemintegrator oder PCS/EMS | ? | ? | nicht bewertet |
| LG Energy Solution | Batterie-/Storage-Komponenten | meist über Integrator | ? | ? | nicht bewertet |
| Samsung SDI | Batterie-Racks/Module | meist über Integrator | ? | ? | nicht bewertet |
| Socomec SUNSYS HES L | C&I BESS, modular 50–300 kVA + LFP-Batterieracks (CATL-BMS) | Modbus TCP / **SunSpec** (DER-Models 1, 701–706, 713, 715, 802, 803) | ja (User Manual `551697C` als PDF) | kein vendor-eigenes Auth-Schema; Schutz netzwerklevel (Firewall, IP-Allowlist, HTTPS, FTPS, AES-256 für Backup) | **A** — SunSpec-Standard, multi-vendor-Hebelwirkung |

Reference-Stufen: **A** = bevorzugter Erst-Adapter post-M1 · **B** = Zweit-Adapter,
bringt Architektur-Constraints · **C** = möglich, aber mit Caveat · *nicht bewertet*
= bisher keine Eigenrecherche.

---

## Architekturhappen aus den geprüften Quellen

Die folgenden Punkte stammen aus konkret eingesehenen Dokumenten und sind
direkter Input für das M1-Mapping-Schema (RM-M1-18) sowie für die
Adapter-Schreibbegrenzung (RM-M1-11).

### Victron CCGX/Cerbo GX (Modbus-TCP-Registerliste, Version 3.71)

- 945 Register, davon 110 schreibbar / 782 nur lesbar — saubere Trennung.
- Spaltenstruktur: `dbus-service-name`, `description`, `Address`, `Type`,
  `Scalefactor`, `Range`, `dbus-obj-path`, `writable`, `dbus-unit`, `Remarks`.
  Diese Felder decken die Pflichtangaben für ein adapterneutrales
  Mapping-Schema ab.
- Battery-Service deckt alle BatteryAsset-Felder ab: SOC (Reg 266, sf=10, %),
  Battery Power (256/258), Voltage/Current, Battery Temperature (262),
  Min/Max Cell Temperature (318/319), SOH (304), Capacity (309),
  Max Charge/Discharge Current (307/308), dedizierte Alarm-Flags.
- Dynamische Unit-ID-Zuweisung ab Venus 2.60 — ein Adapter darf Unit-IDs
  nicht hardcoden, sondern muss sie über das Modbus-Service-Discovery
  ermitteln.

### SMA Sunny Island (SI-Modbus-BA-en-12, gilt für SI4.4M-13 / SI6.0H-13 / SI8.0H-13)

- **Grid Guard Code** als Vendor-Auth-Mechanismus für schreibende Zugriffe
  (Register 43090, Unit ID 3, Hex-Eingabe). Ein Adapter, der schreiben will,
  muss diesen Token vor dem Schreibvorgang setzen.
- **Cyclic-Write-Restriktionen**: Bestimmte RW-Register dürfen maximal
  einmal pro Tag geschrieben werden (Symbol „Warnung"); Grid-Management-Register
  sind explizit cyclic-write-fähig (Symbol „Pflanze"). Das Mapping muss
  diese Klassifizierung pro Register tragen.
- Power-Setpoint-Pfad ist modusabhängig: Parallel-Grid-Operation und
  Stand-Alone-Modus benutzen unterschiedliche Parameter-Sets. Der State-
  Machine-Modus muss bestimmen, welcher Setpoint-Pfad gültig ist.
- Multicluster-Aggregation läuft über **SMA Data Manager M** (EDMx),
  nicht direkt über die Sunny Islands.

### SMA EDMx (EDMx-Modbus-TI-en-16)

- Gateway-Doku, NaN-Marker für ungültige Messwerte als Konvention,
  Datentypauswahl (Int/Long/Float/String), Unit-IDs 1 (Kommunikationsprodukt)
  und 2 (System).
- Nützlich als zweiter Datapoint zur Bestätigung der SMA-Konvention; deckt
  Aggregations-Layer für die Sunny-Island-Cluster.

### Sungrow SH-Serie (Community-Gist, ioBroker-Adapter)

- Community-dokumentierte Register für SH10RT/SH20RT inkl. Batteriebefehle
  (13049–13058) und SOC-Grenzen.
- **Firmware-abhängige Datentypen**: Register 13021 (Batterieladeleistung)
  wechselt ab SAPPHIRE-H v95.03 von `uint16be` → `int16be`. Ohne Firmware-
  Bedingung im Mapping wird der Adapter falsch interpretieren.

### Sungrow Logger1000A/B (User Manual, Version 1.14)

- Datalogger/EMS-Gateway-Manual, keine Modbus-Registerliste enthalten —
  diese wird bei Sungrow als separates „Communication Protocol"-Dokument
  geführt.
- Lizenztext: „It is prohibited to use data contained in firmware or
  software developed by SUNGROW … for commercial purposes by any means."
  Für ein kommerzielles `bess-ems` ein eigenständiges rechtliches Risiko;
  vor produktiver Verwendung der Sungrow-Dokumentation ist eine rechtliche
  Klärung Pflicht.

### Socomec SUNSYS HES L (User Manual `551697C`, Sept. 2022)

- C-Cab (Power Conversion, 50–300 kVA in 50-kVA-Modulen, automatisierte
  AC/DC-Verteilung, integriertes BMS-Gateway) plus B-Cab (LFP-Batterieracks
  von **CATL**, 186 kWh nameplate / 176 kWh useable je Rack, bis 6 parallel).
- Externe Steuerung läuft ausschließlich über **Modbus TCP / SunSpec**
  (Section 3.8.1, S.27). Socomec ist Mitglied der SunSpec Alliance; die
  Registerdefinitionen kommen aus der öffentlichen SunSpec-Spezifikation,
  nicht aus der Socomec-Doku.
- Unterstützte SunSpec-Modelle: **1** (Common), **701** (DER AC Measurement,
  States/Alarms/Messwerte), **702** (DER Capacity), **703** (Enter Service),
  **704** (DER AC Controls — Setpoints `WSet`, `VarSet`, Mode-Bits), **705**
  (Volt-Var), **706** (Volt-Watt), **713** (DER Storage Capacity), **715**
  (DER Ctl — `OpCtl` ON/OFF, `AlarmReset`, **Heartbeat**), **802** (Battery
  Base Model — `SetOp` CONNECT/DISCONNECT, SOC, …), **803** (Li-ion Battery
  Bank Model).
- **Kein protokollebenes Auth.** Schutz ist netzwerklevel: Firewall,
  IP-/MAC-Allowlist, dediziertes Segment für PMS↔EMS-Verbindung. Web-Login
  über HTTPS, Datenexport über FTPS, AES-256 für Backup-Daten.
- **Heartbeat-Pflicht**: Wert in Model 715 muss **jede Sekunde** ändern,
  sonst gilt die EMS-Verbindung als tot.
- **Cooldown**: Nach `802/50 SetOp=2` (DISCONNECT) **5 Minuten warten**,
  bevor wieder verbunden werden darf — kürzere Reconnect-Zyklen schädigen
  die Vorlade-Hardware.
- Konkrete Start-/Stop-Sequenz mit Model + Offset + Werten ist im PDF
  S.27 vollständig dokumentiert; eignet sich direkt als Test-Sequenz für
  einen SunSpec-Simulator.
- Modes-of-operation-Übersicht (Section 3.6): `INIT → SWITCHED-OFF →
  SYSTEM READY → ON-GRID | OFF-GRID | BLACK START`, mit `ALARM` als
  Parallelzustand. Konsistent zur Spec §9, allerdings ohne explizite
  `LIMITED`/`MAINTENANCE`-Zustände.

---

## SunSpec als Querschnitt

Der Socomec-Befund ist nicht nur „ein weiterer Vendor", sondern eröffnet
eine **horizontale** Strategieoption: ein **SunSpec-fähiger Modbus-Adapter**
deckt mehrere Vendoren gleichzeitig ab, weil das Informationsmodell
standardisiert ist.

**Nachgewiesen SunSpec-konform** (im Rahmen dieser Notiz geprüft):

- Socomec SUNSYS HES L (Models 1, 701–706, 713, 715, 802, 803).

**SunSpec-Compliance laut Hersteller-Eigenangabe** (zu validieren, *bevor*
sie als Adapter-Targets aufgenommen werden):

- SMA: Inverter-Familien (Sunny Boy, Sunny Tripower, ggf. Sunny Tripower
  Storage). Sunny Island nutzt laut `SI-Modbus-BA-en-12` ein **eigenes**
  SMA-Modbus-Profil, kein SunSpec — Battery-Inverter-Pfad ist also
  nicht-SunSpec.
- Fronius, SolarEdge, Enphase: Inverter-Modelle (701-Reihe).
- Sungrow: Teile der Inverter-Reihe.

**Praktische Bedeutung für die M1-Architektur**:

- Ein SunSpec-Anker-Scan (Suche nach „SunS"-Magic in Holding Registers ab
  40000 / 0…65535, Auflistung der vorhandenen Modelle) ist generischer
  Adaptercode. Er gehört nicht ins Vendor-Mapping, sondern in den
  Modbus-Adapter selbst.
- Für M1-Simulatorprofile lohnt sich ein **SunSpec-konformes Beispiel**
  (Models 1, 701, 704, 715, 802, optional 803) parallel zum bisher
  geplanten herstellerneutralen Profil.
- Vendoren, deren Battery-Pfad **nicht** SunSpec ist (z. B. SMA Sunny
  Island, Sungrow SH-Serie Battery-Befehle), brauchen weiter ein
  vendor-spezifisches Mapping. SunSpec ersetzt den Vendor-Pfad nicht,
  sondern ergänzt ihn.

**Funktionale Priorisierung** (für die Reihenfolge im Adapter und
Simulator):

| Funktionsbereich | Priorität für `bess-ems` | SunSpec-Modelle |
| ---------------- | ------------------------ | --------------- |
| Inverter-Basismesswerte (P, Q, S, V, I, f) | sehr relevant | 101–103 (legacy), 701 |
| Inverter-Nameplate / Ratings | relevant | 120, 702 |
| Inverter-Status (operating state, alarms) | sehr relevant | 101–103 Status-Block, 701 |
| Wirkleistungsbegrenzung / Curtailment | sehr relevant | 123 (immediate control), 704 |
| Blindleistung / Power Factor | relevant | 124, 705, 706 |
| Metering | sehr relevant | 201–204 |
| Storage Control (CONNECT/DISCONNECT, SOC-Limits) | sehr relevant, geräteabhängig | 124, 713, 802, 803 |
| DER 700er-Modelle als IEEE-1547-2018-Ablösung | perspektivisch wichtig | 701–715 |

Ein SunSpec-Adapter muss **beide Generationen** beim Anker-Scan
abbilden: die Legacy-Modelle 100/120/200 (z. B. ältere SMA-Inverter,
Fronius, SolarEdge, Enphase) **und** die neuen DER-700er (Socomec,
neuere IEEE-1547-2018-konforme Implementierungen). Welcher Pfad zuerst
priorisiert wird, entscheidet sich beim Vendor-Target.

---

## Konsequenzen für M1

Die geprüften Quellen rechtfertigen die folgenden Pflichtfelder im
Mapping-Schema (RM-M1-18), bevor reale Vendor-Profile angelegt werden.
Die Felder sind parallel im Detailplan eingetragen.

- `write_cadence` — `cyclic | once_per_day | one_shot | heartbeat | cooldown`.
  Belegquellen: SMA Sunny Island (`cyclic` / `once_per_day`), Socomec
  SunSpec Model 715 (`heartbeat` jede Sekunde), Socomec Model 802
  DISCONNECT (`cooldown` 5 Minuten).
- `auth_required` — `none | network | token`. Belegquellen: Victron (none),
  Socomec (network — Firewall/IP-Allowlist), SMA Sunny Island (token —
  Grid Guard).
- `firmware_constraint` — minimale Firmware-Version pro Register,
  optional. Beleg: Sungrow SH-Serie Reg 13021 wechselt ab SAPPHIRE-H
  v95.03 von `uint16be` → `int16be`.
- `unit_id_discovery` — `static | dynamic | sunspec`, auf Adapter-Ebene.
  Belegquellen: SMA (`static`, fixe Unit-IDs 1/2/3), Victron (`dynamic`,
  Service-Discovery ab Venus 2.60), Socomec (`sunspec`, Anker-Magic in
  Holding Registers ab 40000).
- `sunspec_model` — Modell-ID (z. B. 704, 715, 802), optional. Pflicht für
  Profile, die `unit_id_discovery=sunspec` setzen. Erlaubt einem Adapter,
  ein Mapping direkt gegen die SunSpec-Spezifikation aufzulösen.

Diese Felder müssen nicht erst beim ersten realen Vendor-Adapter entstehen;
sie sind so generisch, dass sie auch von den herstellerneutralen
Simulatorprofilen aus M1 sinnvoll getragen werden.

---

## Empfehlung für RM-OPEN-02 (post-M1)

1. **SunSpec-First-Strategie als horizontaler Hebel.**
   Implementiere einen SunSpec-fähigen Modbus-Adapter (Anker-Scan,
   Modelle 1, 701, 704, 715, 802, optional 803). Damit deckt ein einzelnes
   Code-Pfad mehrere Vendoren gleichzeitig ab, sobald deren SunSpec-Compliance
   verifiziert ist.

2. **Erste reale Integration: Socomec SUNSYS HES L.**
   Begründung: einzige nachgewiesen SunSpec-konforme Quelle in dieser
   Notiz, mit konkret dokumentierter Start-/Stop-Sequenz (S.27). Liefert
   den ersten echten Validierungsfall für den SunSpec-Pfad.

3. **Zweite reale Integration: Victron CCGX/Cerbo GX.**
   Begründung: nicht-SunSpec, aber vollständig öffentliche Registerliste
   mit klarer Schreib-/Lese-Trennung und dynamischer Unit-ID-Zuweisung.
   Validiert den vendor-spezifischen Pfad neben dem SunSpec-Pfad und
   härtet die Discovery-Logik (`unit_id_discovery=dynamic`).

4. **Dritte reale Integration: SMA Sunny Island.**
   Begründung: bringt Grid-Guard-Auth (`auth_required=token`),
   Cyclic-Write-Restriktionen (`once_per_day`) und modusabhängige
   Setpoint-Pfade. Härtet das Mapping-Schema gegen reale
   Vendor-Komplexität, die SunSpec nicht abdeckt.

5. **Sungrow nur nach rechtlicher Klärung.**
   Restriktiver Lizenztext für kommerzielle Nutzung. Solange das nicht
   geklärt ist, bleibt Sungrow auf Stufe C — auch wenn Teile der
   Inverter-Reihe SunSpec claimen, deckt das nicht den Battery-Pfad ab.

6. **Tesla, Fluence, Wärtsilä, BYD, CATL (außerhalb Socomec), LG, Samsung,
   Saft.** Bisher keine Eigenrecherche. Erste Triage-Frage: SunSpec-konform
   ja/nein, dann „öffentliche Schnittstellendoku verfügbar?".

---

## Offene Punkte

- Sungrow Communication-Protocol-Dokument beschaffen und Lizenz-Spielraum
  rechtlich klären, bevor es als Reference-Profile in den Plan einzieht.
- SMA „Modbus parameters and measured values" (HTML, gerätespezifisch)
  archivieren, sobald Sunny Island als zweiter Adapter ansteht.
- Tesla Megapack, Fluence Gridstack und Wärtsilä auf öffentliche
  Schnittstellendoku prüfen oder explizit als „nur via Projekt-NDA" markieren.
- ADR für die endgültige Vendor-Auswahl post-M1 vorbereiten; diese Notiz
  ist deren Vorstufe, kein Ersatz.
- SunSpec-Compliance der claimenden Vendoren (SMA-Inverter, Fronius,
  SolarEdge, Enphase, Sungrow-Inverter) anhand öffentlicher
  Implementierungsangaben verifizieren, bevor sie als Adapter-Targets
  in den Plan einziehen.
