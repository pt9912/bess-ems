using BatteryEms.Adapters.NativeInterop;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.Adapters.NativeInterop.Tests;

// RM-M3-03 unit tests for the startup loader. Every M3-Zielbild
// outcome (Disabled / LibraryMissing / LoadFailed / AbiMismatch /
// Loaded) is exercised through a fake gateway, so the test suite
// runs without a real .so on disk.
public sealed class NativeControlLoaderTests
{
    private const string TestPath = "/path/to/libbattery_control_core.so";

    [Fact]
    public void Disabled_when_options_enabled_is_false()
    {
        var gateway = new FakeGateway();
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions { Enabled = false });

        Assert.Equal(NativeControlStatus.Disabled, result.Status);
        Assert.Null(result.LibraryPath);
        // The disabled path MUST NOT touch the file system. The
        // operator can put garbage in LibraryPath and the loader
        // still returns Disabled cleanly.
        Assert.Equal(0, gateway.FileExistsCalls);
        Assert.Equal(0, gateway.LoadCalls);
    }

    [Fact]
    public void LibraryMissing_when_file_does_not_exist()
    {
        var gateway = new FakeGateway { FileExistsResult = false };
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = TestPath,
        });

        Assert.Equal(NativeControlStatus.LibraryMissing, result.Status);
        Assert.Equal(TestPath, result.LibraryPath);
        Assert.Equal(0, gateway.LoadCalls);
    }

    [Fact]
    public void LoadFailed_when_dlopen_throws()
    {
        var gateway = new FakeGateway
        {
            FileExistsResult = true,
            LoadException = new DllNotFoundException("libsomething.so.1: cannot open shared object file"),
        };
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = TestPath,
        });

        Assert.Equal(NativeControlStatus.LoadFailed, result.Status);
        Assert.Equal(TestPath, result.LibraryPath);
        Assert.Contains("cannot open shared object", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFailed_when_abi_export_resolution_throws()
    {
        var gateway = new FakeGateway
        {
            FileExistsResult = true,
            LoadResult = (nint)0x1234,
            CallAbiVersionException = new EntryPointNotFoundException(
                "Unable to find an entry point named 'battery_control_core_abi_version'"),
        };
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = TestPath,
        });

        Assert.Equal(NativeControlStatus.LoadFailed, result.Status);
        Assert.Contains("entry point", result.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AbiMismatch_when_major_version_differs()
    {
        const uint LibraryMajor1Minor0 = (1u << 16) | (0u << 8) | 0u;
        var gateway = new FakeGateway
        {
            FileExistsResult = true,
            LoadResult = (nint)0x1234,
            CallAbiVersionResult = LibraryMajor1Minor0,
        };
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = TestPath,
        });

        Assert.Equal(NativeControlStatus.AbiMismatch, result.Status);
        Assert.Equal(LibraryMajor1Minor0, result.AbiVersion);
        Assert.Equal(NativeControlLoader.ExpectedAbiVersion, result.ExpectedAbiVersion);
        Assert.Contains("not compatible", result.Detail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AbiMismatch_when_minor_is_lower_than_expected()
    {
        // Host expects 0.1.0 but library reports 0.0.5: minor too
        // low, additive-compat rule fails.
        const uint Older = (0u << 16) | (0u << 8) | 5u;
        var gateway = new FakeGateway
        {
            FileExistsResult = true,
            LoadResult = (nint)0x1234,
            CallAbiVersionResult = Older,
        };
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = TestPath,
        });

        Assert.Equal(NativeControlStatus.AbiMismatch, result.Status);
    }

    [Fact]
    public void Loaded_when_library_reports_compatible_abi()
    {
        // Host expects 0.1.0; library reports the exact host
        // expectation. This case is the only one a production
        // deployment of RM-M3-04+ may rely on.
        var gateway = new FakeGateway
        {
            FileExistsResult = true,
            LoadResult = (nint)0x1234,
            CallAbiVersionResult = NativeControlLoader.ExpectedAbiVersion,
        };
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = TestPath,
        });

        Assert.Equal(NativeControlStatus.Loaded, result.Status);
        Assert.True(result.IsLoaded);
        Assert.Equal(NativeControlLoader.ExpectedAbiVersion, result.AbiVersion);
        Assert.Equal(NativeControlLoader.ExpectedAbiVersion, result.ExpectedAbiVersion);
        // M3-D2-01: a successful load now carries the OS handle so a
        // DI factory can construct NativeControlKernel without a
        // second dlopen.
        Assert.NotNull(result.Handle);
        Assert.Equal((nint)0x1234, result.Handle!.Value);
    }

    [Fact]
    public void Non_loaded_results_do_not_carry_a_handle()
    {
        // M3-D2-01: only Loaded carries a handle; every other status
        // must leave Handle null so a caller can't accidentally
        // construct a kernel from a half-broken load.
        Assert.Null(NativeControlLoadResult.Disabled().Handle);
        Assert.Null(NativeControlLoadResult.LibraryMissing("/x").Handle);
        Assert.Null(NativeControlLoadResult.LoadFailed("/x", "boom").Handle);
        Assert.Null(NativeControlLoadResult.AbiMismatch(
            "/x", reported: 0u, expected: NativeControlLoader.ExpectedAbiVersion).Handle);
    }

    [Fact]
    public void Loaded_when_library_reports_higher_minor_than_expected()
    {
        // Additive-compat rule: same major, minor at-least-host's
        // expectation, patch arbitrary.
        const uint NewerMinor = (0u << 16) | (5u << 8) | 99u;
        var gateway = new FakeGateway
        {
            FileExistsResult = true,
            LoadResult = (nint)0x1234,
            CallAbiVersionResult = NewerMinor,
        };
        var loader = new NativeControlLoader(gateway, NullLogger<NativeControlLoader>.Instance);

        var result = loader.TryLoad(new NativeControlOptions
        {
            Enabled = true,
            LibraryPath = TestPath,
        });

        Assert.Equal(NativeControlStatus.Loaded, result.Status);
    }

    [Fact]
    public void Constructor_rejects_null_options()
    {
        var loader = new NativeControlLoader(new FakeGateway(),
            NullLogger<NativeControlLoader>.Instance);
        Assert.Throws<ArgumentNullException>(() => loader.TryLoad(null!));
    }

    [Fact]
    public void ApplyAbortPolicy_throws_when_mismatch_and_abort_flag_set()
    {
        // docs/user/quality.md §5.2: opt-in production policy turns
        // an ABI mismatch into a hard start-up failure. Default-off
        // keeps the M3 fallback contract — the negative case is
        // covered by every other AbiMismatch test above (none
        // throws).
        const uint Reported = (1u << 16);
        var result = NativeControlLoadResult.AbiMismatch(
            "/x", Reported, NativeControlLoader.ExpectedAbiVersion);
        var options = new NativeControlOptions
        {
            Enabled = true,
            AbortOnAbiMismatch = true,
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            NativeControlLoader.ApplyAbortPolicy(result, options));
        Assert.Contains("AbortOnAbiMismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyAbortPolicy_passes_when_status_is_loaded_even_with_abort_flag()
    {
        // Abort policy only fires on AbiMismatch; a Loaded result
        // (regardless of the abort flag) must pass through.
        var result = NativeControlLoadResult.Loaded(
            "/x", NativeControlLoader.ExpectedAbiVersion,
            NativeControlLoader.ExpectedAbiVersion,
            handle: (nint)0x1234);
        var options = new NativeControlOptions
        {
            Enabled = true,
            AbortOnAbiMismatch = true,
        };
        NativeControlLoader.ApplyAbortPolicy(result, options); // no throw
    }

    [Fact]
    public void ApplyAbortPolicy_passes_on_mismatch_when_flag_is_false()
    {
        // Default policy = .NET fallback. Mismatch + abort=false
        // must NOT throw — the caller falls back to the managed
        // path instead.
        const uint Reported = (1u << 16);
        var result = NativeControlLoadResult.AbiMismatch(
            "/x", Reported, NativeControlLoader.ExpectedAbiVersion);
        var options = new NativeControlOptions { Enabled = true };
        NativeControlLoader.ApplyAbortPolicy(result, options); // no throw
    }

    private sealed class FakeGateway : INativeLibraryGateway
    {
        public bool FileExistsResult { get; set; }
        public nint LoadResult { get; set; }
        public Exception? LoadException { get; set; }
        public uint CallAbiVersionResult { get; set; }
        public Exception? CallAbiVersionException { get; set; }

        public int FileExistsCalls { get; private set; }
        public int LoadCalls { get; private set; }

        public bool FileExists(string path)
        {
            FileExistsCalls++;
            return FileExistsResult;
        }

        public nint Load(string path)
        {
            LoadCalls++;
            if (LoadException is not null) { throw LoadException; }
            return LoadResult;
        }

        public uint CallAbiVersion(nint handle)
        {
            if (CallAbiVersionException is not null) { throw CallAbiVersionException; }
            return CallAbiVersionResult;
        }

        // Loader tests never reach Compute / Free — the loader's
        // job ends at the ABI handshake. The kernel-specific tests
        // use a separate fake gateway in NativeControlKernelTests.
        public int CallCompute(
            nint handle,
            in BccSnapshot snapshot,
            in BccLimits limits,
            in BccRequest request,
            out BccCommand command)
        {
            command = default;
            throw new InvalidOperationException(
                "NativeControlLoaderTests' FakeGateway should never see a CallCompute "
                + "(the loader does not invoke compute). Use NativeControlKernelTests' "
                + "FakeGateway for kernel-side coverage.");
        }

        public int CallPidStep(
            nint handle,
            in BccPidState state,
            in BccPidOptions options,
            in BccPidInput input,
            out BccPidCommand command)
        {
            command = default;
            throw new InvalidOperationException(
                "NativeControlLoaderTests' FakeGateway should never see a CallPidStep "
                + "(the loader's job ends at the ABI handshake). Use the kernel-side "
                + "FakeGateway for PidStep coverage.");
        }

        public void Free(nint handle)
        {
            // No-op: the loader test fixtures don't manage the handle
            // lifetime explicitly.
        }
    }
}
