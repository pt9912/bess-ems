namespace BatteryEms.Domain;

public enum MarketType
{
    DayAhead,
    Intraday,
    RegelLeistung,
}

public enum CommitmentBindingState
{
    Pending,
    Binding,
    Released,
    Violated,
}

public sealed record MarketCommitment(
    MarketType Market,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    double PowerKw,
    double Penalty,
    CommitmentBindingState BindingState)
{
    public bool Covers(DateTimeOffset moment) => moment >= WindowStart && moment < WindowEnd;
    public TimeSpan Duration => WindowEnd - WindowStart;
}
