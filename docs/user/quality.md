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

### 1.2 C/C++ Native Core

Die nativen Komponenten (`battery_control_core` und Folge-Module) liegen
unter `native/`. Statische Analyse läuft über die `native-lint`-Stage:

```bash
make native-lint   # = docker build --target native-lint
```

Die Stage führt für `native/` aus:

| Tool          | Zweck                                                         |
| ------------- | ------------------------------------------------------------- |
| `clang-format --dry-run -Werror` | Layout-Konformität gemäß `.clang-format`   |
| `clang-tidy` (auf `compile_commands.json`) | regelbasierte statische Analyse |
| `lizard`      | Komplexität, Funktionslänge, Parameteranzahl                  |
| Compiler-Flags `-Wall -Wextra -Wpedantic -Werror` | jede Warnung ist Fehler   |

Standard-Schwellen `lizard`:

- CCN ≤ 10
- Funktionslänge ≤ 50
- Parameteranzahl ≤ 5

Konfigurierbar per Make-Variable, analog zu `cmake-xray`-Pattern:

```bash
make native-lint \
  LIZARD_MAX_CCN=10 \
  LIZARD_MAX_LENGTH=50 \
  LIZARD_MAX_PARAMETERS=5
```

`clang-tidy`-Profil (Auswahl, vollständige Liste in
`native/.clang-tidy`):

| Check-Gruppe              | Zweck                                                  |
| ------------------------- | ------------------------------------------------------ |
| `bugprone-*`              | klassische Bug-Patterns                                |
| `cert-*`                  | CERT C++ Coding Standard                               |
| `cppcoreguidelines-*`     | C++ Core Guidelines                                    |
| `misc-*`                  | Allgemeine Hygiene                                     |
| `performance-*`           | unnötige Kopien, Move-Semantik                         |
| `readability-*`           | Lesbarkeit, Bezeichnerregeln                           |
| `modernize-*`             | C++20-Idiome (selektiv aktiviert)                      |

`// NOLINT`-Kommentare sind nur mit `Why:`-Begründung in der gleichen
Zeile zulässig; `NOLINTBEGIN/END`-Blöcke benötigen einen separaten
Kommentar mit Begründung. Pfadweise Carveouts (z. B. Tests gegen
`bugprone-magic-numbers`) sind in `.clang-tidy` über
`HeaderFilterRegex`/`CheckOptions` dokumentiert.

**Sanitizer-Build**: für die `test`-Stage des Native Core wird ein
zweiter Buildpfad mit aktivierten Sanitizern erzeugt
(`-fsanitize=address,undefined`, separate Stage `native-test-sanitize`).
Verletzungen brechen den Build (LH-TEST-005-nahe).

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

Aktiv ab **M3** (LH-TEST-005). Filter: `Category=NativeInterop`.

```bash
make test-native-interop   # = docker build --target test-native-interop
```

Pflicht-Inhalte:

| Prüfung                              | LH-Bezug                |
| ------------------------------------ | ----------------------- |
| Struct-Layout (sequential, sizes)    | LH-NATIVE-003           |
| ABI-Versionsabfrage beim Start       | LH-NATIVE-005           |
| Fehlercodes (NaN/Inf, neg. dt)       | LH-NATIVE-004           |
| P/Invoke-Ladefähigkeit im Container  | LH-NATIVE-006, LH-DEPLOY-004 |
| Werte-Parität gg. .NET-Referenz      | RM-M3-07, LH-ARCH-006   |

C++-Unit-Tests (`native/tests/`, Catch2 oder GoogleTest) decken die
nativen Kerne separat ab; Verletzung bricht den Build.

### 2.5 Replay-Tests

Aktiv ab **M2**. Filter: `Category=Replay` (LH-TEST-004).

```bash
make test-replay   # = docker build --target test-replay
```

Wiedergabe historischer Telemetrie-Datensätze. Erwartete Commands sind
versioniert als Goldens unter `tests/replay/testdata/`. Byte-stabiler
Vergleich pro Datensatz; Abweichungen sind erklärungspflichtig (ADR oder
Plan-Eintrag).

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
| Native Interop       | `make test-native-interop`       | `Category=NativeInterop`   |
| Replay               | `make test-replay`               | `Category=Replay`          |
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

Aktiv ab **M3**. Werkzeug: `gcov` + `gcovr`, getrennter Build mit
`-DBESS_ENABLE_COVERAGE=ON`.

```bash
make native-coverage-gate   COVERAGE_THRESHOLD=100
make native-coverage-report
```

