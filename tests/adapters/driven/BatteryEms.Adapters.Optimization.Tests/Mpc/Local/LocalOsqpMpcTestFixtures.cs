using BatteryEms.Application.Mpc;
using BatteryEms.Domain;

namespace BatteryEms.Adapters.Optimization.Tests.Mpc.Local;

internal static class LocalOsqpMpcTestFixtures
{
    public const string AssetId = "asset-mpc-local-osqp";
    public static readonly DateTimeOffset Anchor =
        new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan SampleTime = TimeSpan.FromMilliseconds(250);

    public static MpcState BuildState(double socPercent = 50) =>
        new(Anchor, [socPercent], MpcMatrix.Identity(1));

    public static MpcConstraints BuildConstraints(
        double minSocPercent = 10,
        double maxSocPercent = 90,
        double minActivePowerKw = -50,
        double maxActivePowerKw = 50,
        double maxRampKwPerSecond = 10_000) =>
        new(
            minSocPercent,
            maxSocPercent,
            minActivePowerKw,
            maxActivePowerKw,
            maxRampKwPerSecond);

    public static MpcModel BuildModel(MpcConstraints? constraints = null) =>
        new(
            modelVersion: "lti-soc-v1",
            a: MpcMatrix.Identity(1),
            b: new MpcMatrix(1, 1, [-0.0006944]),
            c: MpcMatrix.Identity(1),
            d: new MpcMatrix(1, 1, [0.0]),
            constraints ?? BuildConstraints());

    public static MpcOptions BuildOptions(
        TimeSpan? timeLimit = null,
        TimeSpan? sampleTime = null,
        int horizonLength = 4,
        int maxIterations = 4_000)
    {
        var solver = new MpcSolverOptions(
            timeLimit ?? TimeSpan.FromMilliseconds(500),
            optimalityGap: 1e-4,
            maxIterations);

        var estimator = new MpcEstimatorOptions(
            initialCovariance: MpcMatrix.Identity(1),
            processNoise: MpcMatrix.Identity(1),
            measurementNoise: MpcMatrix.Identity(1),
            maxConsecutiveMissingMeasurements: 5);

        return new MpcOptions(
            sampleTime ?? SampleTime,
            horizonLength,
            solver,
            estimator);
    }

    public static BatteryAsset BuildAsset() =>
        new(
            AssetId,
            capacityKwh: 100,
            maxChargePowerKw: 50,
            maxDischargePowerKw: 50,
            minSocPercent: 10,
            maxSocPercent: 90,
            chargeEfficiency: 0.95,
            dischargeEfficiency: 0.95,
            maxRampKwPerSecond: 10_000,
            minOperatingTemperatureCelsius: -20,
            maxOperatingTemperatureCelsius: 55);

    public static BatteryTelemetry BuildTelemetry(double socPercent = 50, double activePowerKw = 0) =>
        new(
            Timestamp: Anchor,
            AssetId: AssetId,
            SocPercent: socPercent,
            SohPercent: 100,
            ActivePowerKw: activePowerKw,
            ReactivePowerKvar: 0,
            DcVoltage: 800,
            DcCurrent: 0,
            TemperatureCelsius: 25,
            Available: true,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid);

    public static MpcRequest BuildRequest(
        MpcModel? model = null,
        MpcOptions? options = null,
        BatteryTelemetry? telemetry = null) =>
        new(
            AssetId,
            Anchor,
            BuildAsset(),
            telemetry ?? BuildTelemetry(),
            model ?? BuildModel(),
            options ?? BuildOptions(),
            priorState: null);
}
