# Qualität

## Zweck

Dieses Dokument beschreibt die verbindlichen Qualitäts- und Messpfade für
`bess-ems`. Es dokumentiert keine Review-Historie und keine einmaligen
Befunde, sondern die aktuell gültigen, **reproduzierbaren Docker- bzw.
Make-Prüfwege** für statische Analyse, Tests, Coverage, Verträge und
CI/Release.

Aktuelle Werte (Coverage-Quote, Image-Größe, Testlaufzeit) leben nicht
in diesem Dokument; sie ändern sich pro Lauf. Dieses Doc fixiert *wie*
gemessen wird, nicht *was die letzte Messung ergab*.

Bezug:
- [`spec/lastenheft.md`](../../spec/lastenheft.md) §26 (LH-TEST-*),
  §12 (LH-SAFE-*), §18 (LH-NATIVE-*), §24 (LH-DEPLOY-*)
- [`spec/architecture.md`](../../spec/architecture.md) §13 (Native-Core),
  §16 (Testarchitektur)
- [`docs/plan/planning/in-progress/roadmap.md`](../plan/planning/in-progress/roadmap.md)
  (Reihenfolge der Gate-Aktivierung pro Meilenstein)

Status: dieses Dokument fixiert die Gate-Definitionen ab Meilenstein
**M1**. Gates, die erst mit späteren Meilensteinen aktiv werden (Native
Core ab M3, OPC-UA ab M4, MPC/Sidecar ab M5), sind explizit als
Platzhalter mit Aktivierungs-Meilenstein gekennzeichnet.

---

## 1. Statische Analyse

Statische Analyse läuft Docker-basiert und ist Pflicht-Bestandteil des
Build-Prozesses. Verstöße brechen den Build. Es gibt zwei Toolketten:
C#/.NET für die Hauptcodebasis und C/C++ für den Native Core.

### 1.1 C#/.NET (`dotnet`, Roslyn-Analyzer)

Statische Analyse läuft über die `lint`-Stage des `Dockerfile`:

```bash
make lint   # = docker build --target lint
```

Die Stage führt aus:

| Tool                                                    | Zweck                                                              |
| ------------------------------------------------------- | ------------------------------------------------------------------ |
| `dotnet build -warnaserror`                             | Compiler- und Analyzer-Warnings als Fehler                         |
| Roslyn-Analyzer (`Microsoft.CodeAnalysis.NetAnalyzers`) | Maintainability- und SOLID-Regeln auf `AnalysisLevel=latest-all`   |
| `Microsoft.CodeAnalysis.Metrics` (optional)             | Erzeugt `*.Metrics.xml` über `dotnet build /t:Metrics`, Linux-CLI eingeschränkt |

`#pragma warning disable` und `[SuppressMessage]` sind nur mit
`Justification`-Attribut zulässig, das den Pfad und die Begründung
nennt. Globaler `<NoWarn>` ist verboten — Ausnahme: `CA1014`
(CLSCompliant-Markierung) ist projektweit per `Directory.Build.props`
unterdrückt, weil Bess-EMS keine NuGet-Library publiziert.

**SOLID-nahe Designsignale** (Aktivierung über `AnalysisLevel=latest-all`
in `Directory.Build.props`, Severities pro Diagnose-ID in
`.editorconfig`, scharf gestellt ab M1 RM-M1-20):

| Diagnostic-ID                         | Bezug             | Zweck                                                         |
| ------------------------------------- | ----------------- | ------------------------------------------------------------- |
| CA1501                                | SRP               | Vererbungstiefe begrenzt                                      |
| CA1502                                | SRP               | Cyclomatic Complexity ≤ 25 pro Methode                        |
| CA1505                                | SRP               | Maintainability Index pro Typ/Methode                         |
| CA1506                                | SRP               | Class Coupling pro Typ                                        |
| CA1000                                | LSP               | Keine statischen Member auf generischen Typen                 |
| CA1001                                | OCP               | Typen mit Disposable-Feldern müssen `IDisposable` sein        |
| CA1012                                | DIP               | Abstrakte Typen ohne öffentliche Konstruktoren                |
| CA1033                                | LSP / OCP / ISP   | Interface-Methoden auch in Subtypen aufrufbar                 |
| CA1040                                | ISP               | Keine leeren Interfaces                                       |
| CA1715                                | LSP               | Korrektes Präfix für Interfaces / Typparameter                |

Die vollständige Aktivierungstabelle lebt in `.editorconfig` im
Repository-Root und ist Teil des Lint-Gates. Der Build kopiert die
Datei explizit ins Lint-Image — fehlt sie im Container, fallen die
Severities lautlos zurück und das Gate schweigt.

### 1.2 C Native Core

Die nativen Komponenten liegen unter
`native/battery_control_core/` (Implementierung in C, Header in C
mit `extern "C"`-Block für Mixed-Language-Konsumenten; Test-Harness
in C++ mit doctest, siehe §2.4). Statische Analyse läuft über die
`native-lint`-Stage:

```bash
make native-lint   # = docker build --target native-lint
```

Die Stage führt für `native/battery_control_core/src/` aus:

| Tool          | Zweck                                                         |
| ------------- | ------------------------------------------------------------- |
| `clang-tidy` (auf `compile_commands.json` mit `--warnings-as-errors=*`) | regelbasierte statische Analyse gegen `.clang-tidy`-Profil: `-* + bugprone-* + clang-analyzer-* + readability-function-cognitive-complexity` (Threshold 20); `bugprone-easily-swappable-parameters` mit dokumentierter Begründung deaktiviert; `HeaderFilterRegex` schließt FetchContent-Quellen aus |
| Compiler-Flags `-Wall -Wextra -Wpedantic -Werror -Wshadow -Wnull-dereference -Wdouble-promotion` | jede Warnung ist Fehler |

Komplexitätsmetriken auf Funktionsebene (Cognitive-Complexity)
laufen heute über die clang-tidy-Regel
`readability-function-cognitive-complexity` mit Schwelle 20;
eine zusätzliche Metrik-Stage (z. B. `lizard`) ist als Folge-Slice
denkbar, aber nicht Bestandteil der RM-M3-09-Closure.

`clang-tidy`-Profil (vollständige Liste in
`native/battery_control_core/.clang-tidy`):

| Check-Gruppe              | Zweck                                                  |
| ------------------------- | ------------------------------------------------------ |
| `bugprone-*`              | klassische Bug-Patterns (mit Ausnahme `bugprone-easily-swappable-parameters`, dokumentierte Begründung im Config-Header) |
| `clang-analyzer-*`        | statische Programmanalyse (Null-Deref, uninitialisierte Variablen, Dead Stores) |
| `readability-function-cognitive-complexity` | Cognitive-Complexity-Limit pro Funktion |

