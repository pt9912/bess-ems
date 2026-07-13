# Plan: Golden-Vector-Suite MQTT (ADR 0013 §5.2)

**Dokumenttyp:** Slice-Plan / done
**Status:** Abgeschlossen am 2026-07-13 — alle 5 Sub-Slices umgesetzt
(Plan-Review: 4 Befunde vorab eingearbeitet; Implementierungs-Review:
9 Befunde, 8 gefixt + 1 dokumentiert akzeptiert, siehe Abschluss),
`make gates` voll grün. Die ADR-0013-Status-Klausel wurde zum Abschluss gesetzt auf
`Accepted — §5.1–§5.2 umgesetzt; §5.3–§5.4 offen`. Umgesetzt auf Branch
`impl-field-contract-5.2`.
**Datum:** 2026-07-13
**Quelle:** [`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md) §5.2
**Bezug:**
[`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md)
(§2 Achse „Normative Payload-Form", §3 feld-normativer Vertrag + Autoritäten,
§5.2 Umsetzung, §8 Golden-Vector-Format-Details),
[`../done/plan-field-contract-bundle.md`](../done/plan-field-contract-bundle.md)
(§5.1: Envelope-Schema, Bundle, `field-contract-check` — die Vorleistungen,
auf denen diese Suite aufsetzt),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(`LH-MQTT-001/003`, `LH-CONF-002`, `LH-RISK-002`)

---

## Ziel

Umsetzungsschritt §5.2 aus ADR 0013: **portable Golden-Vektoren** für den
MQTT-Feldvertrag, **mechanisch aus dem Producer-Code gehoben** (nicht
hand-gelistet), **strukturell** verglichen (feld-normativ, ADR §3). Die Suite
ist das Abnahme-Geschirr, das grid-gyms Push-Field-Publish-Surface treffen
muss, und schließt zugleich die in §5.1 bewusst offen gelassene Deckungslücke:
der Envelope-Schema-Check pinnt dort nur die **Konsumenten-Erwartung** von
bess-ems; die Deckung mit dem **echten Telemetrie-Produzenten**
(`serializer.go`) stellt erst diese Suite her.

Nach Abschluss trägt ADR 0013 die Status-Klausel
`Accepted — §5.1–§5.2 umgesetzt; §5.3–§5.4 offen`.

---

## Ausgangslage

Verifiziert 2026-07-13 (Stand v2.0.0):

- **Produzenten-Autoritäten je Richtung** (ADR §3, im Code bestätigt):
  - `telemetry`/`status`/`fault` ← `simulators/bess-field-sim/internal/mqtt/serializer.go`:
    `telemetry` = voller Snapshot-Marshal (`payloadFor:61`), `status` = Subset
    `{available, fault_status, offset_millis}` (`:63-67`), `fault` = Subset
    `{fault_status, offset_millis}` mit **Suppression** bei
    `fault_status ∈ {"", "ok"}` (`:69-75`, liefert **keine** Nachricht).
    Öffentliche API: `ResolveTelemetry` (inkl. `{assetId}`-Substitution und
    Retained-Flag aus dem Mapping, `:31-51`).
  - `command_ack` ← Feld produziert: `internal/mqtt/commands.go` marshalt
    `model.CommandAck` als always-accepted Echo (`:114-119`, `Reason:"accepted"`,
    `DispatchedAt` über injizierbare `Clock` `:26` — deterministisch testbar);
    bess-ems konsumiert via `CommandAckPayload` (`MqttCommandSink.OnAckAsync:152`).
  - `command` ← EMS produziert: `MqttCommandSink` serialisiert
    `ToWire(...)` mit `MqttJson.Options` (`:81`); Autorität ist
    `MqttPayloads.cs` (voll-attributierte Records, `WhenWritingNull` lässt
    `reactive_power_kvar` bei `null` entfallen; Mode/Source als
    .NET-Enum-Namen-Vokabular).
