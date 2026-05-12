using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.IO;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryEms.Worker.Tests;

public sealed class WorkerRegistrationTests
{
    [Fact]
    public void AddBessWorker_registers_control_cycle_use_case_and_hosted_service()
    {
        using var provider = BuildWorkerProvider();

        AssertWorkerRegistration(provider);
    }

    [Fact]
    public void AddBessWorker_throws_for_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => WorkerRegistration.AddBessWorker(null!, new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddBessWorker(null!));
    }

    [Fact]
    public void WorkerOptions_default_cycle_interval_is_one_second()
    {
        var options = new WorkerOptions();
        Assert.Equal(TimeSpan.FromSeconds(1), options.CycleInterval);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private static ServiceProvider BuildWorkerProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        AddApplicationDependencies(services);
        services.AddBessWorker(BuildWorkerConfiguration());
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildWorkerConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:CycleInterval"] = "00:00:00.500",
            })
            .Build();

    private static void AddApplicationDependencies(IServiceCollection services)
    {
        services.AddSingleton<IClock>(new SystemClock());
        services.AddSingleton<IBatteryAssetRegistry>(_ => new InMemoryBatteryAssetRegistry());
        services.AddSingleton<ISnapshotStore>(_ => new InMemorySnapshotStore(TimeSpan.FromSeconds(10)));
        services.AddSingleton<ICommandRepository, InMemoryCommandRepository>();
        services.AddSingleton<IMpcRunRepository, InMemoryMpcRunRepository>();
        services.AddSingleton<IScheduleRepository>(_ => new InMemoryScheduleRepository());
        services.AddSingleton<IOperatorStopRegistry, InMemoryOperatorStopRegistry>();
        services.AddSingleton<IScheduleTracker, DefaultScheduleTracker>();
        services.AddSingleton<IDispatchOptimizer, NullOptimizer>();
        services.AddSingleton<IControlCycleMetrics>(NoOpControlCycleMetrics.Instance);
        services.AddSingleton<IBatteryTelemetrySource, NoOpBatteryTelemetrySource>();
        services.AddSingleton<IBatteryCommandSink, NoOpBatteryCommandSink>();
        services.AddSingleton<BatteryEms.Application.Markets.ITimebaseHealthObserver,
            BatteryEms.Application.Markets.InMemoryTimebaseHealthSource>();
    }

    private static void AssertWorkerRegistration(ServiceProvider provider)
    {
        var cycle = provider.GetRequiredService<IControlCycleUseCase>();
        var options = provider.GetRequiredService<IOptions<WorkerOptions>>().Value;
        var hosted = provider.GetServices<IHostedService>().OfType<ControlCycleHostedService>().ToArray();

        Assert.NotNull(cycle);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.CycleInterval);
        Assert.Single(hosted);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by the DI container via reflection.")]
    private sealed class NullOptimizer : IDispatchOptimizer
    {
        public Task<DispatchResult> OptimizeAsync(DispatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(DispatchResult.Idle("noop", "test-stub"));
    }
}
