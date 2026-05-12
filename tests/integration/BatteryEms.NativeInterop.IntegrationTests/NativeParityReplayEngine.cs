using BatteryEms.Adapters.NativeInterop;
using BatteryEms.Application.Control;
using BatteryEms.Domain;

namespace BatteryEms.NativeInterop.IntegrationTests;

internal static class NativeParityReplayEngine
{
    public static KernelResult RunManaged(ParityCase theCase)
    {
        var asset = new BatteryAsset(
            assetId:                         "asset-replay",
            capacityKwh:                     100,
            maxChargePowerKw:                theCase.Limits.MaxChargePowerKw,
            maxDischargePowerKw:             theCase.Limits.MaxDischargePowerKw,
            minSocPercent:                   theCase.Limits.MinSocPercent,
            maxSocPercent:                   theCase.Limits.MaxSocPercent,
            chargeEfficiency:                0.95,
            dischargeEfficiency:             0.95,
            maxRampKwPerSecond:              theCase.Limits.MaxRampKwPerSecond,
            minOperatingTemperatureCelsius:  theCase.Limits.MinTemperatureCelsius,
            maxOperatingTemperatureCelsius:  theCase.Limits.MaxTemperatureCelsius);

        var telemetry = new BatteryTelemetry(
            Timestamp:           DateTimeOffset.UnixEpoch,
            AssetId:             "asset-replay",
            SocPercent:          theCase.Snapshot.SocPercent,
            SohPercent:          100,
            ActivePowerKw:       theCase.Snapshot.ActivePowerKw,
            ReactivePowerKvar:   0,
            DcVoltage:           800,
            DcCurrent:           0,
            TemperatureCelsius:  theCase.Snapshot.TemperatureCelsius,
            Available:           true,
            FaultStatus:         "ok",
            DataQuality:         DataQuality.Valid);

        var input = new KernelInput(
            asset, telemetry,
            theCase.Request.TargetActivePowerKw,
            theCase.Request.PreviousActivePowerKw,
            TimeSpan.FromSeconds(theCase.Request.DtSeconds));

        return new ManagedControlKernel().Compute(input);
    }

    public static KernelResult RunNative(
        NativeControlKernel native,
        ParityCase theCase,
        out int mode)
    {
        var snapshot = new BccSnapshot
        {
            SocPercent          = theCase.Snapshot.SocPercent,
            ActivePowerKw       = theCase.Snapshot.ActivePowerKw,
            TemperatureCelsius  = theCase.Snapshot.TemperatureCelsius,
        };
        var limits = new BccLimits
        {
            MaxChargePowerKw       = theCase.Limits.MaxChargePowerKw,
            MaxDischargePowerKw    = theCase.Limits.MaxDischargePowerKw,
            MinSocPercent          = theCase.Limits.MinSocPercent,
            MaxSocPercent          = theCase.Limits.MaxSocPercent,
            MaxRampKwPerSecond     = theCase.Limits.MaxRampKwPerSecond,
            MinTemperatureCelsius  = theCase.Limits.MinTemperatureCelsius,
            MaxTemperatureCelsius  = theCase.Limits.MaxTemperatureCelsius,
        };
        var request = new BccRequest
        {
            TargetActivePowerKw    = theCase.Request.TargetActivePowerKw,
            PreviousActivePowerKw  = theCase.Request.PreviousActivePowerKw ?? 0.0,
            DtSeconds              = theCase.Request.DtSeconds,
            HasPrevious            = theCase.Request.PreviousActivePowerKw.HasValue ? 1 : 0,
        };

        var status = native.Compute(in snapshot, in limits, in request, out var command);
        if (status != BccStatus.Ok && status != BccStatus.Limited)
        {
            throw new InvalidOperationException(
                $"native returned non-OK status {status} for case '{theCase.Name}' "
                + $"(reason {command.ReasonCode}); replay cases must stay on the OK/LIMITED path.");
        }

        mode = command.Mode;
        return new KernelResult(
            ActivePowerKw: command.ActivePowerKw,
            Reason:        NativeFallbackControlKernel.MapReason(command.ReasonCode),
            WasLimited:    status == BccStatus.Limited,
            Source:        KernelResultSource.Native);
    }

    public static int NormaliseMode(string expected) => expected switch
    {
        "stop"      => BccMode.Stop,
        "idle"      => BccMode.Idle,
        "charge"    => BccMode.Charge,
        "discharge" => BccMode.Discharge,
        _ => throw new ArgumentOutOfRangeException(
            nameof(expected), expected,
            "fixture mode must be one of stop|idle|charge|discharge"),
    };
}
