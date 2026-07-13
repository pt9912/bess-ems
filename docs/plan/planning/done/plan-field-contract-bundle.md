# Plan: Feldvertrag-Bundle + Versionierung (ADR 0013 §5.1)

**Dokumenttyp:** Slice-Plan / done
**Status:** Abgeschlossen am 2026-07-13 — alle 5 Sub-Slices umgesetzt, 2
Review-Runden (alle 9 Befunde gefixt), `make gates` 3× voll grün. Die ADR-0013-Status-Klausel wurde zum Abschluss gesetzt auf
`Accepted — §5.1 umgesetzt; §5.2–§5.4 offen`. Umgesetzt auf Branch
`impl-field-contract-5.1`, per FF-Merge in `main`.
**Datum:** 2026-07-12
**Quelle:** [`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md) §5.1
**Bezug:**
[`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md)
(§2 Entscheidungen, §3 feld-normativer Vertrag, §5 Umsetzung, §8 offene Punkte),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(`LH-CONF-002`, `LH-MQTT-001/003`, `LH-MODB-003`, `LH-RISK-002`)

---

## Ziel

Umsetzungsschritt §5.1 aus ADR 0013 liefern: das Geräte-Mapping-Schema-Set als
**versioniertes, konsumierbares Bundle** publizieren, den bislang un-schema'ten
**MQTT-Telemetrie-Payload** maschinenlesbar machen, und die **Kadenz-Stellschraube**
(`maxAge`) freilegen. Alle inhaltlichen Entscheidungen stehen im ADR; dieser Plan
schneidet nur die Umsetzung. Er referenziert ADR 0013 — nicht umgekehrt.

Nach Abschluss trägt ADR 0013 die kanonische Dash-Klausel
`Accepted — §5.1 umgesetzt; §5.2–§5.4 offen` (bleibt **Accepted** — „Provisional" ist
kein Repo-Vokabular, alle 13 ADRs sind `Accepted`; §5.2/§5.3/§5.4 sind eigene
Folge-Umsetzungen).

---

## Ausgangslage

Heute (verifiziert 2026-07-12):

- Nur `config/schema/opcua-mapping.schema.json` trägt `schema_version` (enum `["v1"]`,
  required + Loader-Enforcement `SupportedOpcUaSchemaVersions`). Modbus/MQTT haben
  keins.
- Der MQTT-**Telemetrie-Payload** (breites Snapshot-Objekt) ist in **keinem** Schema;
  seine Form lebt in `src/adapters/driven/BatteryEms.Adapters.Mqtt/MqttPayloads.cs`.
- Kein Release-/Packaging-Schritt emittiert `config/schema/` als Artefakt (nur
  Source-Tarball + Docker-Image). `make schema-*` deckt **nur** die Postgres-DDL
  (`schema/schema.yaml`), **nicht** `config/schema/`.
- Der Snapshot-Freshness-`maxAge` ist **10 s, hartkodiert**
  (`ApplicationServiceRegistration.cs:27`, Literal in der DI-Registrierung, kein
  `Bess__…`-Key).

Kompatibilitäts-Randnotiz: ein neu-required `schema_version` bricht **jede**
.NET-geladene Modbus/MQTT-Mapping-Datei ohne das Feld — nicht nur die Beispiele
(voller Satz in Sub-Slice 1). Der Go-Simulator (`bess-field-sim`) liest mit
`encoding/json` (ohne `DisallowUnknownFields`, `internal/…/mapping.go:56`) und
**ignoriert** unbekannte Felder — kein Feld-Sim-Bruch durch das Zusatzfeld (im
Compose-Pfad erhält der Sim die gehobenen `*.simulator.json` via Bind-Mount
`deploy/compose.yml:79-80` und ignoriert das Feld).

---

## Sub-Slices

### 1 — `schema_version` auf Modbus- und MQTT-Mapping

