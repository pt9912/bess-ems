namespace BatteryEms.Adapters.Optimization.OrTools;

// RM-M2-04: linear throughput proxy for LH-OPT-004 "Batteriealterungskosten".
// EUR per kWh of energy passing through the battery (charge + discharge
// each contribute their absolute value because both stress the cells).
// The proxy is an LP-friendly approximation — real cycle-life models are
// nonlinear (Arrhenius / Wöhler) and would push the optimisation into
// MILP territory; M2 stays LP and operators calibrate this single rate
// from their cycle-life datasheet.
//
// Setting `EurPerKwhThroughput = 0` keeps the component active in the
// objective breakdown (so dashboards see a zero entry instead of a
// missing one) without applying a penalty.
public sealed record DegradationCostOptions
{
    public required double EurPerKwhThroughput { get; init; }

    public DegradationCostOptions EnsureValid()
    {
        if (!double.IsFinite(EurPerKwhThroughput) || EurPerKwhThroughput < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EurPerKwhThroughput),
                EurPerKwhThroughput,
                "EurPerKwhThroughput must be finite and non-negative.");
        }
        return this;
    }
}

// RM-M2-04: penalty for SOC deviating from a fixed target percent
// (LH-OPT-004 "Strafkosten für SOC-Zielabweichung"). Modelled in LP via
// two non-negative slack variables per step (`soc_below`, `soc_above`)
// constrained to `target - soc[t]` and `soc[t] - target` respectively;
// the objective penalises their sum so any deviation costs in proportion
// to its magnitude (linear penalty) and duration (sum-over-steps).
//
// The initial SOC step (soc[0]) is excluded because it's pinned by the
// solver options; including it would add a fixed offset to the objective
// that the optimiser cannot influence and would inflate the breakdown
// without changing any decision variable.
public sealed record SocTargetPenaltyOptions
{
    public required double TargetSocPercent { get; init; }
    public required double EurPerPercentDeviation { get; init; }

    public SocTargetPenaltyOptions EnsureValid()
    {
        if (!double.IsFinite(TargetSocPercent) || TargetSocPercent < 0 || TargetSocPercent > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TargetSocPercent),
                TargetSocPercent,
                "TargetSocPercent must be in [0, 100].");
        }
        if (!double.IsFinite(EurPerPercentDeviation) || EurPerPercentDeviation < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EurPerPercentDeviation),
                EurPerPercentDeviation,
                "EurPerPercentDeviation must be finite and non-negative.");
        }
        return this;
    }
}
