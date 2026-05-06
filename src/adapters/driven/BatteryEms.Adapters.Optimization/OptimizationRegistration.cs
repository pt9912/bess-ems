using BatteryEms.Application.Optimization;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Adapters.Optimization;

public static class OptimizationRegistration
{
    public static IServiceCollection AddBessOptimization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IDispatchOptimizer, NoOpDispatchOptimizer>();
        return services;
    }
}
