using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryEms.NativeInterop.IntegrationTests;

internal static class NativeParityEngineComparisonReportJsonWriter
{
    public const string ReportDirectoryEnvironmentVariable = "BESS_REPLAY_REPORT_DIR";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string ToJson(
        ReplayManifestV1 manifest,
        NativeParityEngineComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(report);

        var document = new NativeParityEngineComparisonReportDocument(
            SchemaVersion: "replay-diff-report.v1",
            DatasetId: manifest.DatasetId,
            Kind: manifest.Kind,
            IsMatch: report.IsMatch,
            DifferenceCount: report.Differences.Count,
            Differences: report.Differences.Select(NativeParityEngineComparisonReportEntry.FromDifference).ToArray());

        return JsonSerializer.Serialize(document, Options);
    }

    public static string? WriteIfConfigured(
        ReplayManifestV1 manifest,
        NativeParityEngineComparisonReport report)
    {
        var directory = Environment.GetEnvironmentVariable(ReportDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        return WriteToDirectory(manifest, report, directory);
    }

    public static string WriteToDirectory(
        ReplayManifestV1 manifest,
        NativeParityEngineComparisonReport report,
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

    private sealed record NativeParityEngineComparisonReportDocument(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("dataset_id")] string DatasetId,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("is_match")] bool IsMatch,
        [property: JsonPropertyName("difference_count")] int DifferenceCount,
        [property: JsonPropertyName("differences")] IReadOnlyList<NativeParityEngineComparisonReportEntry> Differences);

    private sealed record NativeParityEngineComparisonReportEntry(
        [property: JsonPropertyName("case_name")] string CaseName,
        [property: JsonPropertyName("engine")] string Engine,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("expected")] string Expected,
        [property: JsonPropertyName("actual")] string Actual,
        [property: JsonPropertyName("tolerance")] double? Tolerance)
    {
        public static NativeParityEngineComparisonReportEntry FromDifference(
            NativeParityEngineDifference difference) =>
            new(
                CaseName: difference.CaseName,
                Engine: difference.Engine,
                Kind: difference.Kind,
                Field: difference.Field,
                Expected: difference.Expected,
                Actual: difference.Actual,
                Tolerance: difference.Tolerance);
    }
}