- **Aufgabe:** `schema_version` (required, enum `["v1"]`) in
  `modbus-mapping.schema.json` + `mqtt-mapping.schema.json` ergänzen, gespiegelt vom
  OPC-UA-Muster (`JsonFileConfigurationLoader.cs:19,231-240`); Loader-Enforcement
  analog `SupportedOpcUaSchemaVersions` an `LoadModbusMapping`/`LoadMqttMapping`,
  **inkl. `SchemaVersion`-Feld auf `ModbusMappingConfiguration`/`MqttMappingConfiguration`**
  (heute nur OPC-UA, vgl. `LoadModbusMapping:174` vs. OPC-UA `:269`) — sonst halber Port.
  **Alle .NET-geladenen** Modbus/MQTT-Mappings gleichzeitig auf `schema_version:"v1"`
  heben — nicht nur die Beispiele: `config/examples/adapters/{modbus.simulator,
  modbus.hil-simulator,modbus.sunspec-simulator,mqtt.simulator}.json`,
  `tests/integration/fixtures/modbus.simulator.json`, sowie **Inline-JSON nur in
  `JsonFileConfigurationLoaderTests`** (die Roundtrip-/`MultiAssetHostCompositionTests`
  laden die oben gelisteten Datei-Fixtures per Pfad — kein Inline-JSON).
- **Enforcement = Pre-Check (entschieden, nicht offen):** wie OPC-UA
  (`SupportedOpcUaSchemaVersions`, `LoadOpcUaMapping:205-241`) **vor** der
  Schema-Auswertung — Message-Qualität konsistent mit OPC-UA. (Alternative reines
  Schema-`required`+enum verworfen: bliebe zwar „Schema validation failed", verwäscht
  aber die Diagnose.) Die Reihenfolge bestimmt die Testanalyse unten, darum vorab fixiert.
- **Bestehende Negativtests migrieren (sonst bricht `make gates`):** jedes bestehende
  Negativ-Inline-Mapping in `JsonFileConfigurationLoaderTests`, das **einen anderen**
  Fehler testet, bekommt ein gültiges `schema_version:"v1"`, damit es weiter aus dem
  **eigentlichen** Grund scheitert. **Konsequenz des Pre-Checks:** ohne diese Migration
  verlieren die `Assert.Contains("Schema validation failed", …)`-Fälle (`:436,:542,:560`)
  ihre Meldung → **Hard-Break**; die bloßen `Assert.Throws`-Fälle (`:399,:470,:928,:966`)
  werfen aus dem falschen Grund → **False-Green**. **Neue** Negativfälle für fehlendes/
  unbekanntes `schema_version` separat ergänzen (opcua-Negativtest gespiegelt).
- **Akzeptanz:** Loader lädt gültige `v1`-Mappings; fehlendes/unbekanntes
  `schema_version` wird hart abgelehnt; **alle** .NET-geladenen Mappings (Beispiele +
  Integrations-Fixtures + positive Inline-Test-JSON) laden weiter; **bestehende
  Negativtests scheitern weiter aus ihrem Originalgrund** (kein Hard-Fail, kein
  False-Green) → `make gates` bleibt grün, sobald das Enforcement scharf ist.
- **Verifikation:** `make gates` (Config-Loader-Tests), `make docs-check`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 2 — MQTT-Telemetrie-Envelope-Schema (C#→Schema + zwei-seitiger Check)

- **Aufgabe:** neues `config/schema/mqtt-telemetry-envelope.schema.json` (breiter
  Snapshot-Feldsatz, `$id` unter `https://bess-ems.io/schema/`), **generiert aus**
  `MqttPayloads.cs` (`TelemetrySnapshotPayload` + `CommandPayload` + `CommandAckPayload`)
  via `JsonSchema.Net.Generation`. Feld-normativ gemäß ADR §3 (Namen/Präsenz/Typen/
  Null-Weglassung; **nicht** Reihenfolge). Deckt die **drei C#-Typen** (telemetry,
  command, command_ack) mit `required` **je Payload-Typ**.
- **Erste Umsetzungsaufgabe (sonst kollidieren (1) und (2)):** Generator-Verhalten für
  `[JsonPropertyName]` + Nullability verifizieren. Respektiert er die Attribute **nicht**
  (emittiert `OffsetMillis` statt `offset_millis`, oder markiert nullable als `required`),
  einen **deterministischen Post-Transform** einziehen (Naming aus `[JsonPropertyName]`,
  `required` aus Non-Nullability) — (1) difft dann gegen das **normalisierte** Generat.
