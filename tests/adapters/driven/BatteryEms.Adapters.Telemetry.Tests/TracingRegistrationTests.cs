using BatteryEms.Adapters.Telemetry.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Xunit;

namespace BatteryEms.Adapters.Telemetry.Tests;

// RM-M2-06: smoke test for the production-side OTel TracerProvider
// wiring. Covers both branches of the OTEL_EXPORTER_OTLP_ENDPOINT
// switch — without the env var the SDK pipeline is configured but no
// exporter is wired (safe default for headless runs); with it set the
// OTLP exporter is added.
public sealed class TracingRegistrationTests
{
    private const string EndpointEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";

    [Fact]
    public void AddBessTracing_registers_tracer_provider_without_endpoint()
    {
        using var scope = new EnvVarScope(EndpointEnvVar, value: null);

        var services = new ServiceCollection();
        services.AddBessTracing();
        using var provider = services.BuildServiceProvider();

        // The TracerProvider is registered via OpenTelemetry's hosted
        // service; resolving it materialises the configure callback so
        // the lambda's branches are exercised.
        Assert.NotNull(provider.GetRequiredService<TracerProvider>());
    }

    [Fact]
    public void AddBessTracing_with_endpoint_set_includes_otlp_exporter_branch()
    {
        using var scope = new EnvVarScope(EndpointEnvVar, "http://localhost:4317");

        var services = new ServiceCollection();
        services.AddBessTracing();
        using var provider = services.BuildServiceProvider();

        // Same materialisation; with the env var present the
        // AddOtlpExporter() call inside the configure lambda runs.
        Assert.NotNull(provider.GetRequiredService<TracerProvider>());
    }

    [Fact]
    public void AddBessTracing_throws_for_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => TracingRegistration.AddBessTracing(null!));
    }

    // Restores the environment variable to its prior value after the
    // test so concurrent tests in this assembly don't leak state.
    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvVarScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() =>
            Environment.SetEnvironmentVariable(_name, _previous);
    }
}
