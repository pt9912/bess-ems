# Notiz: M4-Folgearbeiten (Trigger-Watch)

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen — Folgearbeiten zu aktiven M4-Slices, ohne Plan-Heimat im Master-Plan
**Bezug:**
[`../done/plan-RM-M4.md`](../done/plan-RM-M4.md) (Master-Slice-Plan, abgeschlossen am 2026-05-11 — alle 8 Pflicht-Slices ✅),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md)

---

## Zweck

Während der M4-Umsetzung tauchen Folgearbeiten auf, die der jeweilige
Slice **bewusst draußen lässt** — entweder weil sie nicht-trivialen
Eigenscope haben (Operator-API, neuer Use-Case) oder weil ihr
Trigger noch nicht zündet. Damit sie beim nächsten
Trigger-Watch-Scan sichtbar bleiben — statt in Plan-Tabellen-
Kommentaren zu versinken — sind sie hier zentral geführt.

Anders als der **Bewusst-draußen-Block** in einer Plan-Zeile
(der einen abgeschlossenen Slice-Carve-out beschreibt) ist diese
Notiz **Trigger-Watch-Material** für Folgearbeiten, die einen
eigenen Slice-Plan brauchen wenn sie zünden.

Konkrete Slice-Pläne entstehen erst beim Trigger:

- `plan-RM-M4-01-FUP-NN.md` für Folgearbeiten zum Intraday-Slice
  (F-01, F-02)
- `plan-RM-M4-06-FUP-NN.md` für Folgearbeiten zum MQTT-Slice
  (F-03, F-04, F-05, F-06)
- `plan-RM-M4-07-FUP-NN.md` für Folgearbeiten zum
  OPC-UA-Mapping-Schema-Slice (F-07)
- Oder Carve-out-Sektion innerhalb des auslösenden Plans, falls
  der Trigger ein anderer Slice ist (z. B. Operator-API-Slice der
  F-01 + F-02 zusammenzieht; oder ein Production-Hardening-Slice
  der F-04 als Pflicht-Bestandteil aufnimmt).

---

## Item F-01: Cold-Start-Bootstrap (Day-Ahead → Intraday-Initial)

**Quelle:** RM-M4-01 Design-Entscheidung D-01 — Reoptimierung
verlangt eine existierende Intraday-Baseline. Bei `intraday-
baseline-missing` heute Failed-Run; das produktive Cold-Start-
Verhalten ist nicht modelliert.

**Trigger:** Erster operativer Workflow, der eine frische
Intraday-Welt braucht ohne dass jemand einen Intraday-Schedule
manuell gesetzt hat. Konkret eines der drei:

- Ein Operator-UI-Workflow „initial Intraday-Setup" zündet (heute
  nicht existent).
- Ein produktives Deployment fährt einen neuen Asset hoch und der
  erste Intraday-Reopt-Aufruf scheitert reproduzierbar mit
  `intraday-baseline-missing`.
- Eine Compliance-/Operations-Anforderung verlangt automatischen
  DA→ID-Initial-Transfer beim Tageswechsel.

**Scope-Skizze** (wenn der Trigger zündet):

Drei mögliche Mechanik-Entwürfe — jeder mit eigenem Implementierungs-
Aufwand und Operator-Modell:

- **(a) Auto-Copy bei Reoptimierung-Aufruf:** wenn die Intraday-
  Baseline fehlt, kopiert die Use-Case implizit die aktuelle Day-
  Ahead-Schedule als Intraday-`v1` und reoptimiert sofort darauf.
  Vorteil: kein zusätzlicher API-Endpunkt, Operator sieht den
  Reopt einfach durchlaufen. Nachteil: implizite Schedule-Erzeugung
  ohne explizite Operator-Bestätigung — schwerer zu auditieren.

- **(b) Expliziter `POST /markets/intraday/initialize-from-day-ahead`-
  Endpoint:** Operator triggert die DA→ID-Kopie als eigene Aktion,
  danach läuft Reopt regulär. Vorteil: sauberes Audit-Trail.
  Nachteil: zwei-Schritt-Workflow.

- **(c) Cron-/Scheduled-Initialization:** zum Tageswechsel kopiert
  ein Hosted-Service automatisch DA→ID. Vorteil: kein Operator-
  Eingriff. Nachteil: implizit + braucht Cron-Infrastruktur.

