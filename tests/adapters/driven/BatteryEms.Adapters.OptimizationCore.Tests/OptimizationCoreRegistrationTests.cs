using BatteryEms.Application.Optimization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.OptimizationCore.Tests;

// Plan-RM-M5-01-A: DI-Resolution-Pins für AddBessOptimizationCore.
public sealed class OptimizationCoreRegistrationTests
{
    private static OptimizationCoreOptions Options() => new()
    {
        SidecarEndpoint = new Uri("http://localhost:5001"),
        RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
    };

    [Fact]
    public async Task Resolves_schedule_optimizer_as_optimization_core_impl()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<BatteryEms.Application.Time.IClock, TestClock>();
        services.AddBessOptimizationCore(Options());

        await using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IScheduleOptimizer>();

        Assert.Equal(
            "OptimizationCoreScheduleOptimizer",
            resolved.GetType().Name);
    }

    [Fact]
    public void Registers_options_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        var options = Options();
        services.AddBessOptimizationCore(options);

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<OptimizationCoreOptions>();

        Assert.Same(options, resolved);
    }

    [Fact]
    public void Constructor_null_args_throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OptimizationCoreRegistration.AddBessOptimizationCore(null!, Options()));
        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddBessOptimizationCore(null!));
    }

    [Fact]
    public async Task Resolution_with_production_plaintext_options_throws_at_construction()
    {
        // Plan-RM-M5-01 D-02: Production-Profile + plaintext → harter
        // Startup-Fehler. Der Throw kommt aus
        // OptimizationCoreScheduleOptimizer-Konstruktor (EnsureValid).
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton<BatteryEms.Application.Time.IClock, TestClock>();
        services.AddBessOptimizationCore(new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.Production,
        });

        await using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(
            () => sp.GetRequiredService<IScheduleOptimizer>());
        Assert.Contains(
            "optimization-core-not-hardened-in-production",
            ex.Message,
            StringComparison.Ordinal);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
        Justification = "Instantiated by the DI container.")]
    private sealed class TestClock : BatteryEms.Application.Time.IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
    }
}
