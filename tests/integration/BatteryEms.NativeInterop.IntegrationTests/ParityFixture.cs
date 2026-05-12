using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryEms.NativeInterop.IntegrationTests;

// RM-M3-10 schema records for the versioned native/.NET parity
// dataset under tests/fixtures/native_parity/. The JSON is loaded
// once per test run via ParityFixtureLoader and surfaces case
// metadata to NativeParityReplayTests.
//
// CA1812: every record below is constructed by System.Text.Json
// via reflection, which the analyser cannot see. Suppress at the
// record level with a single justification rather than a project-
// wide NoWarn so the rule still fires for genuine dead types.

[SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated by System.Text.Json reflection in ParityFixtureLoader.")]
internal sealed record ParityFixtureV1(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("tolerance_active_power_kw")] double ToleranceActivePowerKw,
    [property: JsonPropertyName("asset_baseline")] string AssetBaseline,
    [property: JsonPropertyName("cases")] IReadOnlyList<ParityCase> Cases);

[SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated by System.Text.Json reflection in ParityFixtureLoader.")]
internal sealed record ParityCase(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("snapshot")] SnapshotInput Snapshot,
    [property: JsonPropertyName("limits")] LimitsInput Limits,
    [property: JsonPropertyName("request")] RequestInput Request,
    [property: JsonPropertyName("expected")] ExpectedCommand Expected);

[SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated by System.Text.Json reflection in ParityFixtureLoader.")]
internal sealed record SnapshotInput(
    [property: JsonPropertyName("soc_percent")] double SocPercent,
    [property: JsonPropertyName("active_power_kw")] double ActivePowerKw,
    [property: JsonPropertyName("temperature_celsius")] double TemperatureCelsius);

[SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated by System.Text.Json reflection in ParityFixtureLoader.")]
internal sealed record LimitsInput(
    [property: JsonPropertyName("max_charge_power_kw")] double MaxChargePowerKw,
    [property: JsonPropertyName("max_discharge_power_kw")] double MaxDischargePowerKw,
    [property: JsonPropertyName("min_soc_percent")] double MinSocPercent,
    [property: JsonPropertyName("max_soc_percent")] double MaxSocPercent,
    [property: JsonPropertyName("max_ramp_kw_per_second")] double MaxRampKwPerSecond,
    [property: JsonPropertyName("min_temperature_celsius")] double MinTemperatureCelsius,
    [property: JsonPropertyName("max_temperature_celsius")] double MaxTemperatureCelsius);

[SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated by System.Text.Json reflection in ParityFixtureLoader.")]
internal sealed record RequestInput(
    [property: JsonPropertyName("target_active_power_kw")] double TargetActivePowerKw,
    [property: JsonPropertyName("previous_active_power_kw")] double? PreviousActivePowerKw,
    [property: JsonPropertyName("dt_seconds")] double DtSeconds);

[SuppressMessage("Performance", "CA1812",
    Justification = "Instantiated by System.Text.Json reflection in ParityFixtureLoader.")]
internal sealed record ExpectedCommand(
    [property: JsonPropertyName("active_power_kw")] double ActivePowerKw,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("was_limited")] bool WasLimited,
    [property: JsonPropertyName("mode")] string Mode);

internal static class ParityFixtureLoader
{
    private const string ExpectedSchemaVersion = "v1";
    private const string FixtureRelativePath = "fixtures/native_parity/cases.v1.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
    };

    // Loaded once per process; xunit reuses the same AppDomain for
    // the whole test run so this trivially caches without contention.
    private static readonly Lazy<ParityFixtureV1> Cached =
        new(LoadFromDisk, isThreadSafe: true);

    public static ParityFixtureV1 Load() => Cached.Value;

    public static ParityFixtureV1 LoadFromPath(string path) => LoadFromPathCore(path);

    private static ParityFixtureV1 LoadFromDisk() =>
        LoadFromPathCore(Path.Combine(AppContext.BaseDirectory, FixtureRelativePath));

    private static ParityFixtureV1 LoadFromPathCore(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Native parity fixture missing at '{path}'. Ensure "
                + "tests/fixtures/native_parity/cases.v1.json exists in the "
                + "repository or is copied into the test output.",
                path);
        }

        var json = File.ReadAllText(path);
        var fixture = JsonSerializer.Deserialize<ParityFixtureV1>(json, Options)
            ?? throw new InvalidDataException(
                $"Native parity fixture at '{path}' deserialised to null.");

        if (!string.Equals(fixture.SchemaVersion, ExpectedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Native parity fixture at '{path}' has schema_version "
                + $"'{fixture.SchemaVersion}' but the loader expects "
                + $"'{ExpectedSchemaVersion}'. A schema bump must ship a new "
                + "cases.v*.json plus a parallel test class — bumping in-place "
                + "is a parity-history erase.");
        }

        if (fixture.Cases is null || fixture.Cases.Count == 0)
        {
            throw new InvalidDataException(
                $"Native parity fixture at '{path}' has no cases.");
        }

        return fixture;
    }
}
