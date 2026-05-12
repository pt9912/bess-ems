using BatteryEms.Adapters.Optimization.Mpc.Local;
using BatteryEms.Application.Mpc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests.Mpc.Local;

public sealed class LocalOsqpMpcSolverRoundtripTests
{
    [Fact]
    public async Task Solve_returns_horizon_trajectory_inside_constraints()
    {
        var solver = new LocalOsqpMpcSolver();
        var model = LocalOsqpMpcTestFixtures.BuildModel();
        var options = LocalOsqpMpcTestFixtures.BuildOptions(horizonLength: 4);

        var trajectory = await solver.SolveAsync(
            LocalOsqpMpcTestFixtures.BuildState(),
            model,
            options,
            LocalOsqpMpcTestFixtures.Anchor,
            CancellationToken.None);

        Assert.Equal(4, trajectory.Length);
        Assert.Equal(options.SampleTime, trajectory.SampleTime);
        for (var i = 0; i < trajectory.Points.Count; i++)
        {
            Assert.Equal(LocalOsqpMpcTestFixtures.Anchor.AddTicks(options.SampleTime.Ticks * i), trajectory.Points[i].Time);
            Assert.InRange(trajectory.Points[i].ActivePowerKw, -50, 50);
            Assert.InRange(trajectory.Points[i].PredictedSocPercent, 10, 90);
        }
        Assert.True(MpcConstraintValidator.Validate(trajectory, model, options, lastObservedPowerKw: null).IsValid);
    }

    [Fact]
    public async Task Same_strict_solve_is_byte_identical()
    {
        var solver = new LocalOsqpMpcSolver();
        var model = LocalOsqpMpcTestFixtures.BuildModel();
        var options = LocalOsqpMpcTestFixtures.BuildOptions(horizonLength: 6);
        var state = LocalOsqpMpcTestFixtures.BuildState(socPercent: 62);

        var a = await solver.SolveAsync(state, model, options, LocalOsqpMpcTestFixtures.Anchor, CancellationToken.None);
        var b = await solver.SolveAsync(state, model, options, LocalOsqpMpcTestFixtures.Anchor, CancellationToken.None);

        Assert.Equal(a.Points.Count, b.Points.Count);
        for (var i = 0; i < a.Points.Count; i++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.Points[i].ActivePowerKw), BitConverter.DoubleToInt64Bits(b.Points[i].ActivePowerKw));
            Assert.Equal(BitConverter.DoubleToInt64Bits(a.Points[i].PredictedSocPercent), BitConverter.DoubleToInt64Bits(b.Points[i].PredictedSocPercent));
            Assert.Equal(a.Points[i].Time, b.Points[i].Time);
        }
    }

    [Fact]
    public void Strict_mode_pins_osqp_determinism_flags()
    {
        Assert.False(LocalOsqpMpcSolver.StrictSettings.WarmStart);
        Assert.Equal(0, LocalOsqpMpcSolver.StrictSettings.Scaling);
        Assert.False(LocalOsqpMpcSolver.StrictSettings.Polish);
        Assert.Equal(1, LocalOsqpMpcSolver.StrictSettings.Threads);
    }

    [Fact]
    public async Task Orchestrator_with_local_osqp_solver_returns_usable_result()
    {
        var orchestrator = new DefaultMpcDispatchOrchestrator(
            new IdentityStateEstimator(),
            new LocalOsqpMpcSolver());

        var result = await orchestrator.NextStepAsync(
            LocalOsqpMpcTestFixtures.BuildRequest(),
            CancellationToken.None);

        Assert.True(result.IsUsable);
        Assert.Equal(MpcConstraintReasons.Committed, result.Reason);
        Assert.NotNull(result.Trajectory);
    }

    [Fact]
    public void AddBessLocalOsqpMpcSolver_registers_mpc_driving_port()
    {
        var services = new ServiceCollection();

        services.AddBessLocalOsqpMpcSolver();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<LocalOsqpMpcSolver>(provider.GetRequiredService<IMpcModelSolver>());
        Assert.IsType<DefaultMpcDispatchOrchestrator>(provider.GetRequiredService<IMpcDispatchOptimizer>());
        Assert.IsType<IdentityStateEstimator>(provider.GetRequiredService<IMpcStateEstimator>());
    }
}
