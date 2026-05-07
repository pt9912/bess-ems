namespace BatteryEms.Domain;

// LH-MKT-007 "konfigurierte Zeitschrittweite pro Fahrplantyp": each
// schedule type has a documented default time-step that callers can
// rely on without hard-coding the magic number at every site. The
// defaults match the dominant European day-ahead / intraday market
// resolutions:
//
//   DayAhead             — 1 hour  (matches EPEX / Nord Pool DA grid)
//   Intraday             — 15 min  (matches continuous intraday trading
//                                   resolution after EPEX 2017)
//   RegelLeistungReserve — 15 min  (matches PRL/aFRR/mFRR settlement
//                                   resolution)
//
// Defaults are NOT enforced as constraints on Schedule construction —
// an Intraday schedule with 1 h windows is still a valid Schedule
// (intraday-reoptimisation as RM-M4-01 may need different cadence
// per use case). Callers that don't care just use the helper; callers
// that do can pass any positive TimeSpan they need.
public static class ScheduleTimeGrid
{
    private static readonly TimeSpan DayAheadDefault = TimeSpan.FromHours(1);
    private static readonly TimeSpan IntradayDefault = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RegelLeistungReserveDefault = TimeSpan.FromMinutes(15);

    public static TimeSpan DefaultTimeStep(ScheduleType type) => type switch
    {
        ScheduleType.DayAhead => DayAheadDefault,
        ScheduleType.Intraday => IntradayDefault,
        ScheduleType.RegelLeistungReserve => RegelLeistungReserveDefault,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown ScheduleType."),
    };
}