**Aufwandsschätzung:** grob 1-2 Wochen je nach Variante. (a) ist am
kleinsten (~150-200 LOC), (b) braucht zusätzlichen API-Endpoint plus
Auth-/Audit-Bindings (~250-350 LOC), (c) braucht Hosted-Service plus
Cron-Konfiguration (~300-400 LOC).

**Aktivierungs-Pfad:** eigener `plan-RM-M4-01-FUP-cold-start.md` in
`open/`, dann `in-progress/`, dann `done/`. Roadmap-Eintrag.

---

## Item F-02: Alignment-Toleranz (sub-Step-`residualStart`-Snap)

**Quelle:** RM-M4-01 Design-Entscheidung D-02 — `residualStart`
muss heute exakt an einer Window-Grenze liegen. Misalignment ⇒
Failed-Run mit `residual-start-not-aligned`. Operator-Reibung
denkbar wenn die Reopt-Trigger-Zeit nicht natürlicherweise auf
einem Step-Boundary liegt.

**Trigger** (eines reicht):

- Operations-Reibung sichtbar: ein Operator/Service-Account
  bekommt reproduzierbar `residual-start-not-aligned` zurück, weil
  die Reopt-Trigger-Zeit z. B. „now()" ist und der Schedule
  15-min-Steps nutzt, die typischerweise nicht zur Sekunde am
  Quartal beginnen.
- Eine API-Konsumenten-Spec verlangt „Reopt zum aktuellen
  Zeitpunkt, nicht zum nächsten Step-Boundary".
- Telemetrie zeigt nicht-triviale Failed-Run-Rate mit
  `residual-start-not-aligned` als TerminationCode.

**Scope-Skizze** (wenn der Trigger zündet):

- Implementierungswahl 1 — **Snap-Forward**: `residualStart` wird
  intern auf den nächsten Step-Boundary `≥ residualStart`
  hochgesetzt. Die Window dazwischen bleibt unverändert (also als
  Past-Window erhalten). Konservativ: optimiert weniger statt
  potenziell falsch viel.
- Implementierungswahl 2 — **Window-Split mit Power-Erhaltung**:
  das Straddling-Window wird in zwei Teile gesplittet
  (`[Start, residualStart)` als Past mit unverändertem
  `TargetPowerKw`, `[residualStart, End)` als Future-Optimierungs-
  Eingang).
- Tests: alignment-Toleranz-Pin (Snap-Forward), Negative-Test (
  noch immer Failed bei extremer Misalignment z. B. < 1 ms vor
  Step-Boundary?).

**Aufwandsschätzung:** ~1-2 Tage. ~100-150 LOC inkl. Tests.

**Aktivierungs-Pfad:** kleiner Slice, möglicherweise Carve-out im
auslösenden Plan (z. B. wenn ein Operator-Slice F-01 zieht und
beide Items zusammen abdeckt). Sonst eigener `plan-RM-M4-01-FUP-
alignment-tolerance.md`.

---

## Item F-03: Persistente ACK-Tracking über Reconnect

**Quelle:** RM-M4-06 Design-Entscheidung D-02 — `MqttCommandSink`
hält das Pending-Command-Tracking (`ConcurrentDictionary<commandId,
TaskCompletionSource>`) ausschließlich in-process. Reconnect oder
Process-Restart vor ACK-Eintreffen verliert die Pending-Liste; die
betroffenen Commands laufen in `ack-timeout`.

**Trigger** (eines reicht):

- Production-Deployment zeigt häufige Broker-Disconnects (z. B.
  flatternde Netzwerk-Strecke zum Inverter-Broker, Pod-Recreation
  während laufender Commands), und die resultierenden
  `ack-timeout`-Failed-Runs erreichen eine operativ relevante Rate.
- Compliance-/SLA-Anforderung: „kein produktiv-akzeptierter Command
  darf zwischen Publish und ACK verloren gehen" — der heutige
  in-process Speicher ist dem nicht gewachsen.
- Multi-Replica-Deployment: Replica A publisht Command, Replica B
  empfängt das ACK (Subscriber-Sharding) — heute überhaupt nicht
  unterstützt, würde persistente Cross-Replica-Korrelation
  brauchen.

