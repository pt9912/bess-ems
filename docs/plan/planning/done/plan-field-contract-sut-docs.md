# Plan: SUT-Doku + config-only-Pfad (ADR 0013 §5.3)

**Dokumenttyp:** Slice-Plan / done
**Status:** Abgeschlossen am 2026-07-13 — alle 3 Sub-Slices umgesetzt
(aktiviert nach drei Owner-Review-Runden am Plan; nach der Implementierung
eine Agent-Review-Runde mit 10 Befunden und eine Owner-Review-Runde mit
15 Befunden, alle gefixt — siehe Abschluss). `make sut-smoke` grün gegen
den Stand-in, `sut-config-check` Pflicht-Gate. Die ADR-0013-Status-Klausel
wurde mit diesem Slice auf `Accepted — §5.1–§5.3 umgesetzt; §5.4 offen`
gesetzt (Stand Abschluss-Zeitpunkt; §5.4 schreibt sie fort), die
`LH-PROT-002`-Fehlzitation an **vier** Stellen korrigiert. Branch
`impl-field-contract-5.3`.
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
das ist „Protokollfehler → Quality-Flag"; ADR 0013 **trug** dieselbe
Fehlzitation, mit diesem Slice korrigiert)

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
  Code-Lücke): der `Bess__Mqtt*`-Env-Block in `deploy/compose.yml`
  konfiguriert den MQTT-Pfad
  ausschließlich über `Bess__*`-Env-Keys — `MqttMappingPath`,
  `MqttBrokerHost/Port`, `MqttClientId`, `MqttRuntimeProfile`,
  `MqttAllowPlaintext(+Reason)`; dazu die TLS/Auth-Familie
  (`MqttTls*`, `MqttUsername/Password/PasswordPath`,
  `BessHostOptions.cs:58-65`) und seit v2.0.0 `Bess__SnapshotMaxAge`
  (Kadenz-Stellschraube, Default 10 s).
- **Netz-Topologie-Fakt für Sub-Slice 2:** der bestehende Feld-Stack
  publiziert **keinen** Host-Port (mosquitto-Service in
  `deploy/compose.yml` ohne `ports:`), und zwei getrennt gestartete
  Compose-Projekte bekommen
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
  (Plan-Review-Runde 2, Befund 2):** die Posture hängt NICHT an
  `LH-PROT-002` (= Protokollfehler → Quality-Flag); ADR 0013 zitierte
  das im Bezug-Block und in §8 falsch — mit diesem Slice korrigiert
  (das vollständige Vier-Stellen-Inventar steht in Liefergegenstand 3),
  damit die Fehlzitation nicht in die permanente User-Doku wandert.

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
- **Release-Feld:** entfällt — kein eigener RM-Slice (slice-first);
  Rückverfolgbarkeit über den CHANGELOG-Eintrag des nächsten Releases,
  das diesen Merge trägt (aufgelöst im Abschluss; ursprünglicher
  Platzhalter „beim Aktivieren vergeben" war nie einlösbar, weil
  `done/` eingefroren ist).

### 2 — Compose-SUT-Variante + Stand-in-Smoke

- **Kopplungs-Artefakt (entschieden, nicht offen):** ein **shared
  external Docker-Network** (`bess-sut`; das Make-Target legt es bei
  Bedarf an und räumt **nur selbst angelegte** Netze wieder ab — ein
  vorbestehendes Netz wird mit Warnung weiterverwendet und belassen).
  Der Stand-in-Feld-Stack bekommt ein eigenes
  `deploy/compose.field.yml` (mosquitto + `bess-field-sim`, tritt dem
  externen Netz bei, Broker-Service-Alias z. B. `field-mosquitto`,
  **kein** Host-Port-Publish); `deploy/compose.sut.yml` tritt demselben
  Netz bei. Default `BESS_SUT_BROKER_HOST=field-mosquitto`,
  `BESS_SUT_BROKER_PORT=1883`. (Verworfen: Host-Port-Publish + `host.docker.internal`
  — Linux-unportabel ohne `host-gateway`-Zusatz und kollisionsanfällig
  auf 1883.) Für einen **echt** externen Endpoint (grid-gym, andere
  Maschine) dokumentiert die Doku das Env-Override auf eine routbare
  Adresse; die Netz-Topologie ist dann Betreibersache.
