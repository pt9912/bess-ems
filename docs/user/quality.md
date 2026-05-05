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
- [`docs/plan/planning/open/roadmap.md`](../plan/planning/open/roadmap.md)
  (Reihenfolge der Gate-Aktivierung pro Meilenstein; bei Aktivierung wird
  dieser Rückverweis auf `docs/plan/planning/in-progress/roadmap.md`
  aktualisiert)

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

| Tool                                     | Zweck                                                    |
| ---------------------------------------- | -------------------------------------------------------- |
| `dotnet build -warnaserror`              | Compiler- und Analyzer-Warnings als Fehler               |
| `dotnet format --verify-no-changes`      | Style-Konformität gemäß `.editorconfig`                  |
| Roslyn-Analyzer (`Microsoft.CodeAnalysis.NetAnalyzers`) | Standard-Analyzer-Profil          |
| StyleCop.Analyzers                       | Style-Regeln (Naming, Doku, Layout)                      |
| `Microsoft.VisualStudio.Threading.Analyzers` | async/await-Regeln (z. B. ConfigureAwait, Sync over Async) |
| SonarAnalyzer (optional, ab M2)          | Bug-Patterns, Code Smells                                |

`#pragma warning disable` und `[SuppressMessage]` sind nur mit
`Justification`-Attribut zulässig, das den Pfad und die Begründung
nennt. Globaler `<NoWarn>` ist verboten.

**SOLID-nahe Designsignale** (Aktivierung pro Roslyn-Analyzer-Diagnose-ID
in `.editorconfig`, schrittweise scharf gestellt ab M1):

| Diagnostic-ID  | Zweck                                                        |
| -------------- | ------------------------------------------------------------ |
| CA1062         | Argument-Null-Checks an Public-API-Grenzen                   |
| CA1822         | Statische Member, die nicht auf Instance zugreifen           |
| CA2007         | `ConfigureAwait` an Library-Grenzen                          |
| CA1031         | Kein `catch (Exception)` ohne Rethrow/Log                    |
| CA1716         | Reservierte Bezeichner (Sprache-/Framework-Konflikte)        |
| CA1054/55/56   | URI-/Path-Typisierung statt `string`                         |
| SA1200         | `using`-Direktiven Layout                                    |
| VSTHRD-Reihen  | Threading-/async-Regeln aus VS Threading Analyzer            |

Die vollständige Aktivierungstabelle lebt in `.editorconfig` im
Repository-Root und ist Teil des Lint-Gates.

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
| `.editorconfig`             | C#-Style + Roslyn-Diagnostic-Severities              |
| `Directory.Build.props`     | `TreatWarningsAsErrors=true`, gemeinsame Analyzer    |
| `.globalconfig`             | globale Roslyn-Analyzer-Severitäten                  |
| `native/.clang-format`      | C/C++ Layout                                         |
| `native/.clang-tidy`        | C/C++ Check-Profil                                   |
| `native/CMakeLists.txt`     | Compiler-Flags `-Wall -Wextra -Wpedantic -Werror`    |

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
BatteryEms.Realtime
BatteryEms.Control
BatteryEms.Markets
BatteryEms.Optimization
BatteryEms.Protocols.Abstractions
BatteryEms.Protocols.Modbus
BatteryEms.Protocols.Mqtt
BatteryEms.Protocols.OpcUa            # ab M4
BatteryEms.Persistence
BatteryEms.NativeInterop              # ab M3
BatteryEms.Api
```

Threshold: **90 % Line-Coverage**, Ziel **≥ 95 %**. Begründung analog
m-trace: ein Threshold, der mit der Realität gleichzieht statt sie zu
führen, wird typischerweise gesenkt — also wird er von Anfang an hoch
gehalten. Senkung des Defaults ist eine ADR-pflichtige Entscheidung.

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
| `battery_control_core_abi_version()` exportiert     | `native/include/battery_control_core.h` |
| `.NET`-Startup-Check vergleicht erwartete ABI       | `BatteryEms.NativeInterop`       |
| Mismatch → Service startet nicht (LH-OPS-001)       | Integrationstest in M3           |

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
make native-lint        # ab M3
make test
make test-safety
make test-integration
make test-native-interop  # ab M3
make test-container
make coverage-gate
make build              # erzeugt Runtime-Image
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
