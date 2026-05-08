using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-03 minimal startup loader for the native control core.
//
// Five outcomes — one per row of the M3-Zielbild table — emit a
// structured log line so health/metrics adapters (RM-M3-05) can
// surface the same value:
//
//   Disabled          | NativeControl:Enabled is false
//   LibraryMissing    | configured library file not present
//   LoadFailed        | dlopen / GetExport raised an exception
//   AbiMismatch       | ABI major/minor not compatible
//   Loaded            | library opened, ABI compatible, ready
//
// The loader does NOT bind compute functions or activate routing —
// that is RM-M3-04 + RM-M3-05. It only proves the library can be
// reached and is talking the same ABI dialect as the host.
//
// Compatibility rule: major must equal the host expectation, minor
// must be greater-or-equal (additive backward compat). Patch is
// ignored for compatibility — the implementation is free to bug-
// fix under a fixed ABI surface.
public sealed partial class NativeControlLoader
{
    // Host expectation, baked from the header constants in
    // native/battery_control_core/include/battery_control_core.h.
    // Bumping the native ABI requires updating these literals AND
    // shipping the matching .so; the unit test
    // Loaded_when_library_reports_compatible_abi verifies the two
    // sides agree.
    public const uint ExpectedAbiMajor = 0;
    public const uint ExpectedAbiMinor = 1;
    public const uint ExpectedAbiPatch = 0;

    public static uint ExpectedAbiVersion =>
        (ExpectedAbiMajor << 16) | (ExpectedAbiMinor << 8) | ExpectedAbiPatch;

    private readonly INativeLibraryGateway _gateway;
    private readonly ILogger<NativeControlLoader> _logger;

    public NativeControlLoader(ILogger<NativeControlLoader> logger)
        : this(new SystemNativeLibraryGateway(), logger)
    {
    }

    internal NativeControlLoader(
        INativeLibraryGateway gateway,
        ILogger<NativeControlLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(logger);
        _gateway = gateway;
        _logger = logger;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1031",
        Justification = "Loader contract: any exception from the OS "
            + "loader is mapped to a LoadFailed result so the host "
            + "can fall back to the managed kernel rather than crash.")]
    public NativeControlLoadResult TryLoad(NativeControlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            var disabled = NativeControlLoadResult.Disabled();
            LogDisabled();
            return disabled;
        }

        if (!_gateway.FileExists(options.LibraryPath))
        {
            var missing = NativeControlLoadResult.LibraryMissing(options.LibraryPath);
            LogLibraryMissing(options.LibraryPath);
            return missing;
        }

        nint handle;
        try
        {
            handle = _gateway.Load(options.LibraryPath);
        }
        catch (Exception ex)
        {
            var failed = NativeControlLoadResult.LoadFailed(options.LibraryPath, ex.Message);
            LogLoadFailed(options.LibraryPath, ex.Message);
            return failed;
        }

        uint reportedVersion;
        try
        {
            reportedVersion = _gateway.CallAbiVersion(handle);
        }
        catch (Exception ex)
        {
            // GetExport / cdecl invocation failure folds into the same
            // load-failed bucket as the dlopen path; the operator
            // distinguishes them through the detail message, not the
            // status enum.
            var failed = NativeControlLoadResult.LoadFailed(options.LibraryPath, ex.Message);
            LogLoadFailed(options.LibraryPath, ex.Message);
            return failed;
        }

        if (!IsCompatible(reportedVersion))
        {
            var mismatch = NativeControlLoadResult.AbiMismatch(
                options.LibraryPath, reportedVersion, ExpectedAbiVersion);
            LogAbiMismatch(options.LibraryPath, reportedVersion, ExpectedAbiVersion);
            return mismatch;
        }

        var loaded = NativeControlLoadResult.Loaded(
            options.LibraryPath, reportedVersion, ExpectedAbiVersion);
        LogLoaded(options.LibraryPath, reportedVersion);
        return loaded;
    }

    internal static bool IsCompatible(uint reportedVersion)
    {
        var major = (reportedVersion >> 16) & 0xFFu;
        var minor = (reportedVersion >> 8) & 0xFFu;
        return major == ExpectedAbiMajor && minor >= ExpectedAbiMinor;
    }

    // Production-policy escape hatch (docs/user/quality.md §5.2): when
    // a deployment opts into AbortOnAbiMismatch and the loader saw an
    // incompatible library, callers turn the result into a hard
    // start-up failure instead of silently falling back. Default-off
    // policy keeps the M3 fallback contract intact.
    public static void ApplyAbortPolicy(
        NativeControlLoadResult result, NativeControlOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        if (options.AbortOnAbiMismatch
            && result.Status == NativeControlStatus.AbiMismatch)
        {
            throw new InvalidOperationException(
                $"Native control library at '{result.LibraryPath}' "
                + $"reports ABI {NativeControlLoadResult.FormatVersion(result.AbiVersion ?? 0)} "
                + $"but host expects {NativeControlLoadResult.FormatVersion(result.ExpectedAbiVersion ?? 0)}; "
                + "AbortOnAbiMismatch=true requires a compatible library.");
        }
    }

    [LoggerMessage(EventId = 3300, Level = LogLevel.Information,
        Message = "Native control disabled native_control_status=disabled")]
    private partial void LogDisabled();

    [LoggerMessage(EventId = 3301, Level = LogLevel.Warning,
        Message = "Native control library missing native_control_status=library-missing path={Path}")]
    private partial void LogLibraryMissing(string path);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Warning,
        Message = "Native control load failed native_control_status=load-failed path={Path} detail={Detail}")]
    private partial void LogLoadFailed(string path, string detail);

    // CA1873: pass the raw uint into LoggerMessage so the formatted
    // dotted version string is only allocated when the log level is
    // actually enabled. The source-generated method gates the work
    // behind an IsEnabled check; the structured-log capture sees
    // the packed uint, which the operator can decode the same way
    // as NativeControlLoadResult.FormatVersion (major in bits 16..23,
    // minor 8..15, patch 0..7).
    [LoggerMessage(EventId = 3303, Level = LogLevel.Warning,
        Message = "Native control ABI mismatch native_control_status=abi-mismatch path={Path} reported_packed={Reported} expected_packed={Expected}")]
    private partial void LogAbiMismatch(string path, uint reported, uint expected);

    [LoggerMessage(EventId = 3304, Level = LogLevel.Information,
        Message = "Native control loaded native_control_status=loaded path={Path} abi_version_packed={AbiVersion}")]
    private partial void LogLoaded(string path, uint abiVersion);
}
