using System.Text.Json;
using BatteryEms.Api.Auth;
using BatteryEms.Api.Composition;
using BatteryEms.Api.Endpoints;
using BatteryEms.Api.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;

namespace BatteryEms.Api;

// Composition root for the API process. RM-M1-15a wires the read path
// against in-memory repositories from the hexagon: production swaps in
// the Dapper-backed implementations via RM-M1-19's Worker/Infrastructure
// composition, but the API project intentionally stays free of driven-
// adapter and Infrastructure references so the architecture-tabu test
// keeps the boundary clean.
//
// Class is non-static so WebApplicationFactory<Program> in the contract
// test project can pin its TEntryPoint to this assembly's entry point.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1052", Justification = "Program is the WebApplicationFactory<TEntryPoint> marker; static would break the test host.")]
public class Program
{
    protected Program() { }


    public static WebApplication BuildApp(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // LH-MON-001: structured stdout logs. Centralised in
        // LoggingRegistration so the same configuration drives both the
        // API and the Worker host (RM-M1-19).
        builder.Host.ConfigureBessJsonLogging();

        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        });

        builder.Services.AddOpenApi();

        // Application-side stateful in-memory stores + driving-port use
        // cases. Bundled in ApplicationServiceRegistration to keep this
        // method's class coupling under the CA1506 threshold.
        builder.Services.AddBessApplicationInMemoryStores(
            builder.Configuration.GetValue("Bess:SnapshotMaxAge", ApplicationServiceRegistration.DefaultSnapshotMaxAge));

        // RM-M1-16: API-token AuthN + role-based AuthZ for write endpoints.
        builder.Services.AddApiTokenAuth(builder.Configuration);

        var app = builder.Build();
        // Force eager validation of ApiTokens at startup — surfaces a
        // misconfigured token list before the first request hits the
        // pipeline rather than at the first 401 challenge.
        _ = app.Services.GetRequiredService<ApiTokenRegistry>();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseOperatorUiStaticShell();
        app.MapOpenApi();
        app.MapBatteryEms();
        // LH-MON-002: Prometheus scrape endpoint. M1 exposes the default
        // process metrics (CPU, GC, …) from the API host; RM-M1-19 wires
        // PrometheusControlCycleMetrics in the Worker so the regulation-
        // cycle metrics show up alongside.
        app.MapMetrics();
        return app;
    }

    public static void Main(string[] args)
    {
        var app = BuildApp(args);
        app.Run();
    }
}
