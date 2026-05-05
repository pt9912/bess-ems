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
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY BatteryEms.sln ./
COPY src/ src/
COPY tests/ tests/
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
RUN dotnet test tests/hexagon/BatteryEms.Domain.Tests/BatteryEms.Domain.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-restore \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=cobertura \
    /p:CoverletOutput=/src/coverage/Domain/ \
    /p:Include="[BatteryEms.Domain]*" \
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
    /p:Threshold=90 \
    /p:ThresholdType=line \
    /p:ThresholdStat=total

# ---------------------------------------------------------------------------
# Future stages (activated in later waves):
#   FROM lint AS test-integration -> Welle 3: modbus/mqtt/postgres integration
#   FROM ${DOTNET_RUNTIME_IMAGE} AS runtime -> Welle 5: runtime image
# ---------------------------------------------------------------------------
