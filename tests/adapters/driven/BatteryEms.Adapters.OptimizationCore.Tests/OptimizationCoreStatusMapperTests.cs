using BatteryEms.Adapters.OptimizationCore.Grpc.V1;
using BatteryEms.Domain;
using Grpc.Core;
using Xunit;

namespace BatteryEms.Adapters.OptimizationCore.Tests;

// Plan-RM-M5-01-A D-04: Tabelle aus transport-mapping-v1.md 1:1
// gepinnt. Jede Doku-Zeile bekommt einen Test.
public sealed class OptimizationCoreStatusMapperTests
{
    // --- ClassifyResult: solver_status + has_usable_solution -----------

    [Fact]
    public void Optimal_usable_persists_as_sidecar_result()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.Optimal,
            hasUsableSolution: true);

        Assert.Equal(OptimizationSolverStatus.Optimal, outcome.Status);
        Assert.Equal(FallbackSource.SidecarResult, outcome.FallbackSource);
        Assert.Equal(FallbackReason.None, outcome.FallbackReason);
        Assert.True(outcome.PersistSchedule);
    }

    [Fact]
    public void Feasible_usable_persists_as_sidecar_result()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.Feasible,
            hasUsableSolution: true);

        Assert.Equal(OptimizationSolverStatus.Feasible, outcome.Status);
        Assert.Equal(FallbackSource.SidecarResult, outcome.FallbackSource);
        Assert.True(outcome.PersistSchedule);
    }

    [Fact]
    public void Infeasible_falls_back_with_solver_infeasible()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.Infeasible,
            hasUsableSolution: false);

        Assert.Equal(OptimizationSolverStatus.Infeasible, outcome.Status);
        Assert.Equal(FallbackSource.FromMatrix, outcome.FallbackSource);
        Assert.Equal(FallbackReason.SolverInfeasible, outcome.FallbackReason);
        Assert.False(outcome.PersistSchedule);
    }

    [Fact]
    public void Unbounded_falls_back_with_solver_unbounded()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.Unbounded,
            hasUsableSolution: false);

        Assert.Equal(OptimizationSolverStatus.Unbounded, outcome.Status);
        Assert.Equal(FallbackReason.SolverUnbounded, outcome.FallbackReason);
        Assert.False(outcome.PersistSchedule);
    }

    [Fact]
    public void TimeLimit_with_usable_solution_persists_as_feasible()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.TimeLimit,
            hasUsableSolution: true);

        Assert.Equal(OptimizationSolverStatus.Feasible, outcome.Status);
        Assert.Equal(FallbackSource.SidecarResult, outcome.FallbackSource);
        Assert.True(outcome.PersistSchedule);
    }

    [Fact]
    public void TimeLimit_without_usable_solution_falls_back_with_solver_time_limit()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.TimeLimit,
            hasUsableSolution: false);

        Assert.Equal(OptimizationSolverStatus.TimeLimit, outcome.Status);
        Assert.Equal(FallbackReason.SolverTimeLimit, outcome.FallbackReason);
        Assert.False(outcome.PersistSchedule);
    }

    [Fact]
    public void IterationLimit_with_usable_solution_persists_as_feasible()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.IterationLimit,
            hasUsableSolution: true);

        Assert.Equal(OptimizationSolverStatus.Feasible, outcome.Status);
        Assert.True(outcome.PersistSchedule);
    }

    [Fact]
    public void IterationLimit_without_usable_solution_falls_back()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.IterationLimit,
            hasUsableSolution: false);

        Assert.Equal(OptimizationSolverStatus.IterationLimit, outcome.Status);
        Assert.Equal(FallbackReason.SolverIterationLimit, outcome.FallbackReason);
        Assert.False(outcome.PersistSchedule);
    }

    [Theory]
    [InlineData(true)]   // FAILED ignoriert has_usable_solution
    [InlineData(false)]
    public void Failed_falls_back_with_transport_internal_error(bool hasUsableSolution)
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.Failed,
            hasUsableSolution: hasUsableSolution);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Equal(FallbackReason.TransportInternalError, outcome.FallbackReason);
        Assert.False(outcome.PersistSchedule);
    }

    [Fact]
    public void Unspecified_solver_status_falls_back_conservatively_as_failed()
    {
        // Unbekannte Enum-Werte (Sidecar liefert SOLVER_STATUS_UNSPECIFIED
        // oder einen künftigen Wert, den ein älterer Worker noch nicht
        // kennt) werden als Failed klassifiziert.
        var outcome = OptimizationCoreStatusMapper.ClassifyResult(
            OptimizeResult.Types.SolverStatus.Unspecified,
            hasUsableSolution: false);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Equal(FallbackReason.TransportInternalError, outcome.FallbackReason);
    }

    // --- ClassifyTransport: gRPC-StatusCode ohne Payload ----------------

    [Fact]
    public void DeadlineExceeded_maps_to_time_limit_status()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyTransport(StatusCode.DeadlineExceeded);

        Assert.Equal(OptimizationSolverStatus.TimeLimit, outcome.Status);
        Assert.Equal(FallbackReason.DeadlineExceeded, outcome.FallbackReason);
        Assert.False(outcome.PersistSchedule);
    }

    [Fact]
    public void Unavailable_maps_to_sidecar_unavailable()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyTransport(StatusCode.Unavailable);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Equal(FallbackReason.SidecarUnavailable, outcome.FallbackReason);
    }

    [Fact]
    public void Cancelled_maps_to_transport_cancelled()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyTransport(StatusCode.Cancelled);

        Assert.Equal(FallbackReason.TransportCancelled, outcome.FallbackReason);
    }

    [Fact]
    public void InvalidArgument_maps_to_no_activation_path()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyTransport(StatusCode.InvalidArgument);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Equal(FallbackSource.NoActivation, outcome.FallbackSource);
        Assert.Equal(FallbackReason.InvalidRequest, outcome.FallbackReason);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    public void Authz_failures_map_to_unauthorized_client(StatusCode code)
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyTransport(code);

        Assert.Equal(FallbackReason.UnauthorizedClient, outcome.FallbackReason);
    }

    [Theory]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.Unknown)]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.FailedPrecondition)]
    public void Other_transport_failures_map_to_transport_internal_error(StatusCode code)
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyTransport(code);

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Equal(FallbackReason.TransportInternalError, outcome.FallbackReason);
    }

    [Fact]
    public void ClassifyTransport_with_OK_throws()
    {
        Assert.Throws<ArgumentException>(
            () => OptimizationCoreStatusMapper.ClassifyTransport(StatusCode.OK));
    }

    // --- Pre-Request-Gates ---------------------------------------------

    [Fact]
    public void ContractIncompatible_skips_sidecar_with_no_activation()
    {
        var outcome = OptimizationCoreStatusMapper.ClassifyContractIncompatible();

        Assert.Equal(OptimizationSolverStatus.Failed, outcome.Status);
        Assert.Equal(FallbackSource.NoActivation, outcome.FallbackSource);
        Assert.Equal(FallbackReason.ContractIncompatible, outcome.FallbackReason);
        Assert.False(outcome.PersistSchedule);
    }
}
