# Archiv: Native-Core-Ideenskizze

**Status:** Archiviert / nicht normativ
**Ursprünglicher Pfad:** `spec/idea.md`
**Aktive Referenzen:** [`spec/lastenheft.md`](../../spec/lastenheft.md),
[`spec/architecture.md`](../../spec/architecture.md),
[`docs/plan/planning/open/roadmap.md`](../plan/planning/open/roadmap.md)

Dieses Dokument ist eine historische Ideenskizze zur .NET-/Native-Aufteilung.
Es ist keine aktive Spezifikation. Bei Widersprüchen gelten Lastenheft,
Architektur, Qualitätsdokument und Roadmap.

---

```text
C#/.NET = Orchestrierung, Geschäftslogik, APIs, Persistenz, Marktintegration
C/C++ = harte Echtzeitnähe, numerische Kerne, Protokoll-/Treiber-nahe Komponenten, schnelle Optimierung
```

Nicht alles in C/C++ ziehen. Das wäre unnötig teuer in Wartung, Tests und Deployment. C/C++ lohnt sich dort, wo du messbar Latenz, deterministische Ausführung oder native Bibliotheken brauchst.

## Aktualisierte Architektur

```text
Battery EMS
├── .NET Layer
│   ├── REST/gRPC API
│   ├── Worker Services
│   ├── Day-Ahead / Intraday Orchestration
│   ├── Schedule Management
│   ├── Persistence
│   ├── OpenTelemetry
│   ├── Configuration
│   └── Operator Commands
│
├── Native Core Layer C/C++
│   ├── Fast Control Kernel
│   ├── Ramp Limiter
│   ├── Constraint Limiter
│   ├── PID / State-Space Controller
│   ├── Fast Telemetry Validation
│   ├── Solver Integration
│   └── optional Protocol Runtime
│
└── Integration Boundary
    ├── P/Invoke
    ├── NativeAOT / C ABI
    ├── gRPC sidecar
    ├── shared memory optional
    └── message queue optional
```

## Wo C/C++ sinnvoll ist

| Bereich                                     | Empfehlung                                                                    |
| ------------------------------------------- | ----------------------------------------------------------------------------- |
| RampLimiter / ConstraintLimiter             | C# reicht meistens, C/C++ nur bei sehr vielen Assets oder sehr hoher Frequenz |
| PID-Regler                                  | C# reicht bei 1s-Zyklus; C/C++ bei 10–100ms-Zyklus                            |
| State-Space / Kalman / MPC-Kernel           | C/C++ sinnvoll                                                                |
| MILP/LP Solver                              | Solver meist ohnehin native; .NET orchestriert                                |
| Modbus TCP / OPC-UA / MQTT                  | C# reicht meistens                                                            |
| Hochfrequente Telemetrie-Filterung          | C/C++ sinnvoll                                                                |
| Regelleistungs-Aktivierung mit enger Latenz | C/C++ oder Edge-Controller sinnvoll                                           |
| Persistenz, API, Scheduling                 | klar C#/.NET                                                                  |
| Marktlogik Day-Ahead/Intraday               | klar C#/.NET                                                                  |

Die saubere Grenze ist:

```text
C# entscheidet, was fachlich passieren soll.
C/C++ berechnet schnell und deterministisch, wie der nächste Sollwert entsteht.
```

## Wichtige Designentscheidung: Library oder Sidecar?

### Variante A: Native Library via P/Invoke

Gut für sehr niedrige Latenz und einfache Deployment-Einheit.

```text
BatteryEms.Worker
    ↓ P/Invoke
libbattery_control_core.so
```

Vorteile:

```text
- sehr schnell
- wenig Overhead
- einfach im Regelzyklus nutzbar
- gute Option für Rampen, Constraints, PID, Filter
```

Nachteile:

```text
- Prozessabsturz möglich bei nativen Fehlern
- ABI muss stabil gehalten werden
- Debugging schwieriger
- Memory Safety ist deine Verantwortung
```

Empfehlung: Für kleine, klar abgegrenzte Funktionen sehr gut.

---

### Variante B: C/C++ Sidecar über gRPC

Gut für größere native Komponenten, Solver oder komplexe MPC-Kerne.

```text
battery-ems-worker (.NET)
    ↓ gRPC
battery-control-core (C++)
```

Vorteile:

```text
- Prozessisolation
- nativer Crash reißt .NET-Service nicht direkt mit
- unabhängig skalierbar
- sauberere Verantwortungsgrenze
```

Nachteile:

```text
- mehr Deployment-Aufwand
- Netzwerk-/IPC-Overhead
- Protobuf-Verträge nötig
```

Empfehlung: Für MPC, Solver, größere numerische Module und riskantere native Integration.

---

## Empfohlene Aufteilung für dein System

