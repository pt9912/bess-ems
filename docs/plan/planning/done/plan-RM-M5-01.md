# Plan RM-M5-01 — gRPC-Sidecar `optimization-core` (Contract-Slice)

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M5-01)
**Status:** ✅ Abgeschlossen am 2026-05-11 — alle 4 Sub-Slices grün inkl. C-Korrektur-Pass nach externem Review
**Bezug:**
[`../in-progress/plan-RM-M5.md`](../in-progress/plan-RM-M5.md) (Master-Plan, RM-M5-01-Zeile mit
DoD plus Sidecar-Status-Taxonomie, Fallback-Matrix, Idempotenz-Vertrag,
Contract-Versions-Vertrag),
[`../../adr/0005-optimization-core-sidecar-transport.md`](../../adr/0005-optimization-core-sidecar-transport.md)
(gRPC-Transport-Adoption: §2 Entscheidungs-Tabelle, §3 verworfene
Alternativen, §4 Security-Modell, §5 Contract-Versionierung, §6
Mocking-Strategie, §9 Sequenz Schritt 3 = dieser Slice),
[`plan-RM-M4-05.md`](plan-RM-M4-05.md) (Sub-Slice-
Cut-Pattern A/B/C/D, Embedded-TestServer-Fixture-Pattern,
Cert-Trust-Bridge — wird hier auf gRPC-mTLS adaptiert),
[`plan-RM-M2-optimization.md`](plan-RM-M2-optimization.md)
(M2-OptimizationRun-Modell, IScheduleOptimizer-Driven-Port, der vom
Sidecar-Adapter hinter den bestehenden Optimierungsports bedient wird),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(LH-OPT-002/003/006).

---

## 1. Zweck

RM-M5-01 ist der **Contract-Slice** für den Phase-3-Sidecar-Pfad:
der erste produktionsnahe `optimization-core`-gRPC-Vertrag plus
.NET-Adapter, der das Sidecar hinter dem bestehenden
`IScheduleOptimizer`-Driven-Port aus M2 anbietet. ADR 0005 hat die
Transport-Achse (gRPC), die Netz-Topologie (UDS-Default / mTLS-
Cross-Host), die Mocking-Strategie und die Contract-Versionierungs-
Disziplin festgelegt; dieser Slice liefert die konkrete Wire-Surface
plus Adapter, plus den ersten produktionsnahen Test-Sidecar im
Embedded-Pattern.

RM-M5-01 liefert:

- **Versionierter Protobuf-Vertrag** unter
  `proto/optimization-core/v1/` mit `service OptimizationCore`
  (Health + Version + Optimize + OptimizeMpc). Major-Version im
  Paket-Namespace; Backward-Compat-Disziplin als Slice-Pflicht.
- **`BatteryEms.Adapters.OptimizationCore`-Adapter-Projekt** (neu)
  implementiert `IScheduleOptimizer` (M2-Driven-Port) hinter
  einem gRPC-Channel zum konfigurierbaren Sidecar-Endpoint
  (`unix:/...` oder `https://...`). Composition-Root entscheidet
  über `BessHostOptions`-Slot, ob der Sidecar-Adapter, der lokale
  OR-Tools-Adapter (M2-Welle 1) oder der `NoOpScheduleOptimizer`
  registriert wird. **Bestehende Optimierungsports werden nicht
  geändert**; das Sidecar-Verhalten landet hinter derselben
  Application-Driving-Port-Linie aus M2.
- **Persistenter Idempotency-Store** mit Unique-Constraint auf
  `request_id` in derselben Postgres-DB wie `OptimizationRun`/
  Schedule-Versionen (plan-RM-M5 §Request-Idempotenz Und Retry).
  Atomare Terminalzustände `sidecar_committed` /
  `fallback_committed` / `cancelled` / `failed_no_activation`.
  Late-Response-Pfad ist `late_response_ignored` ohne Plan-Aktivierung.
- **Sidecar-Status-Taxonomie-Mapping** (gRPC-Status-Codes →
  normierte Outcomes) als versioniertes Artefakt
  `proto/optimization-core/v1/transport-mapping.md` oder inline
  in `plan-RM-M5-01-A.md`-DoD. RM-M5-01-A-Pflicht.
- **Fallback-Matrix-Implementierung** für Deadline/Unavailable/
  Crash/Invalid-Trajectory/Invalid-Snapshot (plan-RM-M5
  §Fallback-Matrix) inklusive Plan-Gültigkeits-Checks
  (§Fallback-Plan-Gueltigkeit: Zeitindex, MaxFallbackScheduleAge,
  Kontext-Stempel, Telemetrie-Drift, Versionierung).
- **In-Process TestSidecar** unter
  `tests/integration/BatteryEms.OptimizationCore.IntegrationTests/`
  analog zur `EmbeddedTestServerHost`-Linie aus M4-04-D / M4-08-A.
  Zwei Stub-Modi (`OptimalAlwaysSucceedsStub`,
  `ScriptableOutcomeStub`) decken die Sidecar-Status-Taxonomie-
  Matrix.
- **Mixed-Version-Compat-Tests** (Worker alt/Sidecar neu, Worker
  neu/Sidecar alt, unbekannte Major-Version, fehlendes
  Pflicht-Feature-Flag — plan-RM-M5 §Contract-Versionen Und Rollout).
- **Security-Pins** (UDS-Filesystem-Permission-Fehler,
  mTLS-Cert-Mismatch, fehlende TLS-Negotiation gegen TLS-only-
  Sidecar — ADR 0005 §4) plus **Production-Fail-Closed-Pfad** wenn
  ein Production-Profile-Slot nicht gesetzt ist (analog zur
  M4-05-D-02-Linie aus `OpcUaAdapterOptions`).
- **CI-Gate** `make test-hil-optimization-core` (neu, mandatory in
  `make gates` + `make ci`).

**Bewusster Scope-Cut:** RM-M5-01 macht **nur** den Contract-Slice
plus die LP-Surface (M2-`IScheduleOptimizer` ersetzbar). MPC-
Anbindung (RM-M5-02 mit State-Space/Kalman/Horizon), Replay-
Plattform (RM-M5-04), Container-Orchestrierungs-Gate (RM-M5-06)
sind eigene Arbeitspakete; dieser Slice legt die Wire-Disziplin,
auf der die anderen aufbauen.

