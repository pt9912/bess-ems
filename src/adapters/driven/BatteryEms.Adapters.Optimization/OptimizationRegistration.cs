using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Application.Optimization;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Adapters.Optimization;

public static class OptimizationRegistration
{
    public static IServiceCollection AddBessOptimization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // The Application layer ships its own defaults for headless /
        // API-only hosts:
        //   IScheduleOptimizer  → NoOpScheduleOptimizer (overridden by
        //                         AddBessScheduleSolver, RM-M2-OP-05).
        //   IDispatchOptimizer  → ScheduleFollowingDispatchOptimizer
        //                         (RM-M2-01) — picks the highest-priority
        //                         commitment per LH-MKT-006 and emits
        //                         its PowerKw, falls back to Idle when
        //                         no commitment is active. NoOpDispatch
        //                         Optimizer remains in this assembly
        //                         for test hosts that want to wire it
        //                         explicitly.
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
        Action<ScheduleSolverOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new ScheduleSolverOptionsBuilder();
        configure?.Invoke(builder);
        var options = builder.Build().EnsureValid();

        services.AddSingleton(options);
        services.AddSingleton<IScheduleOptimizer, OrToolsScheduleOptimizer>();
        return services;
    }
}
