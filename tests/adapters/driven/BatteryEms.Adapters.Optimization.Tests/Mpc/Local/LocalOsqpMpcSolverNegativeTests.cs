using BatteryEms.Adapters.Optimization.Mpc.Local;
using BatteryEms.Application.Mpc;
using OsqpNet.Native;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests.Mpc.Local;

public sealed class LocalOsqpMpcSolverNegativeTests
{
    [Fact]
    public async Task State_outside_soc_band_maps_to_infeasible()
    {
        var solver = new LocalOsqpMpcSolver();

        var ex = await Assert.ThrowsAsync<LocalOsqpMpcSolverException>(() =>
            solver.SolveAsync(
                LocalOsqpMpcTestFixtures.BuildState(socPercent: 5),
                LocalOsqpMpcTestFixtures.BuildModel(),
                LocalOsqpMpcTestFixtures.BuildOptions(),
                LocalOsqpMpcTestFixtures.Anchor,
                CancellationToken.None));

        Assert.Equal(LocalOsqpMpcReasonCodes.Infeasible, ex.ReasonCode);
    }

    [Fact]
    public async Task Exhausted_solver_budget_maps_to_time_limit()
    {
        var solver = new LocalOsqpMpcSolver();

        var ex = await Assert.ThrowsAsync<LocalOsqpMpcSolverException>(() =>
            solver.SolveAsync(
                LocalOsqpMpcTestFixtures.BuildState(),
                LocalOsqpMpcTestFixtures.BuildModel(),
                LocalOsqpMpcTestFixtures.BuildOptions(timeLimit: TimeSpan.FromTicks(1)),
                LocalOsqpMpcTestFixtures.Anchor,
                CancellationToken.None));

        Assert.Equal(LocalOsqpMpcReasonCodes.TimeLimit, ex.ReasonCode);
    }

    [Fact]
    public async Task Unsupported_multi_input_model_maps_to_model_invalid()
    {
        var solver = new LocalOsqpMpcSolver();
        var model = new MpcModel(
            "two-input-v1",
            a: MpcMatrix.Identity(1),
            b: new MpcMatrix(1, 2, [-0.0006944, 0.0]),
            c: MpcMatrix.Identity(1),
            d: new MpcMatrix(1, 2, [0.0, 0.0]),
            constraints: LocalOsqpMpcTestFixtures.BuildConstraints());

        var ex = await Assert.ThrowsAsync<LocalOsqpMpcSolverException>(() =>
            solver.SolveAsync(
                LocalOsqpMpcTestFixtures.BuildState(),
                model,
                LocalOsqpMpcTestFixtures.BuildOptions(),
                LocalOsqpMpcTestFixtures.Anchor,
                CancellationToken.None));

        Assert.Equal(LocalOsqpMpcReasonCodes.ModelInvalid, ex.ReasonCode);
    }

    [Theory]
    [InlineData(OsqpStatus.Solved, "mpc-osqp-optimal")]
    [InlineData(OsqpStatus.SolvedInaccurate, "mpc-osqp-optimal")]
    [InlineData(OsqpStatus.MaxIterReached, "mpc-osqp-time-limit")]
    [InlineData(OsqpStatus.TimeLimitReached, "mpc-osqp-time-limit")]
    [InlineData(OsqpStatus.PrimalInfeasible, "mpc-osqp-infeasible")]
    [InlineData(OsqpStatus.DualInfeasible, "mpc-osqp-unbounded")]
    [InlineData(OsqpStatus.NonCvx, "mpc-osqp-non-convex")]
    [InlineData(OsqpStatus.SigInt, "mpc-osqp-interrupted")]
    [InlineData(OsqpStatus.Unsolved, "mpc-osqp-unsolved")]
    public void Osqp_status_mapping_is_pinned(OsqpStatus status, string expected)
    {
        Assert.Equal(expected, LocalOsqpMpcStatusMapper.Map(status));
    }
}
