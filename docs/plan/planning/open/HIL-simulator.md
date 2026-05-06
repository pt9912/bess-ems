# Plan: HIL-Simulator-Integration

**Dokumenttyp:** Offener Detailplan / Integrationsnotiz
**Status:** Offen
**Quelle:** `/Development/bess/bess-hil-simulator`
**Bezug:** `docs/plan/planning/in-progress/plan-RM-M1.md`,
`docs/plan/planning/in-progress/plan-RM-M1-simulator.md`,
`spec/lastenheft.md`, `spec/architecture.md`

---

## Ziel

Das externe Image `bess-hil-simulator:local` soll als zusätzlicher
Hardware-in-the-Loop-Simulatorpfad für `bess-ems` nutzbar werden. Der
Simulator ergänzt den bestehenden Go-basierten `bess-field-sim`, ersetzt ihn
aber nicht.

Der Go-Simulator bleibt der deterministische M1-Pflichtpfad für
Modbus/MQTT/ACK/Fixture-Gates. Der HIL-Simulator dient für dynamischere Tests
gegen ein PCS/BESS-Modell mit P/Q-Verhalten, Messlatenz, PQ-Capability-Kurve
und CSV-Auswertung.

---

## Einordnung

| Simulator | Rolle | Gate-Charakter |
| --------- | ----- | -------------- |
| `simulators/bess-field-sim` | deterministischer Blackbox-Simulator für M1-Fixtures, Modbus, MQTT und ACK-Korrelation | M1-Pflicht |
| `bess-hil-simulator:local` | externer HIL-Simulator für dynamische Modbus-Tests gegen P/Q-Modell | optional / Folgegate |

Der HIL-Pfad darf `make gates` nicht blockieren, solange die
Adapter-Kompatibilität und Testdeterministik nicht abgeschlossen sind.

---

## Aktueller HIL-Stand

Der HIL-Simulator:

- baut per Dockerfile mit .NET SDK 8.0
- läuft im Container standardmäßig headless mit `BESS_HIL_CONSOLE=false`
- exponiert Modbus TCP im Container auf Port `502`
- schreibt `BessData.csv` und kopiert `pq-curves.json` in das gemountete
  Datenverzeichnis
- modelliert P/Q-Dynamik, Messlatenz, Netzspannung/-frequenz und
  PQ-Capability-Limits

Beispielstart:

```sh
docker build -t bess-hil-simulator:local /Development/bess/bess-hil-simulator
mkdir -p data
docker run --rm --user "$(id -u):$(id -g)" -p 5020:502 -v "$PWD/data:/data" bess-hil-simulator:local
```

---

## Integrationshindernisse

| Thema | HIL-Simulator | Aktueller `bess-ems`-Stand | Notwendige Anpassung |
| ----- | ------------- | -------------------------- | -------------------- |
| Registertabelle | Messwerte liegen auf Input Registers `30001+` | `ModbusTelemetrySource` liest Read-Mappings aktuell aus Holding Registers | Mapping-Feld `register_table` mit `input`/`holding` einführen und Adapter danach lesen lassen |
| Float-Word-Order | `float32` wird als zwei 16-bit-Wörter in HIL-spezifischer Reihenfolge geschrieben | `RegisterDecoder` kombiniert 32-bit-Werte aktuell fest als High-Word/Low-Word | Mapping-Feld `word_order` einführen, z. B. `high_low`/`low_high` |
| Einheiten | P/Q in MW/MVAR | intern kW/kvar | `scale_factor: 1000` im HIL-Profil verwenden |
| Reactive Power | HIL akzeptiert P- und Q-Setpoints | `ModbusCommandSink` schreibt aktuell primär `active_power_setpoint_kw` | optionalen Schreibpfad für `reactive_power_setpoint_kvar` ergänzen |
| Betriebsmodus | HIL hat kein `operating_mode`-Register | `ModbusCommandSink` schreibt `operating_mode`, falls vorhanden | bereits kompatibel, solange HIL-Profil kein `operating_mode` enthält |
| BESS-Telemetrie | HIL liefert PCS-/Grid-Werte, aber kein SOC/SOH/Temperatur/Availability-Profil | `BatteryTelemetry` erwartet BESS-Zustand | HIL-Test zunächst als PCS-/Modbus-Adaptertest führen oder HIL um BESS-Register erweitern |

---

## Geplante Artefakte

| Artefakt | Zweck |
| -------- | ----- |
| `config/examples/adapters/modbus.hil-simulator.json` | HIL-spezifisches Modbus-Profil mit Input/Holding-Registertabellen, Float-Order und MW/kW-Skalierung |
| `tests/hil/compose.yml` oder Compose-Profil in `tests/integration/compose.yml` | Startet `bess-hil-simulator:local` als zusätzlichen Sidecar |
| `tests/integration/BatteryEms.Hil.IntegrationTests` | HIL-spezifische Integrationstests ohne M1-Pflichtgate |
| `make test-hil-modbus` | optionales Gate für lokale HIL-Prüfung |
| Dokumentation in `docs/user/quality.md` | erklärt optionalen HIL-Lauf und Abgrenzung zu `make test-integration` |

