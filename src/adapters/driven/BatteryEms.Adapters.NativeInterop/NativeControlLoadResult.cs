namespace BatteryEms.Adapters.NativeInterop;

// RM-M3-03 loader outcome record. Carries both the broad status
// classification and the contextual fields a control plane needs
// to surface a useful health/log line.
public sealed record NativeControlLoadResult
{
    public NativeControlStatus Status { get; init; }

    // Path the loader inspected. Null only for Disabled, which
    // never touches the file system.
    public string? LibraryPath { get; init; }

    // Packed ABI version the library reported, when reachable.
    // Filled for AbiMismatch and Loaded.
    public uint? AbiVersion { get; init; }

    // Expected ABI version baked into this host build. Filled
    // alongside AbiVersion so logs can show "expected X, got Y".
    public uint? ExpectedAbiVersion { get; init; }

    // Operator-readable detail (exception message, missing-export
    // name, ...). Null when the status carries no extra context.
    public string? Detail { get; init; }

    // OS handle returned by NativeLibrary.Load when the load+ABI
    // handshake succeeded. Filled only for `Loaded`; null on every
    // other status. M3-D2 hands this to the DI factory so a
    // NativeControlKernel can be constructed without a second
    // dlopen — the caller then owns the handle and Free's it via
    // the kernel's Dispose path. For non-Loaded statuses no handle
    // is open (the loader closes the dlopen on its own error
    // paths) so callers must not assume a handle exists.
    public nint? Handle { get; init; }

    public bool IsLoaded => Status == NativeControlStatus.Loaded;

    public static NativeControlLoadResult Disabled() =>
        new() { Status = NativeControlStatus.Disabled };

    public static NativeControlLoadResult LibraryMissing(string path) =>
        new() { Status = NativeControlStatus.LibraryMissing, LibraryPath = path };

    public static NativeControlLoadResult LoadFailed(string path, string detail) =>
        new() { Status = NativeControlStatus.LoadFailed, LibraryPath = path, Detail = detail };

    public static NativeControlLoadResult AbiMismatch(
        string path, uint reported, uint expected) =>
        new()
        {
            Status = NativeControlStatus.AbiMismatch,
            LibraryPath = path,
            AbiVersion = reported,
            ExpectedAbiVersion = expected,
            Detail = $"library ABI {FormatVersion(reported)} is not "
                + $"compatible with expected {FormatVersion(expected)}",
        };

    public static NativeControlLoadResult Loaded(
        string path, uint reported, uint expected, nint handle) =>
        new()
        {
            Status = NativeControlStatus.Loaded,
            LibraryPath = path,
            AbiVersion = reported,
            ExpectedAbiVersion = expected,
            Handle = handle,
        };

    public static string FormatVersion(uint version)
    {
        var major = (version >> 16) & 0xFF;
        var minor = (version >> 8) & 0xFF;
        var patch = version & 0xFF;
        return $"{major}.{minor}.{patch}";
    }
}