---

## 2. Aktivierungsbedingungen

- **M4 ✅** (`plan-RM-M4.md` — alle 8 Pflicht-Slices grün am
  2026-05-11).
- **ADR 0005 ✅** (`docs/plan/adr/0005-optimization-core-sidecar-
  transport.md` Accepted am 2026-05-11): gRPC-Transport, UDS-Default
  / mTLS-Cross-Host, Mocking-Strategie, Contract-Versionierungs-
  Disziplin sind fixiert.
- **AR-OPEN-002 ✅** geschlossen (`spec/architecture.md` §18).
- **M2-OptimizationRun-Modell stabil** (`plan-RM-M2-optimization.md`
  done; `OptimizationRun` + Solverstatus + `IScheduleOptimizer` +
  `IOptimizationRunRepository` + Dapper-Variante laufen produktiv).

**Optional, nicht-zündend:**

- RM-M5-02..06 sind eigenständige Slices und blocken RM-M5-01-A..D
  nicht.

---

## 3. Scope

**In Scope (RM-M5-01-A..D zusammen):**

- **`proto/optimization-core/v1/optimization_core.proto`** (neuer
  Top-Level-Pfad im Repo): `service OptimizationCore` mit Health,
  Version, Optimize (Server-Streaming für progressive Updates),
  OptimizeMpc (für RM-M5-02-Vorbereitung; M5-01 implementiert nur
  den Vertrag, nicht das Backend), Cancel. Major-Version im
  Paket-Namespace `bess.optimization_core.v1`.
- **`BatteryEms.Adapters.OptimizationCore`-Projekt** (neu):
  - `OptimizationCoreOptions`-Record mit `SidecarEndpoint` (Uri),
    `RuntimeProfile` (Development/HilSimulator/Production analog
    zur OPC-UA-Linie), `ClientCertificatePath`,
    `TrustedServerCertificatesPath`, `BearerTokenSource`,
    `RequestTimeout`, `Deadline`, `MaxFallbackScheduleAge`.
  - `OptimizationCoreClient` (interne) wrappt
    `Grpc.Net.Client.GrpcChannel` + generierte `OptimizationCore.
    OptimizationCoreClient` (aus `Grpc.Tools`-Codegen).
  - `OptimizationCoreScheduleOptimizer : IScheduleOptimizer`
    übersetzt M2-`ScheduleOptimizationRequest` in gRPC-Protobuf,
    treibt Health+Version-Probe, dann Optimize, handelt Streaming-
    Progress, mapped gRPC-Status auf M2-`OptimizationRun.Status`
    via Transport-Mapping-Tabelle.
  - `AddBessOptimizationCore(IConfiguration)`-Extension
    registriert den Adapter; opt-in über `BessHostOptions.
    OptimizationCoreEnabled`.
- **`BessHostOptions`-Erweiterung**: optionale Slots für
  `OptimizationCoreEndpoint`, `OptimizationCoreRuntimeProfile`,
  `OptimizationCoreClientCertPath`,
  `OptimizationCoreTrustedServerCertsPath`,
  `OptimizationCoreBearerTokenPath`. Default leer → Adapter wird
  nicht registriert (M2-OR-Tools-Pfad bleibt).
- **`BessConfigurationBootstrap`-Erweiterung** reicht die Slots
  durch in die `OptimizationCoreOptions`.
- **`OptimizationCoreOptions.EnsureValid(ILogger)`** mit Profile-
  Awareness analog zur OPC-UA-Linie:
  - `RuntimeProfile=Production` + `SidecarEndpoint`-Scheme=`http`
    (plaintext) → harter Startup-Fehler `optimization-core-not-
    hardened-in-production` (analog zu D-02 aus M4-05).
  - `RuntimeProfile=Production` + UDS-Endpoint + Mode ≠ `0600`
    auf dem Socket → Startup-Fehler `optimization-core-uds-
    permissions-not-locked` (siehe ADR 0005 §4 Default).
  - `RuntimeProfile=HilSimulator|Development` + plaintext-TCP-
    Endpoint → durch (Pre-M5-Verhalten für Test-Linien).
- **Persistenter Idempotency-Store**:
  - Neue Migration `0003_optimization_idempotency.sql` mit Tabelle
    `optimization_idempotency` (Spalten: `request_id` PK,
    `terminal_state`, `terminal_reason`, `run_id`, `produced_version`,
    `created_at`, `committed_at`). Unique-Constraint auf
    `request_id`.
  - `IIdempotencyStore`-Driven-Port + `DapperIdempotencyStore`-
    Adapter. Atomare Compare-and-Set-Operation
    `TryFinalizeAsync(requestId, state, reason, runId, version)`.
  - `OptimizationCoreScheduleOptimizer` legt vor dem Sidecar-
    Aufruf einen Idempotency-Eintrag mit `terminal_state=pending`
    an (oder erkennt den bestehenden Eintrag und liest seinen
    Terminalzustand). Nach erfolgreichem oder fehlgeschlagenem
    Lauf CAS auf den finalen Zustand.
- **Sidecar-Status-Taxonomie-Mapping** als Tabelle im Adapter
  (statische Lookup-Funktion) + versionierte Doku-Tabelle
  `proto/optimization-core/v1/transport-mapping-v1.md`. Mapping
  von gRPC-`StatusCode` (`OK`/`DEADLINE_EXCEEDED`/`UNAVAILABLE`/
  `CANCELLED`/`INVALID_ARGUMENT`/`INTERNAL`/`UNKNOWN`) + Sidecar-
  payload `solver_status`/`has_usable_solution` auf
  `OptimizationRun.Status` + `TerminationReason` + Metric-Tags
  (`fallback_source`/`fallback_reason`). Plan-RM-M5 §Sidecar-
  Status-Taxonomie ist normativ.