**Scope-Skizze** (wenn der Trigger zündet):

- Neue `IPendingCommandStore`-Application-Schicht-Schnittstelle
  (Append on Publish, Resolve on ACK, Expire-on-Timeout).
- Persistente Variante (Dapper/Postgres) als M4-Folge — würde
  RM-M3-FUP-01 als ersten echten Schema-Migrationskonsumenten
  nutzen, falls FUP-01 bis dahin nicht anderweitig zündet.
- Reconcile-on-Reconnect: beim Reconnect die persistierten
  Pending-Commands re-laden, ACK-Subscription neu aufbauen, ACKs
  die zwischen Disconnect und Resubscribe ausgesendet wurden gehen
  trotzdem verloren (Broker behält je nach Session-Settings nichts
  vor).
- Time-bounded-Replay: Pending-Commands älter als Schwelle X
  werden als `ack-timeout-after-restart` gemarkiert und entfernt
  statt unendlich gehalten.
- Tests: parallel zwei Pending, Reconnect zwischen Publish und ACK,
  Cross-Replica-Korrelation falls in Scope.

**Aufwandsschätzung:** grob 1-2 Wochen. ~400-600 LOC inkl. Tests
und Migration-Drafts wenn Persistenz dabei ist; ~150-250 LOC für
eine reine in-memory-mit-Reconcile-on-Reconnect-Variante.

**Aktivierungs-Pfad:** eigener `plan-RM-M4-06-FUP-persistent-ack.md`.

---

## Item F-04: TLS und Broker-Auth-Härtung

**Quelle:** RM-M4-06 Design-Entscheidung D-01 — `MqttNetClient`
spricht plaintext-TCP zum Broker. **Pflicht-Slice bevor der Adapter
in Production gegen einen echten Broker zeigt.** Dieser Slice ist
nicht „nice-to-have" — die heutige Konfiguration darf produktiv
nicht laufen.

**Trigger** (eines reicht):

- Erstes Production-Deployment gegen einen realen Broker
  (Inverter-Hersteller-Broker, TSO-Broker, internes
  Production-Mosquitto-Cluster).
- Compliance-/Security-Audit verlangt verschlüsselte
  Broker-Verbindung und authentisierten Client.
- Penetration-Test deckt das Plaintext-Risiko explizit auf.

**Scope-Skizze** (wenn der Trigger zündet):

- `MqttClientOptionsBuilder.WithTlsOptions(...)`: Cert-Validation
  gegen einen konfigurierten CA-Bundle (nicht system-default um
  hostile-default-Trust zu vermeiden), Server-Cert-Hostname-Check.
- `MqttAdapterOptions.Tls` neuer Property-Block: `Enabled` (Default
  `true` in Production, `false` nur über expliziten
  `AllowPlaintextReason` mit Runtime-Profile-Gating analog zu
  RM-M4-05 OPC-UA-Security).
- Username/Password aus `IConfiguration`: fail-closed bei fehlenden
  Credentials in Production-Profile, Klartext-Konfig nur in
  Development.
- Optional: Client-Cert-Authentication als Alternative zu
  Username/Password (industrie-üblich für IoT-Broker).
- Doku in `docs/user/quality.md` oder neue
  `docs/user/security.md`: Broker-CA-Bundle-Verwaltung,
  Credential-Rollover-Workflow.
- Update von `MqttNetClient.cs:12-14` SECURITY-Kommentar.
- Tests: TLS-Handshake gegen Test-Broker mit selbst-signiertem
  Cert, Negativtest für Cert-Mismatch, Negativtest für
  Production-Profile mit Plaintext-Config (Startup-Failure),
  Username/Password fail-closed bei leerer Config.

**Aufwandsschätzung:** grob 1 Woche. ~300-500 LOC inkl. Tests.
Möglicherweise eigene Welle „Production-Hardening / Security",
die TLS/Auth-Härtung für **alle** Adapter (Modbus, MQTT, OPC-UA
mit RM-M4-05) bündelt.

**Aktivierungs-Pfad:** eigener `plan-RM-M4-06-FUP-tls-auth.md`,
oder als Pflicht-Bestandteil eines übergreifenden
Production-Hardening-Slice.

