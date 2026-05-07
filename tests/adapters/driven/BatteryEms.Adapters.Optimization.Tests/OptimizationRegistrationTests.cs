using BatteryEms.Adapters.Optimization;
using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

public sealed class OptimizationRegistrationTests
{
    [Fact]
    public void Without_AddBessScheduleSolver_NoOpScheduleOptimizer_remains_default()
    {
        var services = new ServiceCollection();
        SeedApplicationDefaults(services);
        services.AddBessOptimization();

        using var provider = services.BuildServiceProvider();
        var optimizer = provider.GetRequiredService<IScheduleOptimizer>();

        Assert.IsType<NoOpScheduleOptimizer>(optimizer);
    }

    [Fact]
    public void AddBessScheduleSolver_overrides_with_OrToolsScheduleOptimizer()
    {
        var services = new ServiceCollection();
        SeedApplicationDefaults(services);
        services.AddBessOptimization();
        services.AddBessScheduleSolver();

        using var provider = services.BuildServiceProvider();
        var optimizer = provider.GetRequiredService<IScheduleOptimizer>();

        Assert.IsType<OrToolsScheduleOptimizer>(optimizer);
    }

    [Fact]
    public void AddBessScheduleSolver_passes_configure_options_through()
    {
        var services = new ServiceCollection();
        SeedApplicationDefaults(services);
        services.AddBessOptimization();
        services.AddBessScheduleSolver(opt =>
        {
            opt.TimeLimit = TimeSpan.FromSeconds(5);
            opt.DefaultMarketBidArea = "AT";
            opt.InitialSocPercent = 50;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ScheduleSolverOptions>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.TimeLimit);
        Assert.Equal("AT", options.DefaultMarketBidArea);
        Assert.Equal(50, options.InitialSocPercent);
    }

    [Fact]
    public void AddBessScheduleSolver_validates_options_eagerly()
    {
        var services = new ServiceCollection();
        SeedApplicationDefaults(services);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddBessScheduleSolver(opt => opt.TimeLimit = TimeSpan.Zero));
    }

    private static void SeedApplicationDefaults(IServiceCollection services)
    {
        services.AddSingleton<IClock>(new TestFixtures.FrozenClock(TestFixtures.HorizonStart));
        services.AddSingleton<IScheduleRepository>(_ => new InMemoryScheduleRepository());
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        // Mirror AddBessApplicationInMemoryStores' NoOpScheduleOptimizer default.
        services.AddSingleton<IScheduleOptimizer, NoOpScheduleOptimizer>();
    }
}
