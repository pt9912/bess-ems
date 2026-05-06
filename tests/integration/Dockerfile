# syntax=docker/dockerfile:1.7
#
# Integration-test runner. Builds the three .NET integration test
# projects and runs them sequentially against the sidecars wired up in
# tests/integration/compose.yml: bess-field-sim (Modbus + MQTT publisher
# + command-ACK) and Postgres (persistence round-trips).

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

RUN dotnet restore tests/integration/BatteryEms.Modbus.IntegrationTests/BatteryEms.Modbus.IntegrationTests.csproj \
 && dotnet restore tests/integration/BatteryEms.Mqtt.IntegrationTests/BatteryEms.Mqtt.IntegrationTests.csproj \
 && dotnet restore tests/integration/BatteryEms.Persistence.IntegrationTests/BatteryEms.Persistence.IntegrationTests.csproj
RUN dotnet build tests/integration/BatteryEms.Modbus.IntegrationTests/BatteryEms.Modbus.IntegrationTests.csproj \
        --configuration Release --no-restore \
 && dotnet build tests/integration/BatteryEms.Mqtt.IntegrationTests/BatteryEms.Mqtt.IntegrationTests.csproj \
        --configuration Release --no-restore \
 && dotnet build tests/integration/BatteryEms.Persistence.IntegrationTests/BatteryEms.Persistence.IntegrationTests.csproj \
        --configuration Release --no-restore

# Single shell-form ENTRYPOINT runs all three projects sequentially with
# `set -e` so the first failure surfaces immediately.
ENTRYPOINT ["sh", "-c", "set -e && \
    dotnet test tests/integration/BatteryEms.Modbus.IntegrationTests/BatteryEms.Modbus.IntegrationTests.csproj \
        --configuration Release --no-build --no-restore --logger 'console;verbosity=normal' && \
    dotnet test tests/integration/BatteryEms.Mqtt.IntegrationTests/BatteryEms.Mqtt.IntegrationTests.csproj \
        --configuration Release --no-build --no-restore --logger 'console;verbosity=normal' && \
    dotnet test tests/integration/BatteryEms.Persistence.IntegrationTests/BatteryEms.Persistence.IntegrationTests.csproj \
        --configuration Release --no-build --no-restore --logger 'console;verbosity=normal'"]