- **Akzeptanz (zwei-seitig — sonst self-referential):**
  1. **Drift auf C#-Änderung:** committetes Envelope-Schema == Generat aus dem
     C#-Record (fängt Feld hinzu/weg/umbenannt in `MqttPayloads.cs` ohne Schema-Update).
  2. **Serializer-Konsistenz (kritisch):** repräsentative `TelemetrySnapshotPayload`/
     `CommandPayload`/`CommandAckPayload`-Instanzen mit **`MqttJson.Options`**
     serialisieren und das JSON gegen das Envelope-Schema **validieren**. Fängt, was (1)
     nicht kann: Naming (snake_case aus `[JsonPropertyName]`, nicht aus
     `PropertyNamingPolicy=null` — für die voll-attributierten Records ein No-op) und
     Null-Weglassung **per Payload-Typ**: `reactive_power_kvar` ist in **Telemetry
     required** (`double`, Z. 20), nur im **Command optional** (`double?`, Z. 33);
     `reason` im **Ack optional** (`string?`). `required` je Payload-Typ (ADR §3), nicht global.
- **Reichweite ehrlich (ADR §3):** der Round-trip trifft den echten Draht-Output **nur
  für `command`** (bess-ems ist Produzent, `MqttCommandSink.cs:81`). `telemetry` +
  `command_ack` werden von bess-ems nur **deserialisiert** (`MqttTelemetrySource.cs:83`,
  `MqttCommandSink.cs:152`) — der Check pinnt dort die **Konsumenten-Erwartung**, nicht
  den Feld-Produzenten; die Deckung mit dem echten Telemetrie-Produzenten (`serializer.go`)
  stellt erst die Golden-Vector-Suite (ADR §5.2, deferred) her.
- **Praktisch:** DTOs sind `internal sealed record` → `InternalsVisibleTo` (oder Check
  im `BatteryEms.Adapters.Mqtt.Tests`-Projekt); `JsonSchema.Net.Generation` ist ein
  **separates** Paket mit **eigener Versionslinie** (aktuell `7.3.10`, **nicht** 9.x) →
  Pin in `Directory.Packages.props`. **Koordination:** `7.3.10` verlangt
  `JsonSchema.Net >= 9.2.2`, der Repo pinnt aber `9.2.0` (`:15`) — also `JsonSchema.Net`
  **9.2.0 → 9.2.2 bumpen** (via `make lock-refresh`), sonst scheitert der Locked-Mode-
  Restore am Konflikt.
- **Verifikation:** neuer `make field-contract-check` (Docker) + `make gates`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 3 — Release-Asset-Bundle

- **Aufgabe:** `.github/workflows/release.yml` + `scripts/build-release-assets.sh` um
  ein versioniertes Schema-Bundle erweitern: `config/schema/*.json` inkl. Envelope +
  **Schema-CHANGELOG** + Bundle-`schema_version` (beide **jetzt** in Scope), in
  `SHA256SUMS` aufgenommen. `min_supported` wird **minimal** ausgeliefert (`"v1"` als
  einfacher Floor — bei `v1` gibt es nichts Inkompatibleres); das reichere Band-Format
  ist per ADR §2/§8 **operativ deferred** (trigger-basiert beim ersten Breaking-Bump)
  und wird hier **nicht** stillschweigend designt.
- **Akzeptanz:** `scripts/build-release-assets.sh`-Dry-Run erzeugt Bundle + CHANGELOG +
  `min_supported:"v1"` + Checksumme reproduzierbar.
- **Verifikation:** `make release-assets` (Dry-Run).
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 4 — CI-Drift-Gate auf `config/schema/`

- **Aufgabe (wie umgesetzt):** Gate `make field-contract-check` (Dockerfile-Stage,
  jsonschema auf `${PYTHON_IMAGE}`), das (a) jedes `config/schema/*.schema.json`
  Draft-2020-12-**meta-validiert** und (b) die mitgelieferten
  `config/examples/`-Mappings gegen ihre Schemas validiert (Cross-`$ref` über
  `$id`-Registry; `REQUIRED_EXAMPLE_PATTERNS` erzwingt je ein Mapping-Beispiel pro
  Protokoll). Der Envelope-C#↔Schema-Drift-Check (Sub-Slice 2) läuft **nicht** in
  diesem Gate, sondern in `EnvelopeSchemaTests` (`make test`) — er braucht die
  C#-Quelle (`MqttPayloads.cs`) und kann nicht in Python leben — und damit im
  selben `make gates`-Aggregat. In `make gates` + `.github/workflows/build.yml`
  verdrahtet. Schließt die heutige Lücke (`make schema-*` deckt nur die DDL).
  **Bewusst getrennter Name** — nicht in
  `schema-*` (`Makefile:122/138/151`, Postgres-DDL via d-migrate) gefaltet; der
  Feldvertrag ist eine andere Domäne, die Gates nicht zusammenlegen/verwechseln.
