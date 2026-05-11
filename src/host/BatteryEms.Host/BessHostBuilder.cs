using BatteryEms.Adapters.Modbus;
using BatteryEms.Adapters.Mqtt;
using BatteryEms.Adapters.NativeInterop;
using BatteryEms.Adapters.OpcUa;
using BatteryEms.Adapters.Optimization;
using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Adapters.Persistence;
using BatteryEms.Adapters.Telemetry.OpenTelemetry;
using BatteryEms.Adapters.Telemetry.Prometheus;
using BatteryEms.Api.Auth;
using BatteryEms.Api.Composition;
using BatteryEms.Api.Endpoints;
using BatteryEms.Api.Observability;
using BatteryEms.Application.Assets;
using BatteryEms.Application.IO;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using BatteryEms.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;

namespace BatteryEms.Host;

// Composition root for the bess-ems process. Wires together the API
// driving adapter, the Worker driving adapter, the Telemetry/Prometheus
// driven adapter, the Persistence driven adapter (when configured) and
// the Optimization driven adapter. NoOp telemetry source / command sink
// are the default for headless smokes — RM-M1-19c will swap in the
// Modbus/MQTT adapters once the mapping loaders are wired.
public static class BessHostBuilder
{
    // CA1506 fires on the composition root because it touches every
    // adapter and the Application layer by design — that's its job.
    // Splitting the wiring across helper classes only hides the coupling
    // (it still exists in the dependency graph). Suppress with intent.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "CA1506",
        Justification = "Composition root: wires every adapter together; high class coupling is intrinsic.")]
    public static WebApplication BuildApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var hostOptions = builder.Configuration
            .GetSection(BessHostOptions.SectionName)
            .Get<BessHostOptions>() ?? new BessHostOptions();
        var runtimeConfig = BessConfigurationBootstrap.Load(hostOptions);

        // LH-MON-001: structured stdout — same JsonConsole the API host uses.
        builder.Host.ConfigureBessJsonLogging();

        // System.Text.Json snake-case + enum converter for the API surface.
        builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
            o.SerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower));
        });

        builder.Services.AddOpenApi();

        // Application-side wiring (clock, registries, snapshot store,
        // use cases). The persistence-backed alternatives below replace
        // the in-memory repositories when a connection string is set.
        builder.Services.AddBessApplicationInMemoryStores();

        // M3-D2: explicit IControlKernel registration. Default profile
        // (NativeControl:Enabled=false in appsettings.json) wires the
        // ManagedControlKernel — bit-identisch zum Pre-M3-D2-Verhalten,
        // wo `ControlCycleUseCase` auf `new ManagedControlKernel()` im
        // Konstruktor zurückfiel. Das produktionsnahe Native-Profil
        // (`NativeControl:Enabled=true`, `LibraryPath=/app/native/...`)
        // schaltet auf `NativeFallbackControlKernel` um, mit
        // deterministischem Managed-Fallback bei Native-Fehlern und
        // opt-in `AbortOnAbiMismatch` als Production-Policy
        // (siehe `docs/user/quality.md` §5.2 + ADR 0003/0004).
        builder.Services.AddBessNativeControl(builder.Configuration);

        // Composition-root only: persistence adapter swap.
        if (!string.IsNullOrWhiteSpace(hostOptions.PersistenceConnectionString))
        {
            builder.Services.AddBessPersistence(hostOptions.PersistenceConnectionString!);
        }

        // Driven adapters: optimisation + telemetry are always wired.
        builder.Services.AddBessOptimization();
        ConfigureScheduleSolver(builder.Services, hostOptions.ScheduleSolver);
        builder.Services.AddBessTelemetry();
        // RM-M2-06: OTel tracing for the three Application-grenze flows.
        // Exporter is opt-in via OTEL_EXPORTER_OTLP_ENDPOINT; without it
        // spans flow through the SDK pipeline but never leave the host.
        builder.Services.AddBessTracing();

        // The Modbus / MQTT command sinks need the BatteryAsset; expose
        // the loaded asset to DI so the adapter constructors resolve it.
        builder.Services.AddSingleton(runtimeConfig.Asset);

        // I/O-Adapter-Triage. Detection-Logik in
        // `IoAdapterTriage.SelectConfiguredFamily` (Application/IO),
        // damit sie ohne WebApplicationFactory-Boot unit-getestet
        // werden kann (review-fix #2 zu Sub-Slice C). Mehr als eine
        // konfigurierte Familie wirft `multiple-io-adapters-configured`
        // — der Operator muss sich für genau eine entscheiden.
        var modbusConfigured = runtimeConfig.ModbusMapping is not null
            && !string.IsNullOrWhiteSpace(hostOptions.ModbusHost)
            && hostOptions.ModbusPort > 0;
        var mqttConfigured = runtimeConfig.MqttMapping is not null
            && !string.IsNullOrWhiteSpace(hostOptions.MqttBrokerHost)
            && hostOptions.MqttBrokerPort > 0
            && !string.IsNullOrWhiteSpace(hostOptions.MqttClientId);
        var opcUaConfigured = runtimeConfig.OpcUaMapping is not null
            && hostOptions.OpcUaEndpointUrl is not null;
        var family = IoAdapterTriage.SelectConfiguredFamily(
            modbusConfigured, mqttConfigured, opcUaConfigured);
        switch (family)
        {
            case IoAdapterTriage.Family.Modbus:
                builder.Services.AddBessModbus(
                    runtimeConfig.ModbusMapping!,
                    ModbusAdapterOptions.Defaults(hostOptions.ModbusHost!, hostOptions.ModbusPort, runtimeConfig.Asset.AssetId));
                break;
            case IoAdapterTriage.Family.Mqtt:
                builder.Services.AddBessMqtt(
                    runtimeConfig.MqttMapping!,
                    MqttAdapterOptions.Defaults(hostOptions.MqttBrokerHost!, hostOptions.MqttBrokerPort, hostOptions.MqttClientId!, runtimeConfig.Asset.AssetId));
                break;
            case IoAdapterTriage.Family.OpcUa:
                builder.Services.AddBessOpcUa(
                    runtimeConfig.OpcUaMapping!,
                    BuildOpcUaAdapterOptions(hostOptions));
                break;
            case IoAdapterTriage.Family.None:
            default:
                builder.Services.AddSingleton<IBatteryTelemetrySource, NoOpBatteryTelemetrySource>();
                builder.Services.AddSingleton<IBatteryCommandSink, NoOpBatteryCommandSink>();
                break;
        }

        // Worker hosted-service.
        builder.Services.AddBessWorker(builder.Configuration);

        // API auth (RM-M1-16).
        builder.Services.AddApiTokenAuth(builder.Configuration);

        // Make the runtime configuration available so the seed step can
        // run after the container is built (the registry is a singleton).
        builder.Services.AddSingleton(runtimeConfig);

        var app = builder.Build();

        // Eager schema migration when persistence is wired — fails the
        // start-up if Postgres is unreachable, matching LH-OPS-001.
        // ADR 0001 + RM-M2-MIG-04: pg_advisory_lock inside the migrator
        // serialises this call across replicas; DbUp's __schema_versions
        // journal tracks which scripts have already been applied.
        if (!string.IsNullOrWhiteSpace(hostOptions.PersistenceConnectionString))
        {
            var migrator = app.Services.GetRequiredService<BessDbMigrator>();
            migrator.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        // Seed asset + schedule before the worker starts ticking.
        var assets = app.Services.GetRequiredService<IBatteryAssetRegistry>();
        BessConfigurationBootstrap.SeedAssetRegistry(assets, runtimeConfig.Asset);
        if (runtimeConfig.Schedule is not null)
        {
            var schedules = app.Services.GetRequiredService<IScheduleRepository>();
            BessConfigurationBootstrap.SeedScheduleRepository(schedules, runtimeConfig.Schedule);
        }

        // Eager validation of the API-token table — otherwise a malformed
        // token list would only surface on the first 401.
        _ = app.Services.GetRequiredService<ApiTokenRegistry>();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOpenApi();
        app.MapBatteryEms();
        app.MapMetrics();
        return app;
    }

    private static void ConfigureScheduleSolver(
        IServiceCollection services,
        BessScheduleSolverOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var backend = string.IsNullOrWhiteSpace(options.Backend)
            ? "noop"
            : options.Backend.Trim();
        if (string.Equals(backend, "noop", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!string.Equals(backend, "or_tools", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(backend, "ortools", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported Bess:ScheduleSolver:Backend '{options.Backend}'. Supported values: noop, or_tools.");
        }

        services.AddBessScheduleSolver(solver =>
        {
            if (options.TimeLimitSeconds is { } seconds)
            {
                solver.TimeLimit = TimeSpan.FromSeconds(seconds);
            }
            solver.GapTolerance = options.GapTolerance;
            solver.InitialSocPercent = options.InitialSocPercent;
        });
    }

    // M4-05: BessHostOptions kennt jetzt RuntimeProfile/SecurityMode/
    // SecurityPolicy + Cert-Subject/TrustedStore-Slots. Helper baut die
    // Adapter-Options, indem die Operator-Overrides — falls gesetzt —
    // die Adapter-Defaults überschreiben. Null lässt den Default-Wert
    // aus `OpcUaAdapterOptions` greifen (Production + SignAndEncrypt +
    // Basic256Sha256). Die finale `EnsureValid`-Validierung läuft auf
    // der Source/Sink-Konstruktor-Linie (D-04 + M4-05 D-02).
    private static OpcUaAdapterOptions BuildOpcUaAdapterOptions(BessHostOptions hostOptions)
    {
        var defaults = new OpcUaAdapterOptions
        {
            EndpointUrl = hostOptions.OpcUaEndpointUrl!,
        };
        return defaults with
        {
            SessionName = string.IsNullOrWhiteSpace(hostOptions.OpcUaSessionName)
                ? defaults.SessionName
                : hostOptions.OpcUaSessionName!,
            RuntimeProfile = hostOptions.OpcUaRuntimeProfile ?? defaults.RuntimeProfile,
            SecurityMode = hostOptions.OpcUaSecurityMode ?? defaults.SecurityMode,
            SecurityPolicy = string.IsNullOrWhiteSpace(hostOptions.OpcUaSecurityPolicy)
                ? defaults.SecurityPolicy
                : hostOptions.OpcUaSecurityPolicy!,
            ApplicationCertificateSubject = string.IsNullOrWhiteSpace(
                hostOptions.OpcUaApplicationCertificateSubject)
                    ? defaults.ApplicationCertificateSubject
                    : hostOptions.OpcUaApplicationCertificateSubject!,
            TrustedServerCertificatesPath = string.IsNullOrWhiteSpace(
                hostOptions.OpcUaTrustedServerCertificatesPath)
                    ? defaults.TrustedServerCertificatesPath
                    : hostOptions.OpcUaTrustedServerCertificatesPath!,
            AllowUnsecured = hostOptions.OpcUaAllowUnsecured,
            AllowUnsecuredReason = hostOptions.OpcUaAllowUnsecuredReason,
        };
    }
}
