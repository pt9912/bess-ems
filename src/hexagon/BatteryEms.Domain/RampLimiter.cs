namespace BatteryEms.Domain;

public static class RampLimiter
{
    public static LimitResult Apply(
        BatteryAsset asset,
        double previousActivePowerKw,
        double requestedActivePowerKw,
        TimeSpan timeSinceLastCommand)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (timeSinceLastCommand < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSinceLastCommand), "Time delta must be non-negative.");
        }

        if (asset.MaxRampKwPerSecond == 0 || timeSinceLastCommand == TimeSpan.Zero)
        {
            if (requestedActivePowerKw == previousActivePowerKw)
            {
                return LimitResult.Unchanged(requestedActivePowerKw);
            }

            return LimitResult.Clamped(previousActivePowerKw, "ramp-not-permitted");
        }

        var maxDelta = asset.MaxRampKwPerSecond * timeSinceLastCommand.TotalSeconds;
        var lowerBound = previousActivePowerKw - maxDelta;
        var upperBound = previousActivePowerKw + maxDelta;

        if (requestedActivePowerKw < lowerBound)
        {
            return LimitResult.Clamped(lowerBound, "ramp-down-clamped");
        }

        if (requestedActivePowerKw > upperBound)
        {
            return LimitResult.Clamped(upperBound, "ramp-up-clamped");
        }

        return LimitResult.Unchanged(requestedActivePowerKw);
    }
}
