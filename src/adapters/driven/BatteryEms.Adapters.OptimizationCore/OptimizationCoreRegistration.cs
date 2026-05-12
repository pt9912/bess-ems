using BatteryEms.Application.Optimization;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
// dem M2-`IScheduleOptimizer`-Port. Der optionale Fallback-Optimizer
// (`IFallbackScheduleOptimizer`) und der Plan-Validator
// (`IFallbackPlanValidator`) werden via Factory aufgelöst, damit das
// .NET-DI keinen Required-Service-Resolve-Error wirft wenn der
// Operator keinen Fallback konfiguriert hat (plan-RM-M5 §Fallback-
// Matrix erlaubt no-fallback ⇒ no_valid_plan + Safe-Stop).
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
        services.AddSingleton<IScheduleOptimizer>(sp => new OptimizationCoreScheduleOptimizer(
            sp.GetRequiredService<OptimizationCoreClient>(),
            sp.GetRequiredService<OptimizationCoreOptions>(),
            sp.GetRequiredService<IOptimizationIdempotencyStore>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<OptimizationCoreScheduleOptimizer>>(),
            sp.GetService<IFallbackScheduleOptimizer>(),
            sp.GetService<IFallbackPlanValidator>(),
            sp.GetService<IOptimizationCoreMetrics>()));
        return services;
    }
}
