# Plan: HIL-Simulator-Integration

**Dokumenttyp:** Aktiver Detailplan / M2-Folgewelle
**Status:** Unter `in-progress/`. Der Aktivierungs-Trigger
(LP-Solver-Adapter steht und liefert ein Resultat, das gegen ein
dynamisches PCS-/PQ-Capability-Modell sanity-geprüft werden kann)
ist mit dem Abschluss von OP-05 in
[`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
erfüllt; HIL-01..09 sind noch nicht angefasst.
**Quelle:** `bess-hil-simulator`-Schwesterprojekt (Checkout-Pfad
projekt-/operatorabhängig).
**Bezug:** [`../in-progress/roadmap.md`](../in-progress/roadmap.md)
(M2-Folgewelle), [`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
(Aktivierungs-Trigger), `docs/plan/planning/done/plan-RM-M1.md`,
`docs/plan/planning/done/plan-RM-M1-simulator.md`,
`docs/user/quality.md`, `spec/lastenheft.md`, `spec/architecture.md`

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

## Scope

| Bereich | Entscheidung |
| ------- | ------------ |
| M1-Pflichtgate | nein; `make gates` und `make test-integration` bleiben auf `bess-field-sim` ausgerichtet |
| Erster Nutzen | optionaler Modbus-HIL-Testpfad für Adapter- und PCS-Verhalten |
| Nicht-Ziel | keine Ablösung der deterministischen Szenario-Fixtures |
| Nicht-Ziel | keine vollständige BESS-Anlagenemulation, solange SOC/SOH/Availability im HIL-Simulator fehlen |
| Folgefähigkeit | nach erfolgreichem optionalem Gate kann der HIL-Pfad in RM-M2/RM-M3 ausgebaut werden |

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
- akzeptiert P- und Q-Setpoints über Holding Registers
- liefert Messwerte über Input Registers

Beispielstart:

```sh
docker build -t bess-hil-simulator:local "$BESS_HIL_SIMULATOR_PATH"
mkdir -p data
docker run --rm --user "$(id -u):$(id -g)" -p 5020:502 -v "$PWD/data:/data" bess-hil-simulator:local
```

Für reproduzierbare CI-Nutzung muss später entschieden werden, ob das Image
lokal gebaut, aus einer Registry gezogen oder über einen Commit-/Digest-Pin
referenziert wird.

---

## Aktueller BESS-EMS-Stand

| Bereich | Stand |
| ------- | ----- |
| Device-Point-Metadaten | `config/schema/device-point.schema.json`, `DevicePointMetadata` und Loader-Roundtrips sind vorhanden |
| Modbus-Mapping | trägt fachliche Punktmetadaten, aber noch keine Registertabelle |
| Modbus-Decoder | unterstützt `float32`, aber Word-Order ist aktuell nicht konfigurierbar |
| Modbus-Telemetrie | liest Read-Mappings aktuell aus Holding Registers |
| Modbus-Command | schreibt `active_power_setpoint_kw` und optional `operating_mode`, aber noch keinen Q-Setpoint |
| Integration-Gate | `tests/integration/compose.yml` startet `bess-field-sim`, Mosquitto und Postgres |

---

## Integrationshindernisse

| Thema | HIL-Simulator | Aktueller `bess-ems`-Stand | Notwendige Anpassung |
| ----- | ------------- | -------------------------- | -------------------- |
| Registertabelle | Messwerte liegen auf Input Registers `30001+` | `ModbusTelemetrySource` liest Read-Mappings aktuell aus Holding Registers | Mapping-Feld `register_table` mit Default `holding`; Adapter liest `input` über neuen Client-Port |
| Float-Word-Order | `float32` wird als zwei 16-bit-Wörter in HIL-spezifischer Reihenfolge geschrieben | `RegisterDecoder` kombiniert 32-bit-Werte aktuell fest als High-Word/Low-Word | Mapping-Feld `word_order` mit Default `high_low`; HIL-Profil setzt die tatsächlich benötigte Reihenfolge |
| Einheiten | P/Q in MW/MVAR | intern kW/kvar | `scale_factor: 1000` im HIL-Profil verwenden |
| Reactive Power | HIL akzeptiert P- und Q-Setpoints | `ModbusCommandSink` schreibt aktuell primär `active_power_setpoint_kw` | optionalen Schreibpfad für `reactive_power_setpoint_kvar` ergänzen |
| Betriebsmodus | HIL hat kein `operating_mode`-Register | `ModbusCommandSink` schreibt `operating_mode`, falls vorhanden | bereits kompatibel, solange HIL-Profil kein `operating_mode` enthält |
| BESS-Telemetrie | HIL liefert PCS-/Grid-Werte, aber kein SOC/SOH/Temperatur/Availability-Profil | `BatteryTelemetry` erwartet BESS-Zustand | erster HIL-Test bleibt PCS-/Modbus-Adaptertest; Control-Loop-HIL erst nach HIL-BESS-Registern oder Defaults |

---

## Kompatibilitätsvertrag

Neue Mapping-Felder müssen rückwärtskompatibel sein:

| Feld | Werte | Default | Zweck |
| ---- | ----- | ------- | ----- |
| `register_table` | `holding`, `input` | `holding` | trennt Messwerte auf Input Registers von Holding-Register-basierten Simulatorprofilen |
| `word_order` | `high_low`, `low_high` | `high_low` | steuert 32-bit-Decode/Encode über zwei 16-bit-Register |

Offen bleibt, ob später zusätzlich `byte_order` nötig wird. Für den ersten
HIL-Pfad soll nur die nachweislich benötigte Word-Order eingeführt werden.

Die bestehende `scale_factor`-Semantik bleibt unverändert:

```text
engineering_value = raw_value * scale_factor
wire_value = engineering_value / scale_factor
```

Damit bildet `scale_factor: 1000` MW/MVAR auf kW/kvar ab.

---

## Geplante Artefakte

| Artefakt | Zweck |
| -------- | ----- |
| `config/examples/adapters/modbus.hil-simulator.json` | HIL-spezifisches Modbus-Profil mit Input/Holding-Registertabellen, Float-Order und MW/kW-Skalierung |
| `tests/hil/compose.yml` oder Compose-Profil in `tests/integration/compose.yml` | startet `bess-hil-simulator:local` als zusätzlichen Sidecar |
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

Das Profil muss die vorhandene Gerätepunkt-Basis aus LH-DOM-005 tragen,
mindestens `name`, `display_name`, `unit`, `range`, `writable`,
`exportable`, `write_cadence` und `auth_required`.

Beispielstruktur:

```json
{
  "profile_name": "bess-hil-simulator",
  "unit_id_discovery": "static",
  "static_unit_id": 1,
  "registers": [
    {
      "name": "active_power_kw",
      "display_name": "Active Power",
      "unit": "kW",
      "address": 0,
      "register_table": "input",
      "type": "float32",
      "word_order": "low_high",
      "scale_factor": 1000,
      "range": [-250, 250],
      "writable": false,
      "exportable": true,
      "write_cadence": "cyclic",
      "auth_required": "none"
    }
  ]
}
```

`word_order` ist im Beispiel als Platzhalter zu verstehen und muss im ersten
HIL-Test gegen die reale HIL-Registerkodierung verifiziert werden.

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

Akzeptanzkriterien für den ersten Test:

- TCP-Port wird innerhalb von 30 Sekunden erreichbar.
- erster Messwert kann über Input Registers gelesen und dekodiert werden.
- nach P-Setpoint ist mindestens ein späterer Messwert betragsmäßig größer als
  der Ausgangswert.
- Test läuft unabhängig von `make gates`.
- Fehlschlag nennt den zuletzt gelesenen Rohwert und den dekodierten
  Engineering-Wert.

Folgetests:

- Q-Setpoint und PQ-Capability-Clamping
- Messlatenz bei Sprungantwort
- Spannungssag/-swell und resultierende Q-Limits
- Adapterverhalten bei HIL-Containerstopp
- optionaler Vergleich von Modbus-Messwerten mit `BessData.csv`

---

## Einbauoptionen

| Option | Vorteil | Nachteil | Empfehlung |
| ------ | ------- | -------- | ---------- |
| separates `tests/hil/compose.yml` | trennt optionalen HIL-Pfad klar vom M1-Gate | doppelte Compose-Struktur | bevorzugt für den ersten Schritt |
| Compose-Profil in `tests/integration/compose.yml` | weniger Dateien, gemeinsame Sidecars möglich | Risiko, das M1-Gate versehentlich zu koppeln | erst später, wenn stabil |
| nur manuelles Docker-Run | schnell für lokale Experimente | kein reproduzierbares Gate | nur für Vorprüfung |

---

## Arbeitspunkte

| Status | ID | Punkt | DoD |
| ------ | -- | ----- | --- |
| ✅ | HIL-01 | Modbus-Mapping um Registertabelle erweitern | Schema, Loader, Domain-Konfiguration und Adapter unterstützen `register_table=input|holding`; bestehende M1-Profile bleiben kompatibel. Adapter-Guards (`ModbusTelemetrySource` / `ModbusCommandSink`) werfen explizit bei `input` bzw. `low_high` und verweisen auf HIL-02/03; dadurch verhindert ein vorgezogenes HIL-Profil ein silent-misdecode. 5 neue Tests, integration-suite weiterhin grün. |
| ✅ | HIL-02 | Input-Register-Leseport ergänzen | `IModbusClient.ReadInputRegistersAsync` neu, `FluentModbusClient` ruft `ModbusTcpClient.ReadInputRegistersAsync<ushort>` (FC04). `ModbusTelemetrySource` wählt im Read-Loop anhand `register.RegisterTable` zwischen FC03/FC04 — Holding bleibt M1-Default. Mixed-Profile-Test (zwei FC04-Floats + ein FC03-Sentinel) belegt das Routing über `FakeModbusClient.Reads`-Log. HIL-01-Guard für `input` aufgehoben, neu: Reject auf unbekannte register_table-Werte. |
| ⬜ | HIL-03 | Float-Word-Order konfigurierbar machen | `RegisterDecoder`/Encoder unterstützen mindestens `high_low` und `low_high`; Tests decken beide Varianten ab. |
| ⬜ | HIL-04 | HIL-Modbus-Profil anlegen | `config/examples/adapters/modbus.hil-simulator.json` validiert gegen Schema und beschreibt HIL-Punkte mit Gerätepunkt-Metadaten. |
| ⬜ | HIL-05 | Optionalen Q-Setpoint schreiben | `ModbusCommandSink` schreibt `reactive_power_setpoint_kvar`, wenn das Mapping ein writable Register dafür enthält. |
| ⬜ | HIL-06 | HIL-Compose ergänzen | Ein optionaler Compose-Pfad startet `bess-hil-simulator:local` headless und stellt CSV-Logs in einem Volume bereit. |
| ⬜ | HIL-07 | HIL-Integrationstest ergänzen | `BatteryEms.Hil.IntegrationTests` prüft P-Sprungantwort gegen den HIL-Simulator ohne das M1-Pflichtgate zu blockieren. |
| ⬜ | HIL-08 | Make-Gate ergänzen | `make test-hil-modbus` startet den optionalen HIL-Testpfad und ist getrennt von `make gates`. |
| ⬜ | HIL-09 | Dokumentation ergänzen | `docs/user/quality.md` beschreibt Voraussetzungen, Build, Lauf und Abgrenzung zum Go-Simulator. |

---

## Offene Entscheidungen

| Kennung | Frage | Default |
| ------- | ----- | ------- |
| HIL-OPEN-01 | Wird das HIL-Image lokal gebaut oder über Registry referenziert? | lokal bauen: `bess-hil-simulator:local` |
| HIL-OPEN-02 | Gehört der HIL-Pfad noch zu RM-M1-Folgearbeiten oder zu RM-M2? | **Geschlossen:** RM-M2-Folgewelle. Aktivierungs-Trigger ist RM-M2-OP-05 (LP-Solver-Adapter steht); HIL bleibt optional und bricht `make gates` nicht. |
| HIL-OPEN-03 | Wird der HIL-Simulator um SOC/SOH/Availability ergänzt oder bleibt er PCS-/Grid-orientiert? | zunächst PCS-/Grid-orientiert |
| HIL-OPEN-04 | Soll HIL-CSV als Testartefakt persistiert werden? | nur optional für Debugging, Assertions über Modbus |
| HIL-OPEN-05 | Ist `word_order=low_high` für HIL korrekt? | im ersten HIL-Test gegen Rohregister verifizieren |
