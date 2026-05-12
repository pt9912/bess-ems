using System.Runtime.InteropServices;
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

        Assert.True(report.IsMatch, string.Join(Environment.NewLine, report.Differences));
    }
}
