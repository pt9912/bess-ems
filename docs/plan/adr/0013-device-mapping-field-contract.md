# ADR 0013 — Geräte-Mappings als publizierter, versionierter Feldvertrag (SUT-Kopplung)

**Status:** Accepted — §5.1–§5.4 umgesetzt (§5 vollständig). Design-first,
Owner-Sign-off 2026-07-12, gezogen aus einem SUT-Kopplungs-Smoke gegen das
Schwesterprojekt `grid-gym`. bess-ems macht seine Geräte-Mapping-Schemas
(`config/schema/*.schema.json`) von einer internen Konfigurations-Detailebene zu
einem **publizierten, versionierten Feldvertrag**, gegen den Fremdsimulatoren
**generieren statt spiegeln**. Konkretisiert
[`LH-CONF-002`](../../../spec/lastenheft.md) (Versionierte Gerätemappings) von
„intern versioniert" auf „extern konsumierbar". Gegenstück zu `grid-gym`s
geplanter Feld-Server-Surface, deren Plan die bess-ems-Seite explizit diesem Repo
zuweist. Die Umsetzung erfolgte als Folgearbeit in `docs/plan/planning/` (§5)
und ist **vollständig**: §5.1 (Vertrags-Bundle + Envelope-Schema +
Versionierung + `maxAge`-Stellschraube), §5.2 (Golden-Vector-Suite MQTT),
§5.3 (SUT-Doku + config-only-Pfad + Compose-SUT-Variante) und §5.4
(Modbus-Golden-Vectors je Profil + Schließung des in §1 bewiesenen
`register_table`/`word_order`-Drifts im Simulator, dauerhaft gegated).
**Datum:** 2026-07-12
**Bezug:**

- [`../../../spec/lastenheft.md`](../../../spec/lastenheft.md) — `LH-CONF-002`
  (Versionierte Gerätemappings / Device Mapping Design, `DeviceMappingRepository`,
  ✓ M1), `LH-MQTT-001/002/003/005` (Telemetrie-Empfang, Command-Publishing,
  Topic-Konvention, Command-Ack), `LH-MODB-001/002/003` (Modbus lesen/schreiben/
  Registermapping-über-Konfiguration), `LH-SAFE-007` (Schreibbegrenzung vor
  Feldkommunikation — Anker für den **ausgegliederten** Command-Pfad, §6),
  `LH-RISK-002` (Herstellerabhängige Protokollmappings — der Drift-Risiko-Anker),
  MQTT-Broker-Security (Plaintext lokal, TLS/Auth-Profil) ist in
  `docs/user/quality.md` §2.2.1 verankert (RM-M4-06-FUP-F04) — die frühere
  `LH-PROT-002`-Zuschreibung hier war eine Fehlzitation (`LH-PROT-002` =
  Protokollfehler → Quality-Flag; korrigiert 2026-07-13 mit §5.3).
- [`0005-optimization-core-sidecar-transport.md`](0005-optimization-core-sidecar-transport.md)
  — Präzedenz für das **Muster** „versioniertes Vertrags-Artefakt an einer
  Prozessgrenze". **Nur das Muster** wird übernommen, nicht der Mechanismus: ADR
  0005 handelt Kompatibilität zur **Laufzeit** aus (gRPC Version-RPC); ein
  dateibasiertes, zur Codegen-Zeit konsumiertes Schema hat keinen solchen Kanal
  (§2, Achsen Versionierung + Breaking-Bump-Rollout).
- [`0012-northbound-export-adapter-structure.md`](0012-northbound-export-adapter-structure.md)
  / [`0009-api-service-extraction-criteria.md`](0009-api-service-extraction-criteria.md)
  / [`0011-application-monolithic-module.md`](0011-application-monolithic-module.md)
  — dasselbe **deferred-mit-Trigger**-Muster, hier für den Command-Closed-Loop (§6).
- `grid-gym` (Schwesterprojekt, GitHub) — die **Gegenrolle**: eine
  **Push-Field-Publish-Surface** (MQTT-Broker-Publish) und eine
  **Pull-Device-Server-Surface** (Modbus-Slave/Serve), die Inbound-Write/Command-
  Rückrichtung in grid-gyms Plan zunächst ausgegliedert. Konkrete grid-gym-interne
  Bezeichner (ADR-Nummer, Slice-Nummern, Port-/Typnamen) sind **Stand
  grid-gym-Plan** und dort volatil; dieses ADR referenziert die Gegenrolle über
  **Fähigkeit**, nicht über Fremd-Repo-IDs (§8).

