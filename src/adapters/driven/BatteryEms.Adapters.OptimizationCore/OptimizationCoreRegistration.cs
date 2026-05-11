using BatteryEms.Application.Optimization;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Adapters.OptimizationCore;

// DI-Erweiterung für den optimization-core-Sidecar-Adapter
// (plan-RM-M5-01-A). Der Host (`BessHostBuilder.BuildApp`) ruft
// `AddBessOptimizationCore` nur dann, wenn der Operator den Sidecar-
// Endpoint konfiguriert hat. Mehrere Optimizer-Quellen (NoOp, OR-Tools,
// optimization-core) sind via Composition-Root-Triage exklusiv —
// `BessHostBuilder` entscheidet pre-Registration welcher Optimizer
// gewinnt.
//
// Lifecycle: `OptimizationCoreClient` ist Singleton (hält den gRPC-
// Channel), `OptimizationCoreScheduleOptimizer` ist Singleton hinter
// dem M2-`IScheduleOptimizer`-Port.
public static class OptimizationCoreRegistration
{
    public static IServiceCollection AddBessOptimizationCore(
        this IServiceCollection services,
        OptimizationCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<OptimizationCoreClient>(
            _ => new OptimizationCoreClient(options));
        services.AddSingleton<IScheduleOptimizer, OptimizationCoreScheduleOptimizer>();
        return services;
    }
}