- **Fallback-Matrix-Implementierung** (plan-RM-M5 §Fallback-Matrix):
  Deadline/Unavailable/Crash/Infeasible/Invalid-Trajectory/Invalid-
  Snapshot. **Lokaler Optimierer-Fallback**: wenn
  `BessScheduleSolverOptions.Backend = "or_tools"`, fällt der
  Adapter auf den M2-OR-Tools-Pfad. Sonst keine neue Schedule-
  Version; Control-Pfad geht in Safe-Stop mit
  `fallback_reason=no_valid_plan` (Plan-Gültigkeits-Check schlägt
  bei Erstaufruf zu).
- **Plan-Gültigkeits-Check** (plan-RM-M5 §Fallback-Plan-Gueltigkeit):
  `OptimizationCoreScheduleOptimizer.IsFallbackCandidateValidAsync`
  prüft Zeitindex, `MaxFallbackScheduleAge`, Kontext-Stempel
  (asset_id, schedule_type, horizon, time_step, constraint-version),
  Telemetrie-Drift (SOC, Leistung, Netzlimit, Temperatur). Drift
  invalidiert hart mit Reason `fallback_plan_expired` /
  `fallback_context_mismatch` / `fallback_telemetry_drift` /
  `no_valid_plan`. Master-DoD-`MaxFallbackScheduleAge`-Default:
  `min(Schedule.TimeStep, 2 * ControlCycleInterval)` pro Asset/
  ScheduleType (siehe plan-RM-M5 §Fallback-Plan-Gueltigkeit).
- **In-Process TestSidecar** unter
  `tests/integration/BatteryEms.OptimizationCore.IntegrationTests/`:
  - `EmbeddedOptimizationCoreSidecar` als `IAsyncDisposable`-
    Fixture (analog zu `EmbeddedTestServerHost`); startet
    `Grpc.AspNetCore`-Pipeline mit dem generierten
    `OptimizationCoreBase`-Stub gegen einen Test-UDS in
    `Path.GetTempPath()/BatteryEms/OptimizationCore/{Guid:N}.sock`.
  - Zwei Stub-Implementierungen:
    `OptimalAlwaysSucceedsStub` (Echo-Sidecar: liefert das
    eingegebene `power_setpoint_kw`-Profil als „Lösung" mit
    `solver_status=optimal`) und `ScriptableOutcomeStub`
    (Per-Test-Konfiguration: nächste Antwort = Infeasible/Time-
    Limit/Crash/usw., deckt die Status-Taxonomie-Matrix).
  - `OptimizationCoreTestFixture`-Test-Klasse mit
    `IClassFixture<EmbeddedOptimizationCoreSidecar>` Pattern.
- **Mixed-Version-Compat-Tests** (RM-M5-01-D Pflicht): vier
  Konfigurationen mit ScriptableOutcomeStub als
  Version-Stub-Variante. Worker erwartet `contract_version=1.0`,
  Sidecar antwortet mit `1.0` / `2.0` / `0.5` / fehlendem
  Pflicht-Feature-Flag.
- **Security-Pins** (RM-M5-01-C):
  - UDS-Mode-0644 → Boot wirft `optimization-core-uds-permissions-
    not-locked`.
  - mTLS-Client-Cert ungültig (Self-Signed gegen Trusted-CA) →
    Connect → `unauthorized_client` Status.
  - HTTPS-Client gegen plaintext-Sidecar → connection-refused →
    `sidecar_unavailable`.
  - Production-Profile + plaintext-Endpoint → Startup-Fehler
    `optimization-core-not-hardened-in-production`.
- **Quality-Doku** §2.6 erweitern um `make test-hil-optimization-
  core` als neues Mandatory Gate, plus Pin-Inventory.
- **Master-Plan-Cleanup**: bei Closure flippt RM-M5-01-Zeile in
  `plan-RM-M5.md` auf ✅ mit D-05-Replacement-Text (vorab gepinnt
  in §5).

**Out of Scope (separate Slices / Folgearbeiten):**

- **MPC-Kernel-Implementierung** → RM-M5-02. M5-01 implementiert
  nur den `OptimizeMpc`-RPC-Vertrag, kein State-Space-Backend.
- **Replay-Plattform-Migration** → RM-M5-04. M5-01-Sidecar-
  Outputs werden persistiert (`OptimizationRun`), aber kein
  Manifest-Format / Golden-Diff-Linie.
- **Container-Orchestrierungs-Gate** → RM-M5-06. M5-01 fährt nur
  In-Process-TestSidecar; Compose-/Kubernetes-Sidecar-Topologie
  ist eigener Slice.
- **Erweiterte Metriken** → RM-M5-05. M5-01 emittiert nur die
  M2-baseline-Metriken plus `fallback_source` / `fallback_reason`
  Tags; erweiterte Solverstatus-/Sidecar-Health-/Command-Latenz-
  Metriken sind RM-M5-05.
- **Production-Sidecar-Implementierung in einer Drittsprache**
  (Python/Rust/C++ mit echtem HiGHS/OR-Tools-Backend). M5-01
  liefert nur den `.proto`-Vertrag und den TestSidecar; ein
  produktionsnaher Sidecar-Container ist eigene Folge-Linie
  (`bess-optimization-core` als Schwesterprojekt oder
  `optimization-core/sidecar/` als Subverzeichnis).
- **Hot-Reload / Cert-Rotation** für die mTLS-Cert-Linie
  (analog zu F-18 aus M4-05) → eigene Folge-Slice.
- **Bearer-Token-Auth-Adapter** (gRPC `CallCredentials`) → erst
  wenn Multi-Tenant-Trigger zündet; M5-01 baut den Interceptor-
  Slot, lässt ihn aber leer.

---

