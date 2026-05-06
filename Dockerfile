# syntax=docker/dockerfile:1.7

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
ARG BUILD_CONFIGURATION=Release

# ---------------------------------------------------------------------------
# sdk-base: shared SDK environment
# ---------------------------------------------------------------------------
FROM ${DOTNET_SDK_IMAGE} AS sdk-base
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    DOTNET_GENERATE_ASPNETCORE_CERTIFICATE=false \
    NUGET_XMLDOC_MODE=skip
WORKDIR /src

# ---------------------------------------------------------------------------
# restore: dotnet restore against central package management
# ---------------------------------------------------------------------------
FROM sdk-base AS restore
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY BatteryEms.sln ./
COPY src/ src/
COPY tests/ tests/
COPY config/ config/
RUN dotnet restore BatteryEms.sln

# ---------------------------------------------------------------------------
# lint: dotnet build -warnaserror (RM-M1-01, RM-M1-21)
# ---------------------------------------------------------------------------
FROM restore AS lint
ARG BUILD_CONFIGURATION
RUN dotnet build BatteryEms.sln \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    -warnaserror

# ---------------------------------------------------------------------------
# arch-check: boundary tests (RM-M1-22, RM-M1-23)
# ---------------------------------------------------------------------------
FROM lint AS arch-check
ARG BUILD_CONFIGURATION
RUN dotnet test tests/BatteryEms.ArchitectureTests/BatteryEms.ArchitectureTests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal"

# ---------------------------------------------------------------------------
# test: domain + application unit tests (RM-M1-02/03/04/05/06/07/08)
# ---------------------------------------------------------------------------
FROM lint AS test
ARG BUILD_CONFIGURATION
RUN dotnet test tests/hexagon/BatteryEms.Domain.Tests/BatteryEms.Domain.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal" \
 && dotnet test tests/hexagon/BatteryEms.Application.Tests/BatteryEms.Application.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal" \
 && dotnet test tests/infrastructure/BatteryEms.Infrastructure.Tests/BatteryEms.Infrastructure.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal" \
 && dotnet test tests/adapters/driven/BatteryEms.Adapters.Modbus.Tests/BatteryEms.Adapters.Modbus.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal" \
 && dotnet test tests/adapters/driven/BatteryEms.Adapters.Mqtt.Tests/BatteryEms.Adapters.Mqtt.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal" \
 && dotnet test tests/adapters/driven/BatteryEms.Adapters.Telemetry.Tests/BatteryEms.Adapters.Telemetry.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal" \
 && dotnet test tests/adapters/driving/BatteryEms.Worker.Tests/BatteryEms.Worker.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal" \
 && dotnet test tests/adapters/driving/BatteryEms.Api.Tests/BatteryEms.Api.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal"

# ---------------------------------------------------------------------------
# test-safety: safety-path tests filtered by trait Category=Safety (RM-M1-07)
# ---------------------------------------------------------------------------
FROM lint AS test-safety
ARG BUILD_CONFIGURATION
RUN dotnet test tests/hexagon/BatteryEms.Domain.Tests/BatteryEms.Domain.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --filter "Category=Safety" \
    --logger "console;verbosity=normal" \
 && dotnet test tests/hexagon/BatteryEms.Application.Tests/BatteryEms.Application.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --filter "Category=Safety" \
    --logger "console;verbosity=normal"

# ---------------------------------------------------------------------------
# coverage-gate: line coverage threshold for M1 production assemblies (RM-M1-20)
# Threshold 90% per project (Domain; Application + Optimization adapter).
# Adapter skeletons (Modbus/MQTT/Persistence/Telemetry/Worker/Api/Infrastructure)
# stay outside the gate until they carry production code.
# ---------------------------------------------------------------------------
FROM lint AS coverage-gate
ARG BUILD_CONFIGURATION
# ExcludeByFile drops Microsoft.AspNetCore.OpenApi-generated source files
# (and any other source-generator output) from the report so the OpenApi
# transformer types — which the source generator emits into every project
# transitively reaching the Api package via central package pinning —
# don't drag the percentage down. The generated file convention is
# Roslyn-standard `obj/.../*.generated.cs`.
RUN dotnet test tests/hexagon/BatteryEms.Domain.Tests/BatteryEms.Domain.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=/src/coverage/Domain/ \
    /p:Include="[BatteryEms.Domain]*" \
    /p:ExcludeByFile="**/*.generated.cs" \
    /p:Threshold=90 \
    /p:ThresholdType=line \
    /p:ThresholdStat=total \
 && dotnet test tests/hexagon/BatteryEms.Application.Tests/BatteryEms.Application.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=/src/coverage/Application/ \
    /p:Include="[BatteryEms.Application]*%2C[BatteryEms.Adapters.Optimization]*" \
    /p:ExcludeByFile="**/*.generated.cs" \
    /p:Threshold=90 \
    /p:ThresholdType=line \
    /p:ThresholdStat=total \
 && dotnet test tests/infrastructure/BatteryEms.Infrastructure.Tests/BatteryEms.Infrastructure.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=/src/coverage/Infrastructure/ \
    /p:Include="[BatteryEms.Infrastructure]*" \
    /p:ExcludeByFile="**/*.generated.cs" \
    /p:Threshold=90 \
    /p:ThresholdType=line \
    /p:ThresholdStat=total \
 && dotnet test tests/adapters/driven/BatteryEms.Adapters.Modbus.Tests/BatteryEms.Adapters.Modbus.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=/src/coverage/Modbus/ \
    /p:Include="[BatteryEms.Adapters.Modbus]*" \
    /p:Exclude="[BatteryEms.Adapters.Modbus]*.FluentModbusClient" \
    /p:ExcludeByFile="**/*.generated.cs" \
    /p:Threshold=90 \
    /p:ThresholdType=line \
    /p:ThresholdStat=total \
 && dotnet test tests/adapters/driven/BatteryEms.Adapters.Telemetry.Tests/BatteryEms.Adapters.Telemetry.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=/src/coverage/Telemetry/ \
    /p:Include="[BatteryEms.Adapters.Telemetry]*" \
    /p:ExcludeByFile="**/*.generated.cs" \
    /p:Threshold=90 \
    /p:ThresholdType=line \
    /p:ThresholdStat=total \
 && dotnet test tests/adapters/driving/BatteryEms.Worker.Tests/BatteryEms.Worker.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=/src/coverage/Worker/ \
    /p:Include="[BatteryEms.Worker]*" \
    /p:ExcludeByFile="**/*.generated.cs" \
    /p:Threshold=90 \
    /p:ThresholdType=line \
    /p:ThresholdStat=total

# ---------------------------------------------------------------------------
# Future stages (activated in later waves):
#   FROM lint AS test-integration -> Welle 3: modbus/mqtt/postgres integration
#   FROM ${DOTNET_RUNTIME_IMAGE} AS runtime -> Welle 5: runtime image
# ---------------------------------------------------------------------------
