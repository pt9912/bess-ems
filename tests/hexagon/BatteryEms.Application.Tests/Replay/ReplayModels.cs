using BatteryEms.Domain;

namespace BatteryEms.Application.Tests.Replay;

internal static class ReplaySchemaVersions
{
    public const string Manifest = "replay-manifest.v1";
    public const string TelemetryFixture = "telemetry-replay-fixture.v1";
    public const string GoldenCommands = "telemetry-golden-command.v1";
    public const string NativeParityCases = "native-parity-cases.v1";
    public const string LocalMpcEngineComparison = "local-mpc-engine-comparison.v1";
    public const string OptimizationCoreSidecarFixture = "optimization-core-sidecar-fixture.v1";
    public const string OptimizationCoreSidecarGolden = "optimization-core-sidecar-golden.v1";
    public const string DiffReport = "replay-diff-report.v1";
}

internal sealed record ReplayManifest(
    string DatasetId,
    string Kind,
    ReplayFieldSchema Schema,
    ReplayFileReference Fixture,
    ReplayFileReference Golden,
    ReplayDeterminism Determinism,
    string RequestIdRule,
    ReplaySolverOptions SolverOptions,
    ReplayTolerances Tolerances,
    ReplayCompatibility Compatibility);

internal sealed record ReplayFieldSchema(
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields,
    IReadOnlyList<string> DeprecatedFields,
    IReadOnlyList<string> ToleratedLegacyFields);

internal sealed record ReplayFileReference(string Path, string SchemaVersion);

internal sealed record ReplayDeterminism(
    string Mode,
    int? Seed,
    string Runtime,
    string Numeric);

internal sealed record ReplaySolverOptions(string Optimizer);

internal sealed record ReplayTolerances(
    double ActivePowerKwAbs,
    double ReactivePowerKvarAbs);

internal sealed record ReplayCompatibility(IReadOnlyList<string> CoversLegacyCases);

internal sealed record TelemetryReplayDataset(
    IReadOnlyList<TelemetryReplayRecord> Records,
    IReadOnlyList<Schedule> Schedules);

internal sealed record ReplayManifestLoadResult
{
    private ReplayManifestLoadResult(
        ReplayManifest? manifest,
        string? errorCode,
        string? path,
        string? detail)
    {
        Manifest = manifest;
        ErrorCode = errorCode;
        Path = path;
        Detail = detail;
    }

    public bool IsSuccess => Manifest is not null;
    public ReplayManifest? Manifest { get; }
    public string? ErrorCode { get; }
    public string? Path { get; }
    public string? Detail { get; }

    public static ReplayManifestLoadResult Success(ReplayManifest manifest) =>
        new(manifest, errorCode: null, path: null, detail: null);

    public static ReplayManifestLoadResult Failure(string errorCode, string path, string detail) =>
        new(manifest: null, errorCode, path, detail);
}

internal sealed record ReplayGoldenCommand(
    string? CommandId,
    DateTimeOffset Timestamp,
    string AssetId,
    CommandMode Mode,
    double ActivePowerKw,
    double? ReactivePowerKvar,
    string Reason,
    CommandSource Source);

internal sealed record ReplayDiffReport(IReadOnlyList<ReplayDiff> Differences)
{
    public bool IsMatch => Differences.Count == 0;
}

internal sealed record ReplayDiff(
    string Kind,
    string Path,
    string Expected,
    string Actual,
    double? Tolerance);
