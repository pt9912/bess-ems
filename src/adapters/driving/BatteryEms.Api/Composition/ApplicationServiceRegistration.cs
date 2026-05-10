using BatteryEms.Application.Api;
using BatteryEms.Application.Assets;
using BatteryEms.Application.Control;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Observability;
using BatteryEms.Application.Optimization;
using BatteryEms.Application.Persistence;
using BatteryEms.Application.Realtime;
using BatteryEms.Application.Time;
using BatteryEms.Domain;
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
        services.AddSingleton<IScheduleTracker, DefaultScheduleTracker>();
        services.AddSingleton<IReserveRepository>(_ => new InMemoryReserveRepository());

        // RM-M4-03-B: Regelleistung activation dedupe tracker. Default
        // bindings are in-memory; AddBessPersistence replaces the store
        // with the Dapper variant when persistence is wired. The
        // RegelleistungOptions singleton uses the master-DoD defaults
        // (MaxAge=2s, FutureSkewTolerance=500ms, DedupeWindow=10s);
        // Sub-Slice D adds IConfiguration binding for operator overrides.
        services.AddSingleton(_ => new RegelleistungOptions());
        services.AddSingleton<IActivationDedupeStore>(sp => new InMemoryActivationDedupeStore(
            sp.GetRequiredService<RegelleistungOptions>(),
            sp.GetRequiredService<IClock>()));
        // RM-M4-03-C: pipeline orchestrator. Composes the per-sample
        // schema check, ActivationTimeValidator, TimebaseDegraded gate,
        // and the dedupe store in the DoD-pinned order.
        services.AddSingleton<ActivationValidator>();

        services.AddSingleton<IOperatorStopRegistry, InMemoryOperatorStopRegistry>();
        services.AddSingleton<IOperatorAuditLog, InMemoryOperatorAuditLog>();

        // Optimization-run persistence (RM-M2-OP-04). Dapper variant
        // lands in RM-M2-OP-06 and replaces this binding via the
        // composition root, mirroring the M1 in-memory ↔ Dapper pattern.
        services.AddSingleton<IOptimizationRunRepository, InMemoryOptimizationRunRepository>();

        // Schedule optimiser default — stays Failed/no-solver-configured
        // until RM-M2-OP-05 plugs in the OR-Tools adapter through the
        // Composition Root.
        services.AddSingleton<IScheduleOptimizer, NoOpScheduleOptimizer>();

        // Dispatch optimiser default (RM-M2-01): the schedule-following
        // implementation picks the highest-priority commitment per
        // LH-MKT-006 and emits its PowerKw. With no active commitment
        // it returns Idle — same observable behaviour as the legacy
        // NoOpDispatchOptimizer for hosts that haven't seeded any
        // schedules yet.
        services.AddSingleton<IDispatchOptimizer, ScheduleFollowingDispatchOptimizer>();

        // Observability default. Telemetry hosts (Worker/Host) replace
        // this with PrometheusOptimizationRunMetrics via AddBessTelemetry;
        // API-only test hosts keep the no-op so the use case resolves
        // without dragging the Prometheus adapter in.
        services.AddSingleton<IOptimizationRunMetrics>(_ => NoOpOptimizationRunMetrics.Instance);

        // Driving-port use cases.
        services.AddSingleton<IHealthQuery, DefaultHealthQuery>();
        services.AddSingleton<IBatteryStatusQuery, DefaultBatteryStatusQuery>();
        services.AddSingleton<IScheduleQuery, DefaultScheduleQuery>();
        services.AddSingleton<IOperatorStopUseCase, DefaultOperatorStopUseCase>();
        services.AddSingleton<IScheduleOptimizationUseCase, DefaultScheduleOptimizationUseCase>();
        services.AddSingleton<IIntradayReoptimizationUseCase, DefaultIntradayReoptimizationUseCase>();
        return services;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated by the DI container via reflection.")]
    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
