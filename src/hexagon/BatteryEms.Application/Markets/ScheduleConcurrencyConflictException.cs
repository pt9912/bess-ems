using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Thrown by IScheduleRepository.Replace when the caller's
// expectedBaseVersion does not match the version currently
// persisted for (AssetId, ScheduleType). The use case catches
// this and materialises a Failed OptimizationRun with
// TerminationCode = "concurrent-version-conflict".
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class ScheduleConcurrencyConflictException : Exception
{
    public string AssetId { get; } = string.Empty;
    public ScheduleType ScheduleType { get; }
    public int ExpectedBaseVersion { get; }
    public int ActualVersion { get; }

    public ScheduleConcurrencyConflictException(
        string assetId,
        ScheduleType scheduleType,
        int expectedBaseVersion,
        int actualVersion)
        : base(BuildMessage(assetId, scheduleType, expectedBaseVersion, actualVersion))
    {
        AssetId = assetId;
        ScheduleType = scheduleType;
        ExpectedBaseVersion = expectedBaseVersion;
        ActualVersion = actualVersion;
    }

    // CA1032: keep the standard constructor surface.
    public ScheduleConcurrencyConflictException() { }
    public ScheduleConcurrencyConflictException(string message) : base(message) { }
    public ScheduleConcurrencyConflictException(string message, Exception inner) : base(message, inner) { }

    private static string BuildMessage(
        string assetId,
        ScheduleType scheduleType,
        int expectedBaseVersion,
        int actualVersion) =>
        $"Schedule replace for ({assetId}, {scheduleType}) failed: expected base version {expectedBaseVersion}, actual {actualVersion}.";
}
