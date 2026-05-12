using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Adapters.Optimization.Mpc.Local;
using BatteryEms.Application.Mpc;
using BatteryEms.Application.Optimization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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

    // RM-M5-01-C Korrektur-Pass (plan-RM-M5 §Fallback-Matrix Zeile
    // „Timeout/Deadline oder Unavailable vor Ergebnis"): registriert
    // den OR-Tools-Optimizer als `IFallbackScheduleOptimizer`. Wird
    // vom `BessHostBuilder` gerufen, wenn das primäre Backend
    // `optimization_core` und der Fallback-Slot `or_tools` ist.
    //
    // Verwendet einen Per-Service-`ScheduleSolverOptions`-Container
    // (kein `AddSingleton(options)` auf den primären Slot), damit eine
    // parallele primäre `AddBessScheduleSolver`-Registrierung keinen
    // Konfigurations-Konflikt erzeugt. Praktisch ist das heute
    // unmöglich (BessHostBuilder.AddBessScheduleSolver wählt zwischen
    // backends exklusiv), aber der Schutz hält die Linie robust gegen
    // spätere Multi-Optimizer-Topologien.
    public static IServiceCollection AddBessScheduleSolverAsFallback(
        this IServiceCollection services,
        Action<ScheduleSolverOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new ScheduleSolverOptionsBuilder();
        configure?.Invoke(builder);
        var options = builder.Build().EnsureValid();

        services.AddSingleton<IFallbackScheduleOptimizer>(sp =>
        {
            var inner = new OrToolsScheduleOptimizer(
                options,
                sp.GetRequiredService<BatteryEms.Application.Time.IClock>(),
                sp.GetRequiredService<ILogger<OrToolsScheduleOptimizer>>());
            return new OrToolsFallbackScheduleOptimizer(inner);
        });
        return services;
    }

    public static IServiceCollection AddBessLocalOsqpMpcSolver(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IMpcStateEstimator, DefaultLinearKalmanFilter>();
        services.TryAddSingleton<LocalOsqpMpcSolver>();
        services.AddSingleton<IMpcModelSolver>(sp => sp.GetRequiredService<LocalOsqpMpcSolver>());
        services.AddSingleton<IMpcDispatchOptimizer, DefaultMpcDispatchOrchestrator>();
        services.AddSingleton<IFallbackMpcOptimizer, LocalOsqpFallbackMpcOptimizer>();
        return services;
    }
}

// Wrapper, der den OR-Tools-`IScheduleOptimizer` unter
// `IFallbackScheduleOptimizer` exposed. Vertrag delegiert 1:1 — der
// Marker existiert ausschließlich für die DI-Disambiguierung im
// `OptimizationCoreScheduleOptimizer`-Konstruktor.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated by AddBessScheduleSolverAsFallback factory.")]
internal sealed class OrToolsFallbackScheduleOptimizer : IFallbackScheduleOptimizer
{
    private readonly OrToolsScheduleOptimizer _inner;

    public OrToolsFallbackScheduleOptimizer(OrToolsScheduleOptimizer inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public Task<ScheduleOptimizationResult> OptimizeAsync(
        ScheduleOptimizationRequest request,
        CancellationToken cancellationToken)
        => _inner.OptimizeAsync(request, cancellationToken);
}