## 4. Sub-Slices

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M5-01-A | Proto-Vertrag + .NET-Adapter-Skelett + Transport-Mapping-Artefakt — **~600-900 LOC** | Neues Top-Level-Verzeichnis `proto/optimization-core/v1/` mit `optimization_core.proto`: Package `bess.optimization_core.v1`, Service `OptimizationCore`, RPCs `Health` (returns `HealthResponse`), `Version` (returns `VersionResponse { contract_version, min_compatible_version, max_compatible_version, repeated string features }`), `Optimize` (streams `OptimizeProgress`, terminal `OptimizeResult`), `OptimizeMpc` (Vertrag-only, RM-M5-02 implementiert Backend), `Cancel` (returns `Empty`). Message-Schema deckt `OptimizationRequest` (asset_id, schedule_type, horizon_start, horizon_end, time_step, constraints, request_id, idempotency_key, deadline), `OptimizationResponse` (solver_status, has_usable_solution, solution_quality, schedule_points, objective_value, run_id, termination_reason). Field-Numbers fix, Backward-Compat-Disziplin im Proto-Header dokumentiert. Neues Adapter-Projekt `src/adapters/driven/BatteryEms.Adapters.OptimizationCore/` mit `OptimizationCoreOptions`-Record, `OptimizationCoreClient`-Wrapper über `Grpc.Net.Client.GrpcChannel`, `OptimizationCoreScheduleOptimizer : IScheduleOptimizer`-Implementierung (Health+Version-Probe vor erstem Optimize, Transport-Mapping-Lookup, M2-`OptimizationRun`-Output). `AddBessOptimizationCore`-Extension. `BessHostOptions`-Slots durchgereicht. `OptimizationCoreOptions.EnsureValid` mit Profile-Awareness (Production+plaintext → throws). Sidecar-Status-Taxonomie-Mapping in `OptimizationCoreStatusMapper`-static-class plus versionierte Doku-Tabelle in `proto/optimization-core/v1/transport-mapping-v1.md`. Tests (`BatteryEms.Adapters.OptimizationCore.Tests`): 10+ Pins (Options-EnsureValid-Matrix, Status-Mapper-Tabelle, Defaults-Pin, BessHostOptions-Wiring). `Grpc.Tools`-NuGet im Adapter-`.csproj` für Source-Generator. Keine Wire-Roundtrip-Tests in diesem Sub-Slice (das ist B). |
| ✅ | RM-M5-01-B | In-Process TestSidecar + Mocking-Pattern + erster Roundtrip-Pin — **~500-700 LOC** | Neues Test-Projekt `tests/integration/BatteryEms.OptimizationCore.IntegrationTests/`. `EmbeddedOptimizationCoreSidecar : IAsyncDisposable`-Fixture startet `Grpc.AspNetCore`-WebApplicationFactory mit `OptimizationCoreBase`-Stub auf einem Per-Test-UDS in `Path.GetTempPath()`. Zwei Stub-Klassen: `OptimalAlwaysSucceedsStub` (echo-Stub mit `optimal`-Status) + `ScriptableOutcomeStub` (Per-Test-Queue `Action<OptimizeRequest, IServerStreamWriter<OptimizeProgress>>`). Test-Klassen `OptimizationCoreRoundtripTests` mit 5 Pins (Health-Probe, Version-Probe-Match, Optimize-Success-Optimal, Optimize-Streaming-Progress, Cancellation-mid-Stream). `OptimizationCoreNegativeTests` mit 4 Pins (Deadline-Exceeded → `solver_time_limit`, Unavailable-Crash-mid-Request → `sidecar_unavailable`-Fallback, Infeasible-Sidecar-Result → keine neue Schedule-Version, Invalid-Trajectory-Output → verwirft-Result). Dockerfile-Stage `test-hil-optimization-core` (neu); Makefile-Target gleichnamig (neu); CI-Verdrahtung in `make gates` und `make ci` analog zur `test-hil-opcua`-Linie. Quality-Doku §2.6 wird in C/D synchronisiert. |
| ✅ | RM-M5-01-C | Idempotency-Store + Fallback-Matrix + Plan-Gültigkeits-Check + Security-Pins — **~700-1000 LOC** + Korrektur-Pass +~660 LOC | Neue Migration `migrations/0003_optimization_idempotency.sql` (Tabelle `optimization_idempotency` mit Unique-Constraint auf `request_id`, plus `terminal_state`/`terminal_reason`/`run_id`/`produced_version`/`created_at`/`committed_at`). `IIdempotencyStore`-Driven-Port (Application) + `DapperIdempotencyStore`-Adapter (Persistence). Atomare CAS-Operation `TryFinalizeAsync(requestId, state, reason, runId, version)`. `OptimizationCoreScheduleOptimizer` legt pre-Sidecar einen `pending`-Eintrag an (oder liest existierenden Terminalzustand für Duplicate-Detection); post-Sidecar CAS auf `sidecar_committed` oder `fallback_committed` oder `failed_no_activation`. Late-Response-Handler erkennt bereits-finalisierte `request_id` und gibt `late_response_ignored` ohne zweite Aktivierung zurück. Plan-Gültigkeits-Check `IsFallbackCandidateValidAsync` prüft Zeitindex / MaxFallbackScheduleAge (Default `min(Schedule.TimeStep, 2 * ControlCycleInterval)`) / Kontext-Stempel / Telemetrie-Drift; jede Invalidation liefert maschinenlesbaren Reason aus der Fallback-Taxonomie. Fallback-Matrix-Implementierung: Deadline/Unavailable/Crash → wenn `or_tools`-Backend konfiguriert, lokaler Optimierer-Fallback mit `fallback_source=local_optimizer`; sonst `no_valid_plan` + Safe-Stop. Infeasible/Invalid-Trajectory → keine neue Schedule-Version. Invalid-Snapshot pre-Sidecar → kein Sidecar-Request, Precheck-Failure. Security-Pins: UDS-Mode-0644 → Startup-Fehler `optimization-core-uds-permissions-not-locked`; mTLS-Client-Cert-Mismatch → `unauthorized_client`; HTTPS-Client gegen plaintext-Sidecar → `sidecar_unavailable`; Production+plaintext → Startup-Fehler `optimization-core-not-hardened-in-production`. Tests (10+ neue Pins in `OptimizationCoreNegativeTests` und neuer `OptimizationCoreSecurityTests`, plus 12+ Persistence-Pins in `BatteryEms.Adapters.Persistence.Tests` für die Idempotency-Tabelle inkl. CAS-Race und Restart-Replay). |
| ✅ | RM-M5-01-D | Mixed-Version-Compat-Tests + Quality-Doku + Master-Plan-Cleanup — **~300-500 LOC** | Vier Mixed-Version-Pins in `OptimizationCoreNegativeTests`: (i) Worker `contract_version=1.0`, Sidecar `1.0` → ✅ Optimize-Success; (ii) Worker `1.0`, Sidecar `0.5` (incompatible) → `contract_incompatible`-Fallback + kein Optimize-Request; (iii) Worker `1.0`, Sidecar `2.0` mit `min_compatible_version=2.0` → `contract_incompatible`-Fallback; (iv) Worker erwartet Feature `has-usable-solution`, Sidecar meldet leeres `features`-Array → `contract_incompatible` + Fallback. `ScriptableOutcomeStub` bekommt zwei zusätzliche Per-Test-Slots `OverrideVersionResponse` und `OverrideFeatures`. Quality-Doku §2.6 wird erweitert um `make test-hil-optimization-core` als Mandatory Gate plus Pin-Inventory-Tabelle (5 happy + 4 negativ + 4 mixed-version + 4 security = 17 Pins). Plan-RM-M5 Master-DoD für RM-M5-01: bei Closure flippt die Zeile auf ✅ mit dem in §5 D-05 vorab gepinnten Replacement-Text. F-Folgearbeiten (siehe §9) in `note-RM-M5-followups.md` (neu) anlegen. **Slice-Plan** wird nach `done/plan-RM-M5-01.md` verschoben. |