`// NOLINT`-Kommentare sind im Native-Core nicht zulässig — die
Lint-Stage ist hart auf `--warnings-as-errors=*`. Eine bewusste
Ausnahme erfordert eine `.clang-tidy`-Profiländerung mit `Why:`-
Kommentar im Config-Header und einen ADR-Vermerk; pfadweise
`HeaderFilterRegex` schließt FetchContent-Quellen aus dem
Lint-Scope, nicht aus der Disziplin.

**Sanitizer-Build**: parallel zur `native-build`-Stage erzeugt die
`native-sanitizer`-Stage einen Debug-Build mit
`-fsanitize=address,undefined -fno-sanitize-recover=all` und
führt den doctest-Suite-Lauf aus; jeder ASan/UBSan-Treffer ist
ein Hard-Fail des Gates (RM-M3-09).

```bash
make native-sanitizer   # = docker build --target native-sanitizer
```

### 1.3 Konfigurationsdateien

| Datei                       | Zweck                                                |
| --------------------------- | ---------------------------------------------------- |
| `.editorconfig`             | C#-Style + Roslyn-Diagnostic-Severities (CA-Regeln aus §1.1)        |
| `Directory.Build.props`     | `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, `RestorePackagesWithLockFile=true` (siehe §1.4), gemeinsame Analyzer-PackageReference |
| `Directory.Packages.props`  | Zentrale Package-Versionen inkl. `Microsoft.CodeAnalysis.NetAnalyzers` |
| `**/packages.lock.json`     | Pro Projekt eingecheckte Lock-Datei mit Content-Hashes (siehe §1.4) |
| `native/.clang-format`      | C/C++ Layout                                         |
| `native/.clang-tidy`        | C/C++ Check-Profil                                   |
| `native/CMakeLists.txt`     | Compiler-Flags `-Wall -Wextra -Wpedantic -Werror`    |

### 1.4 Supply-Chain: NuGet-Lock-Files

`Directory.Build.props` setzt `RestorePackagesWithLockFile=true`, dadurch
emittiert jedes Projekt beim Restore eine `packages.lock.json` mit
Content-Hashes für direkte und transitive Dependencies. Die `restore`-
Stage des `Dockerfile` ruft `dotnet restore --locked-mode` auf — fehlt
eine Lock-Datei, oder weicht ein Hash vom Committeten ab, schlägt der
Restore fehl, bevor irgendetwas gebaut wird.

Anlass: RM-M2-OP-05 hat mit `Google.OrTools` die erste Dependency mit
Native-Bindings (`libortools.so`, `libabsl_*.so`) eingeführt. Eine
republished oder kompromittierte Native-DLL würde im aspnet-Prozess als
Code mit User-Rechten ausgeführt; Lock-Files binden den Inhalt an den
Hash, den der Reviewer beim Mergen gesehen hat.

**Workflow zum Versionsbump:**

1. Version in `Directory.Packages.props` ändern.
2. Lock-Files refreshen — aus dem Repo-Root und im SDK-Container, damit
   die Hashes denen entsprechen, die CI sieht. Da `Directory.Build.props`
   `RestoreLockedMode=true` global setzt, muss der Refresh den Lock-Modus
   explizit aushebeln:

   ```bash
   docker run --rm -v $(pwd):/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
     dotnet restore BatteryEms.sln /p:RestoreLockedMode=false
   ```

   (alternativ `--force-evaluate`).

3. Refreshte `packages.lock.json`-Dateien zusammen mit der Versionsänderung
   committen — `make lint` schlägt sonst auf der nächsten Ebene fehl.
4. Im Pull-Request den Lock-File-Diff mit reviewen: ein unerwarteter
   Hash-Wechsel auf einer Version, die nicht angefasst wurde, ist ein
   Supply-Chain-Signal, kein Lärm.

Der Workflow ist als `make lock-refresh` Target verfügbar; er ruft
denselben Docker-Befehl wie oben.

**Aufgeschobene Bumps (Stand 2026-05-07):** Keine offenen Major-/
Native-Binding-Bumps. Beim RM-M2-06-Bulk-Refresh und den Folge-Slices
wurden alle dokumentierten Folge-Bumps abgearbeitet — JsonSchema.Net
9.x, MQTTnet 5.x und Google.OrTools 9.15 (Native-Bindings, Bit-genaue
OP-09-Replay-Tests blieben grün). Die `Directory.Packages.props`-
Versionen sind die jeweils aktuellsten kompatiblen Stable-Releases.

---

## 2. Tests

Tests laufen Docker-basiert und sind in vier Klassen kategorisiert. Die
Klassifikation erfolgt über Test-Kategorien (xUnit `[Trait]` /
`[Category]`); die Stage entscheidet über die aktive Auswahl.

### 2.1 Unit-Tests

```bash
make test           # = docker build --target test
```

Filter: `Category!=Integration & Category!=Replay & Category!=Container`.
Pflicht ab M1 (LH-TEST-001/002).

Mindestumfang:

| Komponente               | LH-Bezug             |
| ------------------------ | -------------------- |
| Constraint Limiter       | LH-CTRL-002, LH-SAFE-002/3 |
| Ramp Limiter             | LH-CTRL-003          |
| State Machine            | LH-SM-001..003       |
| Snapshot Validation      | LH-RT-003, LH-DOM-004 |
| Vorzeichenkonvention     | LH §4.1              |
| Day-Ahead-Fahrplanlogik  | LH-MKT-001, LH-MKT-007 |
| Zeitmodell inkl. DST     | LH-MKT-007           |
| PID (ab M2)              | LH-CTRL-004          |

### 2.2 Integrationstests

```bash
make test-integration   # = docker build --target test-integration
```

Filter: `Category=Integration`. Verwendet Testcontainers für Postgres,
einen Mosquitto-MQTT-Broker und Modbus-/OPC-UA-Simulatoren.

Pflicht ab M1 für Modbus + MQTT (LH-TEST-003); OPC-UA-Integrationstests
werden mit M4 aktiv.

| Adapter / Pfad                         | Aktiv ab | LH-Bezug                  |
| -------------------------------------- | -------- | ------------------------- |
| Modbus-TCP gegen Simulator             | M1       | LH-MODB-001..005, LH-TEST-003 |
| MQTT gegen Mosquitto                   | M1       | LH-MQTT-001..005          |
| Postgres-Persistenz                    | M1       | LH-PERSIST-001..005       |
| API gegen `WebApplicationFactory`      | M1       | LH-API-001..007           |
| OPC-UA gegen Simulator                 | M4       | LH-OPCUA-001..005         |
| Optimization-Sidecar (gRPC)            | M5       | LH-OPT-006                |

#### 2.2.1 HIL-Pfad (optional, RM-M2-HIL-Welle)

```bash
make test-hil-modbus   # = docker compose -f tests/hil/compose.yml up
```

Filter: `Category=HIL`. **Nicht in `make ci` / `make gates` /
`make test-integration` enthalten** — der M1-Pflichtpfad bleibt der
deterministische Go-Simulator (`bess-field-sim`). Der HIL-Pfad
fährt einen separaten Compose-Stack (`tests/hil/compose.yml`) gegen
das externe `bess-hil-simulator:local`-Image hoch und prüft eine
P-Sprungantwort: 25 kW Setpoint via `ModbusCommandSink` →
Konvergenz-Read via `ModbusTelemetrySource` mit ±5 kW Toleranz
(PCS-Dynamik braucht ~1 s).

#### 2.2.2 OPC-UA-Roundtrip-Pfad (Mandatory, RM-M4-04 + RM-M4-08 + RM-M4-05)

```bash
make test-hil-opcua   # process-internal Embedded TestServer
```

Filter: `Category=Integration` im
`BatteryEms.OpcUa.IntegrationTests`-Projekt. **Pflicht-Gate** —
verdrahtet sowohl in `make gates` als auch in `make ci`, analog
zu `test-native-{interop,parity}`. Im Gegensatz zum HIL-Modbus-Pfad
braucht dieser Lauf **kein externes Asset**: der OPC-UA-TestServer
läuft `OPCFoundation.NetStandard.Opc.Ua.Server`-basiert im selben
Test-Prozess (`EmbeddedTestServerHost`, loopback-TCP-Port via
Kernel-Allocate).

**Pin-Inventory** (13 Pins gesamt — 5 happy-path + 2 negativ/stress
+ 6 security):

| Datei | Pin | Quelle |
| ----- | --- | ------ |
| `OpcUaRoundtripTests.cs` | EndToEnd_Read_emits_telemetry_with_mapped_values | RM-M4-04-D |
| `OpcUaRoundtripTests.cs` | EndToEnd_Subscribe_picks_up_value_change_within_two_intervals | RM-M4-04-D |
| `OpcUaRoundtripTests.cs` | EndToEnd_Write_setpoint_roundtrips_through_server | RM-M4-04-D |
| `OpcUaRoundtripTests.cs` | EndToEnd_StatusCode_bad_surfaces_as_protocol_error | RM-M4-04-D |
| `OpcUaRoundtripTests.cs` | EndToEnd_Reconnect_after_server_restart_keeps_stream_alive | RM-M4-04-D |
| `OpcUaNegativeTests.cs` | Multi_cycle_reconnect_keeps_stream_alive_and_does_not_leak_subscriptions | RM-M4-08-A |
| `OpcUaNegativeTests.cs` | Concurrent_source_and_sink_survive_restart_under_contention | RM-M4-08-A |
| `OpcUaSecurityTests.cs` | Secure_handshake_signandencrypt_succeeds_against_test_server | RM-M4-05-D |
| `OpcUaSecurityTests.cs` | Secure_handshake_sign_mode_succeeds_against_test_server | RM-M4-05-D |
| `OpcUaSecurityTests.cs` | Non_allowlisted_policy_throws_at_construction | RM-M4-05-D |
| `OpcUaSecurityTests.cs` | Production_profile_with_unsecured_mode_throws_at_construction | RM-M4-05-D |
| `OpcUaSecurityTests.cs` | Hil_simulator_profile_with_unsecured_mode_passes | RM-M4-05-D |
| `OpcUaSecurityTests.cs` | Production_profile_without_trusted_server_certificate_fails | RM-M4-05-D |

**Security-Default-Schwenk (RM-M4-05)**: pre-M4-05 fuhr der OPC-UA-
Adapter mit `MessageSecurityMode.None` plus dem `AllowUnsecured`-
Bool-Guard. Ab RM-M4-05 sind die Adapter-Defaults
`RuntimeProfile=Production` + `SecurityMode=SignAndEncrypt` +
`SecurityPolicy=Basic256Sha256`; ein Operator, der unsecured fahren
muss (HIL, lokale Entwicklung), setzt `RuntimeProfile=HilSimulator`
oder `Development` explizit. Der Production-Profile rejected
SecurityMode=None **unabhängig** von `AllowUnsecured` — der
AllowUnsecured-Bool ist im Production-Pfad bewusst nicht
ausreichend (M4-05 D-02).

**Konventions-Abgrenzung**:

- `make test-integration` → Modbus + MQTT + Postgres-Roundtrips via
  Compose-Sidecars; deckt die NICHT-OPC-UA-Adapter-Linien.
- `make test-hil-modbus` → opt-in HIL gegen externes
  `bess-hil-simulator:local`-Image.
- `make test-hil-opcua` → mandatory, process-internal Embedded
  TestServer; das einzige OPC-UA-spezifische End-to-End-Gate.

| Voraussetzung | Wer liefert |
| ------------- | ----------- |
| `bess-hil-simulator:local` lokal gebaut (HIL-OPEN-01) | Schwesterprojekt-Operator |
| `tests/hil/compose.yml` + `tests/hil/Dockerfile` | bess-ems |
| `config/examples/adapters/modbus.hil-simulator.json` | bess-ems |
| `BatteryEms.Hil.IntegrationTests` | bess-ems |

Wann den HIL-Pfad fahren: bei Änderungen am Modbus-Adapter
(Word-Order, Register-Tabellen, Float-Encoding) oder vor einem
Release, der dynamisches PCS/PQ-Verhalten gegen ein realistischeres
Modell sanity-prüfen soll. Der Go-Simulator deckt deterministische
Fixtures, der HIL-Simulator deckt PCS-Antwort und PQ-Capability.

#### 2.2.3 Optimization-Core-Sidecar-Pfad (Mandatory, RM-M5-01)

```bash
make test-hil-optimization-core   # in-process gRPC TestSidecar
```

Filter: `Category=Integration` im
`BatteryEms.OptimizationCore.IntegrationTests`-Projekt. **Pflicht-Gate**
— verdrahtet sowohl in `make gates` als auch in `make ci`, analog
zu `test-hil-opcua`. Der Test-Sidecar (`EmbeddedOptimizationCoreSidecar`)
fährt im selben Test-Prozess als `Grpc.AspNetCore`-Application gegen
einen Per-Test-UDS in `Path.GetTempPath()/BatteryEms/OptimizationCore/`;
kein externes Asset und kein Container.

**Pin-Inventory** (25 Pins gesamt — 5 happy-path + 4 negativ + 4
mixed-version + 4 security + 3 adapter-side idempotency + 5
local-fallback; die 4 mixed-version- und 4 security-Pins decken die
plan-RM-M5-01 §6 Akzeptanzkriterien, die 3 adapter-side
idempotency-Pins + 5 local-fallback-Pins kommen aus dem
Sub-Slice-C-Korrektur-Pass (plan §5.1) als Wire-Verhalten gegen
`IOptimizationIdempotencyStore` + Fallback-Matrix-Integration):

| Datei | Pin | Quelle |
| ----- | --- | ------ |
| `OptimizationCoreRoundtripTests.cs` | Health_probe_succeeds_against_test_sidecar | RM-M5-01-B |
| `OptimizationCoreRoundtripTests.cs` | Version_probe_compatibility_check_passes | RM-M5-01-B |
| `OptimizationCoreRoundtripTests.cs` | Optimize_success_produces_optimal_run_with_schedule | RM-M5-01-B |
| `OptimizationCoreRoundtripTests.cs` | Optimize_streaming_progress_does_not_block_final_result | RM-M5-01-B |
| `OptimizationCoreRoundtripTests.cs` | Optimize_cancellation_mid_stream_returns_failed_run | RM-M5-01-B |
| `OptimizationCoreNegativeTests.cs` | Deadline_exceeded_returns_failed_run_with_time_limit_status | RM-M5-01-B |
| `OptimizationCoreNegativeTests.cs` | Sidecar_unavailable_returns_failed_run_with_failed_status | RM-M5-01-B |
| `OptimizationCoreNegativeTests.cs` | Infeasible_sidecar_result_produces_no_schedule | RM-M5-01-B |
| `OptimizationCoreNegativeTests.cs` | Invalid_trajectory_output_is_rejected_as_failed_run | RM-M5-01-B |
| `OptimizationCoreMixedVersionTests.cs` | Worker_1_0_against_sidecar_1_0_optimizes_successfully | RM-M5-01-D |
| `OptimizationCoreMixedVersionTests.cs` | Worker_1_0_against_sidecar_0_5_returns_contract_incompatible | RM-M5-01-D |
| `OptimizationCoreMixedVersionTests.cs` | Worker_1_0_against_sidecar_2_0_min_returns_contract_incompatible | RM-M5-01-D |
| `OptimizationCoreMixedVersionTests.cs` | Worker_required_feature_missing_returns_contract_incompatible | RM-M5-01-D |
| `OptimizationCoreSecurityTests.cs` | Production_profile_with_plaintext_http_endpoint_throws_at_construction | RM-M5-01-C |
| `OptimizationCoreSecurityTests.cs` | Production_profile_with_world_readable_uds_throws_at_connect | RM-M5-01-C |
| `OptimizationCoreSecurityTests.cs` | Production_profile_with_locked_uds_passes_uds_mode_check | RM-M5-01-C |
| `OptimizationCoreSecurityTests.cs` | Hil_simulator_profile_with_world_readable_uds_passes | RM-M5-01-C |
| `OptimizationCoreIdempotencyTests.cs` | First_optimize_creates_pending_then_finalizes_as_sidecar_committed | RM-M5-01-C |
| `OptimizationCoreIdempotencyTests.cs` | Duplicate_optimize_with_same_inputs_skips_sidecar_call | RM-M5-01-C |
| `OptimizationCoreIdempotencyTests.cs` | Different_request_inputs_get_different_request_id | RM-M5-01-C |
| `OptimizationCoreFallbackTests.cs` | Transport_failure_with_fallback_returns_fallback_committed_schedule | RM-M5-01-C-fixup |
| `OptimizationCoreFallbackTests.cs` | Transport_failure_without_fallback_returns_failed_no_activation | RM-M5-01-C-fixup |
| `OptimizationCoreFallbackTests.cs` | Transport_failure_with_fallback_that_throws_falls_through_to_failed | RM-M5-01-C-fixup |
| `OptimizationCoreFallbackTests.cs` | Sidecar_success_does_not_invoke_fallback | RM-M5-01-C-fixup |
| `OptimizationCoreFallbackTests.cs` | Fallback_with_context_mismatch_is_rejected_by_validator | RM-M5-01-C-fixup |

Plus 13 Persistence-Pins in `BatteryEms.Persistence.IntegrationTests`
(`OptimizationIdempotencyStoreIntegrationTests.cs`) für den
Dapper-backed `optimization_idempotency`-Store: CAS-Race,
Restart-Replay, sechs Terminalzustände, Migration-Idempotenz. Diese
laufen unter `make test-integration` (Postgres-Compose-Stack), nicht
unter `make test-hil-optimization-core`.

**Security-Profile-Schwenk (RM-M5-01-C)**: pre-M5-01 kannte die
Composition-Root nur den M2-OR-Tools-Pfad. Ab RM-M5-01 ist der
Sidecar-Adapter ein wählbarer `IScheduleOptimizer`-Slot; der
Production-Profile lehnt plaintext-HTTP-Endpoints (Schema-Fehler
`optimization-core-not-hardened-in-production`) und world-readable
UDS-Sockets (`optimization-core-uds-permissions-not-locked`,
Mode≠0600/0660) hart ab. HilSimulator/Development bleibt für lokale
Test-Topologien plaintext-tolerant (analog zur OPC-UA-Linie).

**MPC-Produktionsgates (RM-M5-02-D)**: ein gesetztes
`Bess:MpcBackend` aktiviert den MPC-Pfad nur für `"local_osqp"`.
Production lehnt zwei unsichere Boot-Formen hart ab:
fehlender `IFallbackMpcOptimizer` ⇒
`mpc-production-without-fallback-pathway`; fehlender
`Bess:MpcClock="monotonic_anchored"` ⇒
`mpc-production-without-monotonic-clock`. Reservierte Backend-Namen
`"optimization_core"` und `"bi_modal"` werfen weiterhin zuerst
`mpc-backend-not-implemented`.

**Konventions-Abgrenzung**:

- `make test-integration` → Modbus + MQTT + Postgres-Roundtrips
  (inkl. `optimization_idempotency`-Persistence-Pins).
- `make test-hil-opcua` → mandatory, in-process OPC-UA-TestServer.
- `make test-hil-optimization-core` → mandatory, in-process
  gRPC-TestSidecar; das einzige optimization-core-spezifische
  End-to-End-Gate. Container-/Cross-Host-Topologie ist Folgearbeit
  (RM-M5-06 Container-Orchestrierungs-Gate).

### 2.3 Sicherheitsfall-Tests

Pflicht ab M1 (LH-TEST-006). Filter: `Category=Safety`. Werden zusätzlich
zur normalen Test-Stage automatisch ausgeführt; Verletzung bricht den
Build.

```bash
make test-safety   # = docker build --target test-safety
```

Verbindliche Sicherheitsfälle aus LH-TEST-006:

| Fall                         | Erwartetes Verhalten                                | LH-Bezug      |
| ---------------------------- | --------------------------------------------------- | ------------- |
| Emergency Stop aktiv         | spätestens nächster Zyklus → Stop-Command           | LH-SAFE-001   |
| BMS nicht verfügbar          | sicherer Zustand (`0 kW`/Stop) mit Reason           | LH-SAFE-004   |
| Wechselrichter nicht verfügbar | sicherer Zustand mit Reason                       | LH-SAFE-004   |
| SOC ungültig (NaN, <0, >100) | DataQuality `invalid`, kein aktiver Befehl          | LH-SAFE-006   |
| Temperatur unplausibel       | DataQuality `invalid`, kein aktiver Befehl          | LH-SAFE-006   |
| Veralteter Snapshot (>3 s)   | sicherer Zustand mit Reason                         | LH-RT-003, LH-CTRL-007 |
| Kommunikationsverlust        | sicherer Zustand nach max. Messwertalter            | LH-SAFE-004   |
| Abgelaufener `ValidUntil`    | Command verworfen                                   | LH-SAFE-005   |
| SOC ≥ MAX                    | Ladeanteil = 0                                      | LH-SAFE-002   |
| SOC ≤ MIN                    | Entladeanteil = 0                                   | LH-SAFE-003   |
| Schreibwert > Limit          | Adapter-seitige Schreibbegrenzung greift            | LH-SAFE-007   |

Jeder Sicherheitsfall ist mit einem dedizierten Test belegt; ein
fehlender Test gilt als Coverage-Fehler unabhängig vom Coverage-Gate.

### 2.4 Native-Interop-Tests

Aktiv ab **M3** (LH-TEST-005). Verteilt auf zwei getrennte
Make-Targets, deren xunit-Trait-Filter sicherstellen dass jeder
Gate-Failure auf seine Kategorie attributiert:

```bash
make test-native-interop   # Layout / ABI / non-finite contract (Category!=Parity)
make test-native-parity    # Replay-basierte Native↔Managed-Parität (Category=Parity)
```

`make test-native-interop` (RM-M3-07) deckt:

| Prüfung                                             | LH-Bezug                |
| --------------------------------------------------- | ----------------------- |
| Struct-Layout (Sequential, Größen 24/56/32/24 Bytes, Offsets, Konstanten) | LH-NATIVE-003 |
| ABI-Handshake `NativeControlLoader.TryLoad` mit echter `.so` | LH-NATIVE-005     |
| Loader-Pfade `Disabled` / `LibraryMissing` / `Loaded` mit echtem Gateway | LH-NATIVE-005 |
| Native-Contract bei nicht-finiten Inputs (Snapshot/Limits/Request/Previous) | LH-NATIVE-004 |
| Negatives `dt` mit `has_previous=1` → `BCC_STATUS_NEGATIVE_DT` | LH-NATIVE-004 |

`make test-native-parity` (RM-M3-10) deckt den replay-basierten
Parity-Vergleich gegen den versionierten Datensatz unter
`tests/fixtures/native_parity/cases.v1.json`; Details in §6.

Die nativen Unit-Tests (`native/battery_control_core/tests/`,
doctest 2.4.11 via FetchContent mit `URL_HASH SHA256`-Pinning)
decken die Constraint/Ramp-Pfade des C-Kernels separat ab und
laufen als ctest-Eintrag im `native-build`-Stage. Verletzung
bricht den Build.

### 2.5 Replay-Tests

Aktiv ab **M2**. Filter: `Category=Replay` (LH-TEST-004). RM-M5-04 stellt
das Gate als eigenes Docker-Target bereit. Die ersten Manifest-v1-Fixtures
liegen unter `tests/fixtures/replay/`.

```bash
make test-replay   # = docker build --target test-replay
make test-mpc-property   # RM-M5-02 MPC-Identity/Determinism/Replay-Hooks
```

Wiedergabe historischer Telemetrie-Datensätze. Erwartete Commands sind als
versionierte Goldens neben dem Manifest abgelegt; Abweichungen sind
erklärungspflichtig (ADR oder Plan-Eintrag). Der RM-M5-04-Diff trennt
`numeric_tolerance` von `business_drift`. Die M3-Native-Parity-Daten bleiben
unter `tests/fixtures/native_parity/cases.v1.json` und werden von RM-M5-04 per
Replay-Manifest referenziert; das ausführende Gate bleibt
`make test-native-parity`. Ab RM-M5-04-C treibt dieses Manifest auch einen
Managed-vs-Native-Engine-Vergleichsreport. Ab RM-M5-04-D geben Replay-Gates
einen maschinenlesbaren `replay-diff-report.v1`-JSON-Report in der
Assertion-Message aus; ist `BESS_REPLAY_REPORT_DIR` gesetzt, schreiben sie je
Datensatz eine gleichnamige Report-Datei fuer CI-Artefakte.

RM-M5-02 ergänzt den MPC-Replay-Vertrag vor der vollständigen
Replay-Plattform: `make test-mpc-property` pinnt das achtachsige
`mpc_request_id`-Tuple, deterministische Default-Seeds, Operator-Seed-
Override, byte-stabile Stamps, Cross-Run-Trajektorien-Toleranz,
MPC-Fallback-Stempel und `mpc_runs`-Retention/Replays.

### 2.6 Container-Tests

Aktiv ab M1 (LH-TEST-007). Filter: `Category=Container`.

```bash
make test-container
```

Startet das Compose-Setup, prüft Health-Endpoint, Boot-Zeit und im
Fall mit Native Core das erfolgreiche Laden der `.so`-Bibliothek
über die ABI-Versionsabfrage.

### 2.7 Tag-Steuerung

| Modus                | Stage / Make-Target              | Filter                     |
| -------------------- | -------------------------------- | -------------------------- |
| Unit (Default)       | `make test`                      | `Category!=Integration & !Replay & !Container & !NativeInterop` |
| Sicherheitsfälle     | `make test-safety`               | `Category=Safety`          |
| Integration          | `make test-integration`          | `Category=Integration`     |
| HIL (optional)       | `make test-hil-modbus`           | `Category=HIL`             |
| OPC-UA-Roundtrip     | `make test-hil-opcua`            | `Category=Integration` im OPC-UA-IntegrationTests-Projekt |
| Optimization-Core    | `make test-hil-optimization-core` | `Category=Integration` im OptimizationCore-IntegrationTests-Projekt |
| MPC Property         | `make test-mpc-property`          | RM-M5-02 Unit-Pins in Application/Worker/Architecture |
| Native Interop       | `make test-native-interop`       | `Category!=Parity` im NativeInterop-IntegrationTests-Projekt |
| Native Parity        | `make test-native-parity`        | `Category=Parity` im NativeInterop-IntegrationTests-Projekt |
| Replay               | `make test-replay`               | `Category=Replay` im Application-Tests-Projekt |
| Container            | `make test-container`            | `Category=Container`       |

---

## 3. Coverage

Coverage ist Pflicht-Gate. Es gibt zwei getrennte Pfade: .NET-Coverage
und Native-Coverage. Beide sind im Root-Target `make coverage-gate`
zusammengeführt.

### 3.1 .NET-Coverage

Werkzeug: Coverlet (über `coverlet.collector`) + ReportGenerator.

```bash
make coverage-gate                 # Default-Threshold 90 %
make coverage-gate THRESHOLD=92    # Threshold ad-hoc anheben
make coverage-report               # Profil + HTML in build/coverage/
```

Coverage-Range bewusst eingeschränkt: `BatteryEms.Worker/Program.cs`
(Hosting/Wiring), generierte Files und `BatteryEms.Infrastructure`-
Bootstrap-Code sind über `[ExcludeFromCodeCoverage]` ausgenommen.
Gemessen wird der Code in:

```
BatteryEms.Domain
BatteryEms.Application
BatteryEms.Api
BatteryEms.Worker
BatteryEms.Adapters.Modbus
BatteryEms.Adapters.Mqtt
BatteryEms.Adapters.OpcUa             # ab M4
BatteryEms.Adapters.Persistence
BatteryEms.Adapters.Telemetry
BatteryEms.Adapters.Optimization
BatteryEms.Adapters.NativeInterop     # ab M3
```

Threshold: **90 % Line-Coverage**, Ziel **≥ 95 %**. Ein Threshold, der
mit der Realität gleichzieht statt sie zu führen, wird typischerweise
gesenkt — also wird er von Anfang an hoch gehalten. Senkung des
Defaults ist eine ADR-pflichtige Entscheidung.

Artefakte:

| Datei                                | Form                          |
| ------------------------------------ | ----------------------------- |
| `build/coverage/dotnet/cobertura.xml`| Cobertura-XML                 |
| `build/coverage/dotnet/lcov.info`    | LCOV                          |
| `build/coverage/dotnet/index.html`   | HTML-Report                   |
| `build/coverage/dotnet/Summary.txt`  | Plain-Text mit Total          |

### 3.2 Native-Coverage

Aktiv ab **M3** (RM-M3-09). Werkzeug: `gcov` + `gcovr`,
getrennter Build mit `-DBCC_ENABLE_COVERAGE=ON` (umgesetzt über
die `bcc_enable_coverage`-Helper-Funktion in
`native/battery_control_core/CMakeLists.txt`).

```bash
make native-coverage-gate    BCC_COVERAGE_THRESHOLD=100   # Threshold-Check
make native-coverage-report                              # nur Report (entwicklerseitig)
```

Coverage-Range: alles unter `native/battery_control_core/src/`.
Tests unter `native/battery_control_core/tests/` sowie
FetchContent-`_deps/`-Quellen sind nicht Teil des Nenners.
Pflicht-Threshold: **100 % Line-Coverage**, weil der Native Core
eng abgegrenzt ist und im Regelpfad sicherheitskritisch wirkt.

**Coverage-Ausnahmen-Disziplin:** Ausnahmen über `// GCOVR_EXCL_START`
… `// GCOVR_EXCL_STOP`-Blöcke sind nur mit `// Why:`-Kommentar
innerhalb des Blocks zulässig. Geprüft über ein dediziertes
Make-Target:

