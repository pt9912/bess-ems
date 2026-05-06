using System.Text.Json;
using BatteryEms.Api.Endpoints;
using BatteryEms.Application.Api;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

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

        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            options.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        });

        builder.Services.AddOpenApi();

        // Application-side stateful in-memory stores. Single instance per
        // process so the snapshot/command pipelines see consistent state.
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IBatteryAssetRegistry>(_ => new InMemoryBatteryAssetRegistry());
        builder.Services.AddSingleton<ISnapshotStore>(_ => new InMemorySnapshotStore(TimeSpan.FromSeconds(10)));
        builder.Services.AddSingleton<ICommandRepository, InMemoryCommandRepository>();
        builder.Services.AddSingleton<IScheduleRepository>(_ => new InMemoryScheduleRepository());
        builder.Services.AddSingleton<IOperatorStopRegistry, InMemoryOperatorStopRegistry>();

        // Driving-port use cases.
        builder.Services.AddSingleton<IHealthQuery, DefaultHealthQuery>();
        builder.Services.AddSingleton<IBatteryStatusQuery, DefaultBatteryStatusQuery>();
        builder.Services.AddSingleton<IScheduleQuery, DefaultScheduleQuery>();
        builder.Services.AddSingleton<IOperatorStopUseCase, DefaultOperatorStopUseCase>();

        var app = builder.Build();
        app.MapOpenApi();
        app.MapBatteryEms();
        return app;
    }

    public static void Main(string[] args)
    {
        var app = BuildApp(args);
        app.Run();
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by the DI container via reflection.")]
    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