---

## 5. Design-Entscheidungen

**D-01 gRPC-Adapter sitzt hinter `IScheduleOptimizer` aus M2.**
Der Adapter implementiert den **bestehenden** Driven-Port; Application
und Domain ändern sich nicht. Composition-Root entscheidet via
`BessHostOptions.OptimizationCoreEnabled`, ob der Sidecar-Adapter,
der M2-OR-Tools-Adapter oder der `NoOpScheduleOptimizer` registriert
wird.

Begründung gegen Alternative (a) „neuer Driving-Port für Sidecar-
Optimize": würde Application gegen den Wire-Vertrag koppeln und
das M2-Optimizations-Modell brechen. Master-DoD plan-RM-M5
§Komponenten Zeile „Adapter" fordert explizit „Driven Adapter
hinter bestehenden Optimierungsports".

**D-02 Production-Profile macht plaintext-TCP unwirksam.**
Analog zur OPC-UA D-02 aus M4-05: `RuntimeProfile=Production` mit
`SidecarEndpoint`-Scheme=`http` (kein `https`, kein `unix`) ist ein
harter Startup-Fehler. Der Test-Profile-Pfad
(`Development|HilSimulator`) erlaubt plaintext für In-Process-
TestSidecar (UDS in `/tmp` braucht kein TLS; HTTPS-over-UDS ist
overkill für Test-Setup).

**D-03 Persistenter Idempotency-Store ist worker-owned.**
Plan-RM-M5 §Request-Idempotenz Und Retry: „Der Worker fuehrt den
Idempotency-Store in derselben persistierten Datenbank wie
`OptimizationRun`/Schedule-Versionen. Der Sidecar bleibt fuer
Aktivierungseffekte stateless." Implementierung als
`optimization_idempotency`-Tabelle in der Postgres-DB; CAS via
`INSERT ... ON CONFLICT (request_id) DO NOTHING` + `UPDATE ...
WHERE terminal_state = 'pending'`. Eine zweite Antwort für eine
bereits-finalisierte `request_id` aktiviert nichts (`late_response_
ignored`).

**D-04 Sidecar-Status-Taxonomie-Tabelle ist versioniertes
Artefakt, nicht inline-Code.**
`proto/optimization-core/v1/transport-mapping-v1.md` ist das
Source-of-Truth-Dokument. `OptimizationCoreStatusMapper`-Code im
Adapter implementiert es; jede Änderung des Mappings verlangt
Doku-Update + Plan-Slice. Begründung: das Mapping ist Operator-
sichtbar (Metric-Tags, Run-Status, Fehlerdiagnose); Code-only-
Änderungen würden den Operator-Vertrag brechen ohne Doku-Trail.

**D-05 Master-Plan-Wortlaut bei Closure (vorab gepinnt).**
Bei Closure wird RM-M5-01-Zeile in `plan-RM-M5.md` umformuliert.
Verbindlicher Replacement-Text:

> Slice-Plan: [`done/plan-RM-M5-01.md`](../done/plan-RM-M5-01.md).
> `proto/optimization-core/v1/optimization_core.proto` mit
> Service `OptimizationCore` (Health, Version, Optimize-streaming,
> OptimizeMpc-Vertrag, Cancel), Major-Version im Paket-Namespace.
> `BatteryEms.Adapters.OptimizationCore`-Adapter implementiert
> M2-`IScheduleOptimizer` (D-01) über `Grpc.Net.Client.GrpcChannel`,
> UDS-Default für Loopback / mTLS für Cross-Host gemäß ADR 0005.
> `OptimizationCoreOptions.EnsureValid` wirft `optimization-core-
> not-hardened-in-production` bei Production+plaintext (D-02
> analog M4-05) plus `optimization-core-uds-permissions-not-locked`
> bei Mode≠0600. Persistenter `optimization_idempotency`-Store mit
> Unique-Constraint auf `request_id` (D-03); atomarer Terminalzustand
> via CAS; Late-Response-Pfad `late_response_ignored`. Sidecar-
> Status-Taxonomie-Mapping in `transport-mapping-v1.md` (D-04
> versioniertes Artefakt) und `OptimizationCoreStatusMapper`-static.
> Fallback-Matrix mit lokalem OR-Tools-Fallback oder Safe-Stop
> (`no_valid_plan`) gemäß plan-RM-M5 §Fallback-Matrix; Plan-
> Gültigkeits-Check (Zeitindex / MaxFallbackScheduleAge /
> Kontext-Stempel / Telemetrie-Drift). 17 Pins (5 happy + 4
> negativ + 4 mixed-version + 4 security) in
> `tests/integration/BatteryEms.OptimizationCore.IntegrationTests/`
> via In-Process `EmbeddedOptimizationCoreSidecar`-Fixture
> (`Grpc.AspNetCore` + UDS). `make test-hil-optimization-core`
> jetzt Mandatory in `make gates` und `make ci`. RM-M5-02 (MPC-
> Kernel), RM-M5-04 (Replay-Plattform), RM-M5-05 (Erweiterte
> Metriken), RM-M5-06 (Container-Orchestrierungs-Gate) bleiben
> eigene Slices. F-Items für Cert-Rotation, Multi-Tenant-Bearer-
> Token-Auth, produktionsnaher Drittsprach-Sidecar siehe
> `note-RM-M5-followups.md`.

