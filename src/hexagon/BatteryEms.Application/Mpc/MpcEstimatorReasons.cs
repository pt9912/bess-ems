namespace BatteryEms.Application.Mpc;

public static class MpcEstimatorReasons
{
    public const string StateEstimated = "mpc-state-estimated";
    public const string MeasurementSkipped = "mpc-state-measurement-skipped";
    public const string StaleTooLong = "mpc-state-stale-too-long";
    public const string NonPhysical = "mpc-state-non-physical";
    public const string CovarianceDiverged = "mpc-covariance-diverged";
    public const string ColdBootNoMeasurement = "mpc-state-cold-boot-no-measurement";
}
