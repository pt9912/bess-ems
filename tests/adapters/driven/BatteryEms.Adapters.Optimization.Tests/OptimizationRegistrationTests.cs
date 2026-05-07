using BatteryEms.Adapters.Optimization;
using BatteryEms.Adapters.Optimization.OrTools;
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
            opt.InitialSocPercent = 50;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ScheduleSolverOptions>();

        Assert.Equal(TimeSpan.FromSeconds(5), options.TimeLimit);
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

    [Fact]
    public void Resolved_options_are_init_only_and_with_expression_does_not_mutate_singleton()
    {
        // Review #11: the resolved ScheduleSolverOptions instance is
        // immutable — a `with` copy yields a new instance, the live
        // singleton stays unchanged. Behavioural test trumps reflection
        // because the IL pattern for init-only setters is brittle.
        var services = new ServiceCollection();
        SeedApplicationDefaults(services);
        services.AddBessScheduleSolver(opt => opt.TimeLimit = TimeSpan.FromSeconds(1));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ScheduleSolverOptions>();
        var copy = options with { TimeLimit = TimeSpan.FromSeconds(99) };

        Assert.Equal(TimeSpan.FromSeconds(99), copy.TimeLimit);
        Assert.Equal(TimeSpan.FromSeconds(1), options.TimeLimit);
        Assert.NotSame(options, copy);
    }

    private static void SeedApplicationDefaults(IServiceCollection services)
    {
        services.AddSingleton<IClock>(new TestFixtures.FrozenClock(TestFixtures.HorizonStart));
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>),
            typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        // Mirror AddBessApplicationInMemoryStores' NoOpScheduleOptimizer default.
        services.AddSingleton<IScheduleOptimizer, NoOpScheduleOptimizer>();
    }
}
