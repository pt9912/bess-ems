using BatteryEms.Domain;

namespace BatteryEms.Adapters.Persistence;

// Wire format for ScheduleType in the persistence assembly. The wire is
// snake_case so the columns read like the rest of the schema; readers
// (DapperOptimizationRunRepository, DapperScheduleRepository) and any
// future schedule-aware repository share this single source of truth so
// a typo on one side fails fast against the same translator on the other.
internal static class ScheduleTypeWire
{
    public static string ToWire(ScheduleType type) => type switch
    {
        ScheduleType.DayAhead => "day_ahead",
        ScheduleType.Intraday => "intraday",
        ScheduleType.RegelLeistungReserve => "regel_leistung_reserve",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown schedule type."),
    };

    public static ScheduleType FromWire(string wire) => wire switch
    {
        "day_ahead" => ScheduleType.DayAhead,
        "intraday" => ScheduleType.Intraday,
        "regel_leistung_reserve" => ScheduleType.RegelLeistungReserve,
        _ => throw new InvalidOperationException($"Unknown schedule type '{wire}' in storage."),
    };
}
