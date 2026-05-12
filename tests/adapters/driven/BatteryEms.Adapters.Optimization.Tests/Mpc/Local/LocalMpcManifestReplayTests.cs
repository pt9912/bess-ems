using System.Globalization;
using System.Text.Json;
using BatteryEms.Adapters.Optimization.Mpc.Local;
using BatteryEms.Application.Mpc;
using Xunit;

namespace BatteryEms.Adapters.Optimization.Tests.Mpc.Local;

public sealed class LocalMpcManifestReplayTests
{
    [Fact]
    public async Task Manifest_drives_local_mpc_engine_comparison()
    {
        var manifestPath = RepositoryPath(
            "tests/fixtures/replay/rm-m5-04/local-mpc/manifest.v1.json");
        var manifest = LocalMpcReplayManifest.Load(manifestPath);
        Assert.Equal("local-mpc-engine-comparison", manifest.Kind);

        var fixture = LocalMpcReplayFixture.Load(
            Path.Combine(Path.GetDirectoryName(manifestPath) ?? string.Empty, manifest.FixturePath));
        Assert.Equal(
            manifest.CoversLegacyCases.Order(StringComparer.Ordinal),
            fixture.Cases.Select(theCase => theCase.Name).Order(StringComparer.Ordinal));

        foreach (var theCase in fixture.Cases)
        {
            var report = await CompareAsync(theCase, manifest.ActivePowerKwTolerance);
            Assert.True(report.IsMatch, report.ToMessage());
        }
    }

    private static async Task<LocalMpcComparisonReport> CompareAsync(
        LocalMpcReplayCase theCase,
        double tolerance)
    {
        var sampleTime = TimeSpan.FromMilliseconds(theCase.SampleTimeMs);
        var options = LocalOsqpMpcTestFixtures.BuildOptions(
            sampleTime: sampleTime,
            horizonLength: theCase.HorizonLength);
        var model = LocalOsqpMpcTestFixtures.BuildModel();

        var directSolver = new LocalOsqpMpcSolver();
        var direct = await directSolver.SolveAsync(
            LocalOsqpMpcTestFixtures.BuildState(theCase.SocPercent),
            model,
            options,
            LocalOsqpMpcTestFixtures.Anchor,
            CancellationToken.None);

        var orchestrator = new DefaultMpcDispatchOrchestrator(
            new IdentityStateEstimator(),
            new LocalOsqpMpcSolver());
        var request = LocalOsqpMpcTestFixtures.BuildRequest(
            model: model,
            options: options,
            telemetry: LocalOsqpMpcTestFixtures.BuildTelemetry(
                theCase.SocPercent,
                theCase.ActivePowerKw));
        var orchestrated = await orchestrator.NextStepAsync(request, CancellationToken.None);

        var differences = new List<LocalMpcComparisonDifference>();
        CompareScalar(theCase.Name, "is_usable", theCase.ExpectedUsable, orchestrated.IsUsable, differences);
        CompareScalar(theCase.Name, "reason", theCase.ExpectedReason, orchestrated.Reason, differences);
        CompareTrajectory(theCase.Name, direct, orchestrated.Trajectory, tolerance, differences);
        return new LocalMpcComparisonReport(differences);
    }

    private static void CompareTrajectory(
        string caseName,
        MpcTrajectory expected,
        MpcTrajectory? actual,
        double tolerance,
        List<LocalMpcComparisonDifference> differences)
    {
        if (actual is null)
        {
            differences.Add(new LocalMpcComparisonDifference(
                caseName,
                "business_drift",
                "$.trajectory",
                "present",
                "missing",
                null));
            return;
        }

        CompareScalar(caseName, "$.trajectory.length", expected.Length, actual.Length, differences);
        var count = Math.Min(expected.Points.Count, actual.Points.Count);
        for (var index = 0; index < count; index++)
        {
            var path = $"$.trajectory.points[{index}]";
            CompareScalar(caseName, $"{path}.time", expected.Points[index].Time, actual.Points[index].Time, differences);
            CompareNumeric(
                caseName,
                $"{path}.active_power_kw",
                expected.Points[index].ActivePowerKw,
                actual.Points[index].ActivePowerKw,
                tolerance,
                differences);
            CompareNumeric(
                caseName,
                $"{path}.predicted_soc_percent",
                expected.Points[index].PredictedSocPercent,
                actual.Points[index].PredictedSocPercent,
                tolerance,
                differences);
        }
    }

    private static void CompareNumeric(
        string caseName,
        string path,
        double expected,
        double actual,
        double tolerance,
        List<LocalMpcComparisonDifference> differences)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            differences.Add(new LocalMpcComparisonDifference(
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
        List<LocalMpcComparisonDifference> differences)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add(new LocalMpcComparisonDifference(
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

    private sealed record LocalMpcReplayManifest(
        string Kind,
        string FixturePath,
        double ActivePowerKwTolerance,
        IReadOnlyList<string> CoversLegacyCases)
    {
        public static LocalMpcReplayManifest Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("replay-manifest.v1", RequiredString(root, "schema_version"));
            return new LocalMpcReplayManifest(
                Kind: RequiredString(root, "kind"),
                FixturePath: RequiredString(root.GetProperty("fixture"), "path"),
                ActivePowerKwTolerance: root.GetProperty("tolerances").GetProperty("active_power_kw_abs").GetDouble(),
                CoversLegacyCases: root.GetProperty("compatibility")
                    .GetProperty("covers_legacy_cases")
                    .EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray());
        }
    }

    private sealed record LocalMpcReplayFixture(IReadOnlyList<LocalMpcReplayCase> Cases)
    {
        public static LocalMpcReplayFixture Load(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("local-mpc-engine-comparison.v1", RequiredString(root, "schema_version"));
            return new LocalMpcReplayFixture(
                root.GetProperty("cases")
                    .EnumerateArray()
                    .Select(ReadCase)
                    .ToArray());
        }

        private static LocalMpcReplayCase ReadCase(JsonElement element) =>
            new(
                Name: RequiredString(element, "name"),
                SocPercent: element.GetProperty("soc_percent").GetDouble(),
                ActivePowerKw: element.GetProperty("active_power_kw").GetDouble(),
                HorizonLength: element.GetProperty("horizon_length").GetInt32(),
                SampleTimeMs: element.GetProperty("sample_time_ms").GetInt32(),
                ExpectedUsable: element.GetProperty("expected_usable").GetBoolean(),
                ExpectedReason: RequiredString(element, "expected_reason"));
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? string.Empty;

    private sealed record LocalMpcReplayCase(
        string Name,
        double SocPercent,
        double ActivePowerKw,
        int HorizonLength,
        int SampleTimeMs,
        bool ExpectedUsable,
        string ExpectedReason);

    private sealed record LocalMpcComparisonReport(
        IReadOnlyList<LocalMpcComparisonDifference> Differences)
    {
        public bool IsMatch => Differences.Count == 0;

        public string ToMessage() => string.Join(Environment.NewLine, Differences);
    }

    private sealed record LocalMpcComparisonDifference(
        string CaseName,
        string Kind,
        string Path,
        string Expected,
        string Actual,
        double? Tolerance);
}
