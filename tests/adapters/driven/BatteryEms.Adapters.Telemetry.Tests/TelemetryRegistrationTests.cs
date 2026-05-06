using BatteryEms.Adapters.Telemetry.Prometheus;
using BatteryEms.Application.Observability;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Adapters.Telemetry.Tests;

public sealed class TelemetryRegistrationTests
{
    [Fact]
    public void AddBessTelemetry_registers_PrometheusControlCycleMetrics_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddBessTelemetry();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IControlCycleMetrics>();
        var second = provider.GetRequiredService<IControlCycleMetrics>();
        Assert.IsType<PrometheusControlCycleMetrics>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AddBessTelemetry_throws_for_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => TelemetryRegistration.AddBessTelemetry(null!));
    }
}
