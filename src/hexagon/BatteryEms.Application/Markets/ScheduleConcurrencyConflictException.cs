using BatteryEms.Domain;

namespace BatteryEms.Application.Markets;

// Thrown by IScheduleRepository.Replace when the caller's
// expectedBaseVersion does not match the version currently
// persisted for (AssetId, ScheduleType). The use case catches
// this and materialises a Failed OptimizationRun with
// TerminationCode = "concurrent-version-conflict".
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

    // CA1032 boilerplate — never used by the production code paths
    // (the typed 4-arg ctor carries the conflict shape callers depend
    // on). Coverage is excluded only on these three so the meaningful
    // ctor and BuildMessage stay measured.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public ScheduleConcurrencyConflictException() { }
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public ScheduleConcurrencyConflictException(string message) : base(message) { }
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public ScheduleConcurrencyConflictException(string message, Exception inner) : base(message, inner) { }

    private static string BuildMessage(
        string assetId,
        ScheduleType scheduleType,
        int expectedBaseVersion,
        int actualVersion) =>
        $"Schedule replace for ({assetId}, {scheduleType}) failed: expected base version {expectedBaseVersion}, actual {actualVersion}.";
}