---

## HIL-Modbus-Profil

Erstes Zielprofil:

| Fachlicher Punkt | Registertabelle | Adresse | Typ | Skalierung | Richtung |
| ---------------- | --------------- | ------- | --- | ---------- | -------- |
| `active_power_kw` | `input` | `0` | `float32` | `1000` | read |
| `reactive_power_kvar` | `input` | `2` | `float32` | `1000` | read |
| `grid_voltage_pu` | `input` | `4` | `float32` | `1` | read |
| `grid_frequency_hz` | `input` | `6` | `float32` | `1` | read |
| `grid_current_ka` | `input` | `8` | `float32` | `1` | read |
| `active_power_setpoint_kw` | `holding` | `0` | `float32` | `1000` | write |
| `reactive_power_setpoint_kvar` | `holding` | `2` | `float32` | `1000` | write |

Das Profil muss zusätzlich die Gerätepunkt-Metadaten aus LH-DOM-005 tragen,
mindestens `name`, `display_name`, `unit`, `range`, `writable`,
`exportable`, `write_cadence` und `auth_required`.

---

## Teststrategie

Erster HIL-Test:

1. HIL-Container starten und TCP-Port abwarten.
2. HIL-Modbus-Profil laden.
3. P-Setpoint schreiben, z. B. `25 kW`.
4. Messwerte mehrfach lesen.
5. Prüfen, dass `active_power_kw` sich innerhalb eines Zeitfensters in
   Richtung Sollwert bewegt.
6. CSV-Datei als optionales Debug-Artefakt behalten, aber nicht als primäres
   Assertion-Interface verwenden.

Folgetests:

- Q-Setpoint und PQ-Capability-Clamping
- Messlatenz bei Sprungantwort
- Spannungssag/-swell und resultierende Q-Limits
- Adapterverhalten bei HIL-Containerstopp

---

## Arbeitspunkte

| Status | ID | Punkt | DoD |
| ------ | -- | ----- | --- |
| ⬜ | HIL-01 | Modbus-Mapping um Registertabelle erweitern | Schema, Loader, Domain-Konfiguration und Adapter unterstützen `register_table=input|holding`; bestehende M1-Profile bleiben kompatibel. |
| ⬜ | HIL-02 | Float-Word-Order konfigurierbar machen | `RegisterDecoder`/Encoder unterstützen mindestens `high_low` und `low_high`; Tests decken beide Varianten ab. |
| ⬜ | HIL-03 | HIL-Modbus-Profil anlegen | `config/examples/adapters/modbus.hil-simulator.json` validiert gegen Schema und beschreibt HIL-Punkte mit Gerätepunkt-Metadaten. |
| ⬜ | HIL-04 | Optionalen Q-Setpoint schreiben | `ModbusCommandSink` schreibt `reactive_power_setpoint_kvar`, wenn das Mapping ein writable Register dafür enthält. |
| ⬜ | HIL-05 | HIL-Compose ergänzen | Ein optionaler Compose-Pfad startet `bess-hil-simulator:local` headless und stellt CSV-Logs in einem Volume bereit. |
| ⬜ | HIL-06 | HIL-Integrationstest ergänzen | `BatteryEms.Hil.IntegrationTests` prüft P-Sprungantwort gegen den HIL-Simulator ohne das M1-Pflichtgate zu blockieren. |
| ⬜ | HIL-07 | Make-Gate ergänzen | `make test-hil-modbus` startet den optionalen HIL-Testpfad und ist getrennt von `make gates`. |
| ⬜ | HIL-08 | Dokumentation ergänzen | `docs/user/quality.md` beschreibt Voraussetzungen, Build, Lauf und Abgrenzung zum Go-Simulator. |

---

## Offene Entscheidungen

| Kennung | Frage | Default |
| ------- | ----- | ------- |
| HIL-OPEN-01 | Wird das HIL-Image lokal gebaut oder über Registry referenziert? | lokal bauen: `bess-hil-simulator:local` |
| HIL-OPEN-02 | Gehört der HIL-Pfad noch zu RM-M1-Folgearbeiten oder zu RM-M2? | RM-M2/Folgegate, solange keine M1-Abnahme davon abhängt |
| HIL-OPEN-03 | Wird der HIL-Simulator um SOC/SOH/Availability ergänzt oder bleibt er PCS-/Grid-orientiert? | zunächst PCS-/Grid-orientiert |
| HIL-OPEN-04 | Soll HIL-CSV als Testartefakt persistiert werden? | nur optional für Debugging, Assertions über Modbus |