Der Closure-Reviewer matcht die Implementierung gegen diesen
Replacement-Text — keine Rückwärts-Rekonstruktion bei Closure.

**D-06 PKI-Pfad-Konvention erbt von M4-05 D-06.**
Per-Instanz-PKI-Pfade für Test-Sidecar unter
`Path.GetTempPath()/BatteryEms/OptimizationCore/{Guid:N}` (analog
zu OPC-UA Review-Fix H5). Operator-bereitgestellte mTLS-Cert-Stores
für Cross-Host-Production-Deployments liegen unter Operator-
gewählten Pfaden via `OptimizationCoreOptions.TrustedServerCertificatesPath`.

**D-07 Test-Layout erbt von M4-05 D-07.**
`OptimizationCoreSecurityTests.cs` ist eine separate Datei neben
`OptimizationCoreRoundtripTests.cs` und `OptimizationCoreNegativeTests.cs`.
Per-class Fixture-Isolation und `[Collection("OptimizationCore
Integration")]`-Serialisierung. Test-Defaults setzen
`RuntimeProfile=HilSimulator` explizit (analog zur OPC-UA-Defaults-
Linie aus M4-05-A).

**D-08 `OptimizeMpc`-RPC ist Vertrag-only in M5-01.**
Der RPC ist im Proto definiert, der Adapter routet ihn weiter
(deserialisiert die Antwort), aber kein State-Space-Backend im
TestSidecar oder produktiv. RM-M5-02 liefert die Backend-
Implementierung. Begründung: Backward-Compat-Disziplin —
RM-M5-02 ist additiv (neue Felder im selben Service); ein leerer
RPC-Slot im Vertrag verhindert späteren breaking-change.

---

## 5.1 Korrektur-Notiz Sub-Slice C (Review-Pass 2026-05-11)

Externer Review-Pass nach der ersten RM-M5-01-D-Closure hat drei
Gaps gegen Sub-Slice-C-DoD aufgedeckt. Die Closure wurde
zurückgenommen (Slice-Plan zurück nach `in-progress/`, Master-Plan
RM-M5-01-Zeile auf 🟡), bis der Korrektur-Pass landet:

- **Finding 1 — Lokaler `or_tools`-Fallback nicht im
  `OptimizationCoreScheduleOptimizer.OptimizeAsync`-Flow wirksam.**
  Plan §3 + §4 Sub-Slice-C-DoD verlangt: „Deadline/Unavailable/Crash
  → wenn `or_tools`-Backend konfiguriert, lokaler Optimierer-
  Fallback mit `fallback_source=local_optimizer`; sonst
  `no_valid_plan` + Safe-Stop". Heute landen alle Transport-Failure-
  Branches in `FailedNoActivation` ohne Fallback-Versuch.
- **Finding 2 — `IFallbackPlanValidator` gebaut + 14 Pins grün, aber
  nicht in den Laufzeit-Flow integriert.** Plan §3 verlangt
  `OptimizationCoreScheduleOptimizer.IsFallbackCandidateValidAsync`
  prüft Zeitindex / MaxFallbackScheduleAge / Kontext-Stempel /
  Telemetrie-Drift; die Hooks existieren in Application + DI, der
  Aufruf-Pfad im Adapter fehlt.
- **Finding 3 — `BessHostOptions.cs:89` stale Kommentar zu
  `NotImplementedException` aus Sub-Slice-A-Stand; Wire-Integration
  ist seit Sub-Slice-B live.**

**Korrektur-Pass-Scope:** neuer Driven-Port
`IFallbackScheduleOptimizer : IScheduleOptimizer` (Marker-Interface),
optionale Injection in `OptimizationCoreScheduleOptimizer`,
Validator-Aufruf gegen den Fallback-Optimizer-Output (Kontext-
Stempel + Telemetrie-Drift; Telemetrie-Drift bleibt skip-bar wenn
Adapter keinen Snapshot hat), `BessHostBuilder` registriert OR-
Tools-Adapter als `IFallbackScheduleOptimizer` wenn
`BessHostOptions.OptimizationCoreFallbackBackend = "or_tools"`,
plus 5 neue Pins (fallback-success, fallback-fails, no-fallback,
sidecar-success-no-fallback-called, validator-rejects). Bewusst
**out of scope für den Korrektur-Pass**: Lookup eines „letzter
bekannter Plan" via `IScheduleRepository` als sekundäre Fallback-
Quelle (Adapter-Seitige Domain-Repo-Querys wären Hexagonal-
Verletzung) — diese Variante landet als F-M5-05 in
`note-RM-M5-followups.md` mit Trigger „erste Use-Case-Schicht-
Anforderung nach Plan-Persistenz als Backup-Quelle".

Pin-Count-Buchhaltung wird im D-05-Replacement-Text bei Re-Closure
aktualisiert (heute Stand: 20 OptimizationCore-Pins + 13
Persistence-Pins; nach Korrektur-Pass voraussichtlich 25 + 13).

### Re-Closure-Notiz (2026-05-11, nach Korrektur-Pass)

Korrektur-Pass landet in Commit `9db135b`:

- Neuer Marker-Driven-Port `IFallbackScheduleOptimizer :
  IScheduleOptimizer` in Application/Optimization.
- `OptimizationCoreScheduleOptimizer` ctor erweitert um
  `IFallbackScheduleOptimizer?` + `IFallbackPlanValidator?`;
  Constructor-Guard wirft `optimization-core-fallback-without-
  validator` wenn Fallback ohne Validator. `TryRunFallbackAsync`-
  Helper kapselt Fallback-Call + 4-Achsen-Check (Kontext/Horizon/
  MaxAge aktiv; Telemetrie-Drift skip'd da Adapter keinen Snapshot
  hat). Drei Transport-Failure-Branches (Connect/Health/Version-
  RpcException, stream-closed-without-result, Optimize-Stream-
  RpcException) routen jetzt zuerst über den Fallback.
