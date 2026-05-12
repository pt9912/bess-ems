using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BatteryEms.Application.Mpc;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests.Mpc;

// Wireing-shape pins. The orchestrator does three things in order
// (estimator → solver → validator); a regression in any of the three
// steps shows up here, not in the validator pin file.
public sealed class DefaultMpcDispatchOrchestratorTests
{
    [Fact]
    public async Task Solver_stub_exception_bubbles_up()
    {
        var orchestrator = new DefaultMpcDispatchOrchestrator(
            new IdentityStateEstimator(),
            new NotImplementedMpcModelSolver());
        var request = MpcTestFixtures.BuildRequest(
            measurement: MpcTestFixtures.BuildTelemetry());

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            orchestrator.NextStepAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task Unhealthy_estimator_short_circuits_before_solver()
    {
        var estimator = new ScriptedEstimator(new MpcStateUpdate(
            State: new MpcState(MpcTestFixtures.Anchor, [50.0], MpcMatrix.Identity(1)),
            IsHealthy: false,
            Reason: "mpc-state-stale-too-long"));
        var solver = new SpySolver(MpcTestFixtures.BuildTrajectory(
            sampleTime: MpcTestFixtures.SampleTime,
            segments: new[] { (0.0, 50.0) }));

        var orchestrator = new DefaultMpcDispatchOrchestrator(estimator, solver);
        var result = await orchestrator.NextStepAsync(
            MpcTestFixtures.BuildRequest(measurement: MpcTestFixtures.BuildTelemetry()),
            CancellationToken.None);

        Assert.False(result.IsUsable);
        Assert.Equal("mpc-state-stale-too-long", result.Reason);
        Assert.Equal(0, solver.CallCount);
        Assert.Null(result.Trajectory);
        Assert.NotNull(result.PosteriorState);
    }

    [Fact]
    public async Task Invalid_trajectory_returns_validator_reason()
    {
        var estimator = new IdentityStateEstimator();
        // Trajectory with SOC out of bounds at index 0
        var invalidTrajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: MpcTestFixtures.SampleTime,
            segments: new[] { (0.0, 5.0) });
        var solver = new SpySolver(invalidTrajectory);

        var orchestrator = new DefaultMpcDispatchOrchestrator(estimator, solver);
        var result = await orchestrator.NextStepAsync(
            MpcTestFixtures.BuildRequest(measurement: MpcTestFixtures.BuildTelemetry()),
            CancellationToken.None);

        Assert.False(result.IsUsable);
        Assert.Equal(MpcConstraintReasons.SocOutOfBounds, result.Reason);
        Assert.Equal(1, solver.CallCount);
    }

    [Fact]
    public async Task Usable_path_returns_trajectory_and_base_stamps()
    {
        var estimator = new IdentityStateEstimator();
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: MpcTestFixtures.SampleTime,
            segments: new[] { (0.0, 50.0), (0.0, 50.0) });
        var solver = new SpySolver(trajectory);

        var orchestrator = new DefaultMpcDispatchOrchestrator(estimator, solver);
        var request = MpcTestFixtures.BuildRequest(measurement: MpcTestFixtures.BuildTelemetry());
        var result = await orchestrator.NextStepAsync(request, CancellationToken.None);

        Assert.True(result.IsUsable);
        Assert.Equal("ok", result.Reason);
        Assert.Same(trajectory, result.Trajectory);
        Assert.NotNull(result.PosteriorState);
        Assert.Equal("lti-soc-v1", result.Stamps["mpc_model_version"]);
        Assert.Equal("Strict", result.Stamps["deterministic_mode"]);
        Assert.Equal("250", result.Stamps["sample_time_ms"]);
        Assert.Equal("4", result.Stamps["horizon_length"]);
    }

    [Fact]
    public void RequestId_is_stable_for_identical_inputs()
    {
        var a = MpcTestFixtures.BuildRequest(measurement: MpcTestFixtures.BuildTelemetry());
        var b = MpcTestFixtures.BuildRequest(measurement: MpcTestFixtures.BuildTelemetry());

        Assert.Equal(
            DefaultMpcDispatchOrchestrator.BuildRequestId(a),
            DefaultMpcDispatchOrchestrator.BuildRequestId(b));
    }

    [Fact]
    public void RequestId_changes_when_tick_changes()
    {
        var a = MpcTestFixtures.BuildRequest(commandTick: MpcTestFixtures.Anchor);
        var b = MpcTestFixtures.BuildRequest(commandTick: MpcTestFixtures.Anchor.AddMilliseconds(250));

        Assert.NotEqual(
            DefaultMpcDispatchOrchestrator.BuildRequestId(a),
            DefaultMpcDispatchOrchestrator.BuildRequestId(b));
    }

    [Fact]
    public async Task Cancellation_propagates_through_estimator()
    {
        var estimator = new CancellingEstimator();
        var solver = new SpySolver(MpcTestFixtures.BuildTrajectory(
            sampleTime: MpcTestFixtures.SampleTime,
            segments: new[] { (0.0, 50.0) }));
        var orchestrator = new DefaultMpcDispatchOrchestrator(estimator, solver);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orchestrator.NextStepAsync(MpcTestFixtures.BuildRequest(), cts.Token));
        Assert.Equal(0, solver.CallCount);
    }

    private sealed class ScriptedEstimator : IMpcStateEstimator
    {
        private readonly MpcStateUpdate _update;
        public ScriptedEstimator(MpcStateUpdate update) => _update = update;
        public Task<MpcStateUpdate> PredictUpdateAsync(
            MpcState? priorState,
            BatteryTelemetry? measurement,
            MpcModel model,
            MpcOptions options,
            CancellationToken cancellationToken) =>
            Task.FromResult(_update);
    }

    private sealed class CancellingEstimator : IMpcStateEstimator
    {
        public Task<MpcStateUpdate> PredictUpdateAsync(
            MpcState? priorState,
            BatteryTelemetry? measurement,
            MpcModel model,
            MpcOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new MpcStateUpdate(
                new MpcState(DateTimeOffset.UnixEpoch, [0.0], MpcMatrix.Identity(1)),
                IsHealthy: true,
                Reason: "ok"));
        }
    }

    private sealed class SpySolver : IMpcModelSolver
    {
        private readonly MpcTrajectory _trajectory;
        public int CallCount { get; private set; }
        public SpySolver(MpcTrajectory trajectory) => _trajectory = trajectory;
        public Task<MpcTrajectory> SolveAsync(
            MpcState currentState,
            MpcModel model,
            MpcOptions options,
            DateTimeOffset trajectoryAnchor,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_trajectory);
        }
    }
}
