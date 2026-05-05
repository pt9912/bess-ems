# syntax=docker/dockerfile:1.7
#
# Integration-test runner. Builds the .NET integration tests once and
# starts them via `dotnet test` against the Modbus endpoint provided by
# the bess-field-sim sidecar (see tests/integration/compose.yml).

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

RUN dotnet restore tests/integration/BatteryEms.Modbus.IntegrationTests/BatteryEms.Modbus.IntegrationTests.csproj
RUN dotnet build tests/integration/BatteryEms.Modbus.IntegrationTests/BatteryEms.Modbus.IntegrationTests.csproj \
    --configuration Release --no-restore

ENTRYPOINT ["dotnet", "test", \
    "tests/integration/BatteryEms.Modbus.IntegrationTests/BatteryEms.Modbus.IntegrationTests.csproj", \
    "--configuration", "Release", \
    "--no-build", \
    "--no-restore", \
    "--logger", "console;verbosity=normal"]