- **Konsumenten-Seite bess-ems:** `MqttTelemetrySource` konsumiert **nur**
  `telemetry` (`:37-43`; status/fault sind bewusst nicht abonniert),
  deserialisiert mit `MqttJson.Options` (`:83`) und normalisiert
  `fault_status ∈ {null, ""}` → `"ok"` (`:108`). Test-Harness mit
  `FakeMqttClient` existiert (`MqttTelemetrySourceTests`).
- **§5.1-Vorleistungen:** `mqtt-telemetry-envelope.schema.json` (C#→Schema,
  Drift-Check in `EnvelopeSchemaTests`), Schema-Bundle
  (`bess-ems-schemas-<v>.tar.gz`, reproduzierbar, `build_schema_bundle` in
  `scripts/build-release-assets.sh:163-172`), `make field-contract-check`
  (Python/jsonschema-Stage: Meta- + Beispiel-Validierung).
- **Format-Präzedenz im Repo:** die Replay-Suite (RM-M5-04) nutzt
  `schema_version: "replay-manifest.v1"`-Manifeste
  (`tests/fixtures/replay/…`) — dieselbe Namenskonvention
  (`<name>.v1`) gilt hier.
- **Byte-Instabilität des Produzenten** (ADR §3): `telemetry` emittiert in
  Go-Struct-Deklarationsreihenfolge, `status`/`fault` via `map[string]any`
  **alphabetisch** — ein Byte-Vergleich wäre strenger als der Draht-Vertrag.
  Struktureller Vergleich ist in ADR §3 bereits **entschieden**.
- **Die Gegenrolle existiert inzwischen real** (grid-gym v0.5.0 2026-07-12 /
  v0.6.0 2026-07-13): eine Push-Field-Publish-Surface (MQTT, publish-only)
  und eine Pull-Device-Server-Surface (Modbus-TCP, Read-Serving; seit v0.6.0
  auch Inbound-Write→Command). Die Push-Seite publisht heute grid-gyms
  **schmales** Punkt-Format (`{topic_prefix}/{device_id}/{metric}`, ein
  Punkt-Objekt je Nachricht) — **nicht** den bess-ems-Vertrag (breiter
  Snapshot je Tick, `battery/{assetId}/telemetry`). Der in ADR §1 antizipierte
  Feldadapter (Frame-Aggregation + Namens-/ID-Mapping) fehlt dort noch;
  **diese Suite ist das Abnahme-Geschirr, gegen das er gebaut wird** — der
  Konsument ist damit nicht mehr hypothetisch, sondern wartet auf das
  Artefakt.

---

## Entscheidungen (Format — hier fixiert, ADR §8 „Umsetzungsdetail")