---

## Item F-06: Explizite ExactlyOnce-Acknowledgement-Gate

**Quelle:** RM-M4-06 Design-Entscheidung D-03 — `ExactlyOnce` (QoS 2)
ist heute voll konfigurierbar via `MqttAdapterOptions.QoS`, aber es
gibt keine Startup-Validierung. „Warn-don't-block" ist die heutige
Politik; wer ExactlyOnce will, kann es ohne Hindernis setzen.

**Trigger** (eines reicht):

- Eine Compliance-/Audit-Anforderung verlangt dass `ExactlyOnce`
  nur über expliziten Acknowledgement-Mechanismus (analog zum
  geplanten OPC-UA `AllowUnsecured=true`/`AllowUnsecuredReason`-
  Pattern aus RM-M4-05) aktiviert werden darf — z. B. um
  versehentliches Aktivieren auf Production-Profilen zu
  verhindern.
- Operations-Reibung: ExactlyOnce-Overhead (PUBREC/PUBREL/PUBCOMP-
  Round-Trip) ist auf Production-Last sichtbar, und Operator
  fragt nach einem strukturellen Hinweis statt nur Plan-Prosa.
- Eine Telemetrie-Auswertung zeigt unbeabsichtigte ExactlyOnce-
  Konfiguration in Production-Deployments.

**Scope-Skizze** (wenn der Trigger zündet):

- Neuer `AllowExactlyOnce: bool`-Flag plus `AllowExactlyOnceReason:
  string?` in `MqttAdapterOptions` analog zum geplanten OPC-UA-
  `AllowUnsecured`-Pattern.
- Startup-Validierung in der Host-Bootstrap: wenn irgendein
  QoS-Slot auf `ExactlyOnce` gesetzt ist, MUSS `AllowExactlyOnce=true`
  plus nicht-leerer `AllowExactlyOnceReason` gesetzt sein, sonst
  Startup-Failure mit strukturierter Diagnose.
- Tests: Negativtest für ExactlyOnce-ohne-Flag (Startup-Throw),
  Positivtest für ExactlyOnce-mit-Flag-und-Reason (durchläuft).

**Aufwandsschätzung:** ~30-50 LOC inkl. Tests.

**Aktivierungs-Pfad:** möglicherweise Carve-out im RM-M4-05-OPC-UA-
Security-Slice (gleiches Pattern); sonst eigener kleiner
`plan-RM-M4-06-FUP-exactly-once-gate.md`.

---

## Item F-07: OPC-UA-Mapping-Migration v1→v2 (Template-Slice)

**Quelle:** RM-M4-07 Design-Entscheidung D-02 — `opcua-mapping.
schema.json` ist heute auf `schema_version: ["v1"]` festgenagelt.
Kein Migration-Code im Loader, keine Backward-Compatibility-Route.
Die DoD-Klausel „Loader akzeptiert nur unterstützte Versionen oder
eine explizit getestete Migration/Backward-Compatibility-Route"
ist über die strikte Versions-Akzeptanz abgedeckt; die Migration
selbst entsteht erst beim ersten v2-Format-Bedarf.

**Trigger** (eines reicht):

- TSO- oder Vendor-Spec-Update verlangt eine zusätzliche
  Feldgruppe in der Mapping-Datei (z. B. neue Sicherheits-
  Metadaten, Group-Subscription-Hints, Encoding-Hints für
  binäre NodeIds).
- Operations-Reibung: bestehende v1-Mappings werden so umfangreich
  dass eine strukturelle Reorganisation (z. B. Node-Group-Hierarchie
  statt flacher Liste) operativ wertvoll wird.
- OPC-UA-Adapter (RM-M4-04) findet beim Discovery-Pfad ein Feld,
  das zwingend ins Mapping muss aber im v1-Format keinen Platz hat.

**Scope-Skizze** (wenn der Trigger zündet):

- `schema_version: ["v1", "v2"]` Akzeptanz im Loader.
- Konkrete v1→v2-Migrations-Funktion (read v1, write v2-Shape) plus
  Round-Trip-Test (v1-Datei lädt → v2-internes-Modell → ist
  semantisch identisch zu nativ-v2-Datei).
