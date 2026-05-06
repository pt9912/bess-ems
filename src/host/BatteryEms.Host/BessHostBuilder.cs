using BatteryEms.Adapters.Modbus;
using BatteryEms.Adapters.Mqtt;
using BatteryEms.Adapters.Optimization;
using BatteryEms.Adapters.Persistence;
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

        // Composition-root only: persistence adapter swap.
        if (!string.IsNullOrWhiteSpace(hostOptions.PersistenceConnectionString))
        {
            builder.Services.AddBessPersistence(hostOptions.PersistenceConnectionString!);
        }

        // Driven adapters: optimisation + telemetry are always wired.
        builder.Services.AddBessOptimization();
        builder.Services.AddBessTelemetry();

        // The Modbus / MQTT command sinks need the BatteryAsset; expose
        // the loaded asset to DI so the adapter constructors resolve it.
        builder.Services.AddSingleton(runtimeConfig.Asset);

        // Modbus / MQTT wiring is opt-in: when the host configuration
        // provides the mapping path + endpoint, the real adapter is
        // registered as IBatteryTelemetrySource / IBatteryCommandSink.
        // Otherwise the NoOp pair keeps the regulation loop safe.
        if (runtimeConfig.ModbusMapping is not null
            && !string.IsNullOrWhiteSpace(hostOptions.ModbusHost)
            && hostOptions.ModbusPort > 0)
        {
            builder.Services.AddBessModbus(
                runtimeConfig.ModbusMapping,
                ModbusAdapterOptions.Defaults(hostOptions.ModbusHost!, hostOptions.ModbusPort, runtimeConfig.Asset.AssetId));
        }
        else if (runtimeConfig.MqttMapping is not null
            && !string.IsNullOrWhiteSpace(hostOptions.MqttBrokerHost)
            && hostOptions.MqttBrokerPort > 0
            && !string.IsNullOrWhiteSpace(hostOptions.MqttClientId))
        {
            builder.Services.AddBessMqtt(
                runtimeConfig.MqttMapping,
                MqttAdapterOptions.Defaults(hostOptions.MqttBrokerHost!, hostOptions.MqttBrokerPort, hostOptions.MqttClientId!, runtimeConfig.Asset.AssetId));
        }
        else
        {
            builder.Services.AddSingleton<IBatteryTelemetrySource, NoOpBatteryTelemetrySource>();
            builder.Services.AddSingleton<IBatteryCommandSink, NoOpBatteryCommandSink>();
        }

        // Worker hosted-service.
        builder.Services.AddBessWorker(builder.Configuration);

        // API auth (RM-M1-16).
        builder.Services.AddApiTokenAuth(builder.Configuration);

        // Make the runtime configuration available so the seed step can
        // run after the container is built (the registry is a singleton).
        builder.Services.AddSingleton(runtimeConfig);

        var app = builder.Build();

        // Eager DDL initialisation when persistence is wired — fails the
        // start-up if Postgres is unreachable, matching LH-OPS-001.
        if (!string.IsNullOrWhiteSpace(hostOptions.PersistenceConnectionString))
        {
            var initializer = app.Services.GetRequiredService<BessDbInitializer>();
            initializer.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
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
}