1. **Zwei Manifest-Dateien, geschnitten nach Autorität** (ADR §3-Tabelle):
   - `config/schema/vectors/mqtt-golden-vectors.field.v1.json` —
     Feld-produziert (Go-gehoben): `telemetry`, `status`, `fault`
     (inkl. Suppression-Fall), `command_ack`.
   - `config/schema/vectors/mqtt-golden-vectors.ems.v1.json` —
     EMS-produziert (C#-gehoben): `command` (mit/ohne `reactive_power_kvar`).
   Jede Seite regeneriert **ausschließlich ihre** Datei — keine
   Merge-Mechanik, klare Ownership. (Verworfen: eine gemeinsame Datei —
   zwei Generatoren müssten in ein Artefakt schreiben; verworfen: Schnitt
   nach Topic — zerreißt die Autoritäts-Tabelle aus ADR §3.)
2. **Payloads liegen inline als JSON-Objekte** im Manifest — nicht als
   Strings, nicht als Einzeldateien. Der Vertrag ist feld-normativ; ein
   eingebettetes Objekt **kann** Byte-Form gar nicht erst pinnen
   (Fehlbedienung ausgeschlossen). Eine kanonische Byte-Referenz gibt es
   **nicht** (ADR §3: höchstens non-normativ — hier bewusst weggelassen).
3. **Manifest-Schema** `config/schema/golden-vector-manifest.schema.json`
   (hand-authored wie die Mapping-Schemas, Draft 2020-12, `$id` unter
   `https://bess-ems.io/schema/`, `schema_version`-Wert
   `"golden-vector-manifest.v1"`). Je Case: `name`, `topic_name`
   (`telemetry|status|fault|command|command_ack`), `direction` (aus
   EMS-Adapter-Sicht, Vokabular wie `mqtt-mapping`), `topic` (aufgelöst mit
   fixem `asset-1`), `retained` (aus dem Mapping), `description`, und
   **entweder** `payload` (Objekt) **oder** `suppressed: true`
   (Fault-ok-Fall: es darf **keine** Nachricht fließen).
4. **Struktureller Vergleich, pinnt:** exakte Feldmenge (keine fehlenden,
   keine zusätzlichen Member), JSON-Typen, Werte; Zahlen **numerisch**
   verglichen (800 ≡ 800.0), Member-Reihenfolge und Whitespace irrelevant.
   Timestamps bleiben Strings und pinnen die **tatsächliche Producer-Form**
   (C# `DateTimeOffset` → `…+00:00`, Go `time.Time` → `…Z`; beides valides
   RFC 3339 — Konsumenten müssen beide Offsets-Schreibweisen akzeptieren,
   das prüfen die Konsum-Tests in Sub-Slice 3/4).
5. **Deterministische Inputs:** Werte-Sätze fix im Generator-/Test-Code
   (Smoke-Werte aus ADR §1 als Basis: `soc 60.5`, `dc_voltage 800`, …;
   Epoch-Timestamps; `Clock`-Injektion für `dispatched_at`). Kein
   Wall-Clock, kein Zufall — Regeneration ist byte-über-Läufe-stabil,
   auch wenn nur strukturell verglichen wird.

---

## Sub-Slices

### 1 — Manifest-Schema + Vektor-Verzeichnis

- **Aufgabe:** `golden-vector-manifest.schema.json` gemäß Entscheidung 3
  anlegen; `config/schema/vectors/` als Ablage einführen;
  `config/schema/CHANGELOG.md`-Eintrag (additiv, `schema_version` des
  Vertrags bleibt `v1`).
- **Akzeptanz:** Schema meta-validiert im bestehenden
  `field-contract-check` (Muster `config/schema/*.schema.json` greift
  automatisch); `oneOf`-Ausschluss `payload`/`suppressed` nachweislich
  wirksam (ein Case mit beidem/keinem von beiden schlägt fehl).
- **Verifikation:** `make field-contract-check`, `make docs-check`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 2 — Feld-Vektoren (Go-gehoben) + Go-Drift-Gate

- **Gate-Topologie (entschieden, nicht offen):** Drift-Gate und Generator
  laufen als **eigene Stage im Root-Dockerfile** (Go-Toolchain,
  **Build-Kontext = Repo-Root**, analog `field-contract-check`), neues
  Pflicht-Target `make field-vectors-check` in `make gates`. Grund:
  `make simulator-test` baut mit **Build-Kontext = Modulverzeichnis**
  (`simulators/bess-field-sim/Makefile`) — weder
  `config/schema/vectors/` noch `config/examples/adapters/
  mqtt.simulator.json` sind dort sichtbar; ein Drift-Gate „in
  `simulator-test`" wäre nicht implementierbar. (Verworfen: (b) Vektor-
  Datei unter `simulators/` — verschöbe ein publiziertes Vertragsartefakt
  ins Modul und bräche die Bundle-Symmetrie aus Entscheidung 1;
  (c) modulinterne Kopie + Sync-Check — institutionalisiert genau den
  Hand-Mirror, den ADR 0013 bekämpft.)
