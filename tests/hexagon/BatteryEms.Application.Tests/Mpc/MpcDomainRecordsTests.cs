using System;
using System.Collections.Generic;
using System.Text.Json;
using BatteryEms.Application.Mpc;
using Xunit;

namespace BatteryEms.Application.Tests.Mpc;

// Construction-invariant pins for the Sub-Slice-A records. These guard
// the shape contracts the Sub-Slice-B solver adapter and the Sub-
// Slice-D persistence layer rely on; widening any of these constraints
// is an additive change but loosening a guard is a breaking one.
public sealed class MpcDomainRecordsTests
{
    [Fact]
    public void MpcMatrix_rejects_non_finite_elements()
    {
        Assert.Throws<ArgumentException>(() => new MpcMatrix(1, 1, [double.NaN]));
        Assert.Throws<ArgumentException>(() => new MpcMatrix(1, 1, [double.PositiveInfinity]));
    }

    [Fact]
    public void MpcMatrix_rejects_shape_count_mismatch()
    {
        Assert.Throws<ArgumentException>(() => new MpcMatrix(2, 2, [1.0, 2.0, 3.0]));
    }

    [Fact]
    public void MpcMatrix_equality_is_value_by_value()
    {
        var a = new MpcMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        var b = new MpcMatrix(2, 2, [1.0, 2.0, 3.0, 4.0]);
        var c = new MpcMatrix(2, 2, [1.0, 2.0, 3.0, 5.0]);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void MpcMatrix_defensive_copy_isolates_caller_buffer()
    {
        var buffer = new[] { 1.0, 2.0, 3.0, 4.0 };
        var m = new MpcMatrix(2, 2, buffer);
        buffer[0] = 999.0;
        Assert.Equal(1.0, m[0, 0]);
    }

    [Fact]
    public void MpcModel_rejects_shape_mismatch_between_a_and_b()
    {
        var constraints = MpcTestFixtures.BuildConstraints();
        var a = MpcMatrix.Identity(2);
        var b = new MpcMatrix(1, 1, [0.0]);
        var c = MpcMatrix.Identity(2);
        var d = new MpcMatrix(2, 1, [0.0, 0.0]);

        Assert.Throws<ArgumentException>(() =>
            new MpcModel("bad", a, b, c, d, constraints));
    }

    [Fact]
    public void MpcOptions_default_deterministic_mode_is_strict()
    {
        var options = MpcTestFixtures.BuildOptions();
        Assert.Equal(DeterministicMode.Strict, options.DeterministicMode);
    }

    [Fact]
    public void MpcOptions_horizon_is_sample_times_length()
    {
        var options = MpcTestFixtures.BuildOptions(
            sampleTime: TimeSpan.FromMilliseconds(250),
            horizonLength: 8);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), options.Horizon);
    }

    [Fact]
    public void MpcRequest_rejects_asset_id_mismatch_against_asset()
    {
        var asset = MpcTestFixtures.BuildAsset();
        Assert.Throws<ArgumentException>(() => new MpcRequest(
            assetId: "wrong-asset",
            commandTick: MpcTestFixtures.Anchor,
            asset: asset,
            latestMeasurement: null,
            model: MpcTestFixtures.BuildModel(),
            options: MpcTestFixtures.BuildOptions(),
            priorState: null));
    }

    [Fact]
    public void MpcRun_from_result_serializes_trajectory_and_state_as_json()
    {
        var request = MpcTestFixtures.BuildRequest(
            measurement: MpcTestFixtures.BuildTelemetry());
        var identity = MpcRunIdentity.Build(request, "identity");
        var trajectory = MpcTestFixtures.BuildTrajectory(
            sampleTime: MpcTestFixtures.SampleTime,
            segments: new[] { (0.0, 50.0) });
        var state = new MpcState(MpcTestFixtures.Anchor, [50.0], MpcMatrix.Identity(1));
        var result = MpcDispatchResult.Usable(
            identity.MpcRequestId,
            trajectory,
            state,
            identity.ToStamps());

        var run = MpcRun.FromResult(request, result, MpcTestFixtures.Anchor);

        Assert.NotNull(run.TrajectoryJson);
        Assert.NotNull(run.TerminalStateJson);
        using var trajectoryJson = JsonDocument.Parse(run.TrajectoryJson);
        using var stateJson = JsonDocument.Parse(run.TerminalStateJson);
        Assert.Equal(250, trajectoryJson.RootElement.GetProperty("sample_time_ms").GetInt32());
        Assert.Equal(1, stateJson.RootElement.GetProperty("covariance").GetProperty("rows").GetInt32());
    }

    [Fact]
    public void MpcRequest_rejects_measurement_asset_id_mismatch()
    {
        var asset = MpcTestFixtures.BuildAsset();
        var telemetry = MpcTestFixtures.BuildTelemetry() with { AssetId = "other-asset" };
        Assert.Throws<ArgumentException>(() => new MpcRequest(
            assetId: MpcTestFixtures.AssetId,
            commandTick: MpcTestFixtures.Anchor,
            asset: asset,
            latestMeasurement: telemetry,
            model: MpcTestFixtures.BuildModel(),
            options: MpcTestFixtures.BuildOptions(),
            priorState: null));
    }

    [Fact]
    public void MpcTrajectory_rejects_non_monotonic_points()
    {
        var anchor = MpcTestFixtures.Anchor;
        var points = new List<MpcTrajectoryPoint>
        {
            new(anchor.AddMilliseconds(250), 0.0, 50.0),
            new(anchor, 0.0, 50.0),
        };
        Assert.Throws<ArgumentException>(() =>
            new MpcTrajectory(points, TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public void MpcDispatchResult_usable_requires_non_null_trajectory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MpcDispatchResult.Usable(
                requestId: "req-1",
                trajectory: null!,
                posteriorState: new MpcState(MpcTestFixtures.Anchor, [50.0], MpcMatrix.Identity(1)),
                stamps: new Dictionary<string, string>()));
    }

    [Fact]
    public void MpcDispatchResult_not_usable_can_have_null_trajectory_and_posterior()
    {
        var result = MpcDispatchResult.NotUsable(
            requestId: "req-1",
            reason: "mpc-state-stale-too-long",
            stamps: new Dictionary<string, string>());
        Assert.False(result.IsUsable);
        Assert.Null(result.Trajectory);
        Assert.Null(result.PosteriorState);
    }
}