```text
.NET:
- Marktintegration Day-Ahead
- Intraday-Reoptimierung
- Regelleistungsprodukt-Management
- Fahrplanverwaltung
- REST/gRPC API
- Datenbankzugriff
- OpenTelemetry
- Konfiguration
- Adapter-Orchestrierung
- Operator-Kommandos
- Audit Logging

C/C++:
- Control Kernel
- harte Begrenzungslogik
- Rampenlogik bei hoher Frequenz
- PID
- State-Space-Modelle
- Kalman-Filter
- MPC-Rechenkern
- schnelle Plausibilitätschecks
- Solver-Anbindung, falls native
```

## Native C ABI als stabile Grenze

Wenn du P/Invoke nutzt, exportiere keine C++-Klassen. Exportiere eine stabile C-API.

```c
// battery_control_core.h

#pragma once

#ifdef __cplusplus
extern "C" {
#endif

typedef struct BatterySnapshotNative {
    double timestamp_unix_ms;
    double soc_percent;
    double soh_percent;
    double active_power_kw;
    double grid_power_kw;
    double pv_power_kw;
    double load_power_kw;
    double temperature_celsius;
    int bms_available;
    int inverter_available;
    int emergency_stop_active;
} BatterySnapshotNative;

typedef struct BatteryLimitsNative {
    double capacity_kwh;
    double min_soc_percent;
    double max_soc_percent;
    double max_charge_power_kw;
    double max_discharge_power_kw;
    double max_ramp_kw_per_second;
} BatteryLimitsNative;

typedef struct BatteryCommandNative {
    double active_power_kw;
    int mode;
    int status;
} BatteryCommandNative;

BatteryCommandNative compute_battery_command(
    BatterySnapshotNative snapshot,
    BatteryLimitsNative limits,
    double target_power_kw,
    double previous_power_kw,
    double dt_seconds
);

#ifdef __cplusplus
}
#endif
```

C++-Implementierung:

```cpp
// battery_control_core.cpp

#include "battery_control_core.h"
#include <algorithm>
#include <cmath>

static double apply_constraints(
    double target_power_kw,
    const BatterySnapshotNative& snapshot,
    const BatteryLimitsNative& limits)
{
    if (!snapshot.bms_available ||
        !snapshot.inverter_available ||
        snapshot.emergency_stop_active)
    {
        return 0.0;
    }

    double limited = target_power_kw;

    // positiv = Entladen, negativ = Laden
    if (snapshot.soc_percent <= limits.min_soc_percent) {
        limited = std::min(limited, 0.0);
    }

    if (snapshot.soc_percent >= limits.max_soc_percent) {
        limited = std::max(limited, 0.0);
    }

    limited = std::min(limited, limits.max_discharge_power_kw);
    limited = std::max(limited, -limits.max_charge_power_kw);

    return limited;
}

static double apply_ramp(
    double previous_power_kw,
    double target_power_kw,
    double max_ramp_kw_per_second,
    double dt_seconds)
{
    if (dt_seconds <= 0.0) {
        return previous_power_kw;
    }

    const double max_delta = max_ramp_kw_per_second * dt_seconds;
    const double delta = target_power_kw - previous_power_kw;

    if (delta > max_delta) {
        return previous_power_kw + max_delta;
    }

    if (delta < -max_delta) {
        return previous_power_kw - max_delta;
    }

    return target_power_kw;
}

extern "C" BatteryCommandNative compute_battery_command(
    BatterySnapshotNative snapshot,
    BatteryLimitsNative limits,
    double target_power_kw,
    double previous_power_kw,
    double dt_seconds)
{
    BatteryCommandNative command{};

    if (!std::isfinite(target_power_kw) ||
        !std::isfinite(previous_power_kw) ||
        !std::isfinite(dt_seconds))
    {
        command.active_power_kw = 0.0;
        command.mode = 0;
        command.status = -1;
        return command;
    }

    const double limited = apply_constraints(
        target_power_kw,
        snapshot,
        limits);

    const double ramped = apply_ramp(
        previous_power_kw,
        limited,
        limits.max_ramp_kw_per_second,
        dt_seconds);

    command.active_power_kw = ramped;
    command.mode = std::abs(ramped) < 0.001 ? 1 : 2;
    command.status = 0;

    return command;
}
```

CMake:

```cmake
cmake_minimum_required(VERSION 3.22)

project(battery_control_core LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(battery_control_core SHARED
    battery_control_core.cpp
)

target_include_directories(battery_control_core
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}
)

target_compile_options(battery_control_core PRIVATE
    -Wall
    -Wextra
    -Wpedantic
    -Werror
)
```

## C# P/Invoke Binding

