# syntax=docker/dockerfile:1.7
#
# RM-M2-HIL-07: HIL-test-runner. Builds and runs the optional HIL
# integration project against the bess-hil-simulator sidecar wired
# up in tests/hil/compose.yml.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true \
    NUGET_XMLDOC_MODE=skip
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY BatteryEms.sln ./
COPY src/ src/
# Carve-out Mn2: copy only what this image actually needs. The
# HIL integration test (csproj + sources) plus the test-tree
# Directory.Build.props (NoWarn settings for test-only analyzer
# rules CA1707/CA2007/CA1062/CA1816/CA1859/CA2000/CA1849/CA1515).
# Avoids the cache-busting `COPY tests/ tests/` that the original
# draft used — every unrelated test edit re-built the layer.
COPY tests/Directory.Build.props tests/Directory.Build.props
COPY tests/integration/BatteryEms.Hil.IntegrationTests/ tests/integration/BatteryEms.Hil.IntegrationTests/
COPY config/ config/

RUN dotnet restore tests/integration/BatteryEms.Hil.IntegrationTests/BatteryEms.Hil.IntegrationTests.csproj
RUN dotnet build tests/integration/BatteryEms.Hil.IntegrationTests/BatteryEms.Hil.IntegrationTests.csproj \
        --configuration Release --no-restore

ENTRYPOINT ["sh", "-c", "set -e && \
    dotnet test tests/integration/BatteryEms.Hil.IntegrationTests/BatteryEms.Hil.IntegrationTests.csproj \
        --configuration Release --no-build --no-restore --logger 'console;verbosity=normal'"]
