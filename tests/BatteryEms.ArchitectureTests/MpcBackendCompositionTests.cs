using System.Reflection;
using System.Runtime.ExceptionServices;
using BatteryEms.Adapters.Optimization.Mpc.Local;
using BatteryEms.Application.Mpc;
using BatteryEms.Host;
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

    private static void InvokeConfigureMpcBackend(IServiceCollection services, BessHostOptions options)
    {
        var method = typeof(BessHostBuilder).GetMethod(
            "ConfigureMpcBackend",
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