- Backward-Compatibility-Pfad: bleibt v1-Mapping akzeptiert, ODER
  Deprecation-Warning mit konkretem Trigger-Datum?
- Tests: v1 lädt, v2 lädt, v0 (deprecated) wird abgelehnt, v3
  (future-incompatible) wird abgelehnt, v1→v2-Migration ist
  bit-konsistent.
- Docs: `docs/user/quality.md` oder neue Migration-Anleitung mit
  Operator-Workflow für Mapping-Dateien-Update.

**Wichtig:** F-07 setzt **nicht nur die zweite Version** um, sondern
das **Pattern für alle nachfolgenden Migrations**. Das Migrations-
Test-Harness und die Versions-Akzeptanz-Reihe sind das eigentliche
Asset; der konkrete v2-Inhalt ist sekundär.

**Aufwandsschätzung:** ~300-400 LOC inkl. Migration-Tests und
Beispiel-Datei-Update. Erstmaliger Slice trägt mehr Aufwand
(Pattern-Etablierung); Folge-Migrationen v2→v3 etc. sind
inkrementell ~100-150 LOC.

**Aktivierungs-Pfad:** eigener `plan-RM-M4-07-FUP-mapping-
migration.md`.

---

## Item F-05: MQTTv5-Properties-Adoption

**Quelle:** RM-M4-06 Design-Entscheidung D-04 — der Adapter
nutzt heute MQTTv3.1.1-Shape. v5-spezifische Properties (User
Properties, Reason-Codes, Subscription-Identifier,
Message-Expiry-Interval) sind nicht im Slice.

**Trigger** (eines reicht):

- Broker upgraded auf MQTTv5 und ein konkreter v5-Property-Bedarf
  meldet sich (z. B. User Properties für Multi-Tenant-
  Routing-Metadaten oder strukturierte Reason-Codes für
  Ablehnungs-Diagnostik statt heute frei-text `ack.Reason`).
- TSO-/Hersteller-Spec verlangt v5-spezifische Properties.
- Operator-Reibung: heutige Plaintext-`Reason`-Strings sind
  schlecht maschinen-konsumierbar; v5-Reason-Codes als enum-
  basierter Pfad gefordert.

**Scope-Skizze** (wenn der Trigger zündet):

- `IMqttClient`-Port-Erweiterung: `MqttMessage` bekommt einen
  Properties-Container (User Properties als `IReadOnlyDictionary<
  string, string>`, Reason-Code als enum).
- `MqttCommandSink`/`MqttTelemetrySource`-Mapping-Schicht für
  Properties.
- MQTTnet 5.x-Mapping (technisch unterstützt, nicht aktiviert).
- `MqttAdapterOptions.ProtocolVersion` (Default `V311`, Opt-in
  `V500`).
- Tests: User-Property-Roundtrip, Reason-Code-Mapping,
  Backward-Compatibility-Pfad für v3.1.1-Broker.

**Aufwandsschätzung:** grob 1-2 Wochen. ~250-400 LOC inkl.
Tests.

**Aktivierungs-Pfad:** eigener `plan-RM-M4-06-FUP-mqttv5.md`.

---

## Item F-09: OPC-UA-Activation-Source-Adapter (incl. Failover-Replay-via-Reconnect)

**Quelle:** RM-M4-03 D-06 / §9 (Source-Adapter für Aktivierungssignal-
Empfang sind eigene Slices), RM-M4-04 D-05 (M4-04 deckt nur Telemetrie/
Command, M4-04-Carve-out für OPC-UA-Activation explizit abgelehnt),
RM-M4-08-A D-01/D-03 (M4-08 dupliziert RM-M4-03-Use-Case-Pins nicht via
OPC-UA-Wire; **Failover-Replay-Pin via OPC-UA-Reconnect** erbt hierher).

Heute ist der `IRegelleistungActivationUseCase`-Driving-Port der
Eingangspunkt für Aktivierungssignale; die Use-Case-Schicht ist
wire-agnostisch und durch RM-M4-03 mit ~110 Application-Pins +
12 Persistence-Integration-Pins gepinnt. Was fehlt: ein konkreter
Wire-Source-Adapter, der OPC-UA-Subscriptions auf den Driving-Port
hebelt.

**Trigger** (eines reicht):

