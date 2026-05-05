namespace BatteryEms.Domain;

public static class ConstraintLimiter
{
    public static LimitResult Apply(BatteryAsset asset, BatteryTelemetry telemetry, double requestedActivePowerKw)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(telemetry);

        if (!telemetry.Available)
        {
            return LimitResult.Clamped(0, "asset-unavailable");
        }

        if (telemetry.TemperatureCelsius < asset.MinOperatingTemperatureCelsius
            || telemetry.TemperatureCelsius > asset.MaxOperatingTemperatureCelsius)
        {
            return LimitResult.Clamped(0, "temperature-out-of-range");
        }

        if (requestedActivePowerKw < 0 && telemetry.SocPercent >= asset.MaxSocPercent)
        {
            return LimitResult.Clamped(0, "soc-at-max-charge-blocked");
        }

        if (requestedActivePowerKw > 0 && telemetry.SocPercent <= asset.MinSocPercent)
        {
            return LimitResult.Clamped(0, "soc-at-min-discharge-blocked");
        }

        if (requestedActivePowerKw < -asset.MaxChargePowerKw)
        {
            return LimitResult.Clamped(-asset.MaxChargePowerKw, "max-charge-power");
        }

        if (requestedActivePowerKw > asset.MaxDischargePowerKw)
        {
            return LimitResult.Clamped(asset.MaxDischargePowerKw, "max-discharge-power");
        }

        return LimitResult.Unchanged(requestedActivePowerKw);
    }
}
