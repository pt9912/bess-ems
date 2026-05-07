using BatteryEms.Adapters.Optimization.OrTools;
using BatteryEms.Domain;
using Google.OrTools.LinearSolver;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests;

public sealed class OrToolsResultMapperTests
{
    [Theory]
    [InlineData(Solver.ResultStatus.OPTIMAL, OptimizationSolverStatus.Optimal, "or-tools-optimal")]
    [InlineData(Solver.ResultStatus.FEASIBLE, OptimizationSolverStatus.Feasible, "or-tools-feasible-not-proven-optimal")]
    [InlineData(Solver.ResultStatus.INFEASIBLE, OptimizationSolverStatus.Infeasible, "or-tools-infeasible")]
    [InlineData(Solver.ResultStatus.UNBOUNDED, OptimizationSolverStatus.Unbounded, "or-tools-unbounded")]
    [InlineData(Solver.ResultStatus.ABNORMAL, OptimizationSolverStatus.Failed, "or-tools-abnormal")]
    [InlineData(Solver.ResultStatus.MODEL_INVALID, OptimizationSolverStatus.Failed, "or-tools-model-invalid")]
    [InlineData(Solver.ResultStatus.NOT_SOLVED, OptimizationSolverStatus.Failed, "or-tools-not-solved")]
    public void Backend_status_maps_to_expected_solver_status(
        Solver.ResultStatus backendStatus,
        OptimizationSolverStatus expected,
        string expectedReasonPrefix)
    {
        var (status, reason) = OrToolsResultMapper.Map(backendStatus, TimeSpan.FromMilliseconds(5), timeLimit: null);
        Assert.Equal(expected, status);
        Assert.Equal(expectedReasonPrefix, reason);
    }

    [Fact]
    public void Not_solved_strictly_past_time_limit_maps_to_time_limit()
    {
        var (status, reason) = OrToolsResultMapper.Map(
            Solver.ResultStatus.NOT_SOLVED,
            elapsed: TimeSpan.FromSeconds(2.5),
            timeLimit: TimeSpan.FromSeconds(2));
        Assert.Equal(OptimizationSolverStatus.TimeLimit, status);
        Assert.StartsWith("or-tools-time-limit", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_solved_at_exact_time_limit_boundary_stays_failed()
    {
        // Review #2: strict `>` protects boundary cases — at elapsed
        // exactly equal to the budget the solver might still have a
        // valid result; only NOT_SOLVED past the budget gets the
        // TimeLimit reclass.
        var (status, _) = OrToolsResultMapper.Map(
            Solver.ResultStatus.NOT_SOLVED,
            elapsed: TimeSpan.FromSeconds(2),
            timeLimit: TimeSpan.FromSeconds(2));
        Assert.Equal(OptimizationSolverStatus.Failed, status);
    }

    [Fact]
    public void Feasible_past_time_limit_keeps_feasible_status_and_preserves_schedule_path()
    {
        // Review #2 fix: previously a FEASIBLE solve at time-limit was
        // re-classified to TimeLimit and its schedule discarded. Now
        // FEASIBLE always stays FEASIBLE — the caller keeps the run's
        // produced schedule. TimeLimit only applies to NOT_SOLVED.
        var (status, _) = OrToolsResultMapper.Map(
            Solver.ResultStatus.FEASIBLE,
            elapsed: TimeSpan.FromSeconds(5),
            timeLimit: TimeSpan.FromSeconds(2));
        Assert.Equal(OptimizationSolverStatus.Feasible, status);
    }

    [Fact]
    public void Optimal_inside_time_limit_window_stays_optimal()
    {
        var (status, _) = OrToolsResultMapper.Map(
            Solver.ResultStatus.OPTIMAL,
            elapsed: TimeSpan.FromSeconds(1),
            timeLimit: TimeSpan.FromSeconds(2));
        Assert.Equal(OptimizationSolverStatus.Optimal, status);
    }
}
