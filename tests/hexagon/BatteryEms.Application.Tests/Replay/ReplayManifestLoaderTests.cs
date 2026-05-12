using System.Text.Json;
using BatteryEms.Adapters.Optimization;
using BatteryEms.Application.Markets;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests.Replay;

[Trait("Category", "Replay")]
public sealed class ReplayManifestLoaderTests
{
    private const string ReplayFixtureRoot = "tests/fixtures/replay/rm-m5-04";

    private static readonly string[] MandatoryM2Cases =
    {
        "m2-replay-bit-exact",
        "m2-schedule-following-golden",
        "m2-missing-valid-recovery",
        "m2-stale-valid-recovery",
    };

    [Fact]
    public async Task M2_manifest_fixtures_replay_against_goldens_and_cover_legacy_inventory()
    {
        var manifests = Directory.GetFiles(
            RepositoryPath(ReplayFixtureRoot),
            "manifest.v1.json",
            SearchOption.AllDirectories)
            .Where(path => path.Contains("telemetry-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(MandatoryM2Cases.Length, manifests.Length);
        var covered = new List<string>();
        foreach (var manifestPath in manifests)
        {
            var loadResult = ReplayManifestLoader.Load(manifestPath);
            Assert.True(loadResult.IsSuccess, loadResult.Detail);
            var manifest = Assert.IsType<ReplayManifest>(loadResult.Manifest);
            covered.AddRange(manifest.Compatibility.CoversLegacyCases);

            var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var dataset = TelemetryReplayJsonLoader.LoadDataset(
                Path.Combine(manifestDirectory, manifest.Fixture.Path));
            var golden = ReplayGoldenJsonLoader.Load(
                Path.Combine(manifestDirectory, manifest.Golden.Path));
            var commands = await new TelemetryReplayHarness(
                TestFixtures.CreateAsset("asset-1"),
                CreateOptimizer(manifest),
                dataset.Schedules)
                .RunAsync("asset-1", dataset.Records, CancellationToken.None);

            var diff = ReplayGoldenComparer.Compare(commands, golden, manifest.Tolerances);
            var reportJson = ReplayDiffReportJsonWriter.ToJson(manifest, diff);
            ReplayDiffReportJsonWriter.WriteIfConfigured(manifest, diff);
            Assert.True(diff.IsMatch, reportJson);
        }

        Assert.Equal(MandatoryM2Cases.Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void M3_native_parity_manifest_references_existing_dataset()
    {
        var manifestPath = RepositoryPath(
            $"{ReplayFixtureRoot}/native-parity/manifest.v1.json");
        var loadResult = ReplayManifestLoader.Load(manifestPath);

        Assert.True(loadResult.IsSuccess, loadResult.Detail);
        var manifest = Assert.IsType<ReplayManifest>(loadResult.Manifest);
        Assert.Equal("native-control-parity", manifest.Kind);
        Assert.Equal("repo://tests/fixtures/native_parity/cases.v1.json", manifest.Fixture.Path);
        Assert.Equal(manifest.Fixture.Path, manifest.Golden.Path);

        var referencedPath = ResolveRepoReference(manifest.Fixture.Path);
        Assert.True(File.Exists(referencedPath), $"Missing referenced native parity fixture at {referencedPath}.");

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(referencedPath));
        var root = new ReplayJsonReader(document.RootElement, "$");
        Assert.Equal("v1", root.RequiredString("schema_version"));
        var caseNames = root.RequiredArray(
            "cases",
            (item, path) => new ReplayJsonReader(item, path).RequiredString("name"));
        Assert.Equal(25, caseNames.Count);
        Assert.Equal(manifest.Compatibility.CoversLegacyCases.Order(StringComparer.Ordinal), caseNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Unknown_manifest_schema_version_is_rejected_with_machine_readable_error()
    {
        var json = File.ReadAllText(RepositoryPath($"{ReplayFixtureRoot}/telemetry-linear/manifest.v1.json"))
            .Replace(ReplaySchemaVersions.Manifest, "replay-manifest.v99", StringComparison.Ordinal);
        using var temporary = TemporaryReplayFile.FromContent(json);

        var result = ReplayManifestLoader.Load(temporary.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal("unsupported_schema_version", result.ErrorCode);
        Assert.Equal("$.schema_version", result.Path);
    }

    [Fact]
    public void Unknown_manifest_top_level_field_is_rejected()
    {
        var json = File.ReadAllText(RepositoryPath($"{ReplayFixtureRoot}/telemetry-linear/manifest.v1.json"))
            .Replace(
                "\"compatibility\":",
                "\"unexpected\": true, \"compatibility\":",
                StringComparison.Ordinal);
        using var temporary = TemporaryReplayFile.FromContent(json);

        var result = ReplayManifestLoader.Load(temporary.Path);

        Assert.False(result.IsSuccess);
        Assert.Equal("unknown_field", result.ErrorCode);
        Assert.Equal("$.unexpected", result.Path);
    }

    [Fact]
    public void Golden_diff_classifies_numeric_tolerance_separately_from_business_drift()
    {
        var expected = new[]
        {
            new ReplayGoldenCommand(
                CommandId: "ctrl-1770292800000-asset-1",
                Timestamp: new DateTimeOffset(2026, 2, 5, 12, 0, 0, TimeSpan.Zero),
                AssetId: "asset-1",
                Mode: CommandMode.Idle,
                ActivePowerKw: 1,
                ReactivePowerKvar: 0,
                Reason: "noop-optimizer",
                Source: CommandSource.Optimization),
        };
        var actual = new[]
        {
            new BatteryCommand(
                CommandId: "ctrl-1770292800000-asset-1",
                Timestamp: new DateTimeOffset(2026, 2, 5, 12, 0, 0, TimeSpan.Zero),
                AssetId: "asset-1",
                Mode: CommandMode.Discharge,
                ActivePowerKw: 1.5,
                ReactivePowerKvar: 0,
                ValidUntil: new DateTimeOffset(2026, 2, 5, 12, 0, 5, TimeSpan.Zero),
                Reason: "noop-optimizer",
                Source: CommandSource.Optimization),
        };

        var report = ReplayGoldenComparer.Compare(
            actual,
            expected,
            new ReplayTolerances(ActivePowerKwAbs: 0.1, ReactivePowerKvarAbs: 0));

        Assert.Contains(report.Differences, difference => difference.Kind == "business_drift");
        Assert.Contains(report.Differences, difference => difference.Kind == "numeric_tolerance");
    }

    [Fact]
    public void Replay_diff_report_serializes_machine_readable_json_and_artifact()
    {
        var loadResult = ReplayManifestLoader.Load(
            RepositoryPath($"{ReplayFixtureRoot}/telemetry-linear/manifest.v1.json"));
        var manifest = Assert.IsType<ReplayManifest>(loadResult.Manifest);
        var report = new ReplayDiffReport(
        [
            new ReplayDiff(
                Kind: "numeric_tolerance",
                Path: "$.commands[0].active_power_kw",
                Expected: "1",
                Actual: "1.5",
                Tolerance: 0.1),
            new ReplayDiff(
                Kind: "business_drift",
                Path: "$.commands[0].mode",
                Expected: "Idle",
                Actual: "Discharge",
                Tolerance: null),
        ]);

        var json = ReplayDiffReportJsonWriter.ToJson(manifest, report);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(ReplaySchemaVersions.DiffReport, root.GetProperty("schema_version").GetString());
        Assert.Equal("rm-m5-04-telemetry-linear", root.GetProperty("dataset_id").GetString());
        Assert.False(root.GetProperty("is_match").GetBoolean());
        Assert.Equal(2, root.GetProperty("difference_count").GetInt32());
        Assert.Equal("numeric_tolerance", root.GetProperty("differences")[0].GetProperty("kind").GetString());
        Assert.Equal(0.1, root.GetProperty("differences")[0].GetProperty("tolerance").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("differences")[1].GetProperty("tolerance").ValueKind);

        var directory = Path.Combine(Path.GetTempPath(), $"replay-report-{Guid.NewGuid():N}");
        try
        {
            var path = ReplayDiffReportJsonWriter.WriteToDirectory(manifest, report, directory);
            Assert.Equal(
                "rm-m5-04-telemetry-linear.replay-diff-report.v1.json",
                Path.GetFileName(path));
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
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

    private static string ResolveRepoReference(string path)
    {
        const string Prefix = "repo://";
        Assert.StartsWith(Prefix, path, StringComparison.Ordinal);
        return RepositoryPath(path[Prefix.Length..]);
    }

    private static IDispatchOptimizer CreateOptimizer(ReplayManifest manifest) =>
        manifest.SolverOptions.Optimizer switch
        {
            "NoOpDispatchOptimizer" => new NoOpDispatchOptimizer(),
            "ScheduleFollowingDispatchOptimizer" => new ScheduleFollowingDispatchOptimizer(new NoOpActivationDispatchSource()),
            _ => throw new InvalidOperationException($"Unsupported replay optimizer '{manifest.SolverOptions.Optimizer}'."),
        };

    private sealed class TemporaryReplayFile : IDisposable
    {
        private TemporaryReplayFile(string path) => Path = path;

        public string Path { get; }

        public static TemporaryReplayFile FromContent(string content)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"replay-manifest-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, content);
            return new TemporaryReplayFile(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
