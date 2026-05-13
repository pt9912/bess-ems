# ADR 0005 — Optimization-Core Sidecar: Transport (gRPC)

**Status:** Accepted — gRPC über HTTP/2 ist der Sidecar-Transport
für den `optimization-core` (Phase 3 gemäß
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§13.1). Schließt `AR-OPEN-002`
([`../../../spec/architecture.md`](../../../spec/architecture.md)
§18). Voraussetzung für die Aktivierung von
[`../planning/done/plan-RM-M5.md`](../planning/done/plan-RM-M5.md)
(RM-M5-01 Sidecar-Vertrag).
**Datum:** 2026-05-11
**Bezug:**
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§13.1 (Phasenmodell), §13.2 (Bibliothek vs. Sidecar), §18
(AR-OPEN-002),
[ADR 0004 — Native Control Kernel: Process Isolation](0004-native-kernel-process-isolation.md)
(§3 Alternativen-Vergleich gRPC vs Unix-Socket/Shared-Mem/REST/fork+pipe,
§4 Trigger 2 = Phase-3-Komponenten),
[`../planning/done/plan-RM-M5.md`](../planning/done/plan-RM-M5.md)
(§Aktivierungsbedingungen, §Sidecar-Status-Taxonomie,
§Contract-Versionen Und Rollout),
[`../../user/quality.md`](../../user/quality.md) §2.6 (Container-
Gates, §5.2 Native/Sidecar).

---

## 1. Kontext

