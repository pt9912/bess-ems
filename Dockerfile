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
# test: domain unit tests (RM-M1-02/04/05/06)
# ---------------------------------------------------------------------------
FROM lint AS test
ARG BUILD_CONFIGURATION
RUN dotnet test tests/hexagon/BatteryEms.Domain.Tests/BatteryEms.Domain.Tests.csproj \
    --configuration "${BUILD_CONFIGURATION}" \
    --no-build \
    --no-restore \
    --logger "console;verbosity=normal"

# ---------------------------------------------------------------------------
# Future stages (activated in later waves):
#   FROM lint AS test-safety     -> Welle 2 (RM-M1-07): safety-path tests
#   FROM lint AS test-integration -> Welle 3: modbus/mqtt/postgres integration
#   FROM ${DOTNET_RUNTIME_IMAGE} AS runtime -> Welle 5: runtime image
# ---------------------------------------------------------------------------