- TSO-/Vendor-Spec verlangt Aktivierungssignale auf einem OPC-UA-
  Endpoint (`opc.tcp://...`-Subscribe statt heutige Driving-Port-Form
  via REST/HTTP/Worker-Schedule).
- Operator-Anforderung nach Mid-Stream-Reconnect-Replay-Verifikation
  via OPC-UA — der Master-DoD-Begriff „Failover-Replay" aus RM-M4-08
  koppelt sich an OPC-UA-Reconnect-Re-Delivery (`plan-RM-M4.md:295-
  298`); ohne Wire-Source-Adapter ist dieser Pfad heute strukturell
  nicht testbar.
- Konkrete Operator-Sicht-Anforderung nach Aktivierungsjitter-
  Profilen über OPC-UA (Timestamp-Drift-Pins, Subscription-
  Buffering-Reorder).

**Scope-Skizze** (wenn der Trigger zündet):

Drei Sub-Bullets, die zusammen die F-09-Lieferung definieren:

- **(a) OPC-UA-Activation-Source-Adapter selbst**:
  Implementiert `IRegelleistungActivationSource` über eine
  OPC-UA-Subscription auf einen Aktivierungs-NodeId; pro
  Subscribe-Notification wird ein `RegelleistungActivation`-Domain-
  Objekt zusammengebaut (NodeId-Mapping via Activation-Mapping-Schema
  analog zur Telemetrie-Mapping-Linie aus M4-07) und an
  `IRegelleistungActivationUseCase.ReceiveAsync` gehebelt. Reconnect-
  und Mid-Stream-Recovery-Mechanik ist von RM-M4-04-D bereits
  implementiert (`OpcUaTelemetrySource` Pattern); Reuse via einer
  gemeinsamen Recovery-Schicht oder per Sub-Source-Pattern.

- **(b) Failover-Replay-Pin via OPC-UA-Reconnect**:
  Pin-Test gegen die Embedded TestServer-Fixture aus RM-M4-04-D
  (oder eine neue F-09-spezifische Fixture). Sequenz: Server emittet
  Aktivierung mit `payload_hash=H1` → Use-Case akzeptiert → Server
  kappt die Session → F-09-Source reconnectet → Server liefert
  dieselbe Aktivierung erneut → Use-Case erkennt sie als Replay-
  Idempotent (gleicher `payload_hash` → `Accepted`-Outcome
  `Replay_Idempotent` aus M4-03-B). Diese Sub-Obligation erbt den
  in RM-M4-08-A explicit-deferred Master-DoD-Begriff „Failover-
  Replay aus persistentem Dedupe-Tracker" und macht ihn testbar.

- **(c) Aktivierungsjitter-Profile-Pins**:
  Pin-getestet mit verschiedenen `valid_from`/`valid_until`-Skews
  gegen `IClock.UtcNow` (M4-03 hat das auf der Use-Case-Schicht;
  hier auf der Wire-Schicht), Timestamp-Drift gegen
  `RegelleistungOptions.FutureSkewTolerance`,
  Subscription-Buffering-Reorder (mehrere Notifications kommen
  out-of-order an, Tiebreak via `§148`-Logik bleibt deterministisch).

**Aufwandsschätzung:** grob 2-3 Wochen. ~600-1000 LOC inkl.
Tests. Sub-Slice-Aufteilung wahrscheinlich (Adapter / Failover-Pin /
Jitter-Pins).

**Aktivierungs-Pfad:** eigener `plan-RM-M4-FUP-opcua-activation-
source.md` mit eigenem Detail-Plan und Review-Pass-Pattern.

---

## Item F-17: OPC-UA-Security-Policy-Allowlist-Erweiterung

**Quelle:** RM-M4-05 §3 + D-04 (Allowlist-Erweiterung verlangt
Plan-Änderung — kein Magic-Config-Knopf).

Heute (Post-M4-05): `OpcUaSecurityPolicies.IsAllowed` ist eine
statische Klasse mit einer hart codierten Allowlist. Die einzige
heute zugelassene Policy ist `Basic256Sha256`. Jede zusätzliche
Policy verlangt einen Plan-Slice, einen Code-Change am Adapter,
neue Pins und Doku.

**Trigger** (eines reicht):

