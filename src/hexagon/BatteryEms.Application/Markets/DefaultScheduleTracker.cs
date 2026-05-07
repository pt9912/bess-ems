using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

public sealed class DefaultScheduleTracker : IScheduleTracker
{
    private readonly IScheduleRepository _repository;

    public DefaultScheduleTracker(IScheduleRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public IReadOnlyList<MarketCommitment> GetActiveCommitments(string assetId, DateTimeOffset moment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);

        var commitments = new List<MarketCommitment>();
        foreach (var schedule in _repository.FindAll(assetId))
        {
            var window = schedule.WindowCovering(moment);
            if (window is null)
            {
                continue;
            }

            commitments.Add(new MarketCommitment(
                Market: MapType(schedule.Type),
                // RM-M2-02 / LH-MKT-007: market area travels with the
                // commitment so downstream consumers (LP penalty
                // modelling, audit logs) don't have to back-reference
                // the originating schedule. Schedule already carries it.
                MarketBidArea: schedule.MarketBidArea,
                WindowStart: window.Start,
                WindowEnd: window.End,
                PowerKw: window.TargetPowerKw,
                // Penalty modelling lands with the optimizer (post-MVP per
                // LH-MKT-001); M1 carries 0 so the dispatch sees the
                // commitment but does not cost it.
                Penalty: 0,
                BindingState: BindingStateFor(schedule.Type)));
        }
        return commitments;
    }

    private static MarketType MapType(ScheduleType type) => type switch
    {
        ScheduleType.DayAhead => MarketType.DayAhead,
        ScheduleType.Intraday => MarketType.Intraday,
        ScheduleType.RegelLeistungReserve => MarketType.RegelLeistung,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown schedule type."),
    };

    private static CommitmentBindingState BindingStateFor(ScheduleType type) => type switch
    {
        // Day-Ahead is the only binding-by-default category in M1
        // (LH-MKT-001 + LH-MKT-006 priority 6). Intraday and RL-Reserve
        // are post-MVP; their windows are surfaced as Pending so the
        // optimiser can opt in without a binding contract being implied.
        ScheduleType.DayAhead => CommitmentBindingState.Binding,
        _ => CommitmentBindingState.Pending,
    };
}