---

## 1. Kontext

**Topologie (verifiziert, SUT-Smoke 2026-07-12):** `grid-gym` ist als
Feld-/SUT-Umgebung für ein produktives EMS wie bess-ems gedacht (READMEs beider
Repos), seine Protokolladapter sind heute aber **client/master-seitig** — dieselbe
Rolle wie bess-ems. grid-gyms Plan ergänzt die fehlende Server-/Push-Gegenrolle
(eine Push-Field-Publish- und eine Pull-Device-Server-Surface). Damit wird die
Kopplung real — und ihr **einziges Risiko ist der Payload-/Topic-Vertrag**: zwei
Encodings desselben Vertrags, driftanfällig.

**Ist-Stand des Vertrags in bess-ems:**

- Die Mapping-Schemas (`config/schema/{device-point,modbus-mapping,mqtt-mapping,
  opcua-mapping}.schema.json`) sind sauber, `$id`-adressierbar unter
  `https://bess-ems.io/schema/` (als Identität konzipiert, keine ausgelieferte
  Auflösungs-Quelle) und teilen eine `device-point`-Basis per `$ref`. Aber:
  (a) **kein** Release-/Packaging-Schritt emittiert sie als Artefakt; sie reisen
  nur im Source-Tarball und im Docker-Image mit; (b) Versionierung ist
  **inkonsistent** — nur `opcua-mapping` trägt `schema_version`, Modbus/MQTT
  keins; (c) `profile_name` ist ein Label, kein Kompatibilitäts-Pin.