- **Aufgabe:** `deploy/compose.sut.yml` — bess-ems (+ Postgres) **ohne**
  eigenen Broker und **ohne** `bess-field-sim`; Broker-Adresse per Env
  (siehe oben). `make sut-smoke` mit **derselben Dependency-Kette wie
  `runtime`** — umgesetzt als gemeinsames Prerequisite
  `simulator-build` (Prerequisites dedupliziert make über
  `runtime`/`test-integration`/`sut-smoke`; wörtliche Rezept-Kopien
  von `$(SIMULATOR_MAKE) build` hätten den Simulator in `fullbuild`
  dreimal gebaut, Owner-Review-Befund 15) — beide Stacks nutzen
  `pull_policy: never`-Images (`bess-ems-runtime:latest`,
  `bess-field-sim:latest`), standalone auf frischem Checkout wäre der
  Smoke sonst rot. Ablauf: Netz anlegen (falls fehlend) → Feld-Stack
  (`compose.field.yml`) hoch → SUT-Variante dagegen → Grün-Kriterium
  **mechanisch** (Plan-Review-Runde 2, Befund 1: **nur** der
  Safe-Stop-Pfad trägt ein `decision=`-Feld; ein Grep auf
  „`decision=` ungleich Safe-Stop" wäre unerfüllbar): innerhalb von
  90 s (Default `SUT_SMOKE_TIMEOUT`) erscheint die Gutfall-Zeile —
  gematcht am **JSON-Anker `"EventId":1701`** („Control cycle emitted
  command"), umformulierungsfest; die zunächst umgesetzte
  Freitext-Variante hätte bei einem Meldungs-Umbau still zum
  False-Green degenerieren können (Owner-Review-Befund 7). **Ab dem
  Gutfall-Signal** läuft ein 20-s-Beobachtungsfenster
  (`SUT_SMOKE_WARMUP`), in dem **keine neue** Safe-Stop-Zeile
  (`"EventId":1702`) hinzukommen darf; Anlauf-Safe-Stops **vor** dem
  ersten Gutfall sind erwartbar (der Zyklus läuft, bevor Telemetrie
  eintrifft) und zählen bewusst nicht — danach endet die Beobachtung:
  der Smoke ist ein Kopplungs-Beweis, kein Dauer-Monitor. Abschließend
  beide Stacks abräumen, selbst angelegte Netze entfernen (auch im
  Fehlerpfad). Ein `decision=`-Feld in die Gutfall-Zeile aufzunehmen
  wäre ein Runtime-Code-Delta und ist per Nicht-Ziel ausgeschlossen.
  Damit ist der config-only-Pfad mechanisch belegt.
- **Gate-Einordnung (entschieden, nicht offen — Plan-Review-Runde 1,
  Befund 2):** zwei Ebenen. (1) **`make sut-config-check`**
  (`docker compose -f <datei> config -q` für beide neuen
  Compose-Dateien) ist **Pflicht-Gate** in
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
- **Release-Feld:** entfällt — wie Sub-Slice 1 (CHANGELOG-Eintrag des
  nächsten Releases; aufgelöst im Abschluss).

### 3 — Offener Verifikations-Slot: grid-gym-E2E (trigger-basiert)

- **Aufgabe:** In der SUT-Doku einen explizit als **offen** markierten
  Abschnitt „Verifikation gegen grid-gym" führen: der E2E gegen die
  reale Push-Surface ist **Abnahmekriterium des grid-gym-seitigen
  CR** (bess-ems-konformer breiter Publisher, dort in Arbeit) und wird
  hier nachgetragen, sobald er geliefert ist — inklusive Flip des
  Doku-Status von „Stand-in-verifiziert" auf „grid-gym-verifiziert".
  **Abweichung (dokumentiert):** ausgeliefert wurde der Abschnitt als
  „Verifikation gegen eine reale externe Feld-Umgebung — OFFEN", und
  die Doku enthält den String `grid-gym` bewusst **nirgends** —
  stärker anonymisiert, als ADR §8 verlangt (das ADR verbietet nur
  renummerierbare Fremd-IDs, nicht den Projektnamen); wer die Doku
  nach dem Projektnamen greppt, findet den Slot über den
  Abschnittstitel in §6.
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
   (Status-Zeile **und** Header-Prosa-Absatz, sonst driftet die
   Prosa), plus die Korrektur der `LH-PROT-002`-Fehlzitation. Geplant
   waren **drei** Stellen (ADR Bezug-Block, ADR §8, Header-Kommentar
   `deploy/compose.yml`); die Implementierungs-Review-Runde fand eine
   **vierte** (`deploy/mosquitto.conf`-Header — die Datei, die beide
   Stacks mounten), korrigiert im Review-Fix-Commit. Broker-Security-
   Anker ist `quality.md` §2.2.1/RM-M4-06-FUP, nicht `LH-PROT-002` =
   Protokollfehler→Quality-Flag.

---

## Akzeptanzkriterien

- Ein Betreiber kann bess-ems ausschließlich per Doku + Env-Keys auf
  einen externen MQTT-Feld-Endpoint richten; jeder dokumentierte Key
  ist gegen den Code verifiziert, die `asset_id`↔`{assetId}`-
  Korrespondenz ist benannt, und die zwei Safe-Stop-Ursachen sind im
  Rezept unterscheidbar.
- Der Pfad ist mechanisch belegt: `make sut-smoke` grün gegen den
  Stand-in-Stack über das shared external Network, Grün-Kriterium per
  Log-Grep auf JSON-Anker (`"EventId":1701` binnen 90 s; ab dem
  Gutfall-Signal keine **neue** `"EventId":1702`-Zeile im
  20-s-Beobachtungsfenster — Anlauf-Safe-Stops davor zählen nicht),
  fortlaufend in `fullbuild`; `make sut-config-check` als Pflicht-Gate
  hält die Compose-Artefakte gegen Rott-Drift. Der grid-gym-E2E ist
  als offener, trigger-basierter Slot markiert — kein stiller
  Verifikations-Claim.
- `make gates` bleibt grün; kein Runtime-Verhaltensdelta.

---

## Definition of Done (DoD)

- [x] Sub-Slice 1 — SUT-Doku, key-verifiziert (alle 20 `Mqtt*`-Properties
      als Tabellenzeilen = Code→Doku-Richtung erfüllt; TLS/Auth-Zeilen,
      QoS-Semantik inline, `{assetId}`-Korrespondenz, unterscheidbare
      Ursachen-Signale). (`bf1313a`)
- [x] Sub-Slice 2 — `compose.sut.yml` + `compose.field.yml` +
      `make sut-smoke` grün (Stand-in; JSON-Anker `"EventId":1701`
      binnen 90 s, keine **neue** `"EventId":1702`-Zeile im
      20-s-Fenster ab Gutfall-Signal; fullbuild) + `sut-config-check`
      Pflicht-Gate. (`fa4f47f`; gehärtet durch beide Review-Runden —
      Evidenz-Stand ist der Branch-Tip, nicht `fa4f47f`)
- [x] Sub-Slice 3 — offener Verifikations-Slot markiert (Doku §6,
      fähigkeits-referenziert; ausgelieferter Titel weicht bewusst vom
      Plan-Wortlaut ab, siehe Sub-Slice-3-Abweichungsnotiz).
- [x] Alle Akzeptanzkriterien erfüllt; alle Pflicht-Gates grün
      (auf dem Endstand nach beiden Review-Runden).
- [x] ADR 0013: Status-Klausel
      `Accepted — §5.1–§5.3 umgesetzt; §5.4 offen` an **beiden**
      Stellen (Status-Zeile + Header-Prosa) + `LH-PROT-002`-Korrektur
      an allen **vier** Stellen (ADR Bezug, ADR §8,
      `deploy/compose.yml`, `deploy/mosquitto.conf` — die vierte fand
      die Implementierungs-Review).

---

## Abschluss (2026-07-13)

Alle 3 Sub-Slices umgesetzt; die Evidenz-Kette umfasst **drei Wellen**
auf dem Branch (die DoD-Haken zertifizieren den Branch-Tip, nicht die
Sub-Slice-Commits):

1. **Sub-Slice-Commits + Finalisierung.** Bemerkenswert: der **erste**
   `sut-smoke`-Lauf schlug korrekt fehl — das Standard-Integrations-
   Szenario hält nach Tick 0 für 24 h still (Einzel-Publish), bess-ems
   lief nach dem initialen Gutfall-Signal in
   `decision=snapshot-unusable`/`reason=snapshot-aged-<N>s`. Das ist
   die Kadenz-Regel aus ADR 0013 §1 in freier Wildbahn und bestätigt
   die Ursachen-Signale des Doku-Rezepts.
2. **Agent-Review-Runde: 10 Befunde, alle gefixt** (`2cfd5d3`) —
   darunter: das Real-Endpoint-Rezept legte das externe Netz nie an
   (Doku-Hauptkriterium war damit zum ersten Abhak-Zeitpunkt
   **nicht** erfüllt; erst dieser Fix machte es wahr), ein
   Bind-Mount-Footgun (fehlende Datei wird als gitignoriertes
   Verzeichnis angelegt und verklemmt Folge-Läufe →
   `create_host_path: false`), ein False-Green-Pfad der Log-Erfassung,
   Env-Pinning, Netz-Lifecycle, die **vierte** `LH-PROT-002`-Stelle
   (`mosquitto.conf`), Doku-Präzisierungen.
3. **Owner-Review-Runde: 15 Befunde, alle gefixt** — schwerpunktmäßig
   Genauigkeit des Done-Records und Härtung: Grün-Kriterium auf
   **JSON-Anker** `"EventId":1701/1702` umgestellt (Freitext-Greps
   wären bei Meldungs-Umbau still zum False-Green degeneriert) und die
   Safe-Stop-Semantik überall auf die tatsächliche Implementierung
   präzisiert (Beobachtungsfenster **ab** Gutfall-Signal, keine
   Dauer-Überwachung); das Kadenz-Szenario ist jetzt ein
   **committetes Fixture**
   (`simulators/bess-field-sim/testdata/scenarios/sut-smoke-cadence.json`,
   diff-reviewbar, `TestAllFixturesLoad`-gedeckt) statt einer zur
   Laufzeit generierten gitignorierten Datei; `simulator-build` als
   gemeinsames Prerequisite (vorher baute `fullbuild` das
   Simulator-Image dreimal); zwei falsche Doku-Signale korrigiert
   (`/health` liefert 503 bei unhealthy Komponenten, nicht pauschal
   200; das 1903-Zitat lautet real `Command-sink dispatch failed`);
   drei veraltete „10 s hartkodiert"-Stellen im ADR auf den
   §5.1-Stand gebracht; Release-Feld-Platzhalter aufgelöst;
   Zitate/Zeilenanker drift-fest gemacht.

**Bewusst nicht umgesetzt** (Owner-Review, unterschwellige Kandidaten):
ein mechanischer Wächter gegen künftige `LH-PROT-002`-Fehlzitationen —
notiert als möglicher d-check-/Sensor-Kandidat, kein §5.3-Scope.

**Verifikations-Kommandos:** `make sut-config-check` (Pflicht-Gate),
`make sut-smoke` (fullbuild-Mitglied), `make gates` (voll; auf langsamen
Maschinen ggf. in Chunks — die `--no-cache-filter`-Simulator-Stages
laufen je Aufruf neu), `make docs-check`.
