using BatteryEms.Application.Observability;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BatteryEms.Adapters.Telemetry.OpenTelemetry;

// RM-M2-06 / LH-MON-003: production-side OTel TracerProvider wiring.
// Subscribes the SDK to the BatteryEms ActivitySources defined in
// Application.Observability and exports via OTLP when the
// OTEL_EXPORTER_OTLP_ENDPOINT environment variable is set. With no
// endpoint configured the spans still flow through the SDK pipeline
// but never leave the process — safe default for headless / dev hosts
// that don't run a collector. Test hosts that don't call
// AddBessTracing get the Activity events directly via ActivityListener
// without any SDK dependency on the BCL ActivitySource side.
public static class TracingRegistration
{
    private const string ServiceName = "bess-ems";

    public static IServiceCollection AddBessTracing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenTelemetry()
            .ConfigureResource(builder => builder.AddService(ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(BessActivitySources.ControlCycleName)
                    .AddSource(BessActivitySources.CommandDispatchName)
                    .AddSource(BessActivitySources.ScheduleOptimizationName)
                    .AddAspNetCoreInstrumentation();

                // Endpoint is opt-in via env var. AddOtlpExporter without
                // a configured endpoint defaults to localhost:4317; only
                // wire the exporter when the operator has explicitly
                // pointed at a collector so accidental dev runs don't
                // hammer a non-existent endpoint with retries.
                var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    tracing.AddOtlpExporter();
                }
            });

        return services;
    }
}
