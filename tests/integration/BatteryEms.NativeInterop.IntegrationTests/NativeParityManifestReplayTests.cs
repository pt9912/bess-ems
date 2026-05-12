using System.Runtime.InteropServices;
using System.Text.Json;
using BatteryEms.Adapters.NativeInterop;
using Xunit;

namespace BatteryEms.NativeInterop.IntegrationTests;

[Collection("native-library")]
[Trait("Category", "Parity")]
public sealed class NativeParityManifestReplayTests
{
    [Fact]
    public void Manifest_drives_managed_native_engine_comparison()
    {
        var manifest = ReplayManifestV1.LoadFromRepo(
            "tests/fixtures/replay/rm-m5-04/native-parity/manifest.v1.json");
        Assert.Equal("native-control-parity", manifest.Kind);
        Assert.Equal(manifest.Fixture.Path, manifest.Golden.Path);
        Assert.Equal("native-parity-cases.v1", manifest.Fixture.SchemaVersion);

        var fixture = ParityFixtureLoader.LoadFromPath(
            ReplayManifestV1.ResolveRepoReference(manifest.Fixture.Path));
        Assert.Equal(
            manifest.CoversLegacyCases.Order(StringComparer.Ordinal),
            fixture.Cases.Select(theCase => theCase.Name).Order(StringComparer.Ordinal));

        var libraryPath = NativeLibraryLocator.Locate();
        var handle = NativeLibrary.Load(libraryPath);
        using var native = new NativeControlKernel(handle);

        var report = NativeParityEngineComparisonRunner.Compare(manifest, fixture, native);
        var reportJson = NativeParityEngineComparisonReportJsonWriter.ToJson(manifest, report);
        NativeParityEngineComparisonReportJsonWriter.WriteIfConfigured(manifest, report);

        Assert.True(report.IsMatch, reportJson);
    }

    [Fact]
    public void Manifest_engine_comparison_report_serializes_machine_readable_json()
    {
        var manifest = ReplayManifestV1.LoadFromRepo(
            "tests/fixtures/replay/rm-m5-04/native-parity/manifest.v1.json");
        var report = new NativeParityEngineComparisonReport(
        [
            new NativeParityEngineDifference(
                CaseName: "case-a",
                Engine: "managed/native",
                Kind: "numeric_tolerance",
                Field: "active_power_kw",
                Expected: "1",
                Actual: "1.5",
                Tolerance: 0.1),
            new NativeParityEngineDifference(
                CaseName: "case-b",
                Engine: "native",
                Kind: "business_drift",
                Field: "reason",
                Expected: "ok",
                Actual: "limited",
                Tolerance: null),
        ]);

        var json = NativeParityEngineComparisonReportJsonWriter.ToJson(manifest, report);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("replay-diff-report.v1", root.GetProperty("schema_version").GetString());
        Assert.Equal("m3-native-parity-cases-v1", root.GetProperty("dataset_id").GetString());
        Assert.Equal("native-control-parity", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("is_match").GetBoolean());
        Assert.Equal(2, root.GetProperty("difference_count").GetInt32());
        Assert.Equal("case-a", root.GetProperty("differences")[0].GetProperty("case_name").GetString());
        Assert.Equal("managed/native", root.GetProperty("differences")[0].GetProperty("engine").GetString());
        Assert.Equal(0.1, root.GetProperty("differences")[0].GetProperty("tolerance").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("differences")[1].GetProperty("tolerance").ValueKind);
    }
}
