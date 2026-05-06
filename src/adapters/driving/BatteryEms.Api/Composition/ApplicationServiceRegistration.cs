using BatteryEms.Application.Api;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using Microsoft.Extensions.DependencyInjection;

namespace BatteryEms.Api.Composition;

// Concentrates the Application-layer DI wiring used by the API host.
// Pulling these AddSingleton calls out of Program.BuildApp keeps the
// composition root's class coupling under the CA1506 threshold; the
// Worker (RM-M1-19) will reuse this extension for the same shape.
public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddBessApplicationInMemoryStores(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IBatteryAssetRegistry>(_ => new InMemoryBatteryAssetRegistry());
        services.AddSingleton<ISnapshotStore>(_ => new InMemorySnapshotStore(TimeSpan.FromSeconds(10)));
        services.AddSingleton<ICommandRepository, InMemoryCommandRepository>();
        services.AddSingleton<IScheduleRepository>(_ => new InMemoryScheduleRepository());
        services.AddSingleton<IOperatorStopRegistry, InMemoryOperatorStopRegistry>();
        services.AddSingleton<IOperatorAuditLog, InMemoryOperatorAuditLog>();

        // Driving-port use cases.
        services.AddSingleton<IHealthQuery, DefaultHealthQuery>();
        services.AddSingleton<IBatteryStatusQuery, DefaultBatteryStatusQuery>();
        services.AddSingleton<IScheduleQuery, DefaultScheduleQuery>();
        services.AddSingleton<IOperatorStopUseCase, DefaultOperatorStopUseCase>();
        return services;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by the DI container via reflection.")]
    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
