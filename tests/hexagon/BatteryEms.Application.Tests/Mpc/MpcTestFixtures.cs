using System;
using System.Collections.Generic;
using BatteryEms.Application.Mpc;
using BatteryEms.Domain;

namespace BatteryEms.Application.Tests.Mpc;

// Shared fixture builders for the Sub-Slice-A property pins. The
// numbers are deliberately round so failure cases pick out a single
// axis (SOC, power, or ramp) without colliding with another bound.
internal static class MpcTestFixtures
{
    public const string AssetId = "asset-mpc-1";
    public static readonly DateTimeOffset Anchor =
        new(2026, 5, 12, 10, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan SampleTime = TimeSpan.FromMilliseconds(250);

    public static BatteryAsset BuildAsset(double maxRampKwPerSecond = 10) =>
        new(
            AssetId,
            capacityKwh: 100,
            maxChargePowerKw: 50,
            maxDischargePowerKw: 50,
            minSocPercent: 10,
            maxSocPercent: 90,
            chargeEfficiency: 0.95,
            dischargeEfficiency: 0.95,
            maxRampKwPerSecond: maxRampKwPerSecond,
            minOperatingTemperatureCelsius: -20,
            maxOperatingTemperatureCelsius: 55);

    public static MpcConstraints BuildConstraints(
        double minSocPercent = 10,
        double maxSocPercent = 90,
        double minActivePowerKw = -50,
        double maxActivePowerKw = 50,
        double maxRampKwPerSecond = 10) =>
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
        TimeSpan? sampleTime = null,
        int horizonLength = 4,
        DeterministicMode mode = DeterministicMode.Strict)
    {
        var solver = new MpcSolverOptions(
            timeLimit: TimeSpan.FromMilliseconds(50),
            optimalityGap: 1e-4,
            maxIterations: 200);

        var p0 = MpcMatrix.Identity(1);
        var q = MpcMatrix.Identity(1);
        var r = MpcMatrix.Identity(1);
        var estimator = new MpcEstimatorOptions(
            initialCovariance: p0,
            processNoise: q,
            measurementNoise: r,
            maxConsecutiveMissingMeasurements: 5);

        return new MpcOptions(
            sampleTime ?? SampleTime,
            horizonLength,
            solver,
            estimator,
            mode);
    }

    public static BatteryTelemetry BuildTelemetry(
        double socPercent = 50,
        double activePowerKw = 0,
        double temperatureCelsius = 25,
        DateTimeOffset? timestamp = null) =>
        new(
            Timestamp: timestamp ?? Anchor,
            AssetId: AssetId,
            SocPercent: socPercent,
            SohPercent: 100,
            ActivePowerKw: activePowerKw,
            ReactivePowerKvar: 0,
            DcVoltage: 800,
            DcCurrent: 0,
            TemperatureCelsius: temperatureCelsius,
            Available: true,
            FaultStatus: "ok",
            DataQuality: DataQuality.Valid);

    public static MpcTrajectory BuildTrajectory(
        TimeSpan? sampleTime = null,
        DateTimeOffset? anchor = null,
        params (double powerKw, double socPercent)[] segments)
    {
        if (segments.Length == 0)
        {
            throw new ArgumentException("Supply at least one segment.", nameof(segments));
        }
        var step = sampleTime ?? SampleTime;
        var t = anchor ?? Anchor;
        var points = new List<MpcTrajectoryPoint>(segments.Length);
        for (var i = 0; i < segments.Length; i++)
        {
            points.Add(new MpcTrajectoryPoint(t, segments[i].powerKw, segments[i].socPercent));
            t = t.Add(step);
        }
        return new MpcTrajectory(points, step);
    }

    public static MpcRequest BuildRequest(
        BatteryAsset? asset = null,
        MpcModel? model = null,
        MpcOptions? options = null,
        BatteryTelemetry? measurement = null,
        MpcState? priorState = null,
        DateTimeOffset? commandTick = null) =>
        new(
            assetId: AssetId,
            commandTick: commandTick ?? Anchor,
            asset: asset ?? BuildAsset(),
            latestMeasurement: measurement,
            model: model ?? BuildModel(),
            options: options ?? BuildOptions(),
            priorState: priorState);
}
