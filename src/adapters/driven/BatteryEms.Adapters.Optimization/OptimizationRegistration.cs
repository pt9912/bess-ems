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
        // the default for headless / API-only hosts; production wiring
        // (RM-M2-OP-05) lands here once OR-Tools is plugged in.
        return services;
    }
}
