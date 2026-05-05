# Plan RM-M1 Simulator: Feldgeräte- und Telemetrie-Simulation

**Dokumenttyp:** Detailplan / Simulator-Spezifikation
**Status:** In Arbeit
**Meilenstein:** RM-M1
**Bezug:** [`plan-RM-M1.md`](plan-RM-M1.md), [`roadmap.md`](roadmap.md),
[`spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`spec/architecture.md`](../../../../spec/architecture.md),
[`docs/user/quality.md`](../../../user/quality.md)

---

## Zweck

Dieses Dokument beschreibt den Simulatorumfang fuer RM-M1. Der Simulator
stellt reproduzierbare Feldgeraete-Szenarien bereit, damit Modbus-, MQTT-,
Mapping-, Safety- und Runtime-Gates ohne reale Batteriehardware laufen.

Der Simulator ist kein Anlagenmodell, kein Hardware-in-the-Loop-System und
kein Ersatz fuer herstellerspezifische Abnahmetests. Er ist ein deterministischer
Test- und Entwicklungsadapter fuer die sichere M1-Regelpipeline.

---

## Ziele

- Modbus-TCP-Lese- und Schreibpfad gegen ein herstellerneutrales BESS-Profil
  testen.
- MQTT-Telemetrie, Command-Publish und Command-ACK gegen Mosquitto testen.
- SunSpec-konformes Modbus-Beispielprofil als generischen Pfad validieren.
- Safety-Faelle wie stale Snapshot, Kommunikationsverlust, invalid SOC,
  BMS-/Wechselrichter-Ausfall und Schreibwertbegrenzung reproduzierbar
  ausloesen.
- Testdaten deterministisch halten, damit `make test-integration`,
  `make test-safety` und `make runtime` lokal und in CI dieselben Ergebnisse
  liefern.

---

## Nicht-Ziele

- Keine physikalisch vollstaendige Batterie-, Wechselrichter- oder Netzsimulation.
- Keine Echtzeitgarantie unterhalb des M1-Regelzyklus von 1 Sekunde.
- Keine produktive Herstellerzertifizierung.
- Kein OPC-UA-Simulator in M1; OPC-UA wird mit M4 aktiviert.
- Keine Intraday-, Regelleistungs- oder MPC-Szenarien.

---

## Komponenten

| Komponente | Rolle | Aktiv ab | Bezug |
| ---------- | ----- | -------- | ----- |
| Modbus-Simulator | TCP-Server mit Registern fuer Telemetrie, Status und Command-Write | M1 | RM-M1-09, RM-M1-11 |
| MQTT-Simulator | Telemetrie-Publisher und Command-ACK-Consumer/Publisher ueber Mosquitto | M1 | RM-M1-10, RM-M1-11 |
| SunSpec-Profil | Beispielmapping fuer generische SunSpec-Discovery und Modelldaten | M1 | RM-M1-18, RM-M1-09 |
| Szenario-Fixtures | Deterministische Zeitreihen und Fehlersequenzen | M1 | RM-M1-03, RM-M1-07, RM-M1-20 |
| Compose-Integration | Lokaler Start mit `bess-ems`, Postgres und Mosquitto | M1 | RM-M1-19 |

---

## Protokollverhalten

### Modbus TCP

Der Modbus-Simulator muss mindestens folgende Bereiche abbilden:

| Bereich | Mindestsignale | Verhalten |
| ------- | -------------- | --------- |
| Telemetrie | SOC, SOH, Wirkleistung, Blindleistung, DC-Spannung, DC-Strom, Temperatur | zyklisch lesbar, skalierbar, mit Plausibilitaetsgrenzen |
| Status | BMS-Verfuegbarkeit, Wechselrichter-Verfuegbarkeit, Fehlerstatus, Betriebszustand | gezielt pro Szenario umschaltbar |
| Command-Write | Wirkleistungssollwert, Modus/Stop, optional ValidUntil/Heartbeat | finaler Schreibwert wird erfassbar und gegen Limits pruefbar |
| Fehler | Timeout, Verbindungsabbruch, unplausible Werte, stale Timestamp | deterministisch per Szenario ausloesbar |

Das herstellerneutrale Profil lebt unter
`config/examples/adapters/modbus.simulator.json`. Das SunSpec-Profil lebt
unter `config/examples/adapters/modbus.sunspec-simulator.json` und deckt
mindestens die in `plan-RM-M1.md` genannten SunSpec-Modelle ab.

### MQTT

Der MQTT-Simulator nutzt Mosquitto und muss mindestens folgende Topics
bereitstellen:

| Topic | Richtung | Zweck |
| ----- | -------- | ----- |
| `battery/{assetId}/telemetry` | Simulator -> EMS | Telemetrie-Payload |
| `battery/{assetId}/status` | Simulator -> EMS | Verfuegbarkeit und Fehlerstatus |
| `battery/{assetId}/command` | EMS -> Simulator | Command-Publish |
| `battery/{assetId}/command/ack` | Simulator -> EMS | Command-Acknowledgement |
| `battery/{assetId}/fault` | Simulator -> EMS | explizite Fehler- und Fault-Signale |

Das MQTT-Beispielprofil lebt unter
`config/examples/adapters/mqtt.simulator.json`. Retained Commands sind
standardmaessig aus; ACKs muessen ueber `CommandId` korrelierbar sein.

---

## Szenarien

| ID | Name | Eingang | Erwartung |
| -- | ---- | ------- | --------- |
| SIM-M1-01 | Normalbetrieb Entladen | gueltiger Snapshot, Day-Ahead-Ziel > 0 | Command bleibt innerhalb SOC-/Power-/Rampengrenzen |
| SIM-M1-02 | Normalbetrieb Laden | gueltiger Snapshot, Day-Ahead-Ziel < 0 | Command bleibt innerhalb SOC-/Power-/Rampengrenzen |
| SIM-M1-03 | SOC am Maximum | SOC >= SOC_MAX, Ladeziel < 0 | Ladeanteil wird 0 oder Stop |
| SIM-M1-04 | SOC am Minimum | SOC <= SOC_MIN, Entladeziel > 0 | Entladeanteil wird 0 oder Stop |
| SIM-M1-05 | Stale Snapshot | Timestamp aelter als maximales Messwertalter | sicherer Zustand mit Reason |
| SIM-M1-06 | Kommunikationsverlust | Modbus/MQTT-Datenquelle faellt aus | sicherer Zustand nach konfiguriertem Alter |
| SIM-M1-07 | Invalid SOC | SOC NaN, < 0 oder > 100 | DataQuality invalid, kein aktiver Befehl |
| SIM-M1-08 | Temperatur unplausibel | Temperatur ausserhalb Mapping-Range | DataQuality invalid, kein aktiver Befehl |
| SIM-M1-09 | BMS nicht verfuegbar | BMS-Verfuegbarkeit false | sicherer Zustand mit Reason |
| SIM-M1-10 | Wechselrichter nicht verfuegbar | Inverter-Verfuegbarkeit false | sicherer Zustand mit Reason |
| SIM-M1-11 | Command-ACK | Command wird publiziert/geschrieben | ACK korreliert ueber CommandId |
| SIM-M1-12 | Schreibwert ueber Limit | EMS versucht zu hohen Schreibwert | Adapter-Schreibbegrenzung greift unmittelbar vor Versand |

Alle Szenarien muessen mit deterministischer Zeitquelle laufen. Tests duerfen
nicht von Wanduhrzeit, Schlafzeiten oder zufaelligen Startwerten abhaengen.

---

## Akzeptanzdaten

| Pfad | Inhalt | Verwendet von |
| ---- | ------ | ------------- |
| `config/examples/asset.single-bess.json` | Asset-Grenzen, Rampen, Temperaturbereich | Startup-Validierung, Simulator-Smoke |
| `config/examples/adapters/modbus.simulator.json` | herstellerneutrales Modbus-Registermapping | RM-M1-09, RM-M1-18 |
| `config/examples/adapters/modbus.sunspec-simulator.json` | SunSpec-Beispielmapping | RM-M1-09, RM-M1-18 |
| `config/examples/adapters/mqtt.simulator.json` | MQTT-Topics und ACK-Konvention | RM-M1-10, RM-M1-18 |
| `tests/fixtures/telemetry/simulator-normal.json` | Normalbetrieb Laden/Entladen | Integration, Safety |
| `tests/fixtures/telemetry/safe-fallback.json` | stale Snapshot, invalid SOC, BMS-Ausfall, Operator-Stop | Safety |

Wenn Implementierung oder Testframework andere Dateiformate erzwingen, muss
dieses Dokument im gleichen PR angepasst werden.

---

## Gates

| Gate | Mindestpruefung |
| ---- | --------------- |
| `make test-safety` | SIM-M1-03 bis SIM-M1-10 und SIM-M1-12 fuehren zu definiert sicherem Verhalten |
| `make test-integration` | Modbus- und MQTT-Simulatorpfade laufen gegen Beispielprofile |
| Mapping-Schema-Gate | alle Simulatorprofile enthalten die Pflichtfelder aus `plan-RM-M1.md` |
| `make runtime` | Compose startet EMS, Postgres und Mosquitto; Simulatorpfad liefert Health-relevante Testdaten |

Fehlt ein Simulatorprofil oder ein Szenario, gilt das Gate als rot. Coverage
ersetzt keine Szenarioabdeckung.

---

## Umsetzungshinweise

- Simulatorcode gehoert nicht in Domain oder Application. Er lebt unter
  Test-/Adapter-nahen Pfaden und darf keine Produktionsentscheidung
  beeinflussen.
- Szenarien werden ueber Fixtures konfiguriert, nicht ueber fest verdrahtete
  Testlogik.
- Fehlerfaelle muessen explizit benannt werden; generische Zufallsfehler sind
  fuer M1 nicht zulaessig.
- Das SunSpec-Profil ist ein Beispiel fuer generischen Adaptercode. Es ersetzt
  keine realen Herstellerprofile.

---

## Offene Punkte

| Kennung | Frage | Default fuer M1 |
| ------- | ----- | --------------- |
| SIM-OPEN-01 | Wird der Modbus-Simulator als eigener Testhost oder im Integrationstest-Prozess gestartet? | Testhost im Integrationstest-Prozess, solange Compose keinen separaten Service braucht |
| SIM-OPEN-02 | Wird der MQTT-Simulator als eigener Prozess benoetigt? | Nein; Mosquitto plus Test-Publisher/Consumer reicht fuer M1 |
| SIM-OPEN-03 | Braucht `make runtime` einen dedizierten Simulator-Service? | Nur falls Health/Smoke ohne Testfixture nicht stabil pruefbar ist |