`spec/architecture.md` §13.1 nennt für Phase 3 explizit „Native
Sidecar via gRPC" (MPC, State-Space, Solver-Anbindung) und §13.2
führt gRPC als den Sidecar-Pol in der Library-vs-Sidecar-Tabelle.
ADR 0004 (Process Isolation für den `battery_control_core`) hat
gRPC als Phase-3-Pfad analysiert (§3 „Out-of-Process Sidecar via
gRPC (deferred, Phase-3-Pfad)") plus die drei Hauptalternativen
(Unix-Socket-only, REST/HTTP, fork+pipe) **explizit für M3
verworfen** mit konkreter Begründung — der Phase-3-Transport
selbst ist dort aber bewusst offen gelassen worden, weil ADR 0004
nur die In-Process-Wahl für die M3-Surface (Constraint, Ramp, PID)
entscheidet.

`spec/architecture.md` §18 trägt diesen Rest-Punkt als
**AR-OPEN-002 — gRPC vs. REST-only für externe Optimierungs-
Sidecars in Phase 3? — Offen** seit M2.

`plan-RM-M5.md` machte in den Aktivierungsbedingungen die
Schließung von AR-OPEN-002 zum **harten Blocker** vor RM-M5-01-
Implementierung (Tabellen-Zeile „Transportentscheidung" in
§Aktivierungsbedingungen, plus §Sequenz Schritt 1: „ADR 0004 oder
Architektur §13 muss die finale Transportentscheidung inklusive
Security-, Mocking- und CI-Konsequenzen enthalten."). Plan-RM-M5
ist im Übrigen **transportneutral** geschrieben: Sidecar-Status-
Taxonomie spricht von „normiertem Transportstatus" (`success`,
`deadline_exceeded`, `unavailable`, `cancelled`,
`invalid_request`, `internal_error`), nicht von gRPC-Codes — das
konkrete Transport-Mapping ist ein eigenes versioniertes Artefakt
(siehe §3 unten).

Diese ADR schließt AR-OPEN-002 mit gRPC als formaler Adoption,
nennt die Security-Achse + Mocking-Strategie + CI-Konsequenzen
und benennt die Trigger für einen späteren Transport-Pivot
(Phase 4: Shared Memory / Edge Controller gemäß §13.1).

---

## 2. Entscheidung

| Achse | Entscheidung | Pin / Trigger |
| ----- | ------------ | -------------- |
| Transport | **gRPC über HTTP/2** (Protobuf-Wire, Grpc.Net.Client als .NET-Adapter, Grpc.AspNetCore oder Drittsprach-gRPC-Server als Sidecar-Endpunkt) | RM-M5-01 Contract-Slice; ProtoBuf-Service in `proto/optimization-core/v1/*.proto` mit `contract_version`-Feld |
| Netz-Topologie (Default) | **Unix Domain Socket** (`unix:///var/run/bess-ems/optimization-core.sock`) für Loopback-Deployments (Single-Host Worker + Sidecar) | M5-Default-Compose-Topologie; Filesystem-Permissions (Owner=`bess`, Mode=0600) als AuthZ-Erstschicht |
| Netz-Topologie (Opt-in Cross-Host) | **TCP über mTLS** mit Client- und Server-Cert (Trusted-CA pro Deployment), wenn der Sidecar auf eigener VM/Pod läuft | Konfig-Slot `SidecarEndpoint = "unix:/..."` oder `"https://..."`; mTLS-Cert-Material kommt aus dem Operator-Provisioning analog zur OPC-UA-PKI (RM-M4-05 D-06 Pattern) |
| AuthN/AuthZ | mTLS (Cross-Host) oder Filesystem-Permissions (UDS) **als Pflicht-Erstschicht**; per-RPC Bearer-Token-Header optional als zweite Schicht | RM-M5-01 Security-Freeze; Negativ-Pin: unautorisierter Client (kein Cert, falscher CN, fremder UDS-Owner) → `unauthorized_client` (Sidecar-Status-Taxonomie) |
| Contract-Versionierung | Health/Version-RPC liefert `contract_version`, `min_compatible_version`, `max_compatible_version` + Feature-Flag-Liste. Inkompatible Version blockiert Sidecar-Aktivierung hart pre-Request (Fallback-Pfad). | plan-RM-M5 §Contract-Versionen Und Rollout; Mixed-Version-Pins als RM-M5-01-Pflicht |
| Mocking-Strategie | In-Process `TestSidecarHost` via Grpc.AspNetCore + `WebApplicationFactory`-Pattern (analog zur OPC-UA `EmbeddedTestServerHost`-Linie aus M4-04-D / M4-08-A); keine externen Container im Unit-Test-Pfad | `make test-hil-optimization-core` als neues Pflicht-Gate (RM-M5-01-D oder RM-M5-06) |
| Streaming/Cancellation | gRPC-Server-Streaming für lange Solver-Läufe (Progress-Updates) + Client-side `CallOptions.CancellationToken` für Deadline-Propagation; Sidecar muss `CancellationToken` honorieren | plan-RM-M5 §Sidecar-Status-Taxonomie Zeile `cancelled` durch Caller; RM-M5-01-Test mit explizitem Mid-Stream-Cancel |
| Transport-Pivot (Phase 4) | **Trigger-getrieben** (§4 unten); Shared Memory / UDS-mit-eigenem-Wire-Format gemäß §13.1 Phase 4 | Performance-Trigger oder Edge-Controller-Pfad (LH-RT-004) |

---

## 3. Achse 1 — Transport-Optionen

Die Latenz-, Tooling- und Topologie-Trade-offs sind in ADR 0004 §3
ausführlich für den Native-Control-Kernel-Kontext analysiert
(`gRPC über Loopback`, `Unix-Domain-Socket`, `Shared Memory`,
`fork+pipe`, `REST/HTTP`, `WebAssembly-Sandbox`). Diese ADR
referenziert die dort dokumentierten Werte und ergänzt nur die
**MPC-/Solver-spezifischen** Aspekte, die ADR 0004 (das auf
Sub-µs-Constraint/Ramp-Calls optimiert) nicht erfasst.

### gRPC (gewählt für RM-M5-01)

Konkrete Wins für die **MPC-/Solver-Surface**:

- **Streaming-Vertrag.** MPC-Läufe können progressive Trajektorie-
  Updates über `stream OptimizeProgress` liefern; Solver-Status-
  Updates während eines mehrsekundigen MILP-Laufs ohne neue
  Round-Trips. Ein REST/HTTP-Pfad bräuchte SSE oder Long-Polling,
  beide mit schwächeren Cancellation-Garantien.
- **Erstklassige Cancellation/Deadline.** `CallOptions.Deadline`
  ist Teil des Wire-Vertrags — der Sidecar bekommt den Deadline-
  Timestamp und kann seinen Solver mit `MathOpt`-Style-Limit
  konfigurieren. Mit REST müsste ein eigener `X-Deadline-Until`-
  Header definiert + dokumentiert werden.
- **Multi-Sprache.** Der Sidecar kann in Python (für SciPy/cvxpy/
  GLPK-Bindings) oder Rust (für klassische MILP-Solver) oder C++
  (HiGHS/OR-Tools-native) laufen, ohne dass die `.NET`-Seite
  Tool-Chain-Setup neu erfinden muss — gRPC + Protobuf ist
  Sprach-Stack-Standard. Das ist Trigger 2 aus ADR 0004 §4
  („Phase-3-Komponenten kommen in Scope") in seiner praktisch-
  konkreten Form.
- **Health/Reflection.** `grpc.health.v1.Health` und `grpc.reflection.v1`
  sind Standard-Services — Container-Orchestration-Probes
  (`grpc_health_probe`, k8s `grpc`-probe-Type) und Operator-
  Debug-Tools (`grpcurl`) sind Standard-Werkzeug.
- **.NET-Tooling.** `Grpc.Net.Client` ist Microsoft-supported, läuft
  über `HttpClient` mit HTTP/2-Backend, integriert mit
  `Microsoft.Extensions.DependencyInjection` über
  `services.AddGrpcClient<T>()`, hat Built-in-Interceptors für
  Auth/Tracing/Retry, und `Grpc.AspNetCore.HealthChecks` integriert
  mit den bestehenden `IHealthCheck`-Patterns aus RM-M1-15.

Konkrete Trade-offs:

- **Protobuf-Toolchain im Build-Pfad.** `protoc` (oder
  `Grpc.Tools` NuGet-Source-Generator) muss im SDK-Image laufen.
  Schon für den OPC-UA-Stack haben wir den OPC-Foundation-Codegen
  im Build — eine weitere Codegen-Quelle ist akzeptabel. Vermeidet
  manuelle DTO-Bauerei für `OptimizationRequest`/`Response`.
- **HTTP/2-Stack.** UDS-mit-HTTP/2 funktioniert in .NET 8+ über
  `SocketsHttpHandler.ConnectCallback` — getestetes Pattern. Über
  TCP ist HTTP/2 mit TLS Standard.
- **Latenz-Hit.** Plan-RM-M5 §Replay-Kompatibilität und
  §Fallback-Matrix nennen Deadline/Timeout explizit als Erst-
  Klasse-Fall — die p50-Latenz von 200 µs–2 ms über Loopback
  ist für einen Solver-Lauf (Größenordnung 50 ms – mehrere
  Sekunden) im Rauschen. Für die MPC-Real-Time-Linie (LH-CTRL-005/
  006) bleibt der In-Process-Pfad aus ADR 0004 die produktive
  Linie; gRPC ist explizit für die **größeren, weniger zeitkritischen**
  Kerne (MPC-Optimierung pro Schritt, MILP-Schedule-Optimierung).

### Verworfene Alternativen

ADR 0004 §3 hat folgende für **M3** verworfen; die hier
relevanten Beobachtungen für die **M5-Sidecar-Surface**:

- **REST/HTTP+JSON.** Latenz-Klasse identisch zu gRPC, aber
  schwächeres Schema-Tooling (kein Codegen für versionierte
  DTOs ohne OpenAPI-Pipeline), keine Streaming-Cancellation-
  Semantik, kein Standard-Health-Probe. Plan-RM-M5 §Sidecar-
  Status-Taxonomie bräuchte mit REST eine eigene Status-Code-
  Mapping-Tabelle pro Endpoint; mit gRPC ist sie Wire-Standard.
- **Unix-Socket-mit-eigenem-Wire-Format.** Niedrigste Latenz
  (30–100 µs), aber eigene Versionierung + Backward-Compat-
  Disziplin + Streaming/Cancellation-Eigenbau. Für Phase 3 ist
  der Latenz-Win nicht der treibende Faktor (siehe oben); das
  Mehrwerk gegenüber gRPC ist nicht zu rechtfertigen. Bleibt als
  Phase-4-Option (`spec/architecture.md` §13.1) wenn die MPC-Linie
  zu einer Latenz-kritischen Inner-Loop wird.
- **fork+pipe.** Plattform-spezifisch (Unix-only),
  Drittsprach-Solver-Bindings (Python/Rust) brauchen meist
  selbst einen Server-Lifecycle (Solver-Initialisierung,
  Connection-Pooling) — fork+pipe ist nicht der natürliche Fit
  für stateful Solver-Prozesse.
- **WebAssembly-Sandbox.** Sub-Process-Crash-Isolation als
  Linear-Memory-Sandbox im Host — Trigger 6 aus ADR 0004 §4
  (Zertifizierung). Hat heute keinen RM-M5-Trigger; bleibt eigene
  Folge-ADR wenn Sandbox-Pflicht zündet.

---

## 4. Achse 2 — Security-Modell

`plan-RM-M5.md` §Aktivierungsbedingungen Zeile „Security-Freeze"
verlangt vor RM-M5-01-Freeze: AuthN/AuthZ, verschlüsselter
Transport oder geschützter lokaler Socket, Secret-Handling,
Negativtests für unautorisierte Clients. Diese ADR fixiert die
Achse:

### Default: Unix Domain Socket + Filesystem-Permissions

- **Adresse:** `unix:///var/run/bess-ems/optimization-core.sock`
  (Pfad konfigurierbar pro Deployment).
- **Owner/Mode:** Socket gehört dem `bess`-User des Worker-
  Containers; Mode `0600` (Owner-Read/Write-only). Sidecar läuft
  als selber User, oder als Member einer dedizierten Gruppe mit
  Mode `0660`.
- **Container-Topologie:** Worker + Sidecar im selben Pod
  (Kubernetes) oder im selben Compose-Network mit Shared-Volume
  für den UDS-Pfad. **Kein** TCP-Listener öffnet sich nach außen.
- **Mocking-Strategie:** Test-Sidecar bindet einen UDS in einem
  per-Test-Tempverzeichnis (`Path.GetTempPath()/BatteryEms/
  OptimizationCore/{Guid:N}.sock`) — analog zur OPC-UA-PKI-
  Konvention aus M4-04 Review-Fix H5.
- **Negativ-Pin (RM-M5-01-Pflicht):** Sidecar startet mit Mode
  `0644` oder Owner ≠ `bess` → Worker-Konnektor wirft
  `unauthorized_client` (Sidecar-Status-Taxonomie). Filesystem-
  Permission-Verletzung ist Boot-Fehler, nicht Soft-Fail.

### Opt-in Cross-Host: TCP + mTLS

- **Adresse:** `https://optimization-core.internal:8443` (oder
  äquivalent). HTTP/2 über TLS ist Pflicht; HTTP/2-Cleartext (`h2c`)
  über TCP **nicht** erlaubt im Production-Profile.
- **Cert-Material:** Operator stellt CA-Cert + Client-Cert (für
  den Worker) und Server-Cert (für den Sidecar). Analog zur
  OPC-UA-PKI-Linie aus RM-M4-05 — Cert-Trust ist Operator-
  Provisioning, nicht Auto-Accept. Konfig-Slots:
  `SidecarTrustedCaPath`, `SidecarClientCertPath`,
  `SidecarClientKeyPath` (oder PKCS#12-Container).
- **Validation:** Client-Cert-Subject-Alternative-Name wird
  vom Sidecar geprüft (Whitelist konfigurierbar); Server-Cert-
  Subject-CN wird vom Worker geprüft (Pinning). Beide Seiten
  reject untrusted Certs ohne Auto-Accept (analog zum M4-05-
  `Production`-Profile aus `OpcUaAdapterOptions`).
- **Negativ-Pin (RM-M5-01-Pflicht):** Worker mit falschem Client-
  Cert → `unauthorized_client`; Worker ohne TLS gegen einen
  TLS-only-Sidecar → Connection-Refused → `sidecar_unavailable`
  (Fallback-Matrix-Pfad).

### Sekundäre Schicht (optional): Per-RPC Bearer-Token

- **gRPC-Metadata-Header:** `authorization: Bearer <token>` per
  `CallCredentials`. Token-Material aus dem Operator-Secret-
  Store (analog zur F-04-MQTT-Auth-Linie aus RM-M4-06).
- **Wann notwendig:** Sidecar-Service teilt sich Identität
  zwischen mehreren EMS-Worker-Instanzen (z.B. Multi-Tenant
  Future-Pfad RM-M6); ohne diesen Bedarf reicht mTLS- oder
  UDS-Erstschicht. Token-Auth ist also **eingebaut als
  Interceptor-Slot**, aber nicht Pflicht für RM-M5-01-Single-
  Tenant.

### Secret-Handling

- **Cert/Key/Token-Material** wird nicht in `appsettings.json`
  abgelegt — Pfade ja, Inhalte nein. Container-Secrets via
  Kubernetes-Secret-Volume oder Docker-Compose-Secret. Plan-
  RM-M5 §Aktivierungsbedingungen Zeile „Security-Freeze" pinnt
  das als RM-M5-01-Pflicht.

---

## 5. Achse 3 — Contract-Versionierung und Rollout

`plan-RM-M5.md` §Contract-Versionen Und Rollout listet die
Verträge. Diese ADR fixiert die gRPC-spezifische Umsetzung:

| Thema | gRPC-Umsetzung |
| ----- | --------------- |
| Service-Layout | `proto/optimization-core/v1/optimization_core.proto` mit `service OptimizationCore { rpc Health(...); rpc Version(...); rpc Optimize(...) returns (stream OptimizeProgress); rpc OptimizeMpc(...); }`. Major-Version im Paket-Namespace (`bess.optimization_core.v1`). |
| Contract-Version-Feld | `VersionResponse { string contract_version = 1; string min_compatible_version = 2; string max_compatible_version = 3; repeated string features = 4; }` — SemVer-Strings, Worker und Sidecar prüfen Mutual-Range. |
| Feature-Flags | `features` enthält per Convention Kebab-Case-Strings wie `has-usable-solution`, `deterministic-seed`, `auth-bearer-token`. Worker prüft Pflicht-Features pre-Request. |
| Inkompatibilität | Vor jedem ersten Optimize-Call: Health+Version-Probe → Compat-Check → bei Mismatch `failed_no_activation` + lokaler Fallback gemäß plan-RM-M5 §Fallback-Matrix. **Kein** Sidecar-Request bei inkompatibler Version. |
| Mixed-Version-Test-Surface | RM-M5-01-Pflicht: Test-Sidecar in vier Konfigurationen (Worker alt/Sidecar neu, Worker neu/Sidecar alt, unbekannte Major-Version, fehlendes Feature-Flag). Jede Konfig zündet eigenen Fallback-Pin. |

### Codegen + Source-Versionierung

- **Source-of-Truth:** `proto/optimization-core/v1/*.proto` im
  Repo (neuer Top-Level-Pfad). Versionierung via Major-Namespace;
  Breaking Changes erzwingen `.v2/`-Schwester-Verzeichnis.
- **Worker-Codegen:** `Grpc.Tools` NuGet-Source-Generator im
  `BatteryEms.Adapters.OptimizationCore`-Adapter-Projekt
  (analog zur OPC-UA-Adapter-Linie); generierte Klassen sind
  `obj/`-Output, nicht committed.
- **Sidecar-Codegen:** sprach-spezifisch (Python via
  `grpcio-tools`, Rust via `tonic-build`, etc.). Sidecar-Repo
  ist eigenes Artefakt; das `.proto` ist gemeinsamer Vertrag.
- **Backward-Compat-Disziplin:** Field-Numbers nie wiederverwenden;
  neue optionale Felder OK; required-Felder nur in Major-Bumps.
  Buf-Lint (oder analoge Linter) im CI.

---

## 6. Achse 4 — Mocking-Strategie und CI

`plan-RM-M5.md` §Aktivierungsbedingungen Zeile „Toolchain"
verlangt: Sidecar-Build und Container-Build reproduzierbar in
Docker/CI. Diese ADR konkretisiert den Test-Pfad:

### In-Process Test-Sidecar

- **Pattern:** `TestOptimizationCoreSidecar : IAsyncDisposable`
  in `tests/integration/BatteryEms.OptimizationCore.IntegrationTests/`,
  startet eine `Grpc.AspNetCore`-Pipeline mit dem generierten
  `OptimizationCoreBase`-Implementierungs-Stub gegen einen
  Test-UDS in `Path.GetTempPath()`. Analog zur
  `EmbeddedTestServerHost`-Linie aus M4-04-D / M4-08-A.
- **Test-Doubles:** zwei Stub-Modi (`OptimalAlwaysSucceedsStub`,
  `ScriptableOutcomeStub`) decken die Sidecar-Status-Taxonomie-
  Tabelle aus plan-RM-M5. Mocking via Server-Side-Stub, **nicht**
  per Channel-Mocking — die Wire-Round-Trip-Disziplin (Protobuf-
  Encode/Decode, gRPC-Status-Codes, Cancellation-Propagation)
  bleibt im Pin.
- **Negativ-Pins:** unautorisierter Client, Server-Cert-Mismatch,
  Inkompatible Contract-Version, Mid-Stream-Cancel, Late-Response-
  nach-Timeout — alle gegen das Test-Sidecar.

### CI-Gate

- **Makefile-Target:** `make test-hil-optimization-core` ist das
  Pflicht-Gate in `make gates` und `make ci`, analog zu
  `make test-hil-opcua` (RM-M4-04 / RM-M4-08).
- **Dockerfile-Stage:** eigener Test-Stage analog zur
  `test-hil-opcua`-Linie.
- **`buf` / Proto-Lint:** Codegen-Hygiene-Gate bleibt post-M5 als
  Trigger-Watch im F-M5-03-/Drittsprach-Sidecar-Pfad; RM-M5-01 und
  RM-M5-06 wurden ohne eigenes Buf-Gate abgeschlossen und leben mit
  dokumentierter Proto-Hand-Disziplin.

### Container-Orchestrierungstest

- RM-M5-06 nutzt Compose-Sidecar-Pattern (analog zu
  `tests/integration/compose.yml` aus M2/M3) mit zwei Services
  (Worker + Sidecar) plus Shared-Volume für den UDS-Pfad.

---

## 7. Trigger für Transport-Pivot (Phase 4)

`spec/architecture.md` §13.1 Phase 4 nennt „Shared Memory / CPU
Pinning / Edge Controller". Konkrete Trigger für einen Pivot weg
von gRPC:

1. **Latenz-Pflicht-Bound.** Wenn die MPC-Inner-Loop p99-Latenz
   unter 1 ms pro Schritt fordert (z.B. weil Primärregelleistung
   oder eine 100-Hz-Inner-Control-Loop on top des EMS landet),
   kippt gRPC aus dem Latenz-Korridor. Antwort: Shared Memory
   mit Lock-Free-Ringbuffer (siehe ADR 0004 §3 Latenz-Werte) oder
   Edge-Controller-Pfad gemäß LH-RT-004.
2. **Drittsprach-Solver-Lifecycle wird zur Kostenstelle.** Wenn
   der Sidecar-Prozess so groß wird dass Cold-Start oder Solver-
   Initialisierung Aufwand kostet, lohnt sich In-Process via
   `embedded`-Variante (z.B. HiGHS direkt in der .NET-Process via
   P/Invoke). Das ist Trigger 7 aus ADR 0004 §4 (Performance-
   Trigger) in seiner Solver-spezifischen Form.
3. **Multi-Asset / Multi-Tenant mit per-Asset-Sidecar.** Wenn
   pro-Asset-Sidecar nötig wird (Trigger 3 aus ADR 0004 §4), kann
   gRPC-Server-Sidecar bleiben — aber die Topologie wird
   komplexer (ein Sidecar pro Asset oder ein Sidecar mit Multi-
   Asset-Routing). Eigene Folge-ADR.
4. **Architektur-Spec-Re-Eval Phase 4.** Wenn `spec/architecture.md`
   §13.1 Phase 4 aktiv wird (Edge-Controller-Anbindung,
   harte-Realzeit-Pfad), wird die Transport-Frage neu gestellt.

Diese Trigger ändern die ADR nicht silent — jeder bekommt eine
eigene Folge-ADR mit Migrations-Plan.

---

## 8. Konsequenzen

### Positiv

- **AR-OPEN-002 ist geschlossen.** Plan-RM-M5 konnte von `open/`
  nach `in-progress/` migrieren; RM-M5-01-Detail-Slice konnte
  Code beginnen. M5 ist seit 2026-05-13 abgeschlossen und der
  Masterplan liegt in `planning/done/`.
- **Sidecar-Status-Taxonomie ist mappbar.** Plan-RM-M5
  §Sidecar-Status-Taxonomie listet `success`/`deadline_exceeded`/
  `unavailable`/`cancelled`/`invalid_request`/`internal_error`
  als „normierten Transportstatus"; gRPC-Status-Codes mappen
  1:1 (`OK`/`DEADLINE_EXCEEDED`/`UNAVAILABLE`/`CANCELLED`/
  `INVALID_ARGUMENT`/`INTERNAL` plus `UNKNOWN` als Fallback).
  Das versionierte Transport-Mapping-Artefakt (plan-RM-M5
  §Aktivierungsbedingungen) wird als RM-M5-01-Pflicht in einer
  Tabelle in `docs/plan/sidecar/transport-mapping-v1.md` oder
  inline im Slice-Plan abgelegt.
- **Container-Topologie folgt Standard-Patterns.** UDS für
  Single-Pod ist Kubernetes-Standard; mTLS für Cross-Host ist
  Service-Mesh-Standard. Operator-UX bleibt vertraut.
- **Tooling existiert.** `Grpc.Net.Client`, `Grpc.AspNetCore`,
  `grpc_health_probe`, `grpcurl`, `buf` sind reife Tools.
  Kein Eigenbau-Wire-Format wie ein Unix-Socket-Custom-Protokoll
  brauchen würde.

### Negativ

- **Latenz-Hit über In-Process.** Plan-RM-M5 nennt das explizit
  als Trade-off (`spec/architecture.md` §13.2). Für MPC-/Solver-
  Surface tolerabel; für die M3-Control-Kernel-Surface (1 Hz
  Limiter/Ramp/PID) wäre gRPC zu teuer — daher bleibt der
  In-Process-Pfad aus ADR 0004 für den `battery_control_core`
  produktiv. **Zwei-Stack-Adapter** ist die Folge: M3 P/Invoke
  + M5 gRPC nebeneinander.
- **Protobuf-Toolchain ist neuer Build-Stack.** `Grpc.Tools` ist
  klein und stabil, aber ein Codegen-Layer mehr im CI-Pfad. Der
  OPC-UA-Codegen (für die SDK-Bindings) ist bereits da — der
  Mehraufwand ist begrenzt.
- **Cross-Host-Konfigurations-Surface.** mTLS-Cert-Provisioning
  ist die zweite PKI-Linie nach OPC-UA. Operator muss zwei Cert-
  Stores pflegen — Mitigation: gemeinsames PKI-Root, Cert-
  Rotation-Pattern teilt sich mit F-18 (OPC-UA-Cert-Rotation).
- **gRPC-spezifische Bugs.** HTTP/2-Stack hatte historisch
  CVEs (Rapid-Reset etc.); Mitigation: regulärer Dependency-
  Sweep, `Grpc.AspNetCore`-Updates im üblichen Patch-Window.

### Neutral

- **`spec/architecture.md` §13.1/§13.2 bleibt unverändert.**
  Beide Sections nennen gRPC bereits als Phase-3-Sidecar-
  Transport. Diese ADR formalisiert die Adoption, ohne neue
  Spec-Aussagen einzuführen.
- **`spec/architecture.md` §18 AR-OPEN-002** wird auf
  „Geschlossen mit ADR 0005" gesetzt (Sync-Update gehört zur
  Aktivierungs-Sequenz).
- **ADR 0004 bleibt `Accepted`.** Die In-Process-P/Invoke-
  Entscheidung für `battery_control_core` ist orthogonal — ADR
  0005 öffnet einen **zweiten Native-Pfad** (gRPC-Sidecar für
  größere Kerne), ersetzt ihn aber nicht. Trigger 2 in ADR 0004
  §4 („Phase-3-Komponenten kommen in Scope") zündet damit teil-
  weise: der Sidecar-Pfad wird eröffnet, aber der
  `battery_control_core` zieht **nicht** automatisch um (siehe
  ADR 0004 §6 Schritt 4 — der Bundle-Trigger bleibt offen
  solange die Kosten-Nutzen-Abwägung für die heutige Surface
  stabil bleibt).

---

## 9. Sequenz und Abschlussstand

1. **AR-OPEN-002 in `spec/architecture.md` §18 geschlossen:**
   Status-Zelle auf „Geschlossen mit ADR 0005".
2. **`plan-RM-M5.md` abgeschlossen:** Der Plan wurde zur Aktivierung von
   `open/` nach `in-progress/` verschoben und liegt seit M5-Closure
   2026-05-13 in `planning/done/`.
3. **RM-M5-01 Detail-Slice abgeschlossen** (`plan-RM-M5-01.md` in
   `planning/done/`) mit Sub-Slice-Schnitt analog zu RM-M4-05
   (A: Proto-Vertrag + .NET-Adapter; B: Test-Sidecar + Mocking-
   Pattern; C: Security-Pfad (UDS + mTLS) + Negativ-Pins;
   D: Contract-Version-Mixed-Tests + Idempotency-Store + Master-
   Plan-Closure).
4. **Transport-Mapping-Artefakt** (`docs/plan/sidecar/transport-mapping-v1.md`
   oder inline in RM-M5-01-A): mappt gRPC-Status-Codes auf die
   normierten Outcomes der plan-RM-M5 §Sidecar-Status-Taxonomie-
   Tabelle. RM-M5-01-A-Pflicht.
5. **Trigger-Watch:** §7 oben. Neue ADR (z.B. `0006-...`) wenn
   einer der Trigger zündet.

Bis ein Trigger zündet bleibt diese ADR `Accepted` und gRPC ist
der Sidecar-Transport für den `optimization-core` und
zukünftige Phase-3-Sidecars.