```bash
make native-coverage-exclusions
```

Es enumeriert jeden Block und versagt non-zero, wenn ein Block
keinen `Why:`-Kommentar enthält. Zur RM-M3-09-Closure ist die
Anzahl der Exclusions im `src/`-Tree gleich **null** (der frühere
C++-`catch (...)`-Defense-in-Depth-Block wurde mit dem C-Pivot
entfernt; ein zukünftiger PID-Slice (RM-M3-13) darf eine neue
Ausnahme nur mit `Why:`-Kommentar einführen).

### 3.3 Monorepo-Gate

`make coverage-gate` ist die Klammer und ruft `dotnet-coverage-gate` und
ab M3 zusätzlich `native-coverage-gate` auf. CI verwendet ausschließlich
das Root-Target, damit Workflow- und lokale Disposition deckungsgleich
bleiben.

---

## 4. Runtime-Image

Das Runtime-Image ist gehärtet und folgt dem Multi-Stage-Pattern aus
[`spec/architecture.md`](../../spec/architecture.md) §15:

- Final-Image: `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu 24.04 Noble, kein SDK)
- `native-build`-Stage erzeugt `libbattery_control_core.so` aus
  `native/battery_control_core/` reproduzierbar; das Runtime-Image
  COPYed sie nach `/app/native/libbattery_control_core.so`
  (Default in `NativeControlOptions.LibraryPath`). Ein Build-Time
  `ldd`-Gate fail't das Image, falls die `.so` nicht-auflösbare
  Dynamic-Deps zieht. Aktuelle `.so` (RM-M3-09 closure) ist
  C-only, hat null `NEEDED`-Einträge und keine libstdc++-Linkage.
  (LH-NATIVE-006, LH-DEPLOY-004)
- App läuft als nicht-root User `app` (UID 1654, in der
  aspnet-Base bereits angelegt)
- Exposed Port: `8080`
- Healthcheck im Compose-Manifest auf `/health`; `make runtime`
  prüft zusätzlich `test -f /app/native/libbattery_control_core.so`
  plus `ldd` im laufenden Container

Smoke-Test (LH-DEPLOY-001/002, LH-TEST-007):

```bash
make runtime
# startet Compose, fragt /health, beendet
```

---

## 5. Vertrags-Gates

### 5.1 Domain-Vorzeichenkonvention

Pflicht-Gate ab M1 (LH §4.1). Eigene Test-Klasse
`SignConventionTests` enthält Properties:

- positiver Sollwert → Adapter-Schreibwert > 0 für Discharge-Devices
- negativer Sollwert → Schreibwert < 0 / inverse Skala laut
  Geräte-Mapping
- 0 kW → Adapter publiziert exakt `0` ohne Vorzeichendrift

Verstöße brechen den Build und sind nicht über
Coverage-Gates kompensierbar.

### 5.2 Native ABI

Aktiv ab **M3** (LH-NATIVE-005, RM-M3-03).

| Gate                                                | Ort                              |
| --------------------------------------------------- | -------------------------------- |
| `battery_control_core_abi_version()` exportiert     | `native/battery_control_core/include/battery_control_core.h` |
| `.NET`-Startup-Check vergleicht erwartete ABI       | `BatteryEms.Adapters.NativeInterop` (`NativeControlLoader.TryLoad`) |
| Mismatch → .NET-Fallback, Health/Logs/Metrik nennen `abi-mismatch` | Integrationstest in M3 |

Der Loader liefert fünf dokumentierte Endzustände: `disabled` (Opt-in
nicht gesetzt), `library-missing` (Pfad existiert nicht),
`load-failed` (dlopen oder Symbol-Lookup wirft), `abi-mismatch`
(major != erwartet oder minor < erwartet) und `loaded`. Major muss
exakt matchen, minor darf höher sein (additive Backward-Compat).

Seit **M3-D2** ist der Loader-Pfad im Host-Wiring aktiv:
`NativeInteropRegistration.AddBessNativeControl(IConfiguration)`
registriert `IControlKernel` als Singleton. Die Default-Konfiguration in
`src/host/BatteryEms.Host/appsettings.json` setzt
`NativeControl:Enabled=false`; damit startet der Host mit
`ManagedControlKernel`, auch wenn `/app/native/libbattery_control_core.so`
im Runtime-Image vorhanden ist. Ein produktionsnahes Native-Profil
aktiviert den Pfad explizit, z. B. per Environment:

```bash
NativeControl__Enabled=true
NativeControl__LibraryPath=/app/native/libbattery_control_core.so
```

Bei `NativeControl:Enabled=true` und kompatibler `.so` registriert der
Host `NativeFallbackControlKernel`; native `ok`/`limited`-Ergebnisse
werden verwendet, native Fehler aus validem .NET-Kontext fallen
deterministisch im selben Tick auf `ManagedControlKernel` zurück. Bei
`library-missing`, `load-failed` oder `abi-mismatch` ohne Abort-Flag
registriert der Host ebenfalls den Managed-Pfad und startet weiter.

Ein Startabbruch bei erwarteter, aber inkompatibler Library ist keine
M3-Default-Policy. Er braucht den expliziten
`NativeControlOptions.AbortOnAbiMismatch`-Wert (Default `false`) und
einen eigenen Test. Solange das Flag false ist, fällt der Host bei
`abi-mismatch` auf den Managed-Pfad zurück; mit
`NativeControl__AbortOnAbiMismatch=true` führt `abi-mismatch` beim
ersten `IControlKernel`-Resolve zum harten Startup-Fehler.

### 5.3 Adapter-Mapping-Schema

Pflicht ab M1 für Modbus + MQTT, ab M4 für OPC-UA (LH-CONF-002).
Jedes Mapping unter `config/adapters/**` wird beim Start gegen ein
JSON-Schema validiert. Schemata leben unter `config/schema/`.

| Schema                              | Aktiv ab |
| ----------------------------------- | -------- |
| `config/schema/modbus-mapping.json` | M1       |
| `config/schema/mqtt-mapping.json`   | M1       |
| `config/schema/opcua-mapping.json`  | M4       |
| `config/schema/asset.json`          | M1       |
| `config/schema/limits.json`         | M1       |

CI-Gate validiert alle Beispiel-Mappings unter `config/examples/`.

### 5.4 OpenAPI-Vertrag

Pflicht ab M1 (LH-API-001..007). Die API exportiert eine OpenAPI-3.1-
Beschreibung. Drei Gates:

- Schema-Wohlgeformtheit (gegen offizielles Meta-Schema)
- Endpunkt-Paritätstest: jeder im Lastenheft genannte Endpunkt ist
  vorhanden und antwortet mit dokumentiertem Statuscode
- AuthZ-Negativtest: schreibende Endpunkte ohne Rolle → 401/403
  (LH-API-007)

### 5.5 Konfigurations-Startvalidierung

Pflicht ab M1 (LH-CONF-003, LH-OPS-001). Test prüft, dass das System
mit unvollständiger oder ungültiger Konfiguration nicht in den aktiven
Regelbetrieb übergeht.

### 5.6 Hexagonale Architektur-Tabus

Pflicht-Gate ab M1 (LH-ARCH-002, LH-NF-006, RM-M1-22/23). Setzt die
Dependency Rule und die Architektur-Tabus aus
[`spec/architecture.md`](../../spec/architecture.md) §4.2 durch.

```bash
make arch-check
```

Implementierung in `tests/BatteryEms.ArchitectureTests` mit NetArchTest
oder ArchUnitNET (Tooling-Auswahl: AR-OPEN-009). Verstöße brechen den
Build und sind nicht über Coverage- oder andere Gates kompensierbar.

Verbindliche Regeln:

| Regel                                                                | Bezug                  |
| -------------------------------------------------------------------- | ---------------------- |
| `BatteryEms.Domain` referenziert keine Adapter-Bibliothek (ASP.NET, EF Core, MQTTnet, NModbus, OPC Foundation, OTel, Npgsql, gRPC, P/Invoke); nur `Microsoft.Extensions.Logging.Abstractions`/`Options` erlaubt | Architektur §4.2, AR-P-011 |
| `BatteryEms.Application` referenziert nur `BatteryEms.Domain`; keine Adapter-Refs, kein konkreter Solver | Architektur §4.2 |
| `adapters/driving/*` referenzieren nicht `adapters/driven/*` und keine anderen `adapters/driving/*` | Architektur §4.2 |
| `adapters/driven/*` referenzieren nicht `adapters/driving/*` und keine anderen `adapters/driven/*` | Architektur §4.2 |
| `BatteryEms.Adapters.NativeInterop` (ab M3) referenziert nur Application-Ports und Domain | Architektur §4.2, LH-NATIVE-* |
| Nur `BatteryEms.Infrastructure` referenziert sowohl `hexagon/` als auch `adapters/`; kein anderer Pfad referenziert `Infrastructure` | Architektur §4.2 |
| Driving Adapter implementieren nur Driving-Ports (`I*UseCase`, `I*Query`); Driven Adapter implementieren nur Driven-Ports | Architektur §4.2 §5.1 |

Suppressionen sind nicht zulässig. Eine bewusste Ausnahme erfordert eine
ADR und ergänzt die Regelmenge in dieser Sektion.

---

## 6. Native-/.NET-Parity

Aktiv ab **M3** (RM-M3-10). Pflicht-Gate, das den Native Core und
die .NET-Referenzimplementierung gegeneinander vergleicht.

```bash
make test-native-parity
```

Prinzip:

- Versionierter Golden-Datensatz unter
  `tests/fixtures/native_parity/cases.v1.json` (Schema-Doku in
  `tests/fixtures/native_parity/README.md`). Jeder Case ist ein
  `(snapshot, limits, request, expected)`-Tupel mit
  `expected.{active_power_kw, reason, was_limited, mode}`.
- Pro Case läuft sowohl `ManagedControlKernel`
  (`BatteryEms.Application.Control`) als auch der Native-Kern via
  `NativeControlKernel`
  (`BatteryEms.Adapters.NativeInterop`); Reason-Code-Mapping über
  `NativeFallbackControlKernel.MapReason`.
- Toleranz aus dem Fixture-Header
  (`tolerance_active_power_kw`, Default `1e-12`); in der Praxis
  bit-exakt, weil beide Pfade dieselbe FP-Sequenz auf identischen
  `double`-Werten ausführen. Die Toleranz dient als Headroom gegen
  zukünftige Plattformen mit abweichender FMA-Kontraktion.
- Toleranz-Erweiterungen oder Schema-Bumps benötigen ein neues
  `cases.v2.json` plus parallele Test-Klasse — In-Place-Bump ist
  Parity-History-Erase und verboten.
- **Bewusst nicht im Datensatz** (Plan-Vorgabe + README-
  Begründung): negatives `dt`, nicht-finite Snapshot/Limits/
  Request-Felder, Stale-Snapshot, `Available=false`,
  `ValidUntil`-Ablauf — diese bleiben Managed-Control- bzw.
  Native-Contract-Tests in `BatteryEms.Application.Tests` /
  `NativeAbiNegativeTests` und werden nicht als Parity mit
  ungültigen Inputs modelliert.

Der Native-Pfad ist niemals exklusiv: bei ABI-Mismatch oder
nativem Fehler aus validem .NET-Kontext fällt der Adapter
deterministisch auf die Managed-Referenz zurück, der Regelkreis
bleibt funktionsfähig (LH-ARCH-006, AR-P-009; siehe §5.2 für die
Loader-Endzustände).

---

## 7. CI-Pipeline

CI läuft auf GitHub Actions, Workflow `.github/workflows/build.yml`,
Runner `ubuntu-24.04`. Ausgeführt wird sie auf Pull Requests gegen
`main` und auf Pushes nach `main`.

Verbindliche Make-Targets pro Run, in dieser Reihenfolge:

```bash
make lint
make native-lint
make arch-check
make test
make test-safety
make coverage-gate
make simulator-lint
make simulator-test
make simulator-race
make simulator-coverage-gate
make schema-validate
make schema-drift-check
make native-build
make native-sanitizer
make native-coverage-gate
make native-coverage-exclusions
make test-native-interop
make test-native-parity
make test-hil-opcua
make test-hil-optimization-core
make test-integration
make test-container
make build                # erzeugt Runtime-Image
```

Die Targets delegieren in dieselben Docker-Stages, die lokal genutzt
werden. CI speichert pro Target das Build-Log als Workflow-Artefakt.
Coverage-Artefakte werden aus den Coverage-Images extrahiert:
`.NET`-Coverage aus `bess-ems-coverage-gate`, Simulator-Coverage aus
`bess-field-sim:coverage` und Native-Coverage aus
`bess-ems-native-coverage-gate`.

`docker build --no-cache-filter <stage>` wird in CI verwendet, um die
Re-Evaluation von Test-/Coverage-Stages zu erzwingen, ohne `deps`-Layer
zu verwerfen.

---

## 8. Release-Pipeline-Gates

Release-Gates sind in
[`docs/plan/adr/0002-release-pipeline-gates.md`](../plan/adr/0002-release-pipeline-gates.md)
entschieden. Workflow `.github/workflows/release.yml` läuft auf Tags
`v*.*.*` und ist vor dem ersten Tag `v0.1.0` verbindlich.

Verbindliche Gates:

- Tag-Validator: semver `vMAJOR.MINOR.PATCH[-PRERELEASE]`, kein
  Build-Metadata-Suffix.
- `make fullbuild`: alle CI-Gates plus Runtime-Image und Compose-Smoke.
- Runtime-Image-Build mit `SOURCE_DATE_EPOCH` aus dem getaggten Commit.
- OCI-Labels für Version, Revision und Source.
- Versionscheck: Tag-Version ↔ OCI-Label
  `org.opencontainers.image.version`; Revision ↔ `GITHUB_SHA`.
- Native-Library-Check: `/app/native/libbattery_control_core.so`
  existiert und `ldd` enthält kein `not found`.
- SBOM als Pflicht-Artefakt ab Major-Release (`v1.0.0` und höher).

Der Workflow ist Gate-only: Er veröffentlicht kein Image und signiert
noch nicht. Registry-Push und Cosign-Signatur werden erst aktiviert,
wenn Registry, Namensschema und Signatur-/OIDC-Policy entschieden sind.

---

## 9. Hinweise

- Dieses Dokument beschreibt den **reproduzierbaren Soll-Stand** der
  Gates. Veraltete Review-Notizen, einmalige Befunde und Werte
  einzelner Läufe gehören nicht hier hinein.
- Aktuelle Zahlen (Coverage-Quote, Image-Größe, Testlaufzeit) leben in
  CI-Artefakten und nicht in diesem Dokument.
- Gates, die mit späteren Meilensteinen aktiv werden, sind im Text
  explizit als Platzhalter mit Aktivierungs-Meilenstein gekennzeichnet.
  Ergänzungen werden hier eingetragen, sobald die jeweilige Folge-ADR
  existiert.
- Suppressionen (`SuppressMessage`, `// NOLINT`, `[ExcludeFromCodeCoverage]`,
  `LCOV_EXCL_*`) sind ohne `Why:`-/`Justification`-Begründung
  nicht zulässig. CI prüft das Vorhandensein der Begründung pro Pfad.
