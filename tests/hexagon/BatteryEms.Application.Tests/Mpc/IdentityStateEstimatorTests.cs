using System;
using System.Threading;
using System.Threading.Tasks;
using BatteryEms.Application.Mpc;
using Xunit;

namespace BatteryEms.Application.Tests.Mpc;

// Identity invariants for the Sub-Slice-A estimator stub. The Sub-
// Slice-C `DefaultLinearKalmanFilter` replaces these once the real
// predict/update kicks in; these pins keep the stub from drifting
// into accidental "almost-filter" behaviour while we wait.
public sealed class IdentityStateEstimatorTests
{
    [Fact]
    public async Task Prior_state_is_passed_through_unchanged()
    {
        var estimator = new IdentityStateEstimator();
        var prior = new MpcState(
            MpcTestFixtures.Anchor,
            [42.0],
            MpcMatrix.Identity(1));

        var update = await estimator.PredictUpdateAsync(
            priorState: prior,
            measurement: MpcTestFixtures.BuildTelemetry(socPercent: 99.0),
            model: MpcTestFixtures.BuildModel(),
            options: MpcTestFixtures.BuildOptions(),
            cancellationToken: CancellationToken.None);

        Assert.True(update.IsHealthy);
        Assert.Same(prior, update.State);
        Assert.Equal("mpc-state-passthrough", update.Reason);
    }

    [Fact]
    public async Task Cold_boot_with_measurement_seeds_state_from_telemetry()
    {
        var estimator = new IdentityStateEstimator();
        var telemetry = MpcTestFixtures.BuildTelemetry(socPercent: 73.5);

        var update = await estimator.PredictUpdateAsync(
            priorState: null,
            measurement: telemetry,
            model: MpcTestFixtures.BuildModel(),
            options: MpcTestFixtures.BuildOptions(),
            cancellationToken: CancellationToken.None);

        Assert.True(update.IsHealthy);
        Assert.Equal("mpc-state-cold-boot-seeded", update.Reason);
        Assert.Equal(73.5, update.State.Mean[0]);
        Assert.Equal(telemetry.Timestamp, update.State.Timestamp);
    }

    [Fact]
    public async Task Cold_boot_without_measurement_is_unhealthy()
    {
        var estimator = new IdentityStateEstimator();

        var update = await estimator.PredictUpdateAsync(
            priorState: null,
            measurement: null,
            model: MpcTestFixtures.BuildModel(),
            options: MpcTestFixtures.BuildOptions(),
            cancellationToken: CancellationToken.None);

        Assert.False(update.IsHealthy);
        Assert.Equal("mpc-state-cold-boot-no-measurement", update.Reason);
    }
}