- TSO-/Vendor-Spec verlangt eine Policy außerhalb von
  `Basic256Sha256` (z.B. `Aes128Sha256RsaOaep`,
  `Aes256Sha256RsaPss` für modernere Vendor-Stacks).
- Migration weg von `Basic256Sha256` weil die Policy aus Sicherheits-
  Gründen retired wird.

**Scope-Skizze** (wenn der Trigger zündet):

- (a) Policy-Konstante in `OpcUaSecurityPolicies` ergänzen mit
  full URI.
- (b) `IsAllowed`-Pin um den neuen Policy-String erweitern;
  `OpcUaAdapterOptions.EnsureValid`-Pin pro neuer Policy (Sign +
  SignAndEncrypt jeweils).
- (c) Embedded TestServer-Fixture um die Policy erweitern
  (`AddPolicy(SecurityMode, Policy)` analog zur heutigen
  `AddSignAndEncryptPolicies`-Linie).
- (d) Quality-Doku §2.2.2 dokumentiert die erweiterte Allowlist.
- (e) Ein neuer Integration-Pin in `OpcUaSecurityTests.cs`, der
  einen sicheren Handshake mit der neuen Policy gegen den
  Embedded TestServer fährt (Trust-Bridge gilt unverändert).

**Aufwandsschätzung:** ~0.5-1 Tag pro Policy (Code + Tests +
Doku). Skaliert linear mit der Anzahl der Policies.

**Aktivierungs-Pfad:** entweder als kleinen Patch direkt am Adapter
mit Carve-out-Plan-Eintrag in der jeweiligen Roadmap-Zeile, oder
als eigenes `plan-RM-M4-FUP-opcua-policy-{name}.md` wenn mehrere
Policies gleichzeitig anstehen.

---

## Item F-18: OPC-UA-Cert-Rotation/Renewal

**Quelle:** RM-M4-05 §3 + §9 (Cert-Rotation/Renewal-Workflows
explizit aus M4-05-Scope ausgeklammert — heute geht M4-05 davon
aus, dass Certs statisch sind und der Operator manuell re-deployt).

Heute: Der OpcUaClient lädt die App-Cert beim ersten
`EnsureApplicationConfiguredAsync` und der Trusted-Peer-Store
wird einmalig beim Start aufgelöst. Eine Cert-Lifecycle-Änderung
(Operator kopiert eine neue Server-Cert in den Trusted-Pfad,
Vendor rotiert sein Server-Cert) wird **nicht** erkannt; ein
Re-Trust verlangt Process-Restart.

**Trigger** (eines reicht):

- Erstes Cert-Lifecycle-Event in der Operator-Praxis (Validity-
  Period läuft ab; Operator-Team will Re-Trust ohne Restart).
- Vendor rotiert Server-Cert proaktiv (Compliance-Anforderung);
  Adapter muss reload-en, bevor der bestehende Trust expired.
- Operator-Anforderung nach Hot-Reload des
  `TrustedServerCertificatesPath`-Inhalts.

**Scope-Skizze** (wenn der Trigger zündet):

- (a) Cert-Watcher auf dem Trusted-Store-Path
  (`FileSystemWatcher` oder Polling) mit Throttle (Multi-Event-
  Bursts während Cert-Dateischreibens schlucken).
- (b) `OpcUaClient.ReloadCertificatesAsync()`-Pfad ruft
  `appConfig.CertificateValidator.UpdateAsync(...)` und löst
  ein optionales Force-Reconnect aus, wenn die aktuell aktive
  Session-Cert nicht mehr in der refreshten Trust-Liste ist.
- (c) Pin-Test gegen Embedded TestServer mit Cert-Swap mid-stream:
  Server bekommt ein neues Cert, alte Cert wird aus Client-Trust
  entfernt → Client soll Re-Trust laden + Session erhalten/
  re-establishen.
- (d) Optional: Pre-Expiration-Warning-Log (EventId 4223?) wenn
  die App-Cert oder eine getrustete Server-Cert in N Tagen
  abläuft.

**Aufwandsschätzung:** ~3-5 Tage inkl. Tests + Edge-Cases (Cert-
File-mid-write, atomic-rename-vs-truncate-detect).

**Aktivierungs-Pfad:** eigener `plan-RM-M4-FUP-opcua-cert-
rotation.md` Slice-Plan.