```csharp
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public readonly struct BatterySnapshotNative
{
    public readonly double TimestampUnixMs;
    public readonly double SocPercent;
    public readonly double SohPercent;
    public readonly double ActivePowerKw;
    public readonly double GridPowerKw;
    public readonly double PvPowerKw;
    public readonly double LoadPowerKw;
    public readonly double TemperatureCelsius;
    public readonly int BmsAvailable;
    public readonly int InverterAvailable;
    public readonly int EmergencyStopActive;

    public BatterySnapshotNative(SystemSnapshot snapshot)
    {
        TimestampUnixMs = snapshot.Timestamp.ToUnixTimeMilliseconds();
        SocPercent = (double)snapshot.SocPercent;
        SohPercent = (double)snapshot.SohPercent;
        ActivePowerKw = (double)snapshot.BatteryPowerKw;
        GridPowerKw = (double)snapshot.GridPowerKw;
        PvPowerKw = (double)snapshot.PvPowerKw;
        LoadPowerKw = (double)snapshot.LoadPowerKw;
        TemperatureCelsius = (double)snapshot.BatteryTemperatureCelsius;
        BmsAvailable = snapshot.BmsAvailable ? 1 : 0;
        InverterAvailable = snapshot.InverterAvailable ? 1 : 0;
        EmergencyStopActive = snapshot.EmergencyStopActive ? 1 : 0;
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct BatteryLimitsNative
{
    public readonly double CapacityKwh;
    public readonly double MinSocPercent;
    public readonly double MaxSocPercent;
    public readonly double MaxChargePowerKw;
    public readonly double MaxDischargePowerKw;
    public readonly double MaxRampKwPerSecond;

    public BatteryLimitsNative(BatteryAsset battery)
    {
        CapacityKwh = (double)battery.CapacityKwh;
        MinSocPercent = (double)battery.MinSocPercent;
        MaxSocPercent = (double)battery.MaxSocPercent;
        MaxChargePowerKw = (double)battery.MaxChargePowerKw;
        MaxDischargePowerKw = (double)battery.MaxDischargePowerKw;
        MaxRampKwPerSecond = (double)battery.MaxRampKwPerSecond;
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct BatteryCommandNative
{
    public readonly double ActivePowerKw;
    public readonly int Mode;
    public readonly int Status;
}

public static partial class BatteryControlNative
{
    private const string LibraryName = "battery_control_core";

    [LibraryImport(LibraryName, EntryPoint = "compute_battery_command")]
    public static partial BatteryCommandNative ComputeBatteryCommand(
        BatterySnapshotNative snapshot,
        BatteryLimitsNative limits,
        double targetPowerKw,
        double previousPowerKw,
        double dtSeconds);
}
```

Wrapper in .NET:

```csharp
public sealed class NativeBatteryControlKernel
{
    private decimal _previousPowerKw;
    private DateTimeOffset? _previousTimestamp;

    public BatteryCommand Compute(
        SystemSnapshot snapshot,
        decimal targetPowerKw)
    {
        var dtSeconds = _previousTimestamp is null
            ? 1.0
            : (snapshot.Timestamp - _previousTimestamp.Value).TotalSeconds;

        var nativeCommand = BatteryControlNative.ComputeBatteryCommand(
            new BatterySnapshotNative(snapshot),
            new BatteryLimitsNative(snapshot.Battery),
            (double)targetPowerKw,
            (double)_previousPowerKw,
            dtSeconds);

        if (nativeCommand.Status != 0)
        {
            _previousPowerKw = 0m;
            _previousTimestamp = snapshot.Timestamp;
            return BatteryCommand.Stop("Native control kernel returned error");
        }

        _previousPowerKw = (decimal)nativeCommand.ActivePowerKw;
        _previousTimestamp = snapshot.Timestamp;

        return BatteryCommand.ActivePower(
            _previousPowerKw,
            "Native control kernel");
    }
}
```

## Docker Multi-Stage Build für .NET + C++

```dockerfile
FROM ubuntu:24.04 AS native-build

RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    ninja-build \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /native

COPY native/battery_control_core/ .

RUN cmake -S . -B build -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    && cmake --build build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dotnet-build

WORKDIR /src

COPY . .

RUN dotnet restore
RUN dotnet publish src/BatteryEms.Worker/BatteryEms.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

RUN useradd --create-home --shell /bin/bash appuser

COPY --from=dotnet-build /app/publish .
COPY --from=native-build /native/build/libbattery_control_core.so /usr/local/lib/libbattery_control_core.so

RUN ldconfig

USER appuser

ENV DOTNET_EnableDiagnostics=0
ENV ASPNETCORE_URLS=http://+:8080
ENV LD_LIBRARY_PATH=/usr/local/lib

ENTRYPOINT ["dotnet", "BatteryEms.Worker.dll"]
```

## Repository-Struktur mit Native Core

