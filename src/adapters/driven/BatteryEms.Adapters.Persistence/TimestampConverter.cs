namespace BatteryEms.Adapters.Persistence;

// Npgsql 9 reads `timestamptz` columns as DateTime with Kind=Utc; the
// domain layer carries DateTimeOffset everywhere. SpecifyKind is
// defensive in case a column ever returns Unspecified (some derived
// queries can produce that). The reverse direction is implicit: writing
// DateTimeOffset to `timestamptz` works without conversion.
internal static class TimestampConverter
{
    public static DateTimeOffset ToOffset(DateTime utcDateTime) =>
        new(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);
}
