using BatteryEms.Application.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;

namespace BatteryEms.Adapters.Telemetry.Prometheus;

// DI + routing extensions for the Prometheus-backed telemetry adapter.
// Bundling the wiring keeps Program.BuildApp's class coupling under the
// CA1506 threshold and gives tests a single registration entry point.
public static class TelemetryRegistration
{
    public static IServiceCollection AddBessTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IControlCycleMetrics, PrometheusControlCycleMetrics>();
        return services;
    }

    public static IEndpointRouteBuilder MapBessMetrics(this IEndpointRouteBuilder routes, string path = "/metrics")
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // prometheus-net's MapMetrics() returns IEndpointConventionBuilder
        // — ignore so the existing fluent chains in Program stay tidy.
        _ = routes.MapMetrics(path);
        return routes;
    }
}
