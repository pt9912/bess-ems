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
COPY tests/ tests/
COPY config/ config/

RUN dotnet restore tests/integration/BatteryEms.Hil.IntegrationTests/BatteryEms.Hil.IntegrationTests.csproj
RUN dotnet build tests/integration/BatteryEms.Hil.IntegrationTests/BatteryEms.Hil.IntegrationTests.csproj \
        --configuration Release --no-restore

ENTRYPOINT ["sh", "-c", "set -e && \
    dotnet test tests/integration/BatteryEms.Hil.IntegrationTests/BatteryEms.Hil.IntegrationTests.csproj \
        --configuration Release --no-build --no-restore --logger 'console;verbosity=normal'"]
