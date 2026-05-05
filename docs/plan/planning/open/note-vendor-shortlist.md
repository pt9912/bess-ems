# Notiz: Vendor-Shortlist für RM-OPEN-02

**Dokumenttyp:** Vorabklärung / Decision-Input
**Status:** Offen
**Bezug:** [`plan-RM-M1.md`](../in-progress/plan-RM-M1.md) (RM-OPEN-02), [`roadmap.md`](../in-progress/roadmap.md)

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
| Socomec | C&I Storage und Power Conversion | Modbus/SCADA-nahe Integration | ? | ? | nicht bewertet |

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

---

## Konsequenzen für M1

Die geprüften Quellen rechtfertigen vier zusätzliche Pflichtfelder im
Mapping-Schema (RM-M1-18), bevor reale Vendor-Profile angelegt werden.
Diese Felder werden parallel im Detailplan ergänzt:

- `write_cadence` (`cyclic | once_per_day | one_shot`) — Hardware-Schutz
  gegen Cyclic-Write-Verstöße.
- `auth_required` — Vendor-Auth-Token (Grid Guard, OEM-Tokens) als
  Vorbedingung für Schreibzugriffe.
- `firmware_constraint` — minimale Firmware-Version pro Register, falls
  Datentyp oder Skalierung firmware-abhängig wechseln.
- `unit_id_discovery` (`static | dynamic`) — auf Adapter-Ebene, damit
  dynamische Unit-IDs (Victron) und feste Unit-IDs (SMA Unit-ID 3 für
  Grid Guard) sauber abgebildet werden.

Diese Felder müssen nicht erst beim ersten realen Vendor-Adapter entstehen;
sie sind so generisch, dass sie auch von den herstellerneutralen
Simulatorprofilen aus M1 sinnvoll getragen werden.

---

## Empfehlung für RM-OPEN-02 (post-M1)

1. **Erste reale Integration: Victron CCGX/Cerbo GX.**
   Begründung: einzige im Rahmen dieser Notiz auswertbare,
   vollständig öffentliche Registerliste mit klarer Schreib-/Lese-Trennung,
   dynamischer Unit-ID-Zuweisung und vollständiger BESS-Telemetrie inklusive
   Cell-Temperaturen. Niedrigste rechtliche und technische Eintrittshürde.

2. **Zweite reale Integration: SMA Sunny Island.**
   Begründung: bringt Grid-Guard-Auth, Cyclic-Write-Restriktionen und
   modusabhängige Setpoint-Pfade. Wer dafür einen sauberen Adapter baut,
   härtet das Mapping-Schema und die Adapter-Schreibbegrenzung gegen
   reale Vendor-Komplexität.

3. **Sungrow nur nach rechtlicher Klärung.**
   Restriktiver Lizenztext für kommerzielle Nutzung. Solange das nicht
   geklärt ist, bleibt Sungrow auf Stufe C.

4. **Tesla, Fluence, Wärtsilä, BYD, CATL, LG, Samsung, Saft, Socomec.**
   Bisher keine Eigenrecherche. Vor einer Aufnahme in die Reference-Stufe
   muss mindestens die Frage „öffentliche Schnittstellendoku verfügbar?"
   beantwortet sein.

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