- `BessHostOptions.OptimizationCoreFallbackBackend` + Stale-Kommentar-
  Fix (Finding 3); `BessHostBuilder.ConfigureOptimizationCoreFallback`
  registriert OR-Tools als `IFallbackScheduleOptimizer` wenn `"or_tools"`
  gesetzt ist.
- 5 neue Pins in `OptimizationCoreFallbackTests.cs` decken: fallback-
  registered-success, no-fallback-failed-no-activation, fallback-
  throws-falls-through, sidecar-success-no-fallback-called, fallback-
  with-context-mismatch-rejected-by-validator.
- HIL-Gate `make test-hil-optimization-core`: 25/25 grün (vorher 20).
- F-M5-05 „Letzter-bekannter-Plan-Fallback" als trigger-watch in
  `note-RM-M5-followups.md` angelegt (out-of-scope-Cut weil Hexagonal-
  konformer Pfad einen neuen Application-Driven-Port verlangt).

Pin-Count-Final: 25 OptimizationCore-Pins + 13 Persistence-Pins.
Master-Plan-Eintrag und D-05-Replacement-Wortlaut werden im Re-
Closure-Commit auf 25 + 13 synchronisiert; das vorab-gepinnte D-05-
Pin-Tally (17) bleibt im Slice-Plan als historisches Asset
dokumentiert.

---

## 6. Akzeptanzkriterien

- **Proto-Vertrag** in `proto/optimization-core/v1/` versioniert,
  `Grpc.Tools`-Codegen integriert.
- **`OptimizationCoreScheduleOptimizer`** implementiert
  `IScheduleOptimizer` und ist composition-root-mäßig austauschbar.
- **Adapter wirft drei neue Reasons** über die jeweils passende
  Validierungs-Schicht:
  `optimization-core-not-hardened-in-production` (EnsureValid, D-02)
  und `optimization-core-contract-incompatible` (EnsureValid bei
  nicht-semver-parsbarem `ExpectedContractVersion`; Runtime-Variante
  derselbe Reason in `EnsureContractCompatibleAsync` wenn der Sidecar
  einen inkompatiblen Range reportet) sowie
  `optimization-core-uds-permissions-not-locked` (Runtime-Check in
  `OptimizationCoreClient.ConnectAsync`, weil Filesystem-Mode-Bits
  zur Options-Konstruktions-Zeit noch nicht verfügbar sind).
- **Persistenter Idempotency-Store** mit Unique-Constraint auf
  `request_id`; atomare CAS-Operation; Restart-Replay-Pin grün.
- **Sidecar-Status-Taxonomie-Mapping** als versioniertes Doku-
  Artefakt + `OptimizationCoreStatusMapper`-static.
- **Fallback-Matrix-Implementierung** für alle Fehlerklassen aus
  plan-RM-M5 §Fallback-Matrix; Plan-Gültigkeits-Check für alle
  Invalidations-Reasons.
- **25 pinned Tests** im
  `BatteryEms.OptimizationCore.IntegrationTests`-Projekt (5 happy +
  4 negativ + 4 mixed-version + 4 security + 3 adapter-side
  idempotency + 5 local-fallback) plus 13 Persistence-Pins in
  `BatteryEms.Persistence.IntegrationTests`. Die 5 local-fallback-
  Pins + 3 adapter-side-idempotency-Pins kommen aus dem Sub-Slice-C-
  Korrektur-Pass (§5.1); das vorab-gepinnte D-05-Pin-Tally (17)
  bleibt im Slice-Plan als historischer Replacement-Text erhalten.
- **`make test-hil-optimization-core` grün** in `make gates` und
  `make ci`.
- **Quality-Doku** (`docs/user/quality.md` §2.6) listet die neuen
  Pins und das neue Pflicht-Gate.
- **`note-RM-M5-followups.md`** (neu) trägt die F-Items für
  Cert-Rotation, Multi-Tenant-Bearer-Token-Auth, produktionsnaher
  Drittsprach-Sidecar mit konkreten Triggern.
- **Slice-Plan** in `docs/plan/planning/done/plan-RM-M5-01.md`.
- **Master-Plan-Zeile RM-M5-01** flippt auf ✅ mit dem D-05-
  Replacement-Text.

---

## 7. Risiken und Tradeoffs

- **gRPC-Codegen-Disziplin.** `Grpc.Tools` ist Microsoft-supported
  und stabil, aber jede Proto-Änderung bricht potentiell den Build.
  Mitigation: `buf-lint`-CI-Hygiene-Gate als RM-M5-06-Carve-out;
  RM-M5-01 lebt mit Hand-Disziplin.
- **UDS auf .NET 8+.** `SocketsHttpHandler.ConnectCallback` für
  UDS-HTTP/2 ist getestetes Pattern (siehe Microsoft Docs), aber
  weniger weit verbreitet als TCP. Mitigation: B-Sub-Slice fängt
  die UDS-Mocking-Pfade als erste Test-Linie; falls .NET-Runtime-
  Regression auftaucht, kann Test-Linie temporär auf TCP fallen
  (Production-Pfad bleibt UDS-Default).
- **Idempotency-Store-Latenz.** Pro Optimize-Call zusätzliche
  DB-Round-Trip pre-Sidecar + post-Sidecar (CAS). Mitigation:
  `OptimizationRun.CreatedAt`-Insert und Idempotency-Insert
  laufen in der selben Transaktion (Outbox-Pattern); falls
  Latenz-Hit messbar wird, ist Async-Outbox-Carve-out eigene
  Folge-Slice.
- **Mixed-Version-Test-Komplexität.** Vier separate Stub-
  Konfigurationen plus Version-Override-Slots. Mitigation: D-Sub-
  Slice ist als letzter Slice geschnitten; A-C bauen das Fundament,
  D legt nur das Test-Material.
- **TestSidecar-Lifecycle.** `Grpc.AspNetCore`-WebApplicationFactory
  + UDS-Bind-Race kann unter parallelen Tests flackern. Mitigation:
  per-Test-UDS-Pfad mit `Guid:N` (D-06 PKI-Pfad-Konvention) plus
  per-class Fixture-Isolation (D-07 Test-Layout).
