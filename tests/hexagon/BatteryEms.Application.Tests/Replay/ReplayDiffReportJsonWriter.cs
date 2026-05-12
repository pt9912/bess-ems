using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryEms.Application.Tests.Replay;

internal static class ReplayDiffReportJsonWriter
{
    public const string ReportDirectoryEnvironmentVariable = "BESS_REPLAY_REPORT_DIR";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string ToJson(ReplayManifest manifest, ReplayDiffReport report)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(report);

        var document = new ReplayDiffReportDocument(
            SchemaVersion: ReplaySchemaVersions.DiffReport,
            DatasetId: manifest.DatasetId,
            Kind: manifest.Kind,
            IsMatch: report.IsMatch,
            DifferenceCount: report.Differences.Count,
            Differences: report.Differences.Select(ReplayDiffReportEntry.FromDiff).ToArray());

        return JsonSerializer.Serialize(document, Options);
    }

    public static string? WriteIfConfigured(ReplayManifest manifest, ReplayDiffReport report)
    {
        var directory = Environment.GetEnvironmentVariable(ReportDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        return WriteToDirectory(manifest, report, directory);
    }

    public static string WriteToDirectory(
        ReplayManifest manifest,
        ReplayDiffReport report,
        string directory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{SafeFileName(manifest.DatasetId)}.replay-diff-report.v1.json");
        File.WriteAllText(path, ToJson(manifest, report));
        return path;
    }

    private static string SafeFileName(string datasetId)
    {
        var characters = datasetId.Select(static character =>
            IsAsciiFileNameCharacter(character) ? character : '_');
        var fileName = new string(characters.ToArray());
        return string.IsNullOrWhiteSpace(fileName) ? "replay-dataset" : fileName;
    }

    private static bool IsAsciiFileNameCharacter(char character) =>
        character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-'
            or '_'
            or '.';

    private sealed record ReplayDiffReportDocument(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("dataset_id")] string DatasetId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("is_match")] bool IsMatch,
        [property: JsonPropertyName("difference_count")] int DifferenceCount,
        [property: JsonPropertyName("differences")] IReadOnlyList<ReplayDiffReportEntry> Differences);

    private sealed record ReplayDiffReportEntry(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("expected")] string Expected,
        [property: JsonPropertyName("actual")] string Actual,
        [property: JsonPropertyName("tolerance")] double? Tolerance)
    {
        public static ReplayDiffReportEntry FromDiff(ReplayDiff diff) =>
            new(
                Kind: diff.Kind,
                Path: diff.Path,
                Expected: diff.Expected,
                Actual: diff.Actual,
                Tolerance: diff.Tolerance);
    }
}