---

## Item F-19: OPC-UA-User-Identity (UserName/Password / UserToken)

**Quelle:** RM-M4-05 §3 + §9 (User/Token-Identity explizit out-of-
scope: M4-05 fährt mit `UserIdentity=null` (Anonymous);
Server-seitige Authentifizierung jenseits der Cert-basierten ist
aus M4-05-Scope ausgeklammert).

Heute: `OpcUaClient.ConnectAsync` ruft
`DefaultSessionFactory.CreateAsync` mit `identity: null` —
Anonymous-Auth gegen den OPC-UA-Server. Cert-basierter Trust ist
in M4-05 bidirektional gepinnt, aber zusätzliche
User-Authentifizierung (UserName/Password, UserToken,
Kerberos/IssuedToken) ist nicht implementiert.

**Trigger** (eines reicht):

- TSO-/Vendor-Spec verlangt nicht-Anonymous-Authentication am
  OPC-UA-Endpoint (UserName/Password als zweiten Faktor zur
  Cert-Trust, oder Token-basierter Identity-Provider).
- Compliance-Anforderung nach Audit-fähigem Per-User-Session-
  Logging (Anonymous lässt sich nicht auf konkrete Operator-
  Accounts mappen).

**Scope-Skizze** (wenn der Trigger zündet):

- (a) `OpcUaAdapterOptions.UserIdentity`-Slot als neuer
  `OpcUaUserIdentityOptions`-Record (UserName + Password aus
  Secret-Store-Reference, oder UserToken-Bytes, oder IssuedToken-
  Endpoint). Default `null` ⇒ Anonymous (heutiges Verhalten).
- (b) `OpcUaClient.ConnectAsync` materialisiert die Identity via
  SDK-`UserIdentity`-Konstruktor und reicht sie an
  `DefaultSessionFactory.CreateAsync(identity: ...)` durch.
- (c) Secret-Resolver-Strategie: wir wollen keine Klartext-
  Passwörter in der Config — `UserIdentity` referenziert einen
  Secret-Identifier (z.B. `secret://op-tso-1`), und ein
  `IOpcUaSecretResolver`-Driven-Port löst ihn beim Connect auf.
  Default-Implementation z.B. Environment-Variable oder File-
  basiert; Production-Impl via HashiCorp-Vault o.ä. ist eigene
  Carve-out-Linie.
- (d) Pin-Test gegen Embedded TestServer mit UserName-Token-
  Policy: Server bekommt eine User-Token-Policy (Username/
  Password); Client connectet mit korrektem Credential →
  Session aktiv; Client mit falschem Credential → ServiceResult
  „BadIdentityTokenRejected" wird in unsere kebab-case-Reason
  gewrappt.

**Aufwandsschätzung:** ~1 Woche inkl. Tests + Secret-Resolver-
Driven-Port + Doku. Production-Secret-Resolver (Vault o.ä.) ist
weitere ~1 Woche, eigene Linie.

**Aktivierungs-Pfad:** eigener `plan-RM-M4-FUP-opcua-user-
identity.md` Slice-Plan.

---

## Trigger-Watch-Disziplin

Diese Notiz wird **nicht aktiv abgearbeitet**. Sie wird gescannt:

- Beim Beginn jedes neuen M4-Slice-Plans (insbesondere wenn der
  Slice Operator-Workflows, API-Endpoints oder Schedule-Lebenszyklus
  berührt).
- Beim quartalsweisen Architektur-Review.
- Bei jedem Production-Vorfall-Postmortem rund um Intraday-Reopt
  oder DA→ID-Übergänge.

Beim Zünden eines Triggers:

1. Item aus dieser Notiz extrahieren.
2. Eigenen Slice-Plan in `docs/plan/planning/open/` anlegen (Name
   `plan-RM-M4-01-FUP-NN.md` für RM-M4-01-Folgen, oder analoger
   Name für andere M4-Slices).
3. Item-Eintrag hier mit Verweis auf den neuen Plan markieren oder
   nach Abschluss entfernen.
4. Roadmap-„Aktueller Stand"-Block ergänzen.

So bleibt die Trigger-Liste lebendig statt in Plan-Tabellen-
Kommentaren zu verschwinden.
