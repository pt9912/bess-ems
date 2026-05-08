namespace BatteryEms.Domain;

public enum OperatingState
{
    Init,
    Standby,
    Ready,
    Idle,
    Charging,
    Discharging,
    Limited,
    Fault,
    EmergencyStop,
    Maintenance,
}

public sealed class BatteryAsset
{
    public string AssetId { get; }
    public double CapacityKwh { get; }
    public double MaxChargePowerKw { get; }
    public double MaxDischargePowerKw { get; }
    public double MinSocPercent { get; }
    public double MaxSocPercent { get; }
    public double ChargeEfficiency { get; }
    public double DischargeEfficiency { get; }
    public double MaxRampKwPerSecond { get; }
    public double MinOperatingTemperatureCelsius { get; }
    public double MaxOperatingTemperatureCelsius { get; }

    public BatteryAsset(
        string assetId,
        double capacityKwh,
        double maxChargePowerKw,
        double maxDischargePowerKw,
        double minSocPercent,
        double maxSocPercent,
        double chargeEfficiency,
        double dischargeEfficiency,
        double maxRampKwPerSecond,
        double minOperatingTemperatureCelsius,
        double maxOperatingTemperatureCelsius)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        // RM-M3-05 review M-1: NaN comparisons are always false in C#,
        // so a NaN limit slips past every `< 0`, `>= max`, `<= 0 or > 1`
        // guard below — the bad value would propagate into the kernel
        // and either silently pass through Constraint (every comparison
        // with a NaN bound is false → "within-limits" with a NaN
        // result) or trip the native non-finite check. Reject every
        // numeric limit at construction so neither path can see one.
        ThrowIfNotFinite(capacityKwh, nameof(capacityKwh));
        ThrowIfNotFinite(maxChargePowerKw, nameof(maxChargePowerKw));
        ThrowIfNotFinite(maxDischargePowerKw, nameof(maxDischargePowerKw));
        ThrowIfNotFinite(minSocPercent, nameof(minSocPercent));
        ThrowIfNotFinite(maxSocPercent, nameof(maxSocPercent));
        ThrowIfNotFinite(chargeEfficiency, nameof(chargeEfficiency));
        ThrowIfNotFinite(dischargeEfficiency, nameof(dischargeEfficiency));
        ThrowIfNotFinite(maxRampKwPerSecond, nameof(maxRampKwPerSecond));
        ThrowIfNotFinite(minOperatingTemperatureCelsius, nameof(minOperatingTemperatureCelsius));
        ThrowIfNotFinite(maxOperatingTemperatureCelsius, nameof(maxOperatingTemperatureCelsius));

        if (capacityKwh <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityKwh), "CapacityKwh must be positive.");
        if (maxChargePowerKw < 0)
            throw new ArgumentOutOfRangeException(nameof(maxChargePowerKw), "MaxChargePowerKw must be non-negative.");
        if (maxDischargePowerKw < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDischargePowerKw), "MaxDischargePowerKw must be non-negative.");
        if (minSocPercent < 0 || minSocPercent >= maxSocPercent)
            throw new ArgumentOutOfRangeException(nameof(minSocPercent), "MinSocPercent must satisfy 0 <= min < max.");
        if (maxSocPercent > 100)
            throw new ArgumentOutOfRangeException(nameof(maxSocPercent), "MaxSocPercent must be <= 100.");
        if (chargeEfficiency is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(chargeEfficiency), "ChargeEfficiency must be in (0,1].");
        if (dischargeEfficiency is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(dischargeEfficiency), "DischargeEfficiency must be in (0,1].");
        if (maxRampKwPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRampKwPerSecond), "MaxRampKwPerSecond must be non-negative.");
        if (minOperatingTemperatureCelsius >= maxOperatingTemperatureCelsius)
            throw new ArgumentOutOfRangeException(nameof(minOperatingTemperatureCelsius), "MinOperatingTemperatureCelsius must be < max.");

        AssetId = assetId;
        CapacityKwh = capacityKwh;
        MaxChargePowerKw = maxChargePowerKw;
        MaxDischargePowerKw = maxDischargePowerKw;
        MinSocPercent = minSocPercent;
        MaxSocPercent = maxSocPercent;
        ChargeEfficiency = chargeEfficiency;
        DischargeEfficiency = dischargeEfficiency;
        MaxRampKwPerSecond = maxRampKwPerSecond;
        MinOperatingTemperatureCelsius = minOperatingTemperatureCelsius;
        MaxOperatingTemperatureCelsius = maxOperatingTemperatureCelsius;
    }

    public override bool Equals(object? obj) =>
        obj is BatteryAsset other && string.Equals(AssetId, other.AssetId, StringComparison.Ordinal);

    public override int GetHashCode() => AssetId.GetHashCode(StringComparison.Ordinal);

    private static void ThrowIfNotFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName,
                $"{paramName} must be a finite double; got {value}.");
        }
    }
}
