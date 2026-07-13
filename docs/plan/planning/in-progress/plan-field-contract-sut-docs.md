# Plan: SUT-Doku + config-only-Pfad (ADR 0013 §5.3)

**Dokumenttyp:** Slice-Plan / in-progress
**Status:** In Progress — aktiviert 2026-07-13 nach drei Owner-Review-Runden
(u. a. Grün-Kriterium auf `Control cycle emitted command` korrigiert,
`LH-PROT-002`-Fehlzitation dreistellig im Korrektur-Scope, Netz-Kopplung
via shared external Network fixiert). Branch `impl-field-contract-5.3`.
**Datum:** 2026-07-13
**Quelle:** [`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md) §5.3
**Bezug:**
[`../../adr/0013-device-mapping-field-contract.md`](../../adr/0013-device-mapping-field-contract.md)
(§2 Achse „SUT-Modus", §5 Umsetzung, §8 offene Punkte),
[`../done/plan-field-contract-bundle.md`](../done/plan-field-contract-bundle.md) (§5.1),
[`../done/plan-field-contract-golden-vectors.md`](../done/plan-field-contract-golden-vectors.md) (§5.2),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(`LH-MQTT-001/003`),
[`../../../user/quality.md`](../../../user/quality.md) (TLS/Auth-Abschnitt)
und [`../done/plan-RM-M4-06-FUP-tls-auth.md`](../done/plan-RM-M4-06-FUP-tls-auth.md)
(Verankerung der MQTT-Plaintext/TLS-Posture — **nicht** `LH-PROT-002`,
das ist „Protokollfehler → Quality-Flag"; ADR 0013 trägt dieselbe
Fehlzitation, Korrektur nimmt dieser Slice mit)

---

## Ziel

Umsetzungsschritt §5.3 aus ADR 0013: der **config-only-Pfad** „richte
bess-ems auf einen externen Feld-Endpoint" wird als konsumierbare
Betriebs-Doku festgehalten und mit einer **Compose-SUT-Variante**
mechanisch belegt — ohne jeden Runtime-Code-Pfad (ADR §2, Smoke-belegt).
Der intendierte Gegenspieler ist grid-gyms Push-Field-Publish-Surface;
referenziert wird er über **Fähigkeit** (ADR §8), der bess-ems-konforme
breite Publisher entsteht dort gerade (extern beauftragt, CR-Referenz im
Schwesterrepo). Bis er liefert, dient `bess-field-sim` als externer
Endpoint-Stand-in — er spricht den Vertrag bereits.

Nach Abschluss trägt ADR 0013 die Status-Klausel
`Accepted — §5.1–§5.3 umgesetzt; §5.4 offen`.

---

## Ausgangslage

Verifiziert 2026-07-13 (Stand v2.1.0):

- **Der config-only-Mechanismus existiert vollständig** (keine
  Code-Lücke): `deploy/compose.yml:28-34` konfiguriert den MQTT-Pfad
  ausschließlich über `Bess__*`-Env-Keys — `MqttMappingPath`,
  `MqttBrokerHost/Port`, `MqttClientId`, `MqttRuntimeProfile`,
  `MqttAllowPlaintext(+Reason)`; dazu die TLS/Auth-Familie
  (`MqttTls*`, `MqttUsername/Password/PasswordPath`,
  `BessHostOptions.cs:58-65`) und seit v2.0.0 `Bess__SnapshotMaxAge`
  (Kadenz-Stellschraube, Default 10 s).
- **Netz-Topologie-Fakt für Sub-Slice 2:** der bestehende Feld-Stack
  publiziert **keinen** Host-Port (`deploy/compose.yml:56-65`, mosquitto
  ohne `ports:`), und zwei getrennt gestartete Compose-Projekte bekommen
  projekt-eigene Netze — ein „einfach beide starten" verbindet sie
  **nicht**; die Kopplung braucht ein explizites Artefakt (Entscheidung
  unten).
- **Aber er ist nirgends als Betriebs-Pfad dokumentiert:** `docs/user/`
  (function, edge-controller, opc-ua, persistence, quality, releasing)
  enthält keine Anleitung „externen Feld-Endpoint anbinden"; das Wissen
  lebt implizit im Compose-File.
- **Der Vertrag, den der Endpoint sprechen muss, ist publiziert**
  (v2.0.0: Schema-Bundle inkl. Envelope; v2.1.0: + Golden-Vektoren) —
  die Doku kann darauf zeigen statt ihn zu wiederholen.
- **Kadenz-Anforderung** (ADR §1): Frische ist empfangs-basiert; ein
  Endpoint, der langsamer als das Freshness-Fenster publisht, treibt
  bess-ems in Dauer-Safe-Stop (`decision=snapshot-unusable`).
- **Security-Posture:** Plaintext-MQTT ist Nur-Sim-Netz;
  `MqttAllowPlaintext` verlangt eine dokumentierte `Reason`;
  TLS/Auth-Profile existieren (RM-M4-06-FUP, dokumentiert in
  `quality.md`), bleiben aber Deployment-Thema. **Zitations-Korrektur
  (Review-Befund 2):** die Posture hängt NICHT an `LH-PROT-002`
  (= Protokollfehler → Quality-Flag); ADR 0013 zitiert das in Bezug
  (Z. 26) und §8 falsch — die Ein-Zeilen-Korrektur wird mit der
  ADR-Status-Klausel dieses Slices miterledigt, damit die Fehlzitation
  nicht in die permanente User-Doku wandert.

---

## Sub-Slices

### 1 — SUT-Doku `docs/user/sut-field-endpoint.md`

- **Aufgabe:** Neues User-Doc „bess-ems als SUT gegen einen externen
  Feld-Endpoint (MQTT-first)": (a) die **vollständige**
  `Bess__Mqtt*`-Key-Tabelle — inkl. der TLS/Auth-Familie (`MqttTls*`,
  `MqttUsername/Password/PasswordPath`, `BessHostOptions.cs:58-65`)
  **und** der QoS-/Exactly-Once-Familie (`MqttCommandPublishQos`,
  `MqttCommandAckSubscribeQos`, `MqttTelemetrySubscribeQos`,
  `MqttAllowExactlyOnce(+Reason)`, `BessHostOptions.cs:68-72` — für
  ein SUT-Doc besonders relevant: QoS-Interop mit fremdem
  Broker/Publisher) als Tabellenzeilen. **Delegations-Entscheidung
  (Review-Runde 3):** die TLS/Auth-Tiefe delegiert an
  [`quality.md`](../../../user/quality.md) **§2.2.1**
  (MQTT-Security-Profil — existiert und trägt); die **QoS-Semantik
  wird in der SUT-Doku selbst ausgeführt** (inkl. des
  Verhaltens bei unset — die drei QoS-Keys sind nullable und ihre
  Defaults sind heute nirgends user-dokumentiert), nur das
  Exactly-Once-**Gate** delegiert an §2.2.1 (deckt dort genau das,
  `quality.md:265-267`) — plus `Bess__SnapshotMaxAge` (bindet
  **nicht** über `BessHostOptions`, sondern direkt in
  `BessHostBuilder.cs:65` bzw. `Api/Program.cs` — Prüfort
  entsprechend) **und** `Bess__AssetConfigPath` mit dem
  Korrespondenz-Satz: die `asset_id` der Asset-Config **muss** dem
  `{assetId}` entsprechen, unter dem der Endpoint publisht — sonst
  subscribt bess-ems ein leeres Topic und läuft still in
  Dauer-Safe-Stop (ADR §2, Zeile „Normative Payload-Form":
  `{assetId}`↔`asset_id`, `LH-MQTT-003`);
  (b) der Vertrag, den der Endpoint erfüllen muss — Verweis auf das
  publizierte Bundle (`bess-ems-schemas-<v>.tar.gz`: Envelope-Schema +
  Golden-Vektoren, strukturell normativ) und die Kadenz-Schranke;
  (c) Security-Posture (Plaintext = Nur-Sim-Netz,
  `AllowPlaintextReason`-Pflicht, `RuntimeProfile`-Wirkung);
  (d) Verifikations-Rezept (Health-Probe, Gutfall-Signal
  `Control cycle emitted command`), das die zwei Safe-Stop-Ursachen
  **unterscheidbar** macht — „nie Telemetrie empfangen" →
  `decision=no-snapshot` (ID-/Topic-Mismatch) vs. „Telemetrie zu alt"
  → `decision=snapshot-unusable` mit `reason=snapshot-aged-Ns`
  (Kadenzverstoß); Signale review-verifiziert, im Slice gegen den
  Code re-verifiziert;
  (e) Modbus/OPC-UA als analoge `Bess__*`-Familien benannt, aber
  MQTT-first ausgeführt (ADR §8).
- **Akzeptanz (beide Prüfrichtungen):** Doku→Code: jeder dokumentierte
  Key gegen `BessHostOptions` bzw. `BessHostBuilder`/`Api/Program`
  (`SnapshotMaxAge`) und Compose geprüft. Code→Doku: **jede**
  `Mqtt*`-Property in `BessHostOptions` hat eine Tabellenzeile
  (Auslassungen fängt die erste Richtung nicht). Ursachen-Signale
  code-verifiziert; `make docs-check` grün.
- **Verifikation:** `make docs-check`; Schritt-für-Schritt-Nachvollzug
  im Sub-Slice-2-Smoke.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 2 — Compose-SUT-Variante + Stand-in-Smoke

- **Kopplungs-Artefakt (entschieden, nicht offen):** ein **shared
  external Docker-Network** (`bess-sut`, angelegt/abgeräumt vom
  Make-Target). Der Stand-in-Feld-Stack bekommt ein eigenes
  `deploy/compose.field.yml` (mosquitto + `bess-field-sim`, tritt dem
  externen Netz bei, Broker-Service-Alias z. B. `field-mosquitto`,
  **kein** Host-Port-Publish); `deploy/compose.sut.yml` tritt demselben
  Netz bei. Default `BESS_SUT_BROKER_HOST=field-mosquitto`,
  `…_PORT=1883`. (Verworfen: Host-Port-Publish + `host.docker.internal`
  — Linux-unportabel ohne `host-gateway`-Zusatz und kollisionsanfällig
  auf 1883.) Für einen **echt** externen Endpoint (grid-gym, andere
  Maschine) dokumentiert die Doku das Env-Override auf eine routbare
  Adresse; die Netz-Topologie ist dann Betreibersache.
- **Aufgabe:** `deploy/compose.sut.yml` — bess-ems (+ Postgres) **ohne**
  eigenen Broker und **ohne** `bess-field-sim`; Broker-Adresse per Env
  (siehe oben). `make sut-smoke` mit **derselben Dependency-Kette wie
  `runtime`** (`build` + `$(SIMULATOR_MAKE) build`, `Makefile:414-415`)
  — beide Stacks nutzen `pull_policy: never`-Images
  (`bess-ems-runtime:latest`, `bess-field-sim:latest`), standalone auf
  frischem Checkout wäre der Smoke sonst rot. Ablauf: Netz anlegen → Feld-Stack
  (`compose.field.yml`) hoch → SUT-Variante dagegen → Grün-Kriterium
  **mechanisch** (Review-Befund 1: **nur** der Safe-Stop-Pfad trägt ein
  `decision=`-Feld, `ControlCycleUseCase.cs:246`; ein Grep auf
  „`decision=` ≠ Safe-Stop" wäre unerfüllbar): innerhalb N Sekunden
  (Default im Slice, z. B. 60 s) erscheint die Gutfall-Zeile
  **`Control cycle emitted command`** (EventId 1701,
  `ControlCycleUseCase.cs:235`), **plus** nach der Warmup-Phase keine
  `Control cycle safe-stop`-Zeile mehr (Log-Grep, kein Augenschein) →
  beide Stacks + Netz abräumen (auch im Fehlerpfad). Ein
  `decision=`-Feld in die Gutfall-Zeile aufzunehmen wäre ein
  Runtime-Code-Delta und ist per Nicht-Ziel ausgeschlossen. Damit ist
  der config-only-Pfad mechanisch belegt.
- **Gate-Einordnung (entschieden, nicht offen — Review-Befund 2):**
  zwei Ebenen. (1) **`make sut-config-check`** (`docker compose -f … 
  config -q` für beide neuen Compose-Dateien) ist **Pflicht-Gate** in
  `make gates`/`ci`/`build.yml` — Sekunden, keine Images; eine nie
  ausgeführte Compose-Datei neben einem Doku-Rezept ist exakt die
  Rott-Drift-Klasse, die ADR 0013 bekämpft. (2) **`make sut-smoke`**
  wird in **`fullbuild`** verdrahtet (neben dem bestehenden
  Compose-Smoke, dessen natürliches Zuhause) — er prüft eine eigene
  Fehlerklasse (Regression der Externalisierbarkeit + die
  Cross-Stack-Kopplung), die `make runtime` (nur `/health` +
  Native-Lib-Probe) nicht deckt, ist aber zu schwer für `gates`.
- **Akzeptanz:** `make sut-smoke` grün (mechanisches Kriterium, im
  Slice einmal belegt und fortan in `fullbuild`); `sut-config-check`
  grün in `gates`; kein Runtime-Code-Delta (reine
  Config/Doku/Make-Arbeit).
- **Verifikation:** `make sut-config-check` (Pflicht), `make sut-smoke`
  (fullbuild), `make gates`.
- **Release-Feld:** RM-Feld beim Aktivieren vergeben.

### 3 — Offener Verifikations-Slot: grid-gym-E2E (trigger-basiert)

- **Aufgabe:** In der SUT-Doku einen explizit als **offen** markierten
  Abschnitt „Verifikation gegen grid-gym" führen: der E2E gegen die
  reale Push-Surface ist **Abnahmekriterium des grid-gym-seitigen
  CR** (bess-ems-konformer breiter Publisher, dort in Arbeit) und wird
  hier nachgetragen, sobald er geliefert ist — inklusive Flip des
  Doku-Status von „Stand-in-verifiziert" auf „grid-gym-verifiziert".
- **Akzeptanz:** der offene Slot ist unübersehbar markiert (kein
  stiller Verifikations-Claim); Trigger **fähigkeits-referenziert**
  gemäß ADR §8 — die permanente User-Doku nennt keine fremd-repo-
  internen, renummerierbaren CR-/Plan-IDs (im Plan hier ist die
  CR-Nennung legitim, in `sut-field-endpoint.md` nicht).
- **Verifikation:** `make docs-check`.
- **Release-Feld:** entfällt (Doku-Slot).

---

## Nicht-Ziele

- **Kein** Runtime-Code-Delta (ADR §2: config-only ist Smoke-belegt).
- **Keine** Modbus-/OPC-UA-SUT-Ausführung — als Key-Familien benannt,
  Rezept bleibt MQTT-first (Modbus-Vektoren = §5.4; die dort nötige
  Metrik-Deckungs-Prüfung gegen grid-gyms Battery-Emissionen gehört zu
  §5.4, nicht hierher).
- **Kein** TLS/Auth-Ausbau (Stand RM-M4-06-FUP bleibt; Posture wird nur
  dokumentiert, Tiefe delegiert an `quality.md`).
- **Keine** grid-gym-seitige Arbeit; der E2E gegen grid-gym ist deren
  CR-Abnahme und hier nur ein markierter Nachtrags-Slot.
- **Kein** schwerer Pflicht-Compose-Lauf in `make gates` — der volle
  `sut-smoke` lebt in `fullbuild`; Pflicht in `gates` ist nur der
  sekundenschnelle `sut-config-check` (Sub-Slice 2).

---

## Liefergegenstände bei Aktivierung

1. `docs/user/sut-field-endpoint.md` (vollständige Key-Tabelle inkl.
   TLS/Auth-Zeilen + `AssetConfigPath`/`{assetId}`-Korrespondenz,
   Vertrag-Verweis, Kadenz, Security, unterscheidbares
   Verifikations-Rezept, offener grid-gym-Slot).
2. `deploy/compose.sut.yml` + `deploy/compose.field.yml` (Stand-in-
   Stack) + shared-external-Network-Mechanik + `make sut-smoke`
   (fullbuild-verdrahtet, mechanisches Grün-Kriterium) +
   `make sut-config-check` (Pflicht-Gate in gates/ci/build.yml).
3. ADR 0013 nach Abschluss: Status-Klausel
   `Accepted — §5.1–§5.3 umgesetzt; §5.4 offen` **an beiden Stellen**
   (Klausel Z. 3 **und** Header-Prosa Z. 12–15, sonst driftet die
   Prosa), plus die Korrektur der `LH-PROT-002`-Fehlzitation an allen
   **drei** Stellen: ADR Bezug Z. 26, ADR §8 (Z. 338) **und der
   Kommentar `deploy/compose.yml:2`** („Mosquitto (LH-PROT-002 MQTT)"
   — dieselbe Fehlzitation; Sub-Slice 2 arbeitet ohnehin in `deploy/`).
   Broker-Security-Anker ist `quality.md` §2.2.1/RM-M4-06-FUP, nicht
   `LH-PROT-002` = Protokollfehler→Quality-Flag.

---

## Akzeptanzkriterien

- Ein Betreiber kann bess-ems ausschließlich per Doku + Env-Keys auf
  einen externen MQTT-Feld-Endpoint richten; jeder dokumentierte Key
  ist gegen den Code verifiziert, die `asset_id`↔`{assetId}`-
  Korrespondenz ist benannt, und die zwei Safe-Stop-Ursachen sind im
  Rezept unterscheidbar.
- Der Pfad ist mechanisch belegt: `make sut-smoke` grün gegen den
  Stand-in-Stack über das shared external Network, Grün-Kriterium per
  Log-Grep (`Control cycle emitted command` binnen N s, keine
  `Control cycle safe-stop`-Zeile nach Warmup), fortlaufend in
  `fullbuild`; `make sut-config-check` als Pflicht-Gate hält die
  Compose-Artefakte gegen Rott-Drift. Der grid-gym-E2E ist als
  offener, trigger-basierter Slot markiert — kein stiller
  Verifikations-Claim.
- `make gates` bleibt grün; kein Runtime-Verhaltensdelta.

---

## Definition of Done (DoD)

- [ ] Sub-Slice 1 — SUT-Doku, key-verifiziert (inkl. TLS/Auth-Zeilen,
      `{assetId}`-Korrespondenz, unterscheidbare Ursachen-Signale).
- [ ] Sub-Slice 2 — `compose.sut.yml` + `compose.field.yml` +
      `make sut-smoke` grün (Stand-in; `Control cycle emitted command`
      binnen N s, kein Safe-Stop nach Warmup; fullbuild) +
      `sut-config-check` Pflicht-Gate.
- [ ] Sub-Slice 3 — offener grid-gym-Verifikations-Slot markiert.
- [ ] Alle Akzeptanzkriterien erfüllt; `make gates` grün.
- [ ] ADR 0013: Status-Klausel
      `Accepted — §5.1–§5.3 umgesetzt; §5.4 offen` an **beiden**
      Stellen (Z. 3 + Header-Prosa) + `LH-PROT-002`-Zitations-Korrektur
      an allen **drei** Stellen (ADR Bezug, ADR §8,
      `deploy/compose.yml:2`).
