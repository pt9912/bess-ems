using BatteryEms.Application.Mpc;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests.Mpc;

public sealed class MpcRunIdentityTests
{
    [Fact]
    public void Identical_inputs_produce_same_request_id_and_default_seed()
    {
        var a = MpcRunIdentity.Build(BuildRequest(), "kalman-v1");
        var b = MpcRunIdentity.Build(BuildRequest(), "kalman-v1");

        Assert.Equal(a.MpcRequestId, b.MpcRequestId);
        Assert.Equal(a.RandomSeed, b.RandomSeed);
    }

    [Theory]
    [InlineData("asset_id")]
    [InlineData("tick")]
    [InlineData("sample_time")]
    [InlineData("model_version")]
    [InlineData("estimator_variant")]
    [InlineData("solver_config")]
    [InlineData("estimator_config")]
    [InlineData("random_seed")]
    public void Each_identity_axis_changes_request_id(string axis)
    {
        var baseline = MpcRunIdentity.Build(BuildRequest(), "kalman-v1");
        var changed = axis switch
        {
            "asset_id" => MpcRunIdentity.Build(BuildRequest(assetId: "asset-mpc-2"), "kalman-v1"),
            "tick" => MpcRunIdentity.Build(BuildRequest(tick: MpcTestFixtures.Anchor.AddMilliseconds(250)), "kalman-v1"),
            "sample_time" => MpcRunIdentity.Build(BuildRequest(sampleTime: TimeSpan.FromMilliseconds(500)), "kalman-v1"),
            "model_version" => MpcRunIdentity.Build(BuildRequest(modelVersion: "lti-soc-v2"), "kalman-v1"),
            "estimator_variant" => MpcRunIdentity.Build(BuildRequest(), "identity"),
            "solver_config" => MpcRunIdentity.Build(BuildRequest(maxIterations: 400), "kalman-v1"),
            "estimator_config" => MpcRunIdentity.Build(BuildRequest(initialCovariance: 2.0), "kalman-v1"),
            "random_seed" => MpcRunIdentity.Build(BuildRequest(seed: 1234), "kalman-v1"),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null),
        };

        Assert.NotEqual(baseline.MpcRequestId, changed.MpcRequestId);
    }

    [Fact]
    public void Operator_seed_override_is_visible_in_stamps()
    {
        var identity = MpcRunIdentity.Build(BuildRequest(seed: 42), "kalman-v1");

        Assert.Equal(42, identity.RandomSeed);
        Assert.Equal("42", identity.ToStamps()["random_seed"]);
    }

    private static MpcRequest BuildRequest(
        string assetId = MpcTestFixtures.AssetId,
        DateTimeOffset? tick = null,
        TimeSpan? sampleTime = null,
        string modelVersion = "lti-soc-v1",
        int maxIterations = 200,
        double initialCovariance = 1.0,
        long? seed = null)
    {
        var asset = BuildAsset(assetId);
        var constraints = MpcTestFixtures.BuildConstraints();
        var model = new MpcModel(
            modelVersion,
            MpcMatrix.Identity(1),
            new MpcMatrix(1, 1, [-0.0006944]),
            MpcMatrix.Identity(1),
            new MpcMatrix(1, 1, [0.0]),
            constraints);
        var p0 = new MpcMatrix(1, 1, [initialCovariance]);
        var options = new MpcOptions(
            sampleTime ?? MpcTestFixtures.SampleTime,
            horizonLength: 4,
            new MpcSolverOptions(TimeSpan.FromMilliseconds(50), 1e-4, maxIterations),
            new MpcEstimatorOptions(p0, MpcMatrix.Identity(1), MpcMatrix.Identity(1), 5),
            randomSeedOverride: seed);

        return new MpcRequest(
            assetId,
            tick ?? MpcTestFixtures.Anchor,
            asset,
            latestMeasurement: null,
            model,
            options,
            priorState: null);
    }

    private static BatteryAsset BuildAsset(string assetId) =>
        new(
            assetId,
            capacityKwh: 100,
            maxChargePowerKw: 50,
            maxDischargePowerKw: 50,
            minSocPercent: 10,
            maxSocPercent: 90,
            chargeEfficiency: 0.95,
            dischargeEfficiency: 0.95,
            maxRampKwPerSecond: 10,
            minOperatingTemperatureCelsius: -20,
            maxOperatingTemperatureCelsius: 55);
}
