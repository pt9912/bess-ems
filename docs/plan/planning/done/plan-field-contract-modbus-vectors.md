# Plan: Modbus-Golden-Vectors (ADR 0013 §5.4)

**Dokumenttyp:** Slice-Plan / done
**Status:** Abgeschlossen am 2026-07-13 — alle 5 Sub-Slices umgesetzt
(Plan aktiviert nach Owner-Review mit 7 Befunden + 1 Kleinigkeit; nach der
Implementierung eine Agent-Review-Runde mit 8 Befunden, alle gefixt —
siehe Abschluss). Die ADR-0013-Status-Klausel wurde mit diesem Slice auf
`Accepted — §5.1–§5.4 umgesetzt (§5 vollständig)` gesetzt (Stand
Abschluss-Zeitpunkt). Branch `impl-field-contract-5.4`.
**Datum:** 2026-07-13
**Quelle:** [`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md) §5.4
**Bezug:**
[`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md)
(§1 „Bewiesener Drift", §2 Provider-Posture, §3 struktureller Vergleich,
§5.4, §8 „kein gemeinsames Device-Vokabular"),
[`../done/plan-field-contract-golden-vectors.md`](../done/plan-field-contract-golden-vectors.md)
(§5.2 — Manifest-Schema, `vectors/`-Ablage, `fieldvectors`-Stage,
Python-Gate: die Infrastruktur, auf der dieser Slice aufsetzt),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(`LH-MODB-001/002/003`, `LH-RISK-002`)

---

## Ziel

Der letzte Umsetzungsschritt aus ADR 0013 §5: **Modbus-Golden-Vectors** —
pro ausgeliefertem Register-Profil das erwartete Draht-Bild
(Register-Wörter je Adresse/Tabelle) für feste Engineering-Werte,
mechanisch durch den echten Codec gehoben und strukturell verglichen.
Dazu wird der im ADR §1 **bewiesene Drift** im `bess-field-sim`
(`register_table`/`word_order` im Go-DTO nie nachgezogen) geschlossen
und mit den Vektoren dauerhaft gegated.

Nach Abschluss trägt ADR 0013 die Status-Klausel
`Accepted — §5.1–§5.4 umgesetzt` — §5 ist damit vollständig.

---

## Ausgangslage

Verifiziert 2026-07-13 (Stand nach §5.3-Merge):

- **Der .NET-Codec ist die vollständige, vertragskonforme
  Referenz-Implementierung** — in **beiden** Richtungen:
  `RegisterDecoder.Decode` (Wörter→Wert: `word_order` via `Combine32`,
  `raw * scale_factor`) und `RegisterDecoder.Encode` (Wert→Wörter:
  `Split32` + Skalierung) — `Encode` ist der **reale Schreibpfad** des
  `ModbusCommandSink` (Holding-Writes für Setpoints + `operating_mode`
  via `MapModeToValue`). `ModbusTelemetrySource` brancht auf
  `register_table` (Holding→FC03, Input→FC04; RM-M2-HIL-02).
  Loader-Defaults bei abwesenden Feldern: `Holding` + `HighLow`
  (`ModbusRegisterMapping.cs:25,29`).
- **Der bewiesene Drift lebt noch** (ADR §1): `model.ModbusRegister`
  (Go, `bess-field-sim`) kennt weder `register_table` noch
  `word_order`; `EncodeSnapshot` schreibt Multi-Word-Typen stur
  high-word-first; der Server bedient **nur** einen
  Holding-Register-Raum (FC03 + FC06/16-Writes, kein FC04, kein
  Input-Raum). Unsichtbar, weil `modbus.simulator.json` nur
  Single-Word-Typen (int16/uint16) mit Default-Tabelle nutzt.
- **Drei ausgelieferte Profile — zwei mit Produzentenpfad, eines bewusst
  ausgeschlossen** (Implementierungs-Review-Befund 3):
  `modbus.sunspec-simulator.json` erhält **keine** Vektoren — es spricht
  ein fremdes Vokabular (`der_*`, `battery_soc` statt der
  bess-ems-Feldnamen), nutzt `unit_id_discovery: "sunspec"` (der reale
  Sink verlangt eine statische Unit-ID) und durchgängig
  `auth_required: "network"` (der Sink verweigert) — es existiert
  **kein** in-repo-Produzentenpfad, weder Encode-Lifting mit
  Vertrags-Semantik noch Sink-Capture; erfundene Wert-Tabellen ohne
  Codec-Gate wären genau das Anti-Muster dieser Suite. Konsequenz: die
  `high_low`-Multi-Word-Deckung (das Sunspec-Profil trägt
  `uint32`/`int32` mit Default `high_low`) bleibt auf Unit-Ebene
  (`RegisterDecoderTests`); das Python-Gate lehnt unbekannte
  Vektor-Manifeste ab, ein Sunspec-Manifest ist also nur **bewusst**
  (mit eigenem Gate) nachrüstbar. Die zwei **gedeckten** Profile:
  - `modbus.simulator.json` (batterie-seitig: 9 Mess- + 2
    Schreib-Register, int16/uint16, Defaults) — Produzent ist
    `bess-field-sim` (M1-Pflichtpfad, Integrations-Roundtrip).
  - `modbus.hil-simulator.json` (grid-seitig: `grid_voltage_pu`,
    `grid_frequency_hz`, `grid_current_ka` neben Batterie-Namen;
    float32, `low_high`, `input`+`holding`) — Produzent ist der
    **externe** HIL-Simulator (optionales `test-hil-modbus`-Gate);
    **kein** in-repo-Produzent.
- **Consumer-Eigenheiten des .NET-Telemetrie-Pfads** (für die
  Vektor-Semantik relevant, nicht Gegenstand dieses Slices):
  `ModbusTelemetrySource` liest **alle** Read-Register des Profils in
  ein Name→Wert-Dictionary, bindet aber nur die Batterie-Feldnamen an
  `BatteryTelemetry` (`BuildTelemetry`; grid_*-Werte werden gelesen und
  verworfen, fehlende Namen defaulten auf 0) und hardcodiert
  `FaultStatus: "ok"` — der Modbus-Pfad transportiert heute keinen
  Fault-Text.
- **§5.2-Infrastruktur trägt:** Manifest-Schema mit dokumentiertem
  Erweiterungspunkt (`contract`-Enum: „Modbus vectors (ADR 0013 5.4)
  will extend this enum"), `config/schema/vectors/`-Ablage,
  `fieldvectors`-Repo-Root-Stage, Python-Gate mit
  `REQUIRED_VECTOR_MANIFESTS`, Bundle-Pack globbt `vectors/*.json`
  (neue Dateien reisen automatisch mit).
- **Konsumenten-Kontext (Fähigkeit, keine Fremd-IDs):** die
  Schwester-Simulationsplattform besitzt eine
  Pull-Device-Server-Surface (Modbus, read + inbound-write) mit
  **konfigurierbarem** Register-Map — die Vektoren sind das
  Abnahme-Geschirr, gegen das ein bess-ems-konformes Register-Serving
  dort gebaut werden kann; ihre Battery-Metriken decken den
  bess-ems-Feldsatz heute nur teilweise (power/soc/temperature) —
  dieselbe Deckungs-Entscheidung wie beim MQTT-CR, dort zu treffen.

---

## Entscheidungen

1. **Autorität: durchgängig EMS/Vertrag** (anders als MQTT §5.2, wo das
   Feld De-facto-Produzent der Telemetrie ist): das Modbus-Draht-Bild
   ist vollständig durch Profil + Schema-Semantik bestimmt; die
   in-repo-Referenz ist der C#-Codec (`RegisterDecoder`, real benutzt
   von Source **und** Sink). Gehoben wird durch ihn. **Eine
   Manifest-Datei je Profil** (`authority: "ems"`):
   `modbus-golden-vectors.simulator.v1.json` und
   `modbus-golden-vectors.hil-simulator.v1.json`.
2. **Manifest-Schema-Erweiterung** (der in §5.2 dokumentierte Punkt):
   `contract`-Enum + `"modbus"`; neuer Case-Typ (`$defs`) für
   Modbus-Cases — `name`, `register` (Profil-Registername), `direction`
   (`read` | `write`), `register_table`, `address`, `type`,
   `word_order`, `scale_factor`, `value` (Engineering-Wert, Zahl) und
   `words` (Array uint16, das normative Draht-Bild). Diskriminierung
   über das manifest-weite `contract`-Feld (if/then), damit
   MQTT-Manifeste unverändert validieren.
3. **Exaktheit gilt auf dem Roh-Wert, nicht dem Engineering-Wert**
   (Plan-Review-Befund 1): maßgeblich ist `raw = value / scale_factor` —
   für float32-Typen muss **raw** float32-exakt (dyadisch) sein, für
   int-Typen ganzzahlig **innerhalb Typ- und Range-Grenzen des
   Profil-Registers**. Ein float32-exakter Engineering-Wert genügt
   nicht: 50.0 kW bei `scale_factor: 1000` ergibt raw 0.05
   (nicht dyadisch, `Decode(Encode(50)) ≠ 50`); 500 kW (raw 0.5)
   funktioniert. Nur so gilt `Decode(Encode(v)) == v` **exakt** und der
   Vergleich bleibt strukturell (keine Toleranzen im Vertrag). Je
   Profil eine Wert-Tabelle, die **jedes** Register des Profils abdeckt
   (auch grid_*) und dessen `range` respektiert (die MQTT-Werte
   −250.5/−313.1 liegen z. B. außerhalb der ±100-Range des
   Simulator-Profils). **Suite-Kohärenz mit den MQTT-§5.2-Snapshots
   gilt deshalb nur für scale-1-Register** (soc, soh, temperature,
   grid_*), nicht pauschal.
4. **Der bewiesene Drift wird geschlossen, nicht nur dokumentiert:**
   `bess-field-sim` erhält `register_table`/`word_order` im DTO, der
   Encoder honoriert die Wortreihenfolge, der Server bekommt einen
   Input-Register-Raum + FC04-Handler. Defaults (`holding`/`high_low`)
   bleiben — das Integrations-Fixture und `modbus.simulator.json`
   verhalten sich byte-identisch. Danach gated ein Konformanz-Check
   (in der `fieldvectors`-Stage) den Sim dauerhaft gegen die Vektoren.
   **Abgrenzung zur ADR-§2-Deferral:** deferred bleibt die
   Codegen-Migration (Ende der Drift-**Klasse**); dies ist der
   test-gepinnte Fix der bekannten **Instanz**, mit den Vektoren als
   Wächter — ohne ihn wären Vektoren fürs HIL-Profil im Repo
   produzentenlos und der ADR-§1-Anlassfall bliebe offen.

---

## Sub-Slices

### 1 — Manifest-Schema-Erweiterung + CHANGELOG

- **Aufgabe:** `golden-vector-manifest.schema.json` gemäß Entscheidung
  2 erweitern (Modbus-Case-Typ, if/then auf `contract`); Eintrag in
  `config/schema/CHANGELOG.md` (additiv, Vertrags-Major bleibt `v1`).
  MQTT-Manifeste müssen unverändert validieren; ein Modbus-Case mit
  MQTT-Feldern (und umgekehrt) muss abgelehnt werden. **Auch die
  MQTT-spezifischen Beschreibungstexte nachziehen** (Review-Befund 5):
  die Top-Level-`description` („…for the MQTT field contract… one
  manifest per authority; each side regenerates only its own file")
  und die `authority`-Beschreibung stimmen nach der Erweiterung nicht
  mehr — Modbus: eine Datei **pro Profil**, beide `authority: "ems"`,
  beide C#-regeneriert.
- **Akzeptanz:** `make field-contract-check` grün; Negativ-Formen
  einmalig demonstriert (falscher Case-Typ je Contract, fehlende
  `words`).
- **Verifikation:** `make field-contract-check`, `make docs-check`.
- **Release-Feld:** entfällt — Rückverfolgbarkeit über den
  CHANGELOG-Eintrag des nächsten Releases (Muster §5.3).

### 2 — C#-Lifting: zwei Profil-Manifeste + Drift-Gate + Round-trip

- **Aufgabe:** Generator im Modbus-Test-Projekt (Muster
  `GoldenVectors.cs`): hebt je Profil und Register die `words` durch
  **`RegisterDecoder.Encode`** (Read-Richtung; Wert aus der
  Profil-Wert-Tabelle) bzw. durch den **realen `ModbusCommandSink`**
  (Write-Richtung: Fake-Client fängt die Holding-Writes eines
  `BatteryCommand`; Werte innerhalb der `SampleAsset`-Limits, keine
  Clamping-Verfälschung). **Die Write-Case-Menge ist pro Profil
  asymmetrisch:** Simulator-Profil = Active-Setpoint +
  `operating_mode` (via `MapModeToValue`), **kein** Q-Register — der
  Generator kommandiert dort `ReactivePowerKvar = 0`/null, sonst
  entsteht ein Q-Drop ohne Draht-Effekt; HIL-Profil = beide Setpoints,
  **kein** Mode-Register. Committet beide Manifeste. Drift-Test
  (Regenerat ↔ committet, strukturell) + **Decode-Round-trip für Read-
  UND Write-Cases** (Review-Befund 2): `RegisterDecoder.Decode(words)
  == value` auch für die vom Sink gefangenen Write-Wörter — `Encode`
  castet trunkierend und die Skalen-Division ist nicht exakt
  (`0.3 / 0.1 → 2` statt 3); ohne den Write-Round-trip produzierte ein
  ungünstiger Wert kommentarlos ein in sich falsches Manifest, das
  kein Gate bemerkt (Drift-Test vergleicht nur Regenerat↔committet —
  beide gleich falsch). Deckung ehrlich benannt (Plan-Review-Befund 3,
  präzisiert nach Implementierungs-Review-Befund 3): die Profil-Vektoren
  decken für Multi-Word-Typen nur den **`low_high`**-Pfad — kein
  **gedecktes** Profil hat high_low-Multi-Word; das bewusst
  ausgeschlossene Sunspec-Profil hätte es (Ausgangslage); der
  `high_low`-32-bit-Pfad bleibt auf Unit-Ebene gedeckt
  (`RegisterDecoderTests`).
- **Akzeptanz:** beide Manifeste validieren gegen das erweiterte
  Schema; Drift-Test rot bei mutiertem Codec/Manifest (einmalig
  demonstriert); Round-trip grün für **alle** Cases beider Profile
  (Read + Write).
- **Verifikation:** `make test`, `make field-contract-check`,
  `make gates`.
- **Release-Feld:** entfällt (wie Sub-Slice 1).

### 3 — Go: Drift-Fix + Konformanz-Check in der `fieldvectors`-Stage

- **Aufgabe (a) Drift-Fix:** `model.ModbusRegister` + Loader-Validierung
  erhalten `register_table`/`word_order` (Defaults `holding`/`high_low`
  — Verhalten für Bestandsprofile byte-identisch); `EncodeSnapshot`
  honoriert `word_order`; der Server führt einen **zweiten**
  Register-Raum (`input`) + FC04-Handler und legt Register gemäß
  `register_table` ab. Bestehende Simulator-Tests + Integrations-
  Roundtrip bleiben grün; neue Unit-Tests decken low_high/input.
- **Aufgabe (b) Konformanz-Check:** `fieldvectors -mode check` prüft
  zusätzlich: für jeden Read-Case beider Modbus-Manifeste, dessen
  Register-Name im Sim-Feldsatz liegt (`valueFor`-Menge —
  grid_*-Register haben im Sim **bewusst** keinen Wert und bleiben
  Vertrag für externe Produzenten), muss `EncodeSnapshot` exakt die
  Vektor-`words` liefern. **Quelle des Snapshots sind die
  `value`-Felder der committeten Manifeste selbst** (Review-Befund 6)
  — eine erneut hand-gelistete Go-Wert-Tabelle wäre exakt der
  Spiegel-Drift, den ADR 0013 beendet; dazu gehört die Rückabbildung
  der zwei nicht-numerischen Snapshot-Felder (`available` 1↔`true`,
  `fault_status` 0↔`"ok"`, `encoder.go`-Semantik). Damit ist der
  ADR-§1-Anlassfall dauerhaft gegated: ein erneut fallengelassenes
  Feld bricht `make field-vectors-check`.
- **Akzeptanz:** Konformanz-Check grün für beide Profile
  (HIL-Profil: nur Batterie-Register); rot demonstriert durch
  temporäres Zurückdrehen des word_order-Honorierens; alle
  `simulator-*`-Gates + `test-integration` grün.
- **Verifikation:** `make field-vectors-check`, `make simulator-test`,
  `make test-integration`, `make gates`.
- **Release-Feld:** entfällt (wie Sub-Slice 1).

### 4 — Python-Gate + Bundle

- **Aufgabe:** `field_contract_check.py`: beide Modbus-Manifeste in
  `REQUIRED_VECTOR_MANIFESTS`; Validierung gegen das erweiterte
  Manifest-Schema läuft über den bestehenden Pfad; zusätzlich je
  Read/Write-Case Konsistenz-Pins gegen das **Mapping-Profil** (Name
  existiert im Profil, `address`/`type`/`register_table`/`word_order`/
  `scale_factor` stimmen mit dem Profil überein — fängt Vektor↔Profil-
  Drift ohne .NET). **Der Profil-Vergleich muss die Loader-Defaults
  nachbilden** (Review-Befund 7): das Simulator-Profil lässt
  `register_table`/`word_order` weg, die Manifest-Cases tragen sie
  explizit — ohne Default-Auflösung (`holding`/`high_low`) wären alle
  11 Simulator-Register false-negative. Bundle-Dry-Run zeigt beide
  Dateien (Pack globbt bereits).
- **Akzeptanz:** Gate grün; Profil-Drift (mutierte Adresse im Vektor)
  bricht es; `make release-assets`-Dry-Run reproduzierbar mit 4
  Vektor-Manifesten.
- **Verifikation:** `make field-contract-check`,
  `make release-assets VERSION=<next>` (Dry-Run).
- **Release-Feld:** entfällt (wie Sub-Slice 1).

### 5 — Finalisierung

- **Aufgabe:** ADR 0013 Status-Klausel
  `Accepted — §5.1–§5.4 umgesetzt` an **beiden** Stellen (Status-Zeile
  + Header-Prosa); Plan nach `done/`; Reviews (Agent + Owner) nach dem
  etablierten Muster; volle Gates. **Scope-Note-Abgleich
  (Review-Befund 4):** `note-v2.2.0-scope.md` reserviert v2.2.0 für
  das Internal-Refinement-Theme; §5.4 liefert additive Bundle-Inhalte,
  die im nächsten Release landen — zieht §5.4 zuerst, wäre das die
  **dritte** Umwidmung. Statt weiter nachzunummerieren wird die Notiz
  hier **versions-agnostisch umbenannt**
  (`note-internal-refinement-scope.md`, „nächste freie Minor" statt
  fixer Nummer) — die Option, die die Notiz selbst vorschlägt.
- **Akzeptanz:** DoD komplett; Abschluss dokumentiert alle
  Evidenz-Wellen; Scope-Note versions-agnostisch, kein Widerspruch
  zwischen `next/`-Bestand und Release-Realität.
- **Verifikation:** `make gates` (ggf. in Chunks), `make docs-check`.

---

## Nicht-Ziele

- **Keine** Codegen-Migration des Simulators (ADR §2/§7: Ende der
  Drift-**Klasse** bleibt trigger-deferred; hier nur der gepinnte Fix
  der bekannten Instanz).
- **Kein** Grid-Register-Serving im `bess-field-sim` (`valueFor` bleibt
  der Batterie-Feldsatz; grid_*-Vektoren sind Vertrag für externe
  Produzenten — HIL-Simulator, Schwester-Plattform).
- **Keine** Änderung der Consumer-Quirks des .NET-Telemetrie-Pfads
  (hardcodiertes `FaultStatus:"ok"`, ungebundene grid_*-Werte) — als
  Ausgangslage dokumentiert; eine Änderung wäre Runtime-Delta ohne
  §5.4-Anlass.
- **Keine** OPC-UA-Vektoren (kein ADR-Schritt; trigger-basiert).
- **Keine** neuen Mapping-Profile; keine grid-gym-seitige Arbeit.
- **Kein** `test-hil-modbus`-Pflichtlauf (bleibt optionales Gate; die
  Vektoren ersetzen den HIL-Roundtrip nicht, sie geben ihm das
  normative Draht-Bild).

---

## Liefergegenstände bei Aktivierung

1. Erweitertes `golden-vector-manifest.schema.json` (+ CHANGELOG).
2. `config/schema/vectors/modbus-golden-vectors.{simulator,hil-simulator}.v1.json`
   — C#-gehoben (Codec + realer Sink), Drift-Gate + Decode-Round-trip.
3. `bess-field-sim`: `register_table`/`word_order`-Fix (DTO, Encoder,
   Input-Raum + FC04) + Konformanz-Check in `field-vectors-check`.
4. Python-Gate-Erweiterung (Pflicht-Manifeste, Profil-Konsistenz-Pins).
5. ADR 0013: `Accepted — §5.1–§5.4 umgesetzt` (beide Stellen).

---

## Akzeptanzkriterien

- Beide Profile tragen strukturell verglichene, durch den echten Codec
  bzw. den echten Sink gehobene Draht-Bild-Vektoren (Read + Write),
  publiziert im Schema-Bundle.
- Drift bricht in jeder Richtung ein Gate: Codec ↔ Vektoren (C#),
  Vektoren ↔ Profil (Python), Sim ↔ Vektoren (Go-Konformanz,
  Batterie-Register beider Profile) — der ADR-§1-Anlassfall
  (`register_table`/`word_order`) ist geschlossen **und** gegated.
- `Decode(words) == value` exakt für **alle** Cases (Read + Write;
  Roh-Wert-exakte Wert-Tabellen gemäß Entscheidung 3); Multi-Word-
  Deckung der Profil-Vektoren ist `low_high` (high_low bleibt
  Unit-gedeckt, `RegisterDecoderTests`).
- Bestandsverhalten unverändert: Integrations-Roundtrip und alle
  Gates grün; Defaults `holding`/`high_low` byte-identisch.

---

## Definition of Done (DoD)

Die Haken zertifizieren den **Branch-Tip nach der Review-Runde**, nicht
die Sub-Slice-Commits (§5.3-Lektion: das Done-Record entsteht **nach**
dem Review).

- [x] Sub-Slice 1 — Manifest-Schema-Erweiterung + CHANGELOG (`c7605c5`;
      Negativ-Formen demonstriert; `authority: ems`-Pin für Modbus im
      Review-Fix nachgezogen).
- [x] Sub-Slice 2 — zwei Profil-Manifeste + C#-Drift-Gate + Round-trip
      Read **und** Write (`198d50d`; Rot-Demo beidseitig;
      string-Register-Filter im Review-Fix).
- [x] Sub-Slice 3 — Sim-Drift-Fix + Go-Konformanz-Check (`425fb27`;
      Rot-Demo „encoder produced 15744, vector pins 0"; die
      **Serving-Hälfte** — FC04/Space-Unabhängigkeit — erst im
      Review-Fix gegated).
- [x] Sub-Slice 4 — Python-Gate + Bundle-Dry-Run (`ad0ba82`; Range- und
      Wortzahl-Pins + Unbekannt-Manifest-Ablehnung im Review-Fix).
- [x] Sub-Slice 5 — ADR-Klausel (beide Stellen) + drei
      Stale-Claim-Marker, Scope-Note versions-agnostisch umbenannt,
      Sunspec-Ausschluss dokumentiert, Plan nach `done/`, alle
      Pflicht-Gates einzeln grün auf dem Endstand.

---

## Abschluss (2026-07-13)

Alle 5 Sub-Slices umgesetzt; Evidenz in **zwei Wellen** (Sub-Slice-
Commits + Agent-Review). Das Agent-Review fand **8 Befunde, alle
gefixt** (`1832a0f`) — die drei gewichtigsten: (1) die
**Serving-Hälfte** des bewiesenen Drifts (FC04/Input-Raum) war von
keinem Pflicht-Gate gedeckt — ein vertauschtes `applyWords` wäre grün
geblieben; jetzt pinnt ein Server-Test die Space-Unabhängigkeit mit der
HIL-typischen Adress-Kollision (input@1 + holding@1) inkl.
Write-Leak-Check; (2) die **Range-Hälfte** von Entscheidung 3 war reine
Konvention — jetzt Python-Pin; (3) ein **drittes ausgeliefertes
Profil** (`modbus.sunspec-simulator.json`) war schlicht übersehen — der
Ausschluss ist jetzt begründet dokumentiert (kein in-repo-
Produzentenpfad: fremdes Vokabular, sunspec-Discovery, network-Auth),
die davon abhängige high_low-Multi-Word-Deckungsaussage korrigiert, und
das Python-Gate lehnt unbekannte Vektor-Manifeste ab (publiziert-aber-
ungegated ist unmöglich). Dazu: drei Stale-Claim-Marker im ADR
(§1/§5/§7, `Instanz geschlossen, Klasse Codegen-deferred`-Muster),
`authority: ems`-Pin, Wortzahl-Pin, string-Register-Pfad,
Diagnose-/Validierungs-Nits im Konformanz-Check.

**Verifikations-Kommandos:** `make field-vectors-check` (Vektor- +
Konformanz-Gate), `make field-contract-check` (Schema/Profil-Pins),
`make test` (C#-Drift + Round-trip), `make simulator-test`
(FC04-/Encoder-Tests), `make release-assets VERSION=<v>` (Bundle mit 4
Manifesten), `make gates` (voll; auf langsamen Maschinen in Chunks).