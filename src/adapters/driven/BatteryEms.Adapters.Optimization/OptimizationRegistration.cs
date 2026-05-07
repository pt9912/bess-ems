using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Adapters.Optimization;

public static class OptimizationRegistration
{
    public static IServiceCollection AddBessOptimization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDispatchOptimizer, NoOpDispatchOptimizer>();
        // The Application layer ships its own NoOpScheduleOptimizer as
        // the default for headless / API-only hosts; AddBessScheduleSolver
        // (RM-M2-OP-05) overrides it when a host activates the OR-Tools
        // backend.
        return services;
    }

    // RM-M2-OP-05: opt-in registration for the OR-Tools-backed schedule
    // optimiser. Hosts that don't call this extension keep the
    // NoOpScheduleOptimizer registered by AddBessApplicationInMemoryStores
    // — that's intentional (API-only test hosts and headless smoke runs
    // shouldn't pull native solver bindings in).
    //
    // Last-registration-wins matches the IControlCycleMetrics / IHealthQuery
    // pattern; AddBessApplicationInMemoryStores must run first so this
    // override actually replaces the default.
    public static IServiceCollection AddBessScheduleSolver(
        this IServiceCollection services,
        Action<ScheduleSolverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ScheduleSolverOptions();
        configure?.Invoke(options);
        options.EnsureValid();

        services.AddSingleton(options);
        services.AddSingleton<IScheduleOptimizer, OrToolsScheduleOptimizer>();
        return services;
    }
}