- **Aufgabe:** Generator im Simulator-Modul (Package-Ort — z. B.
  `internal/mqtt` + schmaler Einstieg — ist Slice-Detail; **Achtung:**
  ein neuer `cmd`-Einstieg läuft gegen das 90 %-Gate
  `simulator-coverage-gate` — beim Umsetzen klären, wie `cmd/` dort
  behandelt wird; da Build/Aufruf über die Repo-Root-Stage laufen, ist
  ggf. gar kein Modul-`cmd`-Einstieg nötig), der die Cases durch die
  **echten** Producer-Pfade hebt: `ResolveTelemetry` gegen das
  Repo-Beispiel-Mapping `config/examples/adapters/mqtt.simulator.json`
  — **direkt, nicht über die `testdata/`-Kopie** (Repo-Root-Kontext
  macht das möglich; liefert Topic + Retained + Payload; Suppression-Fall
  fällt mechanisch als „nicht emittiert" heraus) — und
  `model.CommandAck`-Marshal (der identische Struct, den
  `commands.go:114-119` publiziert; Werte = Echo auf den nominalen
  Command-Case aus Sub-Slice 3, `Reason:"accepted"` — die
  Echo-Kopplung ist **gate-geprüft** über den Korrelations-Harness in
  Sub-Slice 3(b)3, nicht bloß Konvention).
  Case-Satz (Minimum): telemetry nominal, telemetry ladend
  (negatives `active_power_kw`), status nominal, fault aktiv
  (z. B. `"overtemperature"`), fault supprimiert (`"ok"` →
  `suppressed: true`), command_ack accepted-Echo.
  Schreibt `mqtt-golden-vectors.field.v1.json`.
- **Nebenbefund-Fix (aus dem Plan-Review):**
  `simulators/bess-field-sim/testdata/mappings/mqtt.simulator.json` ist
  eine Hand-Kopie des Repo-Beispiels und **bereits gedriftet** (ohne
  `schema_version`/`display_name`, damit seit v2.0.0 schema-invalid) —
  genau die Fehlerklasse des ADR. Der Slice hebt die Kopie auf den
  Beispiel-Stand; sie bleibt danach reine Modul-Unit-Test-Fixture ohne
  Vertrags-Rolle (der Generator liest das Original).
- **Drift-Gate:** die Repo-Root-Stage regeneriert die Cases und
  vergleicht **strukturell** gegen die committete Datei —
  `serializer.go`-Änderung ohne Vektor-Refresh bricht
  `make field-vectors-check`. Refresh-Pfad: `make
  field-vectors-refresh` (gleiche Stage, schreibt die Feld-Datei via
  Build-Output zurück in den Working Tree — Mechanik analog anderer
  Refresh-Targets, Slice-Detail).
- **Akzeptanz:** Datei validiert gegen das Manifest-Schema; Drift-Gate rot
  bei mutiertem Serializer (einmalig demonstriert), grün auf committetem
  Stand; Suppression-Case ist als `suppressed` enthalten (nicht einfach
  weggelassen); `testdata`-Kopie auf Beispiel-Stand gehoben.
- **Verifikation:** `make field-vectors-check` (neu, Pflicht in
  `make gates`), `make simulator-test` (unverändert grün), `make gates`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 3 — EMS-Vektoren (C#-gehoben) + C#-Drift-Gate + Feld-Vektor-Konsum

- **Aufgabe (a) EMS-Datei:** `mqtt-golden-vectors.ems.v1.json` mit den
  Command-Cases (voll; ohne `reactive_power_kvar` → Feld fehlt per
  `WhenWritingNull`), gehoben über `MqttJson.Options`-Serialisierung der
  `CommandPayload`-Instanzen (Werte deterministisch, Epoch-Timestamps;
  Mode/Source decken das Enum-Vokabular exemplarisch ab). Drift-Test nach
  dem `EnvelopeSchemaTests`-Muster (Regenerat ↔ committet, strukturell);
  Refresh analog Envelope-Schema (Testmeldung benennt den Weg —
  Detail-Entscheidung im Slice, ob env-gated Writer oder manuell).
- **Aufgabe (b) Feld-Vektor-Konsum (die §5.1-Lücke):** C#-Tests, die die
  **Feld-Datei** aus Sub-Slice 2 konsumieren:
  1. jeder `telemetry`-Payload validiert gegen
     `mqtt-telemetry-envelope.schema.json` — damit ist erstmals der
     **echte Produzent** gegen das Envelope-Schema gedeckt —
     **plus exakte Key-Set-Gleichheit** zwischen Payload und
     `$defs.telemetry.properties`: das Schema hat `required` über alle
     Felder, aber **kein** `additionalProperties: false` — die reine
     Validierung fängt nur ein **entferntes** Serializer-Feld; ein
     **hinzugefügtes** passierte nach Vektor-Refresh sonst stumm, und
     das publizierte Schema driftete unbemerkt vom Produzenten weg
     (Review-Befund 2; kein Eingriff ins generierte §5.1-Artefakt);
  2. jeder `telemetry`-Payload läuft durch den
     `MqttTelemetrySource`-Harness (`FakeMqttClient`) und ergibt die
     erwarteten `BatteryTelemetry`-Werte;
  3. Echo-Roundtrip **über beide Manifest-Dateien** (Review-Befund 3):
     der Harness dispatcht das Command **aus dem nominalen Case der
     EMS-Datei** über den `MqttCommandSink` und erwartet die Korrelation
     mit dem `command_ack`-Payload **der Feld-Datei** (CommandId-Match).
     Damit ist die Echo-Invariante (Sub-Slice 2 baut den Ack auf den
     Command-Case) gate-geprüft — laufen die `command_id`s auseinander,
     scheitert dieser Test, statt dass eine bloße Konvention driftet.
  `status`/`fault` haben **keinen** C#-Konsumenten (M1-Entscheidung,
  `MqttTelemetrySource:37-43`) und **kein** Schema (§5.1 Nicht-Ziel) — für
  sie pinnt allein das Go-Drift-Gate aus Sub-Slice 2; hier höchstens
  Manifest-Schema-Validierung, kein erfundener Konsum-Test.
- **Akzeptanz:** beide Drift-Richtungen rot bei Mutation (C#-Feld
  umbenannt → (a) rot; Serializer-Feld entfernt → Schema-Validierung (b)1
  rot); alle Konsum-Tests grün auf committetem Stand.
- **Verifikation:** `make test` (Adapter-Tests), `make gates`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 4 — EMS-Vektor-Konsum Go-seitig

- **Aufgabe:** Go-Check, der die **EMS-Datei** konsumiert — läuft in der
  **Repo-Root-Stage aus Sub-Slice 2** (`make field-vectors-check`),
  **nicht** in `make simulator-test`: dessen Build-Kontext ist das
  Modulverzeichnis und sieht `config/schema/vectors/` nicht (dieselbe
  Topologie-Konsequenz wie in Sub-Slice 2 fixiert). Jeder
  `command`-Payload dekodiert nach `model.Command`; asserts:
  `command_id` gesetzt, `reactive_power_kvar == nil` im Weglass-Case,
  Mode-/Source-Strings im dokumentierten Vokabular, Timestamps parsen
  (RFC 3339 mit `+00:00`-Offset — deckt die Offsets-Toleranz aus
  Entscheidung 4).
- **Akzeptanz:** Check grün; ein absichtlich um `command_id` beraubter
  Payload würde vom `CommandHandler`-Pfad verworfen (Negativ-Assert über
  die bestehende `handle`-Logik oder als dokumentierter Verweis auf
  `commands_test.go` — kein Duplikat-Test).
- **Verifikation:** `make field-vectors-check`, `make gates`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 5 — Bundle-Aufnahme + `field-contract-check`-Erweiterung

- **Aufgabe:** (a) `build_schema_bundle` nimmt `config/schema/vectors/`
  als `schema/vectors/` ins Bundle auf (additiv; Reproduzierbarkeits-
  Selbstcheck deckt die neuen Dateien automatisch, Bundle-`bundle.json`
  unverändert `v1`). (b) `scripts/field_contract_check.py` erweitert:
  beide Manifest-Dateien validieren gegen das Manifest-Schema; jeder
  `telemetry`-Payload zusätzlich gegen das Envelope-Schema **plus
  exakter Key-Set-Vergleich** gegen `$defs.telemetry.properties`
  (Python-Spiegel des C#-Asserts aus Sub-Slice 3(b)1 — fängt auch die
  Hinzufügen-Richtung, Review-Befund 2; CI-Redundanz, läuft ohne .NET);
  `golden-vector-manifest.schema.json` wird in `REQUIRED_SCHEMAS`
  aufgenommen (`field_contract_check.py:31` — etabliertes
  Presence-Muster, sonst fiele eine Löschung nur indirekt auf) und
  `REQUIRED_*`-Muster erzwingen die Existenz **beider** Vektor-Dateien
  (Analogie zu `REQUIRED_EXAMPLE_PATTERNS`).
- **Akzeptanz:** `make field-contract-check` grün; ein absichtlich
  invalides Manifest **und** ein gelöschtes Vektor-File brechen es;
  `make release-assets`-Dry-Run zeigt die Vektoren im Bundle,
  Reproduzierbarkeits-Check weiter grün.
- **Verifikation:** `make field-contract-check`, `make release-assets
  VERSION=<next>` (Dry-Run), `make gates`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

---

## Nicht-Ziele

- **Keine Modbus-Golden-Vectors** (ADR §5.4, eigener Schritt; das
  Register-Profil `modbus.hil-simulator.json` bleibt der Anker).
- **Keine SUT-Doku / config-only-Anleitung** (ADR §5.3, eigener Plan).
- **Kein `status`/`fault`-Envelope-Schema** — bleibt §5.1-Nicht-Ziel;
  die Vektoren pinnen ihre Form erstmals überhaupt (Go-Drift-Gate),
  ein hand-geschriebenes Schema würde die C#→Schema-Single-Source-Regel
  aus ADR §2 unterlaufen.
- **Keine kanonische Byte-Referenz** — bewusst nicht einmal non-normativ
  (Entscheidung 2); wer Bytes will, serialisiert das Payload-Objekt selbst.
- **Keine grid-gym-seitige Arbeit** — die Suite ist das Angebot; der
  Abnahme-Lauf gegen grid-gyms Push-Surface passiert im Schwesterprojekt.
- **Kein Command-Closed-Loop** (ADR §6, unverändert deferred).
- **Keine neue Versionslinie:** Vektoren tragen die Vertrags-Version
  (`v1`-Familie) und reisen mit dem App-Release; ein eigener
  SemVer-Strang entstünde erst mit dem ersten Breaking-Bump (ADR §2/§8).

---

## Liefergegenstände bei Aktivierung

1. `golden-vector-manifest.schema.json` + CHANGELOG-Eintrag.
2. `mqtt-golden-vectors.field.v1.json` (Go-gehoben) + Generator +
   `make field-vectors-check` (Repo-Root-Stage, Pflicht in `make gates`)
   + `make field-vectors-refresh`; `testdata`-Mapping-Kopie im
   Simulator-Modul auf Beispiel-Stand gehoben.
3. `mqtt-golden-vectors.ems.v1.json` (C#-gehoben) + C#-Drift-Gate +
   Feld-Vektor-Konsum-Tests (Envelope-Validierung **+ Key-Set-Gleichheit**,
   TelemetrySource-Harness, Echo-Roundtrip über beide Dateien).
4. Go-Konsum-Check der EMS-Vektoren (in der Repo-Root-Stage).
5. Bundle-Aufnahme + `field_contract_check.py`-Erweiterung
   (Manifest-Validierung, Envelope- + Key-Set-Check, `REQUIRED_SCHEMAS`
   + Vektor-Pflicht-Muster).
6. ADR 0013 Status-Klausel nach Abschluss:
   `Accepted — §5.1–§5.2 umgesetzt; §5.3–§5.4 offen`.

---

## Akzeptanzkriterien

- Alle fünf Topic-Payloads (`telemetry`, `status`, `fault` inkl.
  Suppression, `command` ±`reactive_power_kvar`, `command_ack`) sind als
  strukturell verglichene, aus Producer-Code gehobene Vektoren committet
  und publiziert (Bundle).
- Drift in **jeder** Richtung bricht ein Gate: `serializer.go` ↔
  Feld-Vektoren (`make field-vectors-check`), `MqttPayloads.cs` ↔
  EMS-Vektoren (C#), Feld-Vektoren ↔ Envelope-Schema (C# + Python) —
  **einschließlich der Hinzufügen-Richtung** via exakter
  Key-Set-Gleichheit (das Envelope-Schema allein, ohne
  `additionalProperties: false`, fängt nur Entfernungen) —, EMS-Vektoren
  ↔ Go-Decoder, und die `command`↔`command_ack`-Echo-Kopplung über
  beide Dateien (Korrelations-Harness).
- Die §5.1-Reichweiten-Lücke ist geschlossen: der echte
  Telemetrie-Produzent ist gegen das Envelope-Schema gedeckt.
- `make gates` bleibt grün; keine Architektur-/Boundary-Regel verletzt;
  kein Runtime-Verhaltensdelta (reine Test-/Artefakt-/Gate-Arbeit).

---

## Definition of Done (DoD)

- [x] Sub-Slice 1 — Manifest-Schema + `vectors/`-Ablage + CHANGELOG. (`5cdbf42`)
- [x] Sub-Slice 2 — Feld-Vektoren + Generator + Go-Drift-Gate + Refresh-Target. (`3f1f813`; Perms-Fix des `--output`-Exports separat)
- [x] Sub-Slice 3 — EMS-Vektoren + C#-Drift-Gate + Feld-Vektor-Konsum-Tests. (`0b54f84`)
- [x] Sub-Slice 4 — Go-Konsum-Test der EMS-Vektoren. (`802897a`; verschärft im Review-Fix)
- [x] Sub-Slice 5 — Bundle + `field-contract-check`-Erweiterung. (Sub-Slice-Commit + Review-Fix 1: Single-Pack-Pfad `scripts/pack-schema-bundle.sh` für `make release-assets` UND `release.yml`)
- [x] Alle Akzeptanzkriterien erfüllt; `make gates` voll grün (inkl. neuem Pflicht-Gate `field-vectors-check` in gates, ci und `build.yml`).
- [x] ADR 0013 Status-Klausel auf
      `Accepted — §5.1–§5.2 umgesetzt; §5.3–§5.4 offen` aktualisiert.

---

## Abschluss (2026-07-13)

Alle 5 Sub-Slices umgesetzt und in einer adversarischen Review-Runde (per
Agent) geprüft: **9 Befunde, 8 gefixt**, je einzeln verifiziert —
darunter zwei gewichtige: (1) der Release-Workflow packte das Schema-Bundle
über einen Inline-Mirror **ohne** `schema/vectors/` (behoben durch den
gemeinsamen Pack-Pfad `scripts/pack-schema-bundle.sh` für beide Aufrufer);
(2) `field-vectors-check` fehlte in der gehosteten CI (`build.yml`,
explizite `run_gate`-Liste). Dazu Härtungen: EMS-Konsum-Check pinnt
Key-Set (Reflection über `model.Command`-Tags) + Envelope-`required` +
Kernwerte; exakte Emissions-Menge je Input-Snapshot; Ack-Policy über
`mqtt.AcceptedEcho` geteilt statt hand-gespiegelt; Ack-Blindfleck
`dispatched_at` beidseitig geschlossen (C# + Python, generalisierte
Payload-Checks); Echo-Roundtrip pinnt Sink-Topic/Retained;
Wire-Vokabular einfach verankert (`model.WireModes/WireSources`).

**Befund 9 — dokumentiert akzeptiert:** die Rot-Demonstrationen der Gates
(oneOf-Ausschluss, Drift-Richtungen, Python-Fehlermodi) sind einmalig
durchgeführt und in den Commit-Messages festgehalten — plan-konform
(„einmalig demonstriert"); ein repo-persistenter Negativ-Fixture-Selbsttest
wäre eine mögliche §5.4-Beigabe, wird hier nicht stillschweigend nachgeschoben.

Betriebs-Nebenbefund: BuildKit-`--output` legt das Export-Verzeichnis mit
`0700` an — `field-vectors-refresh` stellt die Permissions seither selbst
wieder her (docs-check-Kompatibilität).

**Verifikations-Kommandos:** `make gates` (voll), `make field-vectors-check`
(nur Vektor-Gate), `make field-contract-check` (Schema-/Vektor-Konformität),
`make release-assets VERSION=<v>` (Bundle-Dry-Run inkl. Vektoren).