- **Deckungslücke MQTT-Payload (wichtig):** `mqtt-mapping.schema.json` beschreibt
  nur die **Topic-Ebene** (`topic`, `direction`, `payload_format:"json"`,
  `retained`, `auth_required` + `device-point`-Basis). Der **innere
  Telemetrie-Payload** — das breite Snapshot-Objekt — ist **in keinem Schema**;
  `payload_format:"json"` ist opak. Seine Form lebt implizit in
  `src/adapters/driven/BatteryEms.Adapters.Mqtt/MqttPayloads.cs`
  (`TelemetrySnapshotPayload`) und in `bess-field-sim`s Go-DTOs. **Für Modbus**
  ist der Feldsatz dagegen im Schema (jedes Register *ist* ein Punkt mit
  `name`+`address`+`type`). Publizieren-der-Schemas liefert also den **Modbus**-
  Feldvertrag, aber **nicht** den MQTT-Payload-Vertrag — genau die Stelle, die
  driftet (§2 Achse „Payload-Envelope").
- **Bewiesener Drift:** `simulators/bess-field-sim/internal/model/modbus.go`
  spiegelte `modbus-mapping.schema.json` von Hand und hatte `register_table`
  + `word_order` **nie** nachgezogen — genau die Fehlerklasse, die
  `LH-RISK-002` benennt. **Bereits mit Live-Trigger:** das mitgelieferte Profil
  `config/examples/adapters/modbus.hil-simulator.json` setzt `word_order:"low_high"`
  auf allen 11 Registern (9 Mess-Register `register_table:"input"`/FC04 + 2
  Setpoint-Register `holding`); der Go-DTO, der beide Felder wegwarf, hätte dieses
  **reale, im Repo liegende** Profil aktiv falsch bedient (die 9 Mess-Register als
  FC03/high_low statt FC04/low_high). Ein Single-Source-Vertrag verhindert das.
  **Instanz mit §5.4 geschlossen (2026-07-13):** DTO/Encoder/Server honorieren
  beide Felder; die Modbus-Golden-Vectors gaten sie dauerhaft
  (`make field-vectors-check`). Die Drift-**Klasse** bleibt Codegen-deferred (§7).

**Smoke-Evidenz (der De-facto-MQTT-Feldvertrag, den bess-ems heute konsumiert):**
`deploy/compose.yml` (bess-ems MQTT-only ← mosquitto ← `bess-field-sim`) lief
end-to-end. Tatsächlich auf dem Draht (Wire-Capture):

```
battery/{assetId}/telemetry  (Feld→EMS, retained; von bess-field-sim)
  {"offset_millis":0,"soc_percent":60.5,"soh_percent":99,"active_power_kw":0,
   "reactive_power_kvar":0,"dc_voltage":800,"dc_current":0,
   "temperature_celsius":22,"available":true,"fault_status":"ok"}
```

Genau geflossen sind **vier** Topics: `telemetry` + `status` (retained, vom Feld),
`command` (von **bess-ems** publiziert), `command/ack` (vom **Command-Handler** des
`bess-field-sim`, `internal/mqtt/commands.go` — **nicht** aus dem Telemetrie-Pfad
`serializer.go`, der `command`/`command_ack` überspringt). `fault` **nicht**, weil
`fault_status="ok"` unterdrückt wird (`serializer.go:69`). Der `command/ack`-
Roundtrip ist damit eine Fähigkeit des `bess-field-sim`, die grid-gyms Feld-Surface
in der read-serving-Phase **nicht** hat (§6) — der Smoke ist breiter als die
grid-gym-Kopplung sein wird.

Das Telemetrie-Objekt ist **breit** (eine Nachricht/Tick/Gerät), ohne Wall-Clock —
bess-ems (`MqttTelemetrySource`) stempelt die Frische **beim Empfang**. grid-gyms
interne Telemetrie ist **schmal** (ein Metrik/Wert/Einheit pro Punkt). Zwei
Interop-Befunde:

1. **Kadenz.** Ein einzelner (retained) Publish trieb bess-ems in Dauer-Safe-Stop
   (`decision=snapshot-unusable reason=snapshot-aged-Ns`). Weil Frische
   empfangs-basiert ist, **muss** das Feld Telemetrie **laufend** innerhalb des
   Freshness-Fensters publizieren (Default **10 s**; seit §5.1
   konfigurierbar via `Bess:SnapshotMaxAge`; §8).
2. **Form.** grid-gyms Push-Surface reicht je schmalem Punkt weiter; bess-ems
   braucht **ein breites Objekt/Tick**. Der Feldadapter muss einen Tick-Frame
   aggregieren + `metric`→Feldname + `device_id`↔`asset_id` mappen.

Diese Erwartungen sind heute nirgends als konsumierbarer Vertrag festgehalten.

---

## 2. Entscheidung

bess-ems publiziert seine Geräte-Mappings als versionierten Feldvertrag und ist
dessen **Provider**. Fremdsimulatoren generieren dagegen.

| Achse | Entscheidung | Pin / Trigger |
| ----- | ------------ | -------------- |
| Vertrags-Publikation | Das Schema-Set (`device-point` + `modbus-/mqtt-/opcua-mapping`) wird als **versioniertes, konsumierbares Bundle** ausgeliefert — als Release-Asset neben Helm-Chart/Tarball/SBOM (`.github/workflows/release.yml`), nicht nur im Docker-Image/Source-Tarball. | Neuer Release-Asset-Eintrag; Trigger aktiv: erster externer Konsument (grid-gyms Push-Surface). |
| Payload-Envelope (schließt §1-Lücke) | Der **MQTT-Telemetrie-Envelope** (breiter Snapshot-Feldsatz) wird als eigenes Schema in `config/schema/` aufgenommen. **Source-of-Truth = C#→Schema:** das Envelope-Schema wird aus `MqttPayloads.cs` **generiert** (JsonSchema.Net-Familie, in-repo für Validierung schon genutzt) + ein **CI-Drift-Check** difft Generat gegen committetes Schema. Schema→C# verworfen (würde getesteten, live konsumierten Code für null Gewinn umschreiben und die heutige Realität invertieren). | CI-Drift-Check C#↔Schema; Envelope wird Teil des publizierten Bundles. |
| Versionierungs-Policy | `schema_version` wird auf **allen drei** Protokoll-Mappings Pflicht (heute nur OPC-UA); Kompatibilität = **`schema_version` (SemVer) + CI-Drift-Gate**, **keine** Runtime-Negotiation (dateibasiert, kein Handshake-Kanal — anders als ADR 0005). `profile_name` bleibt **Label**. | CI-Drift-Gate auf `config/schema/` (heute deckt `make schema-*` nur die Postgres-DDL). |
| Breaking-Bump-Rollout | Für den handshake-losen Dateivertrag: (a) **`min_supported`**-Kompatibilitätsband im Bundle → Konsument erkennt Inkompatibilität zur Codegen-/Build-Zeit; (b) Breaking-Major wird **N-1-parallel** neben dem Vorgänger-Major für ein Deprecation-Fenster publiziert; (c) **Schema-CHANGELOG** im Bundle; (d) Konsument pinnt `schema_version` und **fail-closed** außerhalb seines Bandes. | Fensterlänge + exaktes `min_supported`-Feldschema = operativ (§8), trigger-basiert bei erstem Breaking-Bump. |
| Provider-Posture / Anti-Drift | bess-ems ist Vertragsgeber; `bess-field-sim` **und** grid-gym generieren gegen das Bundle statt zu spiegeln. `register_table`/`word_order`-Drift ist der Beleg. | Trigger: `bess-field-sim` → Codegen; Aufnahme in grid-gyms Feld-Surface. |
| Normative Payload-Form | **Feld-normativ** (§3): Feldnamen/Präsenz/Typen/Null-Weglassung verbindlich, **nicht** Reihenfolge/Whitespace (Konsument ist ein JSON-Parser). Die Feldmenge wird **mechanisch aus `serializer.go`/`MqttPayloads.cs` abgeleitet**; Golden-Vektoren vergleichen **strukturell**, nicht byte-gleich (§3). Topic-Schema `battery/{assetId}/{telemetry,status,fault,command,command/ack}` + `{assetId}`↔`asset_id` (`LH-MQTT-003`). Payload ohne Wall-Clock → **Kadenz-Schranke**. | Golden-Vector-Abnahme; `tick_ms ≤ Freshness-Fenster` (Default 10 s, seit §5.1 `Bess:SnapshotMaxAge`; §8). |
| SUT-Modus | Anbindung an einen externen Feld-Endpoint (= grid-gym) ist **config-only** (`Bess__MqttMappingPath`, `Bess__MqttBrokerHost/Port`; analog `Modbus*`/`OpcUa*`); **kein** Runtime-Code-Pfad. | `deploy/compose.yml`-SUT-Variante + dokumentierter Pfad. |
| Command-Closed-Loop | **Deferred** (§6), spiegelbildlich zu grid-gyms ausgegliederter Inbound-Write-Rückrichtung. Bis dahin ist die Kopplung **telemetry-read-only**. | Trigger: grid-gyms Inbound-Write-Aktivierung + `LH-SAFE-007`. |

Die Umsetzung (Reihenfolge §5) erfolgt in separaten Planungs-Einheiten
(`docs/plan/planning/`), die dieses ADR referenzieren — nicht umgekehrt.

---

## 3. Normativer Vertrag: feld-normativ (nicht byte-normativ)

Der Vertrag ist **feld-normativ**: verbindlich sind **Feldnamen, Präsenz, Typen und
Null-Weglassung** — **nicht** Objekt-Memberreihenfolge oder Whitespace. Grund: der
Konsument ist ein JSON-Parser (`System.Text.Json` in `MqttTelemetrySource`), dem
Reihenfolge/Whitespace unsichtbar sind. Ein **Byte-Gleichheits**-Golden-Vergleich
wäre **strenger als der echte Draht-Vertrag** und würde einen konformen Produzenten
ablehnen — schon der De-facto-Produzent `bess-field-sim` ist byte-instabil:
`telemetry` (Go-Struct) emittiert `offset_millis` **zuerst** (Deklarations-
reihenfolge), `status`/`fault` (`map[string]any`) **alphabetisch**
(`available, fault_status, offset_millis`). **Entscheidung:** Golden-Vektoren
vergleichen **strukturell** (feld-normalisiert); eine kanonische Byte-Referenz ist
höchstens eine **nicht-normative** Produzenten-Hilfe.

Die Feldmenge + Null-Weglassung werden **mechanisch aus dem Code abgeleitet** (nicht
hand-gelistet — das wäre derselbe Handspiegel-Drift). **Autorität je Richtung:**
`telemetry`/`status`/`fault` ← `bess-field-sim/internal/mqtt/serializer.go`
(Go/Feld ist De-facto-Produzent, bess-ems test-gepinnt); `command` ←
`MqttPayloads.cs` (C#/EMS **produziert**, `MqttCommandSink.cs:81`); `command_ack` ←
**Feld produziert**, bess-ems **konsumiert** (`MqttCommandSink.cs:152`) — dessen
C#-Typ `CommandAckPayload` pinnt die EMS-Erwartung, nicht den Produzenten.

**Feldmenge** (verbindlich):

- **Telemetrie** (Feld→EMS, retained), `.../telemetry`: `offset_millis` +
  `soc_percent, soh_percent, active_power_kw, reactive_power_kvar, dc_voltage,
  dc_current, temperature_celsius, available, fault_status` — N schmale
  Quell-Punkte → **ein** Frame/Tick.
- **Status** (Feld→EMS, retained), `.../status`: `available, fault_status,
  offset_millis` (`serializer.go:63`).
- **Fault** (Feld→EMS, non-retained), `.../fault`: `fault_status, offset_millis` —
  **nur** bei `fault_status ∉ {ok, ""}` (`serializer.go:69`).
- **Command** (EMS→Feld), `.../command`: `command_id, timestamp, asset_id, mode,
  active_power_kw, reactive_power_kvar?, valid_until, reason, source` —
  read-only für das Feld bis zur Command-Aktivierung (§6).
- **Command-Ack** (Feld→EMS), `.../command/ack`: `command_id, accepted,
  dispatched_at, reason?` — **entfällt** in der read-only-Phase.

**Feld-wirksame Regeln, die die Golden-Vektoren honorieren MÜSSEN** (aus
`MqttPayloads.cs`; wirken auf Feldmenge/Präsenz, nicht auf Reihenfolge):

1. **`offset_millis` gehört zu telemetry, status UND fault** (`MqttPayloads.cs:16`;
   `serializer.go:66` status, `:74` fault) — nicht weglassen. (`offset_millis` ist
   ein Relativ-Offset, **kein** Wall-Clock — die „ohne Wall-Clock"-Aussage aus §1
   bleibt korrekt.)
2. **Jedes Draht-Feld trägt ein explizites `[JsonPropertyName]` — kein
   Policy-Fallback.** Alle drei Records sind voll-attributiert; die snake_case-Namen
   kommen **ausschließlich** aus den Attributen. `PropertyNamingPolicy = null`
   (`MqttPayloads.cs:49`) ist für diese DTOs ein **No-op** (ein un-attributiertes
   Feld würde als **PascalCase** serialisiert, nicht snake_case). Invariante: neues
   Draht-Feld ⇒ explizites Attribut, nie auf eine Policy verlassen.
3. **`DefaultIgnoreCondition = WhenWritingNull`** (`MqttPayloads.cs:50`) →
   nullbare Felder entfallen: `reactive_power_kvar` (`double?`) im Command,
   `reason` (`string?`) im Ack.

---

## 4. Alternativen

- **A1 (verworfen) — beim Hand-Mirror bleiben.** Status quo; bewiesener Drift
  (`register_table`/`word_order` in `bess-field-sim`), `LH-RISK-002`. Jeder neue
  Konsument verdoppelt das Risiko.
- **A2 (verworfen) — kein publiziertes Bundle, weiter nur im Image/Tarball
  mitliefern.** Kein stabiles, versioniertes Konsum-Artefakt; die `$id`-URIs sind
  Identität, keine ausgelieferte Quelle — ein Generator hätte keinen benannten
  Bezug.
- **A3 (verworfen) — bess-ems an grid-gyms schmale Ein-Metrik-Telemetrieform
  anpassen.** bess-ems würde sich beugen; die Frame-Aggregation gehört auf die
  Feldseite. bess-ems' breite Form ist der bestehende, getestete Vertrag
  (`MqttPayloads`, `LH-MQTT-001`).
- **A4 (verworfen) — Command-Closed-Loop sofort mitspecen.** Ein Live-Master-Write
  ist feldseitig exogener Input ohne Simulationszeit und nicht replaybar; grid-gyms
  Plan lagert ihn aus. Vorab-Spec wäre spekulativ (YAGNI); §6 hält nur den Trigger.
- **A5 (verworfen) — Envelope Schema→C# statt C#→Schema.** Würde die bestehende,
  getestete `MqttPayloads.cs` zu generiertem Code umbauen; höheres Risiko, mehr
  Churn, invertiert die heutige Autorität. C#→Schema + Drift-Check liefert dieselbe
  Single-Source-Garantie ohne Umbau (§2).

---

## 5. Umsetzung (Reihenfolge)

Design-first (diese ADR). Die konkrete Planung lebt in `docs/plan/planning/` und
**referenziert dieses ADR** (nicht umgekehrt; Planungs-IDs sind volatil).
Reihenfolge der Umsetzungsschritte:

1. **Vertrags-Bundle + Envelope-Schema + Versionierung:** `schema_version` auf
   Modbus/MQTT ergänzen, das MQTT-Envelope-Schema aus `MqttPayloads.cs` generieren
   (§2), das Set als Release-Asset + CI-Drift-Gate auf `config/schema/`; zusätzlich
   den Snapshot-`maxAge` konfigurierbar machen (`Bess__…`-Key statt des
   10-s-Literals, §8).
2. **Golden-Vector-Suite (MQTT-first):** portable Vektoren **aus `serializer.go`
   gehoben** (nicht hand-gelistet), **strukturell** verglichen unter Beachtung der
   Feld-Regeln §3; das Abnahme-Geschirr, das grid-gyms Push-Surface treffen muss.
3. **SUT-Doku + config-only-Pfad:** „richte bess-ems auf einen externen
   Feld-Endpoint (grid-gym)".
4. **Modbus, Pull-Surface (mit §5.4 umgesetzt, 2026-07-13):** Modbus-Golden-
   Vectors je profiliertem Register-Profil (`modbus.simulator` +
   `modbus.hil-simulator`), gehoben durch den C#-Codec; zugleich wurde die
   in §1 bewiesene Drift-Instanz im Simulator geschlossen und gegated.

Jeder Umsetzungsschritt trägt Akzeptanzkriterien, Verifikationspfad und Release-Feld
in seiner Planungs-Einheit; Verifikation (`make gates`) lebt dort.

---

## 6. Command-Closed-Loop: deferred mit Trigger (0012-Muster)

Ein geschlossener Regelkreis (bess-ems `optimize→dispatch` → Feld-Write → Effekt
in der nächsten Telemetrie) braucht, dass das Feld den `command`-Topic konsumiert
und `command/ack` emittiert. grid-gym liefert diese Inbound-Write-Rückrichtung erst
in einer späteren, in seinem Plan ausgegliederten Phase (ein Live-Write ist
exogener Input ohne Simulationszeit; grid-gyms geschlossenes Self-Replay hat keinen
Recording-Pfad). Bis dahin:

- Die Kopplung ist **telemetry-read-only**; bess-ems published Commands, grid-gyms
  Feld-Surface konsumiert sie nicht (kein Ack, kein Feldeffekt).
- **Annahme, noch zu verifizieren:** dass bess-ems fehlende ACKs über einen langen
  read-only-Lauf toleriert (kein Retry-Spam, keine Beeinflussung der nächsten
  Entscheidung), ist im Smoke **nicht** belegt — dort ackte `bess-field-sim` jedes
  Command. Verifikation: `MqttCommandSink`/ACK-Handling prüfen bzw. ein
  No-ACK-Smoke.
- Trigger für den bess-ems-seitigen Closed-Loop-Nachweis: grid-gyms
  Inbound-Write-Aktivierung **plus** `LH-SAFE-007` (Schreibbegrenzung vor
  Feldkommunikation); dann eine Folge-Umsetzung (kein neues ADR nötig, sofern die
  Vertrags-Policy aus §2 unverändert trägt).

---

## 7. Konsequenzen

### Positiv

- **Single Source of Truth (gestaffelt).** Die **Publikation + `schema_version` +
  CI-Drift-Gate** beenden **Transkriptions-Drift innerhalb des Schema-Sets** (inkl.
  MQTT-Payload dank Envelope-Schema, §2). Der **cross-language-Drift** (Go-DTO lässt
  Schema-Felder fallen) endet als **Klasse** erst mit **Codegen**
  (Provider-Posture, deferred-mit-Trigger); ein Gate über Schema-Dateien fängt ihn
  nicht (ein fehlendes Struct-Feld ändert keine Datei unter `config/schema/`) —
  die *bewiesene Instanz* (`register_table`/`word_order`) ist allerdings seit §5.4
  geschlossen und wird durch die Golden-Vector-Konformanz gefangen (gerade **kein**
  Schema-Datei-Gate, sondern ein Producer-Pfad-Gate). Der
  **Versions-Skew-Drift** hängt am Breaking-Bump-Rollout (§2). „Strukturell beendet"
  gilt also je Drift-Klasse zu unterschiedlichen Triggern.
- **Kopplung wird real** und **prüfbar** (Golden Vectors aus dem Code), nicht
  erhofft. `LH-CONF-002` wächst von „intern versioniert" zu „extern konsumierbar".
- **Symmetrie.** Beide Vertragsenden sind dokumentiert; die Handoff-Frage aus
  grid-gyms Plan ist beantwortet.
- **Keine Runtime-Code-Änderung für den SUT-Modus.** Config-only (Smoke belegt);
  die `maxAge`-Konfigurierbarkeit (§8) ist der einzige kleine Zusatz.

### Negativ

- **Versionierungs-Disziplin-Kosten.** `schema_version` auf allen dreien + ein
  Envelope-Schema (+ C#↔Schema-Drift-Check) + ein CI-Drift-Gate auf
  `config/schema/` + ein weiteres Release-Artefakt.
- **Konsumenten-Bindung.** Publizierte `$id`-URIs/Formen werden zu einer
  Rückwärtskompatibilitäts-Pflicht (`schema_version`-SemVer + Breaking-Bump-Rollout).

### Neutral

- **`bess-field-sim` bleibt** der Pflichtpfad-Simulator (M1); es wird ein weiterer
  Vertrags-Konsument, kein Ersatz. Seine Codegen-Migration ist eigene Folgearbeit.

---

## 8. Nicht Gegenstand dieser ADR / offene Punkte

- **Anforderungs-Verankerung.** ADR 0013 konkretisiert `LH-CONF-002`. Ob eine
  eigene `AR-OPEN`-ID gezogen wird (`spec/architecture.md` §18 endet bei
  AR-OPEN-012) oder es LH-CONF-002-Konkretisierung bleibt, ist eine
  **Owner-Entscheidung** — hier bewusst **nicht** stillschweigend eine ID erfunden.
- **Freshness-Schwellwert (verortet).** Der „snapshot-aged"-Schwellwert war
  zum Entscheidungszeitpunkt **10 s, hartkodiert** (Literal in der
  DI-Registrierung): Staleness bei `age > _maxAge`
  (`InMemorySnapshotStore.cs:41`), erzeugt den `snapshot-aged-{age}s`-String
  (Z. 43), von `ControlCycleUseCase` als `snapshot-unusable` re-labelt.
  **Mit §5.1 umgesetzt (v2.0.0):** konfigurierbar via `Bess:SnapshotMaxAge`
  (Default weiter 10 s, wirkt in Host **und** Api;
  `docs/user/sut-field-endpoint.md`) — ein langsamer tickendes SUT-Feld hat
  damit seine Stellschraube. Falle: `ControlCycleOptions.SafeFallbackValidity`
  (5 s) ist die **Kommando-Gültigkeit**, nicht die Input-Frische — nicht
  verwechseln.
- **Breaking-Bump-Rollout — operatives Detail.** Die Protokoll-**Form** ist
  entschieden (§2); offen bleiben nur die **Deprecation-Fensterlänge** und das
  exakte `min_supported`-Feldschema — trigger-basiert beim ersten Breaking-Bump.
- **Kein gemeinsames Device-Vokabular angestrebt.** Der MQTT-Envelope ist
  batterie-DC-seitig (`dc_voltage`, `dc_current`), das Modbus-HIL-Profil grid-seitig
  (`grid_voltage_pu`, `grid_frequency_hz`, `grid_current_ka`) — verschiedene
  Messgrößen, **keine** Konvergenz von Envelope- und Modbus-Schema.
- **Golden-Vector-Format-Details** (Manifest-Schema; **struktureller** Vergleich ist
  bereits in §3 entschieden) — Umsetzungsdetail (§5.2).
- **Pull-/Modbus-Server-Seite von grid-gym** liegt im Schwesterprojekt; hier nur
  MQTT-first + der Verweis auf das bestehende Register-Profil
  `modbus.hil-simulator.json` als Anker.
- **Command-Closed-Loop** → §6.
- **Auth/TLS der publizierten Feld-Surface** → Deployment-/Profil-Thema
  (`docs/user/quality.md` §2.2.1, RM-M4-06-FUP-F04 — nicht `LH-PROT-002`,
  Fehlzitation korrigiert 2026-07-13); der lokale Plaintext-Mosquitto
  (`deploy/compose.yml`) bleibt Nur-Sim-Netz.
- **Fremd-Repo-Kopplung.** Dieses ADR referenziert grid-gyms Gegenrolle bewusst
  über **Fähigkeit** (Push-Field-Publish- / Pull-Device-Server-Surface), nicht
  über grid-gym-interne, renummerierbare Bezeichner (ADR-/Slice-Nummern,
  Port-/Typnamen) — damit der permanente bess-ems-Record nicht an Plan-IDs eines
  fremden Repos hängt, die dort ohne unser Zutun driften.
