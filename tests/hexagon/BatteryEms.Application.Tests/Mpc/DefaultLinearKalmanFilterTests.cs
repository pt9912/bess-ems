using BatteryEms.Application.Mpc;
using Xunit;

namespace BatteryEms.Application.Tests.Mpc;

public sealed class DefaultLinearKalmanFilterTests
{
    [Fact]
    public async Task Measurement_update_moves_state_toward_observed_soc()
    {
        var estimator = new DefaultLinearKalmanFilter();
        var prior = new MpcState(
            MpcTestFixtures.Anchor,
            [40.0],
            new MpcMatrix(1, 1, [4.0]));
        var options = BuildOptions(
            initialCovariance: 4.0,
            processNoise: 0.0,
            measurementNoise: 1.0);

        var update = await estimator.PredictUpdateAsync(
            prior,
            MpcTestFixtures.BuildTelemetry(socPercent: 50.0),
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            options,
            CancellationToken.None);

        Assert.True(update.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.StateEstimated, update.Reason);
        Assert.InRange(update.State.Mean[0], 47.9, 48.1);
        Assert.InRange(update.State.Covariance[0, 0], 0.79, 0.81);
    }

    [Fact]
    public async Task Missing_measurement_skips_update_until_stale_limit()
    {
        var estimator = new DefaultLinearKalmanFilter();
        var prior = new MpcState(MpcTestFixtures.Anchor, [50.0], MpcMatrix.Identity(1));
        var options = BuildOptions(maxMissing: 1);

        var first = await estimator.PredictUpdateAsync(
            prior,
            null,
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            options,
            CancellationToken.None);
        var second = await estimator.PredictUpdateAsync(
            first.State,
            null,
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            options,
            CancellationToken.None);

        Assert.True(first.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.MeasurementSkipped, first.Reason);
        Assert.False(second.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.StaleTooLong, second.Reason);
    }

    [Fact]
    public async Task Valid_measurement_resets_missing_measurement_counter()
    {
        var estimator = new DefaultLinearKalmanFilter();
        var prior = new MpcState(MpcTestFixtures.Anchor, [50.0], MpcMatrix.Identity(1));
        var options = BuildOptions(maxMissing: 1);

        var skipped = await estimator.PredictUpdateAsync(
            prior,
            null,
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            options,
            CancellationToken.None);
        var recovered = await estimator.PredictUpdateAsync(
            skipped.State,
            MpcTestFixtures.BuildTelemetry(socPercent: 51.0),
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            options,
            CancellationToken.None);
        var skippedAgain = await estimator.PredictUpdateAsync(
            recovered.State,
            null,
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            options,
            CancellationToken.None);

        Assert.True(recovered.IsHealthy);
        Assert.True(skippedAgain.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.MeasurementSkipped, skippedAgain.Reason);
    }

    [Theory]
    [InlineData(5.0, 25.0)]
    [InlineData(50.0, 60.0)]
    public async Task Non_physical_measurement_fails_closed(double socPercent, double temperatureCelsius)
    {
        var estimator = new DefaultLinearKalmanFilter();

        var update = await estimator.PredictUpdateAsync(
            null,
            MpcTestFixtures.BuildTelemetry(
                socPercent: socPercent,
                temperatureCelsius: temperatureCelsius),
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            MpcTestFixtures.BuildOptions(),
            CancellationToken.None);

        Assert.False(update.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.NonPhysical, update.Reason);
    }

    [Theory]
    [InlineData(double.NaN, 0.0, 25.0)]
    [InlineData(50.0, double.NaN, 25.0)]
    [InlineData(50.0, 0.0, double.PositiveInfinity)]
    public async Task Non_finite_measurement_fails_closed(
        double socPercent,
        double activePowerKw,
        double temperatureCelsius)
    {
        var estimator = new DefaultLinearKalmanFilter();

        var update = await estimator.PredictUpdateAsync(
            null,
            MpcTestFixtures.BuildTelemetry(
                socPercent: socPercent,
                activePowerKw: activePowerKw,
                temperatureCelsius: temperatureCelsius),
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            MpcTestFixtures.BuildOptions(),
            CancellationToken.None);

        Assert.False(update.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.NonPhysical, update.Reason);
    }

    [Fact]
    public async Task Diverged_covariance_fails_closed()
    {
        var estimator = new DefaultLinearKalmanFilter();
        var prior = new MpcState(
            MpcTestFixtures.Anchor,
            [50.0],
            new MpcMatrix(1, 1, [2e12]));

        var update = await estimator.PredictUpdateAsync(
            prior,
            MpcTestFixtures.BuildTelemetry(socPercent: 50.0),
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            BuildOptions(processNoise: 0.0, measurementNoise: 1e18),
            CancellationToken.None);

        Assert.False(update.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.CovarianceDiverged, update.Reason);
    }

    [Fact]
    public async Task Missing_measurement_with_diverged_covariance_fails_closed()
    {
        var estimator = new DefaultLinearKalmanFilter();
        var prior = new MpcState(
            MpcTestFixtures.Anchor,
            [50.0],
            new MpcMatrix(1, 1, [2e12]));

        var update = await estimator.PredictUpdateAsync(
            prior,
            null,
            MpcTestFixtures.BuildAsset(),
            MpcTestFixtures.BuildModel(),
            BuildOptions(processNoise: 0.0),
            CancellationToken.None);

        Assert.False(update.IsHealthy);
        Assert.Equal(MpcEstimatorReasons.CovarianceDiverged, update.Reason);
    }

    private static MpcOptions BuildOptions(
        double initialCovariance = 1.0,
        double processNoise = 1.0,
        double measurementNoise = 1.0,
        int maxMissing = 5) =>
        new(
            MpcTestFixtures.SampleTime,
            horizonLength: 4,
            new MpcSolverOptions(TimeSpan.FromMilliseconds(50), 1e-4, 200),
            new MpcEstimatorOptions(
                new MpcMatrix(1, 1, [initialCovariance]),
                new MpcMatrix(1, 1, [processNoise]),
                new MpcMatrix(1, 1, [measurementNoise]),
                maxMissing));
}