```text
battery-ems/
├── src/
│   ├── BatteryEms.Api/
│   ├── BatteryEms.Worker/
│   ├── BatteryEms.Domain/
│   ├── BatteryEms.Control/
│   ├── BatteryEms.Markets/
│   ├── BatteryEms.Optimization/
│   ├── BatteryEms.Realtime/
│   ├── BatteryEms.Protocols.Modbus/
│   ├── BatteryEms.Protocols.OpcUa/
│   └── BatteryEms.Protocols.Mqtt/
│
├── native/
│   └── battery_control_core/
│       ├── CMakeLists.txt
│       ├── battery_control_core.h
│       ├── battery_control_core.cpp
│       └── tests/
│
├── tests/
│   ├── BatteryEms.Control.Tests/
│   ├── BatteryEms.NativeInterop.Tests/
│   └── BatteryEms.Optimization.Tests/
│
├── docker/
│   ├── Dockerfile
│   └── docker-compose.yml
│
├── config/
│   ├── assets/
│   └── control/
│
└── README.md
```

## Teststrategie

Bei C/C++ im Regelpfad brauchst du mehr Tests, nicht weniger.

```text
C++ Unit Tests:
- Constraint Limiter
- Ramp Limiter
- NaN/Inf Handling
- SOC-Grenzen
- Emergency Stop
- negative dt
- Grenzfälle bei Vorzeichenkonvention

.NET Interop Tests:
- Struct Layout
- P/Invoke Ladefähigkeit
- Wertegleichheit C# vs C++
- Fehlerstatus
- Container-Test

Integration Tests:
- Replay historischer Messdaten
- Sollwertvergleich
- Kommunikationsausfall
- stale telemetry
- Regelleistungsaktivierung
```

Wichtig: Baue einen C#-Referenzregler und vergleiche ihn gegen den nativen Kern. So findest du ABI- und Rundungsfehler früh.

## Wichtige technische Regeln

```text
1. C ABI statt C++ ABI exportieren.
2. Keine Speicherallokation über Sprachgrenzen hinweg.
3. Keine Exceptions über die C-Grenze werfen.
4. Keine Pointer auf verwalteten Speicher dauerhaft speichern.
5. Structs explizit layouten.
6. Version des Native Kernels abfragbar machen.
7. Native Fehler über Statuscodes zurückgeben.
8. NaN/Inf immer abfangen.
9. Native Library im Container explizit testen.
10. Native Kernlogik deterministisch und zustandsarm halten.
```

## Versionierung der Native API

Füge eine Versionsfunktion hinzu:

```c
const char* battery_control_core_version(void);
int battery_control_core_abi_version(void);
```

Und in C# beim Start prüfen:

```csharp
public sealed class NativeKernelStartupCheck : IHostedService
{
    private readonly ILogger<NativeKernelStartupCheck> _logger;

    public NativeKernelStartupCheck(ILogger<NativeKernelStartupCheck> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var abiVersion = BatteryControlNative.GetAbiVersion();

        if (abiVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported native battery control ABI version: {abiVersion}");
        }

        _logger.LogInformation(
            "Native battery control kernel loaded. ABI={AbiVersion}",
            abiVersion);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

## Für Performance-kritische Optimierung

Für MILP/LP ist C/C++ oft nicht der Teil, den du selbst schreiben solltest. Nutze einen Solver:

```text
- OR-Tools
- HiGHS
- CBC
- SCIP
- Gurobi
- CPLEX
```

.NET kann das Modell vorbereiten, C/C++/native Solver löst es. Für häufige Reoptimierung ist ein Sidecar oft sauberer:

```text
BatteryEms.Optimization (.NET)
    erstellt Optimierungsrequest
    ↓ gRPC
optimization-core (C++/native Solver)
    löst LP/MILP/MPC
    ↓
DispatchSchedule
```

## Klare Empfehlung

Für dein System würde ich so starten:

```text
Phase 1:
.NET-only für EMS, Marktlogik, Protokolle, Control Loop

Phase 2:
C++ Native Library für:
- ConstraintLimiter
- RampLimiter
- PID
- einfache State-Space-Berechnung

Phase 3:
C++ Sidecar für:
- MPC
- Solver-nahe Optimierung
- hochfrequente Telemetrieverarbeitung

Phase 4:
nur falls nötig:
- Shared Memory
- CPU Pinning
- RT-Linux / Edge Controller
```

Die entscheidende Linie ist:

```text
C/C++ nur dort einsetzen, wo Messung zeigt, dass .NET nicht reicht.
```

Aber: Wenn Regelleistung mit engen Reaktionszeiten wirklich Teil des Produkts ist, solltest du die native/edge-nahe Komponente von Anfang an als Option einplanen. Nicht als Monolith, sondern als klar abgegrenzten **Control Core**.
