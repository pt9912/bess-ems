using BatteryEms.Application.Control;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BatteryEms.Worker;

// DI helpers for hosts that want to run the regulation worker. The host
// (RM-M1-19a composition root) calls AddBessWorker once; everything
// worker-internal (use case, options, hosted service) lands behind it.
// Driven adapters (telemetry source, command sink, optimiser) stay the
// host's responsibility — Worker mustn't reference them.
public static class WorkerRegistration
{
    public static IServiceCollection AddBessWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.AddSingleton(ControlCycleOptions.Default);
        services.AddSingleton<IControlCycleUseCase, ControlCycleUseCase>();
        services.AddHostedService<ControlCycleHostedService>();
        return services;
    }
}