- **Akzeptanz:** Gate grün im vollen `make gates`; ein absichtlich gebrochenes
  Schema/Mapping bricht es.
- **Verifikation:** `make field-contract-check` als Pflicht-Gate.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 5 — Snapshot-`maxAge` konfigurierbar

- **Aufgabe:** Config-Key `Bess:SnapshotMaxAge` (`TimeSpan`, Default `00:00:10`). Die
  Registrierung `AddBessApplicationInMemoryStores` (`ApplicationServiceRegistration.cs:27`,
  Projekt `BatteryEms.Api`) bekommt den Wert als **Parameter**; die **zwei echten
  Call-Sites** — `BessHostBuilder.cs:64` (Host) **und** `Api/Program.cs:51` (Api),
  **nicht** Worker (der konsumiert `ISnapshotStore` nur) — resolven ihn jeweils aus dem
  **eigenen** `builder.Configuration` (`GetValue("Bess:SnapshotMaxAge", <default>)`); der
  10-s-Default ist **eine gemeinsame Konstante** (z. B. auf der Extension/Konfig-Konstante),
  die beide Call-Sites konsumieren — **nicht** zwei Literale (sonst Default-Drift).
- **Api-Layering (kritisch):** der Wert darf **nicht** aus `BessHostOptions` kommen —
  `BatteryEms.Api` referenziert `BatteryEms.Host` **nicht** und darf es per
  Architektur-Tabu nicht (`Api/Program.cs:15-17`). Beide Prozesse lesen den Key selbst
  aus ihrer `IConfiguration`, damit er **in beiden** wirkt (voller Host **und**
  Api-Read-Path) — sonst wäre „konfigurierter Wert wird honoriert" nur im Host wahr
  (stiller Halb-Bug). Ctor `≤ 0` (`InMemorySnapshotStore.cs:13`) → fail-fast.
- **Akzeptanz:** Default 10 s unverändert; ein konfigurierter Wert wird **in beiden
  Prozessen** honoriert; ein langsamer als der Wert tickendes SUT-Feld läuft nicht mehr
  zwangsweise in Safe-Stop.
- **Verifikation:** `make gates` (Application-Test für den konfigurierten `maxAge`).
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

---

## Nicht-Ziele

- **Kein** Command-Closed-Loop / Inbound-Write (deferred, ADR §6).
- **Keine** Modbus-Golden-Vectors (Folge-Umsetzung, ADR §5.4).
- **Keine** grid-gym-seitige Arbeit — liegt im Schwesterprojekt (nur MQTT-first hier).
- **Keine** Operationalisierung der Breaking-Bump-Fensterlänge (ADR §8, trigger-basiert).
- **Kein** Runtime-Verhaltensdelta außer der `maxAge`-Konfigurierbarkeit.
- **Kein** `status`/`fault`-Schema im Envelope — beide sind auf dem Produzenten
  Go-`map[string]any` ohne C#-Typ, also nicht aus `MqttPayloads.cs` generierbar; sie
  gehören zur `serializer.go`-getriebenen Golden-Vector-Umsetzung (ADR §5.2).
- **Keine** Golden-Vector-Suite (ADR §5.2, eigener Plan).
- **Go-Sim-Testdata-Mappings** (`simulators/bess-field-sim/testdata/mappings/*.simulator.json`,
  eigene, vom Repo-Beispiel abweichende Kopien) bleiben **un-gehoben** — Go ignoriert
  das Feld; das ist **kein** Single-Source-Drift im Bundle.

---

## Liefergegenstände bei Aktivierung

1. `schema_version` in Modbus-/MQTT-Schema + Loader-Enforcement + gehobene
   Beispiel-Mappings.
