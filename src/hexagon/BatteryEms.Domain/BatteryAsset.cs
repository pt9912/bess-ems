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
}
