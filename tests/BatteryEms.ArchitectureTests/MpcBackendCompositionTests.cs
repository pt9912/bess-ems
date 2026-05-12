using System.Reflection;
using System.Runtime.ExceptionServices;
using BatteryEms.Adapters.Optimization.Mpc.Local;
using BatteryEms.Application.Mpc;
using BatteryEms.Application.Time;
using BatteryEms.Host;
using BatteryEms.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.ArchitectureTests;

public sealed class MpcBackendCompositionTests
{
    [Fact]
    public void Local_osqp_backend_registers_mpc_optimizer()
    {
        var services = new ServiceCollection();

        InvokeConfigureMpcBackend(services, new BessHostOptions { MpcBackend = "local_osqp" });

        using var provider = services.BuildServiceProvider();
        Assert.IsType<LocalOsqpMpcSolver>(provider.GetRequiredService<IMpcModelSolver>());
        Assert.IsType<DefaultMpcDispatchOrchestrator>(provider.GetRequiredService<IMpcDispatchOptimizer>());
        Assert.IsType<LocalOsqpFallbackMpcOptimizer>(provider.GetRequiredService<IFallbackMpcOptimizer>());
    }

    [Fact]
    public void Default_boot_keeps_mpc_optimizer_unregistered()
    {
        var services = new ServiceCollection();

        InvokeConfigureMpcBackend(services, new BessHostOptions());

        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IMpcDispatchOptimizer>());
    }

    [Fact]
    public void Production_mpc_requires_monotonic_clock()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            InvokeConfigureMpcBackend(services, new BessHostOptions
            {
                MpcBackend = "local_osqp",
                MpcRuntimeProfile = MpcRuntimeProfile.Production,
            }));

        Assert.Contains("mpc-production-without-monotonic-clock", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_mpc_with_monotonic_clock_passes_fallback_gate()
    {
        var services = new ServiceCollection();

        InvokeConfigureMpcBackend(services, new BessHostOptions
        {
            MpcBackend = "local_osqp",
            MpcRuntimeProfile = MpcRuntimeProfile.Production,
            MpcClock = "monotonic_anchored",
        });

        using var provider = services.BuildServiceProvider();
        Assert.IsType<MonotonicAnchoredClock>(provider.GetRequiredService<IClock>());
        Assert.IsType<LocalOsqpFallbackMpcOptimizer>(provider.GetRequiredService<IFallbackMpcOptimizer>());
    }

    [Fact]
    public void Production_validation_rejects_backend_without_fallback_pathway()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock, MonotonicAnchoredClock>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            InvokeValidateMpcProductionGates(services, new BessHostOptions
            {
                MpcBackend = "local_osqp",
                MpcRuntimeProfile = MpcRuntimeProfile.Production,
                MpcClock = "monotonic_anchored",
            }));

        Assert.Contains("mpc-production-without-fallback-pathway", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("optimization_core")]
    [InlineData("bi_modal")]
    public void Reserved_mpc_backend_names_fail_with_not_implemented_reason(string backend)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            InvokeConfigureMpcBackend(services, new BessHostOptions { MpcBackend = backend }));

        Assert.Contains("mpc-backend-not-implemented", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reserved_backend_validation_runs_before_production_gates()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            InvokeConfigureMpcBackend(services, new BessHostOptions
            {
                MpcBackend = "optimization_core",
                MpcRuntimeProfile = MpcRuntimeProfile.Production,
            }));

        Assert.Contains("mpc-backend-not-implemented", ex.Message, StringComparison.Ordinal);
    }

    private static void InvokeConfigureMpcBackend(IServiceCollection services, BessHostOptions options)
    {
        InvokePrivate("ConfigureMpcBackend", services, options);
    }

    private static void InvokeValidateMpcProductionGates(IServiceCollection services, BessHostOptions options)
    {
        InvokePrivate("ValidateMpcProductionGates", services, options);
    }

    private static void InvokePrivate(string methodName, IServiceCollection services, BessHostOptions options)
    {
        var method = typeof(BessHostBuilder).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        try
        {
            method.Invoke(null, [services, options]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
