using System.Globalization;
using System.Text.Json;
using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OptimizationCore.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("OptimizationCore Integration")]
public sealed class OptimizationCoreManifestReplayTests
{
    [Fact]
    public async Task Manifest_drives_sidecar_schedule_engine_comparison()
    {
        var manifestPath = RepositoryPath(
            "tests/fixtures/replay/rm-m5-04/optimization-core/manifest.v1.json");
        var manifest = OptimizationCoreReplayManifest.Load(manifestPath);
        Assert.Equal("optimization-core-sidecar-comparison", manifest.Kind);

        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        var fixture = OptimizationCoreReplayFixture.Load(Path.Combine(manifestDirectory, manifest.FixturePath));
        var golden = OptimizationCoreReplayGolden.Load(Path.Combine(manifestDirectory, manifest.GoldenPath));
        Assert.Equal(
            manifest.CoversLegacyCases.Order(StringComparer.Ordinal),
            fixture.Cases.Select(theCase => theCase.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            fixture.Cases.Select(theCase => theCase.Name).Order(StringComparer.Ordinal),
            golden.Cases.Select(theCase => theCase.Name).Order(StringComparer.Ordinal));

        await using var sidecar = await EmbeddedOptimizationCoreSidecar
            .StartAsync<OptimalAlwaysSucceedsStub>();
        var optimizer = BuildOptimizer(sidecar);
        foreach (var theCase in fixture.Cases)
        {
            var expected = golden.Cases.Single(item => item.Name == theCase.Name);
            var result = await optimizer.OptimizeAsync(theCase.ToRequest(), CancellationToken.None);
            var report = Compare(theCase.Name, result, expected, manifest.ActivePowerKwTolerance);
            Assert.True(report.IsMatch, report.ToMessage());
        }
    }

    private static OptimizationCoreScheduleOptimizer BuildOptimizer(
        EmbeddedOptimizationCoreSidecar sidecar)
    {
        var options = Defaults.ForHilSimulator(sidecar.Endpoint);
        var client = new OptimizationCoreClient(options);
        return new OptimizationCoreScheduleOptimizer(
            client,
            options,
            new InMemoryOptimizationIdempotencyStore(),
            new Defaults.FixedClock(),
            NullLogger<OptimizationCoreScheduleOptimizer>.Instance);
    }

    private static OptimizationCoreReplayReport Compare(
        string caseName,
        ScheduleOptimizationResult actual,
        OptimizationCoreGoldenCase expected,
        double tolerance)
    {
        var differences = new List<OptimizationCoreReplayDifference>();
        CompareScalar(caseName, "$.run.status", expected.ExpectedStatus, actual.Run.Status.ToString(), differences);
        if (actual.ProducedSchedule is null)
        {
            differences.Add(new OptimizationCoreReplayDifference(
                caseName,
                "business_drift",
                "$.schedule",
                "present",
                "missing",
                null));
            return new OptimizationCoreReplayReport(differences);
        }

        CompareScalar(
            caseName,
            "$.schedule.windows.count",
            expected.ExpectedWindowCount,
            actual.ProducedSchedule.Windows.Count,
            differences);
        foreach (var (window, index) in actual.ProducedSchedule.Windows.Select((window, index) => (window, index)))
        {
            CompareNumeric(
                caseName,
                $"$.schedule.windows[{index}].target_power_kw",
                expected.ExpectedTargetPowerKw,
                window.TargetPowerKw,
                tolerance,
                differences);
        }

        return new OptimizationCoreReplayReport(differences);
    }

    private static void CompareNumeric(
        string caseName,
        string path,
        double expected,
        double actual,
        double tolerance,
        List<OptimizationCoreReplayDifference> differences)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            differences.Add(new OptimizationCoreReplayDifference(
                caseName,
                "numeric_tolerance",
                path,
                expected.ToString("R", CultureInfo.InvariantCulture),
                actual.ToString("R", CultureInfo.InvariantCulture),
                tolerance));
        }
    }

    private static void CompareScalar<T>(
        string caseName,
        string path,
        T expected,
        T actual,
        List<OptimizationCoreReplayDifference> differences)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add(new OptimizationCoreReplayDifference(
                caseName,
                "business_drift",
                path,
                Convert.ToString(expected, CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(actual, CultureInfo.InvariantCulture) ?? string.Empty,
                null));
        }
    }

    private static string RepositoryPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BatteryEms.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        return Path.Combine(directory.FullName, relativePath);
    }

    private sealed record OptimizationCoreReplayManifest(
        string Kind,
        string FixturePath,
        string GoldenPath,
        double ActivePowerKwTolerance,
        IReadOnlyList<string> CoversLegacyCases)
    {
        public static OptimizationCoreReplayManifest Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("replay-manifest.v1", RequiredString(root, "schema_version"));
            return new OptimizationCoreReplayManifest(
                Kind: RequiredString(root, "kind"),
                FixturePath: RequiredString(root.GetProperty("fixture"), "path"),
                GoldenPath: RequiredString(root.GetProperty("golden"), "path"),
                ActivePowerKwTolerance: root.GetProperty("tolerances").GetProperty("active_power_kw_abs").GetDouble(),
                CoversLegacyCases: root.GetProperty("compatibility")
                    .GetProperty("covers_legacy_cases")
                    .EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray());
        }
    }

    private sealed record OptimizationCoreReplayFixture(IReadOnlyList<OptimizationCoreReplayCase> Cases)
    {
        public static OptimizationCoreReplayFixture Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("optimization-core-sidecar-fixture.v1", RequiredString(root, "schema_version"));
            return new OptimizationCoreReplayFixture(
                root.GetProperty("cases")
                    .EnumerateArray()
                    .Select(ReadCase)
                    .ToArray());
        }

        private static OptimizationCoreReplayCase ReadCase(JsonElement element) =>
            new(
                Name: RequiredString(element, "name"),
                HorizonStartUtc: DateTimeOffset.Parse(
                    RequiredString(element, "horizon_start_utc"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                HorizonHours: element.GetProperty("horizon_hours").GetInt32(),
                TimeStepHours: element.GetProperty("time_step_hours").GetInt32(),
                BaseScheduleVersion: element.GetProperty("base_schedule_version").GetInt32(),
                MarketBidArea: RequiredString(element, "market_bid_area"));
    }

    private sealed record OptimizationCoreReplayGolden(IReadOnlyList<OptimizationCoreGoldenCase> Cases)
    {
        public static OptimizationCoreReplayGolden Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("optimization-core-sidecar-golden.v1", RequiredString(root, "schema_version"));
            return new OptimizationCoreReplayGolden(
                root.GetProperty("cases")
                    .EnumerateArray()
                    .Select(ReadCase)
                    .ToArray());
        }

        private static OptimizationCoreGoldenCase ReadCase(JsonElement element) =>
            new(
                Name: RequiredString(element, "name"),
                ExpectedStatus: RequiredString(element, "expected_status"),
                ExpectedWindowCount: element.GetProperty("expected_window_count").GetInt32(),
                ExpectedTargetPowerKw: element.GetProperty("expected_target_power_kw").GetDouble());
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? string.Empty;

    private sealed record OptimizationCoreReplayCase(
        string Name,
        DateTimeOffset HorizonStartUtc,
        int HorizonHours,
        int TimeStepHours,
        int BaseScheduleVersion,
        string MarketBidArea)
    {
        public ScheduleOptimizationRequest ToRequest() =>
            Defaults.SampleRequest(
                HorizonStartUtc,
                TimeSpan.FromHours(HorizonHours),
                TimeSpan.FromHours(TimeStepHours),
                BaseScheduleVersion,
                MarketBidArea);
    }

    private sealed record OptimizationCoreGoldenCase(
        string Name,
        string ExpectedStatus,
        int ExpectedWindowCount,
        double ExpectedTargetPowerKw);

    private sealed record OptimizationCoreReplayReport(
        IReadOnlyList<OptimizationCoreReplayDifference> Differences)
    {
        public bool IsMatch => Differences.Count == 0;

        public string ToMessage() => string.Join(Environment.NewLine, Differences);
    }

    private sealed record OptimizationCoreReplayDifference(
        string CaseName,
        string Kind,
        string Path,
        string Expected,
        string Actual,
        double? Tolerance);
}