- **Operator-UX bei zwei Optimierungs-Pfaden.** Composition-Root
  hat jetzt drei Optimizer-Slots (`NoOp`, `or_tools`, `optimization-
  core`). Operator könnte versehentlich beide gleichzeitig
  konfigurieren. Mitigation: `BessConfigurationBootstrap` macht
  Multi-Konfig fail-closed mit `multiple-optimizers-configured`-
  Reason (analog zur OPC-UA-`IoAdapterTriage`-Linie aus M4-04).
- **Fallback-Plan-Gültigkeits-Check-Stempel-Komplexität.**
  plan-RM-M5 §Fallback-Plan-Gueltigkeit fordert Stempel-Vergleich
  über asset_id, schedule_type, horizon_start, horizon_end,
  time_step, constraint-version, market/reserve-Kontext. Schedule-
  Persistenz aus M2 hat heute nicht alle diese Felder; M5-01-C
  muss `Schedule`-Domain entweder erweitern oder den Stempel-
  Vergleich auf verfügbare Felder reduzieren mit konkretem
  Folge-Trigger.

---

## 8. Sequenz

**Schritt 1: Plan reviewen.** Externer Review-Pass analog zur
M4-05-Linie. Kritische Punkte:
- Hält D-01 (Adapter hinter `IScheduleOptimizer`)? Reviewer prüft,
  ob die `OptimizationCoreScheduleOptimizer`-Surface die M2-
  `IScheduleOptimizer`-Vertrags-Form ohne Erweiterung implementieren
  kann.
- Hält D-03 (worker-owned Idempotency-Store)? Reviewer prüft, ob
  die `0003_optimization_idempotency.sql`-Migration die plan-RM-M5
  §Request-Idempotenz-Felder vollständig deckt (terminal-state,
  reason, run_id, produced_version).
- Hält D-04 (Transport-Mapping als versioniertes Artefakt)?
  Reviewer prüft, ob die Tabelle in `transport-mapping-v1.md`
  alle plan-RM-M5 §Sidecar-Status-Taxonomie-Zellen deckt.
- Hält D-08 (`OptimizeMpc` Vertrag-only)? Reviewer prüft, ob
  RM-M5-02 ohne Proto-Bruch auf den Vertrag aufsetzen kann.

**Schritt 2: Sub-Slices in Reihenfolge A → B → C → D umsetzen.**

1. **Sub-Slice A**: Proto-Vertrag + Adapter-Skelett + Transport-
   Mapping. Reine Adapter-/Doku-Linie ohne Wire-Tests. Codegen
   im Build, Options-EnsureValid-Pins, Status-Mapper-Tabelle.
2. **Sub-Slice B**: In-Process TestSidecar + erste Roundtrip-Pins
   (Health, Version, Optimize-Success, Streaming, Cancel). Erste
   echte gRPC-Wire-Round-Trip-Surface.
3. **Sub-Slice C**: Idempotency-Store + Migration + Fallback-Matrix
   + Plan-Gültigkeit + Security-Pins. Persistenz-Layer + Negativ-
   Pfade; größter Sub-Slice.
4. **Sub-Slice D**: Mixed-Version-Compat-Tests + Quality-Doku +
   Closure. Sammel-Slice mit Doku-Sync und Master-Plan-Flip.

**Schritt 3: Closure-Commit.** Pattern wie M4-05-Linie — ein Commit
pro Sub-Slice plus optional Review-Fix-Commit nach externer
Review-Runde. Master-Plan-Move nach allen Sub-Slices grün.

**Schritt 4 (optional): Production-Smoke gegen einen externen
Sidecar-Container.** Wenn ein Drittsprach-Sidecar als Schwester-
Projekt (`bess-optimization-core`) gebaut wird, kann ein
einmaliger Smoke-Test mit `Defaults.ForProductionSecure` (UDS +
mTLS) gegen den realen Sidecar gefahren werden. Das ist **kein**
Pin; reine Validierung der Production-Konfiguration. RM-M5-06
Container-Orchestrierungs-Gate ist der formalisierte Pfad.

---

## 9. Folgearbeiten (gehen in `note-RM-M5-followups.md`)

**Neu von M5-01-D explizit angelegt:**

- **F-M5-01 Cert-Rotation für mTLS-Cross-Host-Pfad.** Analog zu
  F-18 aus M4-05. Trigger: erstes Cert-Lifecycle-Event in der
  Operator-Praxis. Cert-Watcher + `OptimizationCoreClient.
  ReloadCertificatesAsync` ohne Process-Restart.
- **F-M5-02 Multi-Tenant-Bearer-Token-Auth.** Trigger: zweite
  Operator-Instanz oder Multi-Tenant-Future-Pfad
  (`OpcUaAdapterOptions.UserIdentity` aus F-19-Linie als
  paralleles Pattern). `CallCredentials`-Interceptor + Secret-
  Resolver-Driven-Port.
- **F-M5-03 Drittsprach-Sidecar-Produktiv-Implementierung.**
  Trigger: erster echter HiGHS/OR-Tools-Backend-Wunsch. Eigenes
  Schwester-Projekt `bess-optimization-core` oder
  `optimization-core/sidecar/`-Subverzeichnis mit Sprach-Pivot-
  Diskussion (Python/Rust/C++).
- **F-M5-04 Schedule-Stempel-Erweiterung für Plan-Gültigkeits-
  Check.** Trigger: Plan-Gültigkeits-Check braucht mehr Felder
  (z.B. constraint-version, market/reserve-Kontext) als die
  M2-`Schedule`-Domain heute trägt. Schedule-Schema-Migration
  + Stempel-Vergleichs-Erweiterung.

**Bestehend, unverändert (aus ADR 0005 §7 Phase-4-Pivot-Trigger):**

- Latenz-Pflicht-Bound unter 1 ms pro MPC-Schritt → Phase-4-
  Transport-Pivot (Shared Memory / Edge Controller gemäß
  spec/architecture.md §13.1).
- Multi-Asset-per-Sidecar-Topologie → eigene Folge-ADR.
- Drittsprach-Solver-Lifecycle-Kosten → In-Process-Embed-Pivot
  (P/Invoke einer Solver-Library im EMS-Host).