2. `mqtt-telemetry-envelope.schema.json` + C#→Schema-Generator + zwei-seitiger Check
   (Generat-Diff **und** Serializer-Round-trip); `JsonSchema.Net.Generation`-Pin
   (`7.3.10`) + `JsonSchema.Net`-Bump `9.2.0→9.2.2` + `InternalsVisibleTo`.
3. Release-Asset-Bundle (Schema-Set + CHANGELOG + `min_supported`) inkl. Checksumme.
4. `make field-contract-check` als Pflicht-Gate in `make gates` + CI.
5. `Bess__SnapshotMaxAge`-Config-Key + Durchreichung.
6. Tests: `schema_version`-Positiv/Negativ (voller .NET-Mapping-Satz), Envelope-
   Generat-Diff **+ Serializer-Round-trip-Validierung**, Beispiel-Mapping-Validierung,
   `maxAge`-Konfiguration.

---

## Akzeptanzkriterien

- Alle drei Protokoll-Mappings tragen `schema_version` und werden vom Loader
  versions-geprüft.
- Der MQTT-Telemetrie-Payload ist als Schema publiziert; der Round-trip trifft für
  `command` den **echten Serializer-Output** (`MqttJson.Options`), für `telemetry`/`ack`
  die **Konsumenten-Erwartung** (Feld-Produzent-Deckung erst ADR §5.2); Drift auf
  C#-Änderung bricht das Gate.
- Das Schema-Set ist als reproduzierbares, versioniertes Release-Asset abrufbar.
- `config/schema/` steht unter einem CI-Drift-Gate (nicht mehr nur die DDL).
- Der Freshness-`maxAge` ist per `Bess__…`-Key konfigurierbar; Default 10 s bleibt.
- `make gates` bleibt grün; keine Architektur-/Boundary-Regel verletzt.

---

## Definition of Done (DoD)

- [x] Sub-Slice 1 — `schema_version` Modbus/MQTT + Enforcement + Beispiel-Mappings. (`09a3f14`/`afc143e`/`363fc04`/`7dbf65b`)
- [x] Sub-Slice 2 — Envelope-Schema (C#→Schema) + zwei-seitiger Check (Generat-Diff + Serializer-Round-trip). (`bbc7081`)
- [x] Sub-Slice 3 — Release-Asset-Bundle + Checksumme + CHANGELOG/`min_supported`. (`bf3f9a8`; reproduzierbar `6acfe7e`)
- [x] Sub-Slice 4 — `make field-contract-check` als Pflicht-Gate (make + CI). (`ba2667f`; auf Meta+Mapping-Validierung verschärft `a5c0015`, Rest-Fix `e4c0463`)
- [x] Sub-Slice 5 — `Bess__SnapshotMaxAge` konfigurierbar. (`fbefacb`; Config-Test + Doku `f019e92`; Drift-Guard `e4c0463`)
- [x] Alle Akzeptanzkriterien erfüllt; `make gates` grün. (3× voller Lauf grün: nach §5.1-Impl, nach Review-Runde 1, nach Review-Runde 2)
- [x] ADR 0013 Status-Klausel auf `Accepted — §5.1 umgesetzt; §5.2–§5.4 offen` aktualisiert (bleibt Accepted). (Finalisierung 2026-07-13)

---

## Abschluss (2026-07-13)

Alle 5 Sub-Slices von §5.1 implementiert, committet und in **zwei Review-Runden**
(per Agent) adversarisch geprüft; alle 9 Befunde (5 + 4 Rest) gefixt und je einzeln
+ im vollen `make gates`-Aggregat verifiziert. `make gates` dreimal voll grün.
Finalisierung: ADR 0013 Status-Klausel gesetzt, Sub-Slice-4-Wortlaut an die
Umsetzung angeglichen (Gate = Meta- + Beispiel-Validierung; der Envelope-Drift-Check
lebt in `EnvelopeSchemaTests` im selben `make gates`-Aggregat), Plan nach `done/`
verschoben, Branch `impl-field-contract-5.1` per FF-Merge in `main`.

**Verifikations-Kommandos:** `make gates` (voll), `make field-contract-check`
(nur Feldvertrag-Gate).
