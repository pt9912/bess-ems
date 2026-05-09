using BatteryEms.Adapters.NativeInterop;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.NativeInterop.IntegrationTests;

// RM-M3-07 ABI handshake tests against the real
// libbattery_control_core.so. The unit-level loader tests in
// BatteryEms.Adapters.NativeInterop.Tests cover every loader
// outcome through a fake gateway; this suite proves the same
// outcomes hold for the actual library — i.e. the host's
// ExpectedAbiMajor/Minor literals in NativeControlLoader still
// match what the C source defines in battery_control_core.h.
public sealed class NativeAbiVersionTests
{
    [Fact]
    public void Loader_returns_loaded_for_real_library_with_matching_abi()
    {
        var path = NativeLibraryLocator.Locate();
        var loader = new NativeControlLoader(NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = path,
        });

        Assert.Equal(NativeControlStatus.Loaded, result.Status);
        Assert.True(result.IsLoaded);
        Assert.Equal(path, result.LibraryPath);
        Assert.NotNull(result.AbiVersion);
        Assert.Equal(NativeControlLoader.ExpectedAbiVersion, result.AbiVersion);
        Assert.Equal(NativeControlLoader.ExpectedAbiVersion, result.ExpectedAbiVersion);
    }

    [Fact]
    public void Real_library_reports_packed_major_minor_patch_matching_host()
    {
        // Decode the packed uint and pin every component against the
        // host literals so a future native-side bump that forgets to
        // sync NativeControlLoader.Expected* — or vice versa — fails
        // here with a clear field-by-field diff.
        var path = NativeLibraryLocator.Locate();
        var loader = new NativeControlLoader(NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = path,
        });

        Assert.Equal(NativeControlStatus.Loaded, result.Status);
        var reported = result.AbiVersion!.Value;
        var major = (reported >> 16) & 0xFFu;
        var minor = (reported >> 8) & 0xFFu;
        var patch = reported & 0xFFu;
        Assert.Equal(NativeControlLoader.ExpectedAbiMajor, major);
        Assert.Equal(NativeControlLoader.ExpectedAbiMinor, minor);
        Assert.Equal(NativeControlLoader.ExpectedAbiPatch, patch);
    }

    [Fact]
    public void Loader_returns_library_missing_when_path_does_not_exist()
    {
        // RM-M3-07 negative case: the production loader path with a
        // bogus library path must return LibraryMissing rather than
        // throw — exercises the same outcome the unit-level fake
        // covers, but through the real SystemNativeLibraryGateway.
        var loader = new NativeControlLoader(NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = "/nonexistent/path/to/libbattery_control_core.so",
        });

        Assert.Equal(NativeControlStatus.LibraryMissing, result.Status);
    }

    [Fact]
    public void Loader_returns_disabled_without_touching_filesystem_when_disabled()
    {
        // The disabled path must short-circuit before any file/OS
        // access — the unit-level test pins this with a fake
        // gateway; here we verify the same holds when the production
        // gateway is wired up. A garbage LibraryPath proves the
        // file system was never consulted.
        var loader = new NativeControlLoader(NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = false,
            LibraryPath = "/this/path/does/not/exist/and/must/never/be/read",
        });

        Assert.Equal(NativeControlStatus.Disabled, result.Status);
    }
}
