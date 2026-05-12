using BatteryEms.Adapters.Optimization;
using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Application.Tests.Replay;

[Trait("Category", "Replay")]
public sealed class ReplayManifestLoaderTests
{
    private const string FixtureRelativePath =
        "tests/fixtures/replay/rm-m5-04/telemetry-linear/manifest.v1.json";

    private static readonly string[] MandatoryM2Cases =
    {
        "m2-replay-bit-exact",
        "m2-schedule-following-golden",
        "m2-missing-valid-recovery",
        "m2-stale-valid-recovery",
    };

    [Fact]
    public async Task Manifest_fixture_replays_against_golden_commands()
    {
        var manifestPath = RepositoryPath(FixtureRelativePath);
        var loadResult = ReplayManifestLoader.Load(manifestPath);

        Assert.True(loadResult.IsSuccess, loadResult.Detail);
        var manifest = Assert.IsType<ReplayManifest>(loadResult.Manifest);
        Assert.Equal(MandatoryM2Cases.Order(StringComparer.Ordinal), manifest.Compatibility.CoversLegacyCases.Order(StringComparer.Ordinal));

        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        var records = TelemetryReplayJsonLoader.LoadFixture(
            Path.Combine(manifestDirectory, manifest.Fixture.Path));
        var golden = ReplayGoldenJsonLoader.Load(
            Path.Combine(manifestDirectory, manifest.Golden.Path));

        var commands = await new TelemetryReplayHarness(
            TestFixtures.CreateAsset("asset-1"),
            new NoOpDispatchOptimizer())
            .RunAsync("asset-1", records, CancellationToken.None);

        var diff = ReplayGoldenComparer.Compare(commands, golden, manifest.Tolerances);

        Assert.True(diff.IsMatch, string.Join(Environment.NewLine, diff.Differences));
    }

    [Fact]
    public void Unknown_manifest_schema_version_is_rejected_with_machine_readable_error()
    {
        var json = File.ReadAllText(RepositoryPath(FixtureRelativePath))
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
        var json = File.ReadAllText(RepositoryPath(FixtureRelativePath))
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