Coverage-Range: alles unter `native/src/`. Tests unter `native/tests/`
sind nicht Teil des Nenners. Pflichtthreshold: **100 % Line-Coverage**,
weil der Native Core eng abgegrenzt ist und im Regelpfad sicherheits-
kritisch wirkt. Ausnahmen über `// LCOV_EXCL_*` sind nur mit
`Why:`-Kommentar zulässig.

Vor Coverage- oder Release-Prüfung muss zusätzlich gelten:

```bash
rg "LCOV""_EXCL_" native/src | wc -l    # muss in CI dokumentiert sein
```

Geprüft wird über ein dediziertes Make-Target
`make native-coverage-exclusions`; Default-Toleranz: 0.

### 3.3 Monorepo-Gate

`make coverage-gate` ist die Klammer und ruft `dotnet-coverage-gate` und
ab M3 zusätzlich `native-coverage-gate` auf. CI verwendet ausschließlich
das Root-Target, damit Workflow- und lokale Disposition deckungsgleich
bleiben.

---

## 4. Runtime-Image

Das Runtime-Image ist gehärtet und folgt dem Multi-Stage-Pattern aus
[`spec/architecture.md`](../../spec/architecture.md) §15:

- Final-Image: `mcr.microsoft.com/dotnet/aspnet:8.0` (kein SDK)
- Native-Build-Stage erzeugt `libbattery_control_core.so` aus `native/`
  reproduzierbar (LH-NATIVE-006, LH-DEPLOY-004), falls Native Core
  konfiguriert ist
- App läuft als nicht-root User (`appuser`)
- Exposed Port: `8080`
- Healthcheck im Compose-Manifest auf `/health`

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

Ein Startabbruch bei erwarteter, aber inkompatibler Library ist keine
M3-Default-Policy. Er braucht den expliziten
`NativeControlOptions.AbortOnAbiMismatch`-Wert (Default `false`) und
einen eigenen Integrationstest. Solange das Flag false ist, fällt
der Host bei `abi-mismatch` auf den Managed-Pfad zurück.

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

Aktiv ab **M3** (RM-M3-07). Pflicht-Gate, das Native Core und
.NET-Referenzimplementierung gegeneinander vergleicht.

```bash
make test-native-parity
```

Prinzip:

- Replay-Datensatz aus `tests/replay/testdata/parity/` läuft einmal
  durch die .NET-Variante (`ManagedBatteryControlKernel`) und einmal
  durch die Native Variante (`NativeBatteryControlKernel`).
- Vergleich der erzeugten Commands pro Tick mit definierter
  Toleranz (Default: 1e-6 kW absolut).
- Toleranz-Erweiterungen brauchen ADR-Eintrag.

Der Native-Pfad ist niemals exklusiv: das System bleibt durch die
.NET-Referenz lauffähig (LH-ARCH-006, AR-P-009).

---

## 7. CI-Pipeline

CI läuft auf GitHub Actions, Workflow `.github/workflows/build.yml`,
Runner `ubuntu-24.04`. Ausgeführt auf Pull Requests gegen `main` und
auf Pushes nach `main`.

Verbindliche Make-Targets pro Run, in dieser Reihenfolge:

```bash
make lint
make native-lint          # ab M3
make arch-check
make test
make test-safety
make test-integration
make test-native-interop  # ab M3
make test-container
make coverage-gate
make build                # erzeugt Runtime-Image
```

Die Targets delegieren in dieselben Docker-Stages, die lokal genutzt
werden. Test-, Coverage- und Lint-Reports als Workflow-Artefakte sind
optional bis M2; ab M2 sind sie Pflicht-Artefakte.

`docker build --no-cache-filter <stage>` wird in CI verwendet, um die
Re-Evaluation von Test-/Coverage-Stages zu erzwingen, ohne `deps`-Layer
zu verwerfen.

---

## 8. Release-Pipeline-Gates (Platzhalter)

Keine Release-Pipeline definiert. Wird mit dem ersten Tag (`v0.1.0`)
konkret. Erwartete Gates analog zum cmake-xray-Pattern:

- Tag-Validator (semver `vMAJOR.MINOR.PATCH[-PRERELEASE]`, kein
  Build-Metadata-Suffix)
- reproduzierbares Linux-Container-Image mit `SOURCE_DATE_EPOCH`
- OCI-Image-Idempotenz über `docker buildx imagetools inspect`
- Drei-Wege-Versionscheck: Tag ↔ Build-eingebrannte Version ↔
  `/health`-Endpoint-Version
- Native-ABI-Versionscheck: Tag-Major/Minor entspricht ABI-Version
- Asset-Allowlist gegen Asset-Drift
- SBOM (Syft) und Image-Signatur (Cosign) als Pflicht-Artefakte ab
  Major-Release

Die konkrete Ausgestaltung kommt mit einer Folge-ADR vor `v0.1.0`.

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
